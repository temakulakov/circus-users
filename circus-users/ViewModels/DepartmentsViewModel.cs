using System.Collections.ObjectModel;
using Circus.Users.App.Services;
using Circus.Users.Core.Abstractions;
using Circus.Users.Core.ActiveDirectory;
using Circus.Users.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Circus.Users.App.ViewModels;

/// <summary>Узел дерева подразделений.</summary>
public sealed partial class DepartmentNode : ObservableObject
{
    [ObservableProperty]
    private bool _isDepartment = true;

    public DepartmentNode(OrganizationalUnit unit)
    {
        Unit = unit;
        Children = [];
    }

    public OrganizationalUnit Unit { get; }

    public string Name => Unit.Name;

    public ObservableCollection<DepartmentNode> Children { get; }

    /// <summary>Пометка в дереве: не-отдел видно, не открывая его.</summary>
    public string Marker => IsDepartment ? string.Empty : "не отдел";

    public Visibility MarkerVisibility => IsDepartment ? Visibility.Collapsed : Visibility.Visible;

    partial void OnIsDepartmentChanged(bool value)
    {
        OnPropertyChanged(nameof(Marker));
        OnPropertyChanged(nameof(MarkerVisibility));
    }
}

/// <summary>Строка списка с галочкой: группа каталога или группа рассылки.</summary>
public sealed partial class SelectableOption : ObservableObject
{
    private readonly Action<SelectableOption>? _onSelectionChanged;

    [ObservableProperty]
    private bool _isSelected;

    public SelectableOption(string key, string name, string? note, bool isSelected, Action<SelectableOption>? onSelectionChanged)
    {
        Key = key;
        Name = name;
        Note = note ?? string.Empty;
        // Поле, а не свойство: присваивание в конструкторе не должно
        // выглядеть как выбор, сделанный человеком.
        _isSelected = isSelected;
        _onSelectionChanged = onSelectionChanged;
    }

    /// <summary>Чем группа записывается в шаблон.</summary>
    public string Key { get; }

    public string Name { get; }

    /// <summary>Подпись под именем: адрес рассылки либо пометка «нет в списке».</summary>
    public string Note { get; }

    public Visibility NoteVisibility => Note.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    partial void OnIsSelectedChanged(bool value) => _onSelectionChanged?.Invoke(this);
}

/// <summary>Сотрудник в подсказке выбора руководителя.</summary>
public sealed class ManagerOption
{
    public ManagerOption(EmployeeCard card)
    {
        AccountName = card.Get("sAMAccountName") ?? string.Empty;
        DisplayName = card.DisplayName;
        Title = card.Get("title") ?? string.Empty;
    }

    public ManagerOption(string accountName, string displayName, string title)
    {
        AccountName = accountName;
        DisplayName = displayName;
        Title = title;
    }

    public string AccountName { get; }

    public string DisplayName { get; }

    public string Title { get; }

    public string Note => string.IsNullOrEmpty(Title) ? AccountName : $"{Title} · {AccountName}";

    /// <summary>Что попадёт в поле после выбора: AutoSuggestBox показывает результат ToString.</summary>
    public override string ToString() =>
        string.IsNullOrEmpty(AccountName) ? DisplayName : $"{DisplayName} ({AccountName})";
}

/// <summary>
/// Настройка подразделений: дерево слева, шаблон нового сотрудника справа.
/// </summary>
internal sealed partial class DepartmentsViewModel : ObservableObject
{
    /// <summary>Сколько подсказок показывать при выборе руководителя.</summary>
    private const int MaxManagerSuggestions = 8;

    private readonly DirectoryCache _cache;
    private readonly IDepartmentTemplateStore _templates;
    private readonly IDepartmentRegistry _registry;
    private readonly IMailService _mail;
    private readonly IAuditLog _audit;
    private readonly SessionService _session;
    private readonly IPreferencesStore _preferences;
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();

    /// <summary>
    /// Выбранные группы держим отдельно от видимого списка: строка поиска его сокращает,
    /// и выбор, ушедший из виду, не должен пропасть при сохранении.
    /// </summary>
    private readonly HashSet<string> _selectedAdGroups = new(StringComparer.OrdinalIgnoreCase);

    private readonly HashSet<string> _selectedMailGroups = new(StringComparer.OrdinalIgnoreCase);

    private IReadOnlyList<MailGroup> _mailGroupCatalogue = [];
    private bool _mailGroupsRequested;

    /// <summary>Идёт заполнение формы из шаблона — отметки галочек не считаются правкой.</summary>
    private bool _isLoadingTemplate;

    [ObservableProperty]
    private DepartmentNode? _selectedNode;

    [ObservableProperty]
    private string _statusText = "Загрузка подразделений…";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _company = string.Empty;

    [ObservableProperty]
    private string _telephoneNumber = string.Empty;

    [ObservableProperty]
    private string _city = string.Empty;

    [ObservableProperty]
    private string _streetAddress = string.Empty;

    [ObservableProperty]
    private string _office = string.Empty;

    /// <summary>Учётная запись руководителя — то, что уходит в шаблон.</summary>
    [ObservableProperty]
    private string _managerAccount = string.Empty;

    /// <summary>Текст в поле поиска руководителя.</summary>
    [ObservableProperty]
    private string _managerQuery = string.Empty;

    [ObservableProperty]
    private string _groupFilter = string.Empty;

    [ObservableProperty]
    private string _mailGroupFilter = string.Empty;

    [ObservableProperty]
    private string _mailGroupsStatus = string.Empty;

    /// <summary>Считается ли выбранный каталог подразделением.</summary>
    [ObservableProperty]
    private bool _isDepartment = true;

    public DepartmentsViewModel(
        DirectoryCache cache,
        IDepartmentTemplateStore templates,
        IDepartmentRegistry registry,
        IMailService mail,
        IAuditLog audit,
        SessionService session,
        IPreferencesStore preferences)
    {
        _cache = cache;
        _templates = templates;
        _registry = registry;
        _mail = mail;
        _audit = audit;
        _session = session;
        _preferences = preferences;

        _cache.Changed += OnCacheChanged;
        _registry.Changed += OnRegistryChanged;
        Rebuild();
    }

    public ObservableCollection<DepartmentNode> Tree { get; } = [];

    public ObservableCollection<SelectableOption> Groups { get; } = [];

    public ObservableCollection<SelectableOption> MailGroups { get; } = [];

    public ObservableCollection<ManagerOption> ManagerSuggestions { get; } = [];

    /// <summary>Форма шаблона доступна только для каталогов, которые считаются отделами.</summary>
    public bool IsTemplateEnabled => IsDepartment;

    /// <summary>Куда уйдут изменения — то же предупреждение, что и в адресной книге.</summary>
    public string WriteTargetNotice => _preferences.Current.UseFakeData
        ? "Тестовый режим: шаблон сохранится только до закрытия приложения."
        : "Внимание: шаблон будет записан в Active Directory.";

    /// <summary>Общая папка недоступна — отметки «не отдел» не читаются и не сохранятся.</summary>
    public string RegistryErrorText => _registry.LastError is { } error
        ? $"Общие настройки недоступны: {error} Отметки подразделений не сохранятся, "
          + "а уже сделанные не видны — все каталоги временно считаются отделами."
        : string.Empty;

    public Visibility RegistryErrorVisibility =>
        string.IsNullOrEmpty(RegistryErrorText) ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>Пояснение под переключателем «Это подразделение».</summary>
    public string DepartmentSwitchNotice => IsDepartment
        ? "Каталог доступен для создания сотрудников, шаблон применяется к новым учётным записям."
        : "Каталог помечен как не подразделение: сотрудников в нём не заводят, шаблон недоступен, "
          + "служебный контакт в него не создаётся. Отметка хранится в общих настройках, каталог не меняется.";

    private void OnCacheChanged(object? sender, EventArgs e) => _dispatcher.TryEnqueue(Rebuild);

    private void OnRegistryChanged(object? sender, EventArgs e) => _dispatcher.TryEnqueue(() =>
    {
        ApplyRegistryToTree();
        OnPropertyChanged(nameof(RegistryErrorText));
        OnPropertyChanged(nameof(RegistryErrorVisibility));
    });

    private void Rebuild()
    {
        var previous = SelectedNode?.Unit.DistinguishedName;

        BuildTree();
        BuildGroups();
        BuildMailGroups();

        StatusText = _cache.Departments.Count == 0
            ? "Подразделения ещё не загружены"
            : $"Каталогов: {_cache.Departments.Count} · групп: {_cache.Groups.Count}";

        // Возвращаем выделение на то же подразделение после обновления справочника.
        if (previous is not null)
        {
            SelectedNode = FindNode(Tree, previous);
        }
    }

    private void BuildTree()
    {
        Tree.Clear();

        var nodes = _cache.Departments.ToDictionary(
            unit => unit.DistinguishedName,
            unit => new DepartmentNode(unit),
            StringComparer.OrdinalIgnoreCase);

        foreach (var node in nodes.Values.OrderBy(node => node.Name, StringComparer.CurrentCulture))
        {
            node.IsDepartment = _registry.IsDepartment(node.Unit.DistinguishedName);

            // Подразделение попадает в корень дерева, если его родителя нет в выборке.
            if (nodes.TryGetValue(node.Unit.ParentDistinguishedName, out var parent))
            {
                parent.Children.Add(node);
            }
            else
            {
                Tree.Add(node);
            }
        }
    }

    private void ApplyRegistryToTree() => ApplyRegistryToTree(Tree);

    private void ApplyRegistryToTree(IEnumerable<DepartmentNode> nodes)
    {
        foreach (var node in nodes)
        {
            node.IsDepartment = _registry.IsDepartment(node.Unit.DistinguishedName);
            ApplyRegistryToTree(node.Children);
        }
    }

    private void BuildGroups()
    {
        Groups.Clear();

        var query = _cache.Groups.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(GroupFilter))
        {
            var needle = GroupFilter.Trim();
            query = query.Where(group =>
                group.Name.Contains(needle, StringComparison.CurrentCultureIgnoreCase)
                || group.AccountName.Contains(needle, StringComparison.OrdinalIgnoreCase));
        }

        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in query.OrderBy(group => group.Name, StringComparer.CurrentCulture))
        {
            known.Add(group.AccountName);
            Groups.Add(new SelectableOption(
                group.AccountName,
                group.Name,
                null,
                _selectedAdGroups.Contains(group.AccountName),
                OnAdGroupToggled));
        }

        // Группа из шаблона, которой больше нет в каталоге, всё равно должна быть видна:
        // иначе её молча потеряют при первом же сохранении.
        AddMissingSelections(Groups, _selectedAdGroups, known, OnAdGroupToggled);
    }

    private void BuildMailGroups()
    {
        MailGroups.Clear();

        var query = _mailGroupCatalogue.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(MailGroupFilter))
        {
            var needle = MailGroupFilter.Trim();
            query = query.Where(group =>
                group.Name.Contains(needle, StringComparison.CurrentCultureIgnoreCase)
                || group.Email.Contains(needle, StringComparison.OrdinalIgnoreCase));
        }

        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in query.OrderBy(group => group.Name, StringComparer.CurrentCulture))
        {
            var key = string.IsNullOrWhiteSpace(group.Email) ? group.Name : group.Email;
            known.Add(key);
            MailGroups.Add(new SelectableOption(
                key,
                group.Name,
                group.Email,
                _selectedMailGroups.Contains(key),
                OnMailGroupToggled));
        }

        AddMissingSelections(MailGroups, _selectedMailGroups, known, OnMailGroupToggled);
    }

    /// <summary>Дописывает выбранные значения, которых нет в текущем списке.</summary>
    private static void AddMissingSelections(
        ObservableCollection<SelectableOption> target,
        IEnumerable<string> selected,
        HashSet<string> known,
        Action<SelectableOption> onToggled)
    {
        foreach (var key in selected.Where(key => !known.Contains(key)).OrderBy(key => key, StringComparer.CurrentCulture))
        {
            target.Add(new SelectableOption(key, key, "нет в списке", true, onToggled));
        }
    }

    private void OnAdGroupToggled(SelectableOption option) => Toggle(_selectedAdGroups, option);

    private void OnMailGroupToggled(SelectableOption option) => Toggle(_selectedMailGroups, option);

    private void Toggle(HashSet<string> selection, SelectableOption option)
    {
        if (_isLoadingTemplate)
        {
            return;
        }

        if (option.IsSelected)
        {
            selection.Add(option.Key);
        }
        else
        {
            selection.Remove(option.Key);
        }
    }

    private static DepartmentNode? FindNode(IEnumerable<DepartmentNode> nodes, string distinguishedName)
    {
        foreach (var node in nodes)
        {
            if (string.Equals(node.Unit.DistinguishedName, distinguishedName, StringComparison.OrdinalIgnoreCase))
            {
                return node;
            }

            if (FindNode(node.Children, distinguishedName) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    partial void OnGroupFilterChanged(string value) => BuildGroups();

    partial void OnMailGroupFilterChanged(string value) => BuildMailGroups();

    partial void OnIsDepartmentChanged(bool value)
    {
        OnPropertyChanged(nameof(IsTemplateEnabled));
        OnPropertyChanged(nameof(DepartmentSwitchNotice));
    }

    partial void OnSelectedNodeChanged(DepartmentNode? value) => _ = LoadTemplateAsync(value);

    /// <summary>Подсказки для поля «Руководитель».</summary>
    public void UpdateManagerSuggestions(string query)
    {
        ManagerSuggestions.Clear();

        var needle = query.Trim();
        if (needle.Length < 2)
        {
            return;
        }

        var lowered = needle.ToLowerInvariant();

        foreach (var card in _cache.Employees
                     .Where(card => card.SearchText.Contains(lowered, StringComparison.Ordinal))
                     .OrderBy(card => card.DisplayName, StringComparer.CurrentCulture)
                     .Take(MaxManagerSuggestions))
        {
            ManagerSuggestions.Add(new ManagerOption(card));
        }
    }

    /// <summary>Запоминает выбранного руководителя.</summary>
    public void PickManager(ManagerOption option)
    {
        ManagerAccount = option.AccountName;
        ManagerQuery = option.ToString();
    }

    /// <summary>Снимает руководителя, если поле очистили руками.</summary>
    public void ClearManagerIfEmpty()
    {
        if (string.IsNullOrWhiteSpace(ManagerQuery))
        {
            ManagerAccount = string.Empty;
        }
    }

    private async Task LoadTemplateAsync(DepartmentNode? node)
    {
        if (node is null)
        {
            ClearForm();
            return;
        }

        IsBusy = true;
        try
        {
            IsDepartment = _registry.IsDepartment(node.Unit.DistinguishedName);

            await EnsureMailGroupsAsync();

            var template = IsDepartment
                ? await _templates.LoadAsync(node.Unit.DistinguishedName)
                : new DepartmentTemplate();

            _isLoadingTemplate = true;
            try
            {
                Company = template.Company ?? string.Empty;
                TelephoneNumber = template.TelephoneNumber ?? string.Empty;
                City = template.City ?? string.Empty;
                StreetAddress = template.StreetAddress ?? string.Empty;
                Office = template.Office ?? string.Empty;
                SetManager(template.ManagerAccount);

                _selectedAdGroups.Clear();
                foreach (var group in template.AdGroups)
                {
                    _selectedAdGroups.Add(group);
                }

                _selectedMailGroups.Clear();
                foreach (var group in template.MailGroups)
                {
                    _selectedMailGroups.Add(group);
                }
            }
            finally
            {
                _isLoadingTemplate = false;
            }

            BuildGroups();
            BuildMailGroups();
        }
        catch (Exception ex)
        {
            StatusText = $"Не удалось прочитать шаблон: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Показывает руководителя человеку: по учётной записи ищем ФИО в справочнике.</summary>
    private void SetManager(string? accountName)
    {
        ManagerAccount = accountName ?? string.Empty;

        if (string.IsNullOrWhiteSpace(accountName))
        {
            ManagerQuery = string.Empty;
            return;
        }

        var card = _cache.Employees.FirstOrDefault(employee =>
            string.Equals(employee.Get("sAMAccountName"), accountName, StringComparison.OrdinalIgnoreCase));

        ManagerQuery = card is null
            ? accountName
            : new ManagerOption(card).ToString();
    }

    /// <summary>Забирает группы рассылки один раз за сеанс: список меняется редко.</summary>
    private async Task EnsureMailGroupsAsync()
    {
        if (_mailGroupsRequested)
        {
            return;
        }

        _mailGroupsRequested = true;

        if (!_mail.IsConfigured)
        {
            MailGroupsStatus = "Почта не настроена — список рассылок недоступен. Укажите токен в настройках.";
            return;
        }

        try
        {
            _mailGroupCatalogue = await _mail.GetGroupsAsync();
            MailGroupsStatus = _mailGroupCatalogue.Count == 0
                ? "В почтовой организации нет групп рассылки."
                : string.Empty;
        }
        catch (Exception ex)
        {
            // Недоступная почта не должна мешать править остальной шаблон.
            _mailGroupCatalogue = [];
            MailGroupsStatus = $"Не удалось получить группы рассылки: {ex.Message}";
        }
    }

    private void ClearForm()
    {
        _isLoadingTemplate = true;
        try
        {
            Company = string.Empty;
            TelephoneNumber = string.Empty;
            City = string.Empty;
            StreetAddress = string.Empty;
            Office = string.Empty;
            ManagerAccount = string.Empty;
            ManagerQuery = string.Empty;

            _selectedAdGroups.Clear();
            _selectedMailGroups.Clear();
        }
        finally
        {
            _isLoadingTemplate = false;
        }

        BuildGroups();
        BuildMailGroups();
    }

    /// <summary>Сохраняет шаблон. Возвращает текст ошибки или null при успехе.</summary>
    public async Task<string?> SaveTemplateAsync()
    {
        if (SelectedNode is not { } node)
        {
            return "Подразделение не выбрано.";
        }

        if (!IsDepartment)
        {
            // Служебный контакт создаётся при сохранении, а в не-отделе объектов быть не должно.
            return "Каталог помечен как не подразделение — шаблон в нём не хранится.";
        }

        var template = new DepartmentTemplate
        {
            Company = Trim(Company),
            TelephoneNumber = Trim(TelephoneNumber),
            City = Trim(City),
            StreetAddress = Trim(StreetAddress),
            Office = Trim(Office),
            ManagerAccount = Trim(ManagerAccount),
            AdGroups = [.. _selectedAdGroups],
            MailGroups = [.. _selectedMailGroups],
        };

        IsBusy = true;
        try
        {
            await _templates.SaveAsync(node.Unit.DistinguishedName, template);
            await _audit.WriteAsync(
                _session.Current.AccountName,
                "Шаблон подразделения",
                node.Name,
                $"групп AD: {template.AdGroups.Count}, групп рассылки: {template.MailGroups.Count}");
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
        finally
        {
            IsBusy = false;
        }

        StatusText = $"Шаблон подразделения «{node.Name}» сохранён";
        return null;
    }

    /// <summary>
    /// Меняет признак «это подразделение». Возвращает текст ошибки или null при успехе.
    /// </summary>
    public async Task<string?> SetIsDepartmentAsync(bool isDepartment)
    {
        if (SelectedNode is not { } node)
        {
            return "Подразделение не выбрано.";
        }

        IsBusy = true;
        try
        {
            await _registry.SetIsDepartmentAsync(
                node.Unit.DistinguishedName,
                node.Name,
                isDepartment,
                _session.Current.AccountName);

            await _audit.WriteAsync(
                _session.Current.AccountName,
                isDepartment ? "Каталог отмечен подразделением" : "Каталог исключён из подразделений",
                node.Name,
                node.Unit.DistinguishedName);
        }
        catch (Exception ex)
        {
            // Возвращаем переключатель в прежнее положение: отметка не сохранилась.
            IsDepartment = !isDepartment;
            return ex.Message;
        }
        finally
        {
            IsBusy = false;
        }

        IsDepartment = isDepartment;

        if (isDepartment)
        {
            await LoadTemplateAsync(node);
        }
        else
        {
            ClearForm();
        }

        StatusText = isDepartment
            ? $"«{node.Name}» снова считается подразделением"
            : $"«{node.Name}» больше не считается подразделением";

        return null;
    }

    [RelayCommand]
    private void ClearTemplate() => ClearForm();

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await _registry.LoadAsync();
        await _cache.RefreshAsync();
    }

    private static string? Trim(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
