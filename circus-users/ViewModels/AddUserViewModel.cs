using System.Collections.ObjectModel;
using Circus.Users.App.Services;
using Circus.Users.Core.Abstractions;
using Circus.Users.Core.ActiveDirectory;
using Circus.Users.Core.Models;
using Circus.Users.Core.Naming;
using Circus.Users.Core.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Options;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Circus.Users.App.ViewModels;

/// <summary>Подразделение в выпадающем списке формы.</summary>
public sealed record DepartmentChoice(string DistinguishedName, string Name)
{
    public override string ToString() => Name;
}

/// <summary>
/// Создание сотрудника: ФИО разбирается на части, логин и почта подбираются сами,
/// значения подразделения подставляются из его шаблона.
/// </summary>
internal sealed partial class AddUserViewModel : ObservableObject
{
    /// <summary>Пауза перед пересчётом логина — чтобы не дёргать проверку на каждую букву.</summary>
    private static readonly TimeSpan RecalculateDelay = TimeSpan.FromMilliseconds(300);

    private readonly DirectoryCache _cache;
    private readonly IDepartmentTemplateStore _templates;
    private readonly IDepartmentRegistry _registry;
    private readonly IUserProvisioning _provisioning;
    private readonly IMailService _mail;
    private readonly IAuditLog _audit;
    private readonly SessionService _session;
    private readonly IPreferencesStore _preferences;
    private readonly MailSettings _mailSettings;
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();

    private CancellationTokenSource? _pendingRecalculate;
    private DepartmentTemplate _template = new();


    /// <summary>Запрос, у которого не удалась почтовая часть — для повтора.</summary>
    private NewUserRequest? _pendingMailbox;
    /// <summary>Занятые логины почтовой организации; пусто, пока почта не опрошена.</summary>
    private HashSet<string> _mailLogins = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Логин правит администратор — сам его больше не подбираем.</summary>
    private bool _isAccountNameManual;

    [ObservableProperty]
    private string _fullName = string.Empty;

    [ObservableProperty]
    private string _lastName = string.Empty;

    [ObservableProperty]
    private string _firstName = string.Empty;

    [ObservableProperty]
    private string _middleName = string.Empty;

    [ObservableProperty]
    private string _accountName = string.Empty;

    [ObservableProperty]
    private string _email = string.Empty;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _ipPhone = string.Empty;

    [ObservableProperty]
    private DepartmentChoice? _selectedDepartment;

    [ObservableProperty]
    private string _loginHint = string.Empty;

    [ObservableProperty]
    private bool _hasLoginProblem;

    [ObservableProperty]
    private string _templateSummary = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _mailStatus = "Почта не проверялась.";

    public AddUserViewModel(
        DirectoryCache cache,
        IDepartmentTemplateStore templates,
        IDepartmentRegistry registry,
        IUserProvisioning provisioning,
        IMailService mail,
        IAuditLog audit,
        SessionService session,
        IPreferencesStore preferences,
        IOptions<AppSettings> settings)
    {
        _cache = cache;
        _templates = templates;
        _registry = registry;
        _provisioning = provisioning;
        _mail = mail;
        _audit = audit;
        _session = session;
        _preferences = preferences;
        _mailSettings = settings.Value.Mail;

        _cache.Changed += OnCacheChanged;
        _registry.Changed += OnRegistryChanged;

        // Логины почты подтягиваются в фоне: форма должна открыться сразу.
        _ = RefreshMailLoginsAsync();
        RebuildDepartments();
    }

    public ObservableCollection<DepartmentChoice> Departments { get; } = [];

    public string WriteTargetNotice => _preferences.Current.UseFakeData
        ? "Тестовый режим: сотрудник не будет создан в каталоге."
        : "Внимание: сотрудник будет создан в Active Directory.";

    public Visibility LoginHintVisibility =>
        string.IsNullOrEmpty(LoginHint) ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>Можно ли создавать: заполнено обязательное и логин не конфликтует.</summary>
    public bool CanCreate =>
        !IsBusy
        && !HasLoginProblem
        && LastName.Length > 0
        && FirstName.Length > 0
        && AccountName.Length > 0
        && SelectedDepartment is not null;

    private void OnCacheChanged(object? sender, EventArgs e) => _dispatcher.TryEnqueue(RebuildDepartments);

    private void OnRegistryChanged(object? sender, EventArgs e) => _dispatcher.TryEnqueue(RebuildDepartments);

    private void RebuildDepartments()
    {
        var previous = SelectedDepartment?.DistinguishedName;

        Departments.Clear();

        // Каталоги, помеченные как не подразделения, в выбор не попадают:
        // заводить в них сотрудников запрещено.
        foreach (var unit in _cache.Departments
                     .Where(unit => _registry.IsDepartment(unit.DistinguishedName))
                     .OrderBy(unit => unit.Name, StringComparer.CurrentCulture))
        {
            Departments.Add(new DepartmentChoice(unit.DistinguishedName, unit.Name));
        }

        if (previous is not null)
        {
            SelectedDepartment = Departments.FirstOrDefault(choice => choice.DistinguishedName == previous);
        }
    }

    partial void OnFullNameChanged(string value)
    {
        var name = RussianFullName.Parse(value);

        LastName = name.Surname;
        FirstName = name.GivenName;
        MiddleName = name.Patronymic;

        // Логин и почта пересчитываются с задержкой: проверка занятости идёт по всему справочнику.
        ScheduleRecalculate(name);
    }

    partial void OnAccountNameChanged(string value)
    {
        Email = value.Length > 0 ? $"{value}@{_mailSettings.Domain}" : string.Empty;
        OnPropertyChanged(nameof(CanCreate));
    }

    partial void OnLastNameChanged(string value) => OnPropertyChanged(nameof(CanCreate));

    partial void OnFirstNameChanged(string value) => OnPropertyChanged(nameof(CanCreate));

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanCreate));

    partial void OnHasLoginProblemChanged(bool value) => OnPropertyChanged(nameof(CanCreate));

    partial void OnLoginHintChanged(string value) => OnPropertyChanged(nameof(LoginHintVisibility));

    partial void OnSelectedDepartmentChanged(DepartmentChoice? value)
    {
        OnPropertyChanged(nameof(CanCreate));
        _ = LoadTemplateAsync(value);
    }

    private void ScheduleRecalculate(RussianFullName name)
    {
        _pendingRecalculate?.Cancel();

        var cts = new CancellationTokenSource();
        _pendingRecalculate = cts;

        _ = RecalculateAsync(name, cts);
    }

    private async Task RecalculateAsync(RussianFullName name, CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(RecalculateDelay, cts.Token);
            Recalculate(name);
        }
        catch (OperationCanceledException)
        {
            // Пришли новые буквы — этот пересчёт больше не нужен.
        }
        finally
        {
            Interlocked.CompareExchange(ref _pendingRecalculate, null, cts);
            cts.Dispose();
        }
    }

    private void Recalculate(RussianFullName name)
    {
        if (_isAccountNameManual)
        {
            return;
        }

        var suggestion = LoginGenerator.Generate(name, IsTaken);

        HasLoginProblem = suggestion.Problem is not null;
        AccountName = suggestion.Login;
        LoginHint = BuildHint(suggestion);
    }

    private string BuildHint(LoginSuggestion suggestion)
    {
        if (suggestion.Problem is { } problem)
        {
            return problem;
        }

        var parts = new List<string>();

        if (suggestion.Taken.Count > 0)
        {
            parts.Add(suggestion.Taken.Count == 1
                ? $"{suggestion.Taken[0]} уже занят"
                : $"заняты: {string.Join(", ", suggestion.Taken)}");
        }

        if (suggestion.WasTruncated)
        {
            parts.Add($"фамилия сокращена: учётная запись в домене не длиннее {LoginGenerator.MaxAccountNameLength} символов");
        }

        return string.Join("; ", parts);
    }

    /// <summary>
    /// Занятость проверяется в трёх местах: учётная запись в каталоге, почтовый адрес
    /// в каталоге и логин в почтовой организации. Достаточно совпадения в любом из них.
    /// </summary>
    private bool IsTaken(string login)
    {
        var mail = $"{login}@{_mailSettings.Domain}";

        if (_mailLogins.Contains(login))
        {
            return true;
        }

        return _cache.Employees.Any(employee =>
            string.Equals(employee.Get("sAMAccountName"), login, StringComparison.OrdinalIgnoreCase)
            || string.Equals(employee.Get("mail"), mail, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Подтягивает занятые логины из почты. Отдельного метода проверки одного логина
    /// в Yandex 360 нет, поэтому список забирается целиком и держится в памяти.
    /// Недоступная почта не должна мешать заводить сотрудника в каталоге.
    /// </summary>
    private async Task RefreshMailLoginsAsync()
    {
        if (!_mail.IsConfigured)
        {
            MailStatus = "Почта не настроена: ящик создан не будет.";
            return;
        }

        try
        {
            var logins = await _mail.GetLoginsAsync();
            _mailLogins = new HashSet<string>(logins, StringComparer.OrdinalIgnoreCase);
            MailStatus = $"Почта подключена, занятых логинов: {_mailLogins.Count}.";
        }
        catch (Exception ex)
        {
            MailStatus = $"Не удалось получить логины из почты: {ex.Message}";
        }
    }

    private async Task LoadTemplateAsync(DepartmentChoice? department)
    {
        if (department is null)
        {
            _template = new DepartmentTemplate();
            TemplateSummary = string.Empty;
            return;
        }

        try
        {
            _template = await _templates.LoadAsync(department.DistinguishedName);
            TemplateSummary = _template.IsEmpty
                ? "Шаблон подразделения не задан — заполнятся только поля формы."
                : BuildTemplateSummary(_template);
        }
        catch (Exception ex)
        {
            _template = new DepartmentTemplate();
            TemplateSummary = $"Не удалось прочитать шаблон подразделения: {ex.Message}";
        }
    }

    private static string BuildTemplateSummary(DepartmentTemplate template)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(template.Company))
        {
            parts.Add($"организация «{template.Company}»");
        }

        if (!string.IsNullOrWhiteSpace(template.TelephoneNumber))
        {
            parts.Add($"телефон {template.TelephoneNumber}");
        }

        if (template.AdGroups.Count > 0)
        {
            parts.Add($"групп AD: {template.AdGroups.Count}");
        }

        if (template.MailGroups.Count > 0)
        {
            parts.Add($"групп рассылки: {template.MailGroups.Count}");
        }

        return parts.Count > 0 ? "Из шаблона подразделения: " + string.Join(", ", parts) : string.Empty;
    }

    /// <summary>Отключает автоподбор: дальше логин целиком на совести администратора.</summary>
    [RelayCommand]
    private void EditAccountNameManually()
    {
        _isAccountNameManual = true;
        HasLoginProblem = false;
        LoginHint = "Логин задан вручную — автоподбор выключен.";
    }

    [RelayCommand]
    private void Reset()
    {
        _isAccountNameManual = false;
        _template = new DepartmentTemplate();

        FullName = string.Empty;
        LastName = string.Empty;
        FirstName = string.Empty;
        MiddleName = string.Empty;
        AccountName = string.Empty;
        Email = string.Empty;
        Title = string.Empty;
        IpPhone = string.Empty;
        SelectedDepartment = null;
        LoginHint = string.Empty;
        HasLoginProblem = false;
        TemplateSummary = string.Empty;
    }

    /// <summary>Создаёт сотрудника. Возвращает результат либо текст ошибки.</summary>
    public async Task<(ProvisionedUser? Result, string? Error)> CreateAsync()
    {
        if (SelectedDepartment is not { } department)
        {
            return (null, "Не выбрано подразделение.");
        }

        // Отметку мог снять другой администратор, пока форма была открыта.
        if (!_registry.IsDepartment(department.DistinguishedName))
        {
            return (null, $"Каталог «{department.Name}» помечен как не подразделение — сотрудников в нём не заводят.");
        }

        var request = new NewUserRequest
        {
            DepartmentDn = department.DistinguishedName,
            DisplayName = string.Join(' ', new[] { LastName, FirstName, MiddleName }.Where(part => part.Length > 0)),
            FirstName = FirstName,
            LastName = LastName,
            MiddleName = MiddleName,
            AccountName = AccountName,
            Email = Email,
            Title = Empty(Title),
            IpPhone = Empty(IpPhone),
            Company = _template.Company,
            TelephoneNumber = _template.TelephoneNumber,
            City = _template.City,
            StreetAddress = _template.StreetAddress,
            Office = _template.Office,
            AdGroups = _template.AdGroups,
            MailGroups = _template.MailGroups,
        };

        IsBusy = true;
        try
        {
            var created = await _provisioning.CreateAsync(request);

            // Ящик заводится вторым шагом. Сотрудник в каталоге уже есть, поэтому сбой почты
            // не отменяет создание — он возвращается предупреждением и допускает повтор.
            var withMail = await AttachMailboxAsync(created, request);

            await _audit.WriteAsync(
                _session.Current.AccountName,
                "Создание сотрудника",
                request.DisplayName,
                // Пароли в журнал не попадают: он лежит на общей шаре.
                $"{withMail.AccountName}, {withMail.Email}, подразделение: {department.Name}");

            await _cache.RefreshAsync();
            await RefreshMailLoginsAsync();
            return (withMail, null);
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Заводит ящик и подшивает его к результату. Ошибка почты не поднимается наверх:
    /// учётная запись в каталоге уже создана, и терять её из-за почты нельзя.
    /// </summary>
    private async Task<ProvisionedUser> AttachMailboxAsync(ProvisionedUser created, NewUserRequest request)
    {
        if (!_mail.IsConfigured)
        {
            return created with
            {
                Warnings = [.. created.Warnings, "Почта не настроена — ящик не создан. Заведите его вручную."],
            };
        }

        try
        {
            var mailbox = await _mail.CreateMailboxAsync(new NewMailboxRequest
            {
                Nickname = request.AccountName,
                FirstName = request.FirstName,
                LastName = request.LastName,
                MiddleName = request.MiddleName,
                Groups = request.MailGroups,
            });

            _pendingMailbox = null;

            return created with
            {
                Email = mailbox.Email,
                MailPassword = mailbox.Password,
                Warnings = [.. created.Warnings, .. mailbox.Warnings],
            };
        }
        catch (Exception ex)
        {
            // Запоминаем запрос, чтобы повторить только почтовую часть.
            _pendingMailbox = request;

            // Отказ почты попадает в журнал: учётная запись в каталоге уже есть,
            // и по этой записи видно, кому ящик нужно завести.
            await _audit.WriteAsync(
                _session.Current.AccountName,
                "Ящик не создан",
                request.DisplayName,
                $"{request.AccountName}: {ex.Message}");

            return created with
            {
                Warnings = [.. created.Warnings, $"Ящик не создан: {ex.Message} Учётная запись в домене заведена."],
            };
        }
    }

    /// <summary>Есть ли незавершённая почтовая часть последнего создания.</summary>
    public bool HasPendingMailbox => _pendingMailbox is not null;

    /// <summary>Повторяет только создание ящика. Возвращает ящик либо текст ошибки.</summary>
    public async Task<(MailAccount? Result, string? Error)> RetryMailboxAsync()
    {
        if (_pendingMailbox is not { } request)
        {
            return (null, "Нет незавершённого создания ящика.");
        }

        try
        {
            var mailbox = await _mail.CreateMailboxAsync(new NewMailboxRequest
            {
                Nickname = request.AccountName,
                FirstName = request.FirstName,
                LastName = request.LastName,
                MiddleName = request.MiddleName,
                Groups = request.MailGroups,
            });

            _pendingMailbox = null;
            await RefreshMailLoginsAsync();
            return (mailbox, null);
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
    }

    private static string? Empty(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
