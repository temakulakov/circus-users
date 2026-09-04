using System.Collections.ObjectModel;
using System.Text;
using Circus.Users.App.Services;
using Circus.Users.Core.Abstractions;
using Circus.Users.Core.ActiveDirectory;
using Circus.Users.Core.Models;
using Circus.Users.Core.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Extensions.Options;

namespace Circus.Users.App.ViewModels;

/// <summary>Колонка таблицы. Ширина общая для заголовка и всех строк.</summary>
public sealed class TableColumn
{
    public TableColumn(DirectoryField field)
    {
        Attribute = field.Attribute;
        Title = field.Title;
        Width = field.Width;
    }

    public string Attribute { get; }

    public string Title { get; }

    public double Width { get; }
}

/// <summary>Значение в одной ячейке. Ширину берёт у своей колонки.</summary>
public sealed record TableCell(TableColumn Column, string Value);

/// <summary>Строка таблицы: сотрудник и его ячейки в порядке колонок.</summary>
public sealed class EmployeeRow
{
    public EmployeeRow(EmployeeCard employee, IReadOnlyList<TableColumn> columns, bool showActions)
    {
        Employee = employee;
        Cells = columns.Select(column => new TableCell(column, employee.Get(column.Attribute) ?? string.Empty)).ToList();
        ActionsWidth = showActions ? AddressBookViewModel.ActionsColumnWidth : 0;
    }

    public EmployeeCard Employee { get; }

    public IReadOnlyList<TableCell> Cells { get; }

    /// <summary>Место под карандаш. У обычного сотрудника колонки действий нет.</summary>
    public double ActionsWidth { get; }

    /// <summary>Карандаш есть только у администратора.</summary>
    public Visibility ActionsVisibility => ActionsWidth > 0 ? Visibility.Visible : Visibility.Collapsed;
}

/// <summary>Сотрудники одного подразделения — заголовок группы в таблице.</summary>
public sealed class EmployeeRowGroup : List<EmployeeRow>
{
    public EmployeeRowGroup(string key, IEnumerable<EmployeeRow> rows) : base(rows) => Key = key;

    public string Key { get; }
}

/// <summary>Поле в настройке вида: показывать или нет, порядок задаётся перетаскиванием.</summary>
public sealed partial class FieldOption : ObservableObject
{
    [ObservableProperty]
    private bool _isVisible;

    public FieldOption(DirectoryField field, bool isVisible)
    {
        Attribute = field.Attribute;
        Title = field.Title;
        IsVisible = isVisible;
    }

    public string Attribute { get; }

    public string Title { get; }
}

/// <summary>Изменяемый атрибут в диалоге правки сотрудника.</summary>
public sealed partial class EditableField : ObservableObject
{
    [ObservableProperty]
    private string _value;

    public EditableField(string attribute, string title, string? value)
    {
        Attribute = attribute;
        Title = title;
        _value = value ?? string.Empty;
        OriginalValue = _value;
    }

    public string Attribute { get; }

    public string Title { get; }

    public string OriginalValue { get; }

    public bool IsChanged => !string.Equals(Value, OriginalValue, StringComparison.Ordinal);
}

/// <summary>Адресная книга: таблица сотрудников с настраиваемыми колонками и правкой для администратора.</summary>
internal sealed partial class AddressBookViewModel : ObservableObject
{
    /// <summary>Ширина колонки с карандашом.</summary>
    internal const double ActionsColumnWidth = 44;

    private readonly DirectoryCache _cache;
    private readonly IPreferencesStore _store;
    private readonly ISharedLayoutStore _sharedLayout;
    private readonly IDirectoryWriter _writer;
    private readonly IAuditLog _audit;
    private readonly SessionService _session;
    private readonly ActiveDirectorySettings _settings;
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private EmployeeRow? _selectedRow;

    [ObservableProperty]
    private bool _isRefreshing;

    [ObservableProperty]
    private string _statusText = "Загрузка справочника…";

    [ObservableProperty]
    private bool _isEmpty;

    /// <summary>Что написать вместо таблицы, когда показывать нечего.</summary>
    [ObservableProperty]
    private string _emptyText = string.Empty;

    /// <summary>Связь с контроллером домена или шарой потеряна.</summary>
    [ObservableProperty]
    private string _errorText = string.Empty;

    [ObservableProperty]
    private double _tableWidth;

    [ObservableProperty]
    private double _actionsWidth;

    public AddressBookViewModel(
        DirectoryCache cache,
        IPreferencesStore store,
        ISharedLayoutStore sharedLayout,
        IDirectoryWriter writer,
        IAuditLog audit,
        SessionService session,
        IOptions<AppSettings> settings)
    {
        _cache = cache;
        _store = store;
        _sharedLayout = sharedLayout;
        _writer = writer;
        _audit = audit;
        _session = session;
        _settings = settings.Value.ActiveDirectory;

        _cache.Changed += OnCacheChanged;

        RebuildFieldOptions();
        RebuildColumns();
        Rebuild();
    }

    public ObservableCollection<TableColumn> Columns { get; } = [];

    public ObservableCollection<EmployeeRowGroup> Groups { get; } = [];

    public ObservableCollection<FieldOption> FieldOptions { get; } = [];

    public ObservableCollection<EditableField> EditFields { get; } = [];

    public bool IsAdmin => _session.Current.IsAdmin;

    public Visibility AdminVisibility => IsAdmin ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Полоса о потере связи показывается, только когда есть о чём сказать.</summary>
    public Visibility ErrorVisibility =>
        string.IsNullOrEmpty(ErrorText) ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>Сотрудник, которого правят в открытом диалоге.</summary>
    public EmployeeCard? EditTarget { get; private set; }

    public string EditTitle => EditTarget?.DisplayName ?? string.Empty;

    /// <summary>
    /// Куда уйдут изменения. Показывается в каждом диалоге правки и подтверждения:
    /// перепутать тестовый режим с боевым каталогом слишком легко и слишком дорого.
    /// </summary>
    public string WriteTargetNotice => _store.Current.UseFakeData
        ? "Тестовый режим: изменения никуда не сохранятся."
        : "Внимание: изменения будут записаны в Active Directory.";

    public bool IsWritingToLiveDirectory => !_store.Current.UseFakeData;

    /// <summary>Пересобирает таблицу: событие кэша приходит из фонового потока.</summary>
    private void OnCacheChanged(object? sender, EventArgs e) => _dispatcher.TryEnqueue(Rebuild);

    private void RebuildColumns()
    {
        Columns.Clear();

        foreach (var attribute in CurrentLayout())
        {
            if (DirectoryFields.Find(attribute) is { } field)
            {
                Columns.Add(new TableColumn(field));
            }
        }

        ActionsWidth = IsAdmin ? ActionsColumnWidth : 0;

        // Ширина шапки должна совпадать с суммой колонок, иначе она разъедется со строками
        // при горизонтальной прокрутке. 24 px — отступы строки списка.
        TableWidth = Columns.Sum(column => column.Width) + ActionsWidth + 24;
    }

    private void Rebuild()
    {
        IsRefreshing = _cache.IsRefreshing;
        StatusText = BuildStatus();

        var query = _cache.Employees.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var needle = SearchText.Trim().ToLowerInvariant();
            query = query.Where(employee => employee.SearchText.Contains(needle, StringComparison.Ordinal));
        }

        var columns = Columns.ToList();
        var showActions = IsAdmin;

        var groups = query
            .GroupBy(employee => employee.Department)
            .OrderBy(group => group.Key, StringComparer.CurrentCulture)
            .Select(group => new EmployeeRowGroup(
                group.Key,
                group
                    .OrderBy(employee => employee.DisplayName, StringComparer.CurrentCulture)
                    .Select(employee => new EmployeeRow(employee, columns, showActions))))
            .ToList();

        Groups.Clear();
        foreach (var group in groups)
        {
            Groups.Add(group);
        }

        IsEmpty = groups.Count == 0;
        EmptyText = BuildEmptyText();
        ErrorText = BuildErrorText();
        SelectedRow = null;
    }

    /// <summary>
    /// Как сотрудник называется в журнале: ФИО и учётная запись.
    /// Полных тёзок в каталоге хватает, и по одному ФИО потом не разобраться.
    /// </summary>
    private static string Describe(EmployeeCard employee) =>
        employee.Get("sAMAccountName") is { } account
            ? $"{employee.DisplayName} ({account})"
            : employee.DisplayName;

    /// <summary>Имя файла по умолчанию для выгрузки.</summary>

    public string SuggestedExportName =>
        $"Адресная книга {DateTimeOffset.Now:yyyy-MM-dd}";

    /// <summary>Сколько строк уйдёт в выгрузку — то же, что видно в таблице.</summary>
    public int VisibleCount => Groups.Sum(group => group.Count);

    /// <summary>
    /// Собирает таблицу в CSV: те же колонки и та же фильтрация, что на экране.
    /// </summary>
    /// <remarks>
    /// Разделитель — точка с запятой, а файл пишется с меткой порядка байтов:
    /// в русской локали Excel открывает такой файл сразу, без мастера импорта.
    /// </remarks>
    public string BuildCsv()
    {
        var builder = new StringBuilder();
        var columns = Columns.ToList();

        builder.Append(Escape("Подразделение"));
        foreach (var column in columns)
        {
            builder.Append(';').Append(Escape(column.Title));
        }

        builder.AppendLine();

        foreach (var group in Groups)
        {
            foreach (var row in group)
            {
                builder.Append(Escape(group.Key));

                foreach (var cell in row.Cells)
                {
                    builder.Append(';').Append(Escape(cell.Value));
                }

                builder.AppendLine();
            }
        }

        return builder.ToString();
    }

    /// <summary>Экранирует значение по правилам CSV.</summary>
    private static string Escape(string? value)
    {
        var text = value ?? string.Empty;

        // Кавычки, разделители и переносы строк должны пережить выгрузку без потерь.
        if (!text.Any(symbol => symbol is '"' or ';' or '\n' or '\r'))
        {
            return text;
        }

        return $"\"{text.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    /// <summary>Почему таблица пуста: не нашлось по запросу или справочник ещё не приехал.</summary>
    private string BuildEmptyText()

    {
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            return $"Ничего не найдено по запросу «{SearchText.Trim()}»";
        }

        if (_cache.Employees.Count == 0)
        {
            return _cache.IsRefreshing
                ? "Справочник загружается…"
                : "Справочник пуст. Проверьте связь с контроллером домена и настройки поиска.";
        }

        return "Никого не нашлось";
    }

    /// <summary>
    /// Потеря связи не должна выглядеть как пустой справочник: если данные всё же есть,
    /// показываем их и предупреждаем, что они устарели.
    /// </summary>
    private string BuildErrorText()
    {
        if (_cache.LastError is not { } error)
        {
            return string.Empty;
        }

        return _cache.LastUpdated is { } updated
            ? $"Справочник не обновляется: {error} Показаны данные на {updated:HH:mm}."
            : $"Не удалось прочитать справочник: {error}";
    }

    private string BuildStatus()
    {
        if (_cache.IsRefreshing)
        {
            return "Обновление справочника…";
        }

        if (_cache.LastError is { } error)
        {
            return $"Не удалось обновить справочник: {error}";
        }

        if (_cache.LastUpdated is not { } updated)
        {
            return "Справочник ещё не загружен";
        }

        var departments = _cache.Employees.Select(employee => employee.Department).Distinct().Count();
        return $"Сотрудников: {_cache.Employees.Count} · подразделений: {departments} · обновлено в {updated:HH:mm}";
    }

    partial void OnSearchTextChanged(string value) => Rebuild();

    partial void OnErrorTextChanged(string value) => OnPropertyChanged(nameof(ErrorVisibility));

    /// <summary>
    /// Какой вид применить: сначала личная настройка, затем общая от администратора,
    /// затем встроенная по умолчанию.
    /// </summary>
    private IReadOnlyList<string> CurrentLayout()
    {
        var personal = _store.Current.AddressBookFields;
        if (personal.Count > 0)
        {
            return personal;
        }

        return _sharedLayout.Layout is { Count: > 0 } shared ? shared : DirectoryFields.DefaultLayout;
    }

    private void RebuildFieldOptions()
    {
        var layout = CurrentLayout();

        FieldOptions.Clear();

        // Сначала показываемые поля в заданном порядке, затем остальные — выключенными.
        foreach (var attribute in layout)
        {
            if (DirectoryFields.Find(attribute) is { } field)
            {
                FieldOptions.Add(new FieldOption(field, isVisible: true));
            }
        }

        foreach (var field in DirectoryFields.All.Where(field => !layout.Contains(field.Attribute)))
        {
            FieldOptions.Add(new FieldOption(field, isVisible: false));
        }
    }

    private List<string> SelectedLayout() => FieldOptions
        .Where(option => option.IsVisible)
        .Select(option => option.Attribute)
        .ToList();

    [RelayCommand]
    private async Task RefreshAsync() => await _cache.RefreshAsync();

    /// <summary>Сохраняет набор колонок для себя.</summary>
    [RelayCommand]
    private void ApplyFields()
    {
        _store.Current.AddressBookFields = SelectedLayout();
        _store.Save();

        RebuildColumns();
        Rebuild();
    }

    [RelayCommand]
    private void ResetFields()
    {
        _store.Current.AddressBookFields = [];
        _store.Save();

        RebuildFieldOptions();
        RebuildColumns();
        Rebuild();
    }

    /// <summary>
    /// Сохраняет набор колонок как общий. Личные настройки других сотрудников не трогаются:
    /// общий вид действует только на тех, кто не настраивал книгу под себя.
    /// </summary>
    public async Task<string?> ApplyFieldsForEveryoneAsync()
    {
        if (!IsAdmin)
        {
            return "Недостаточно прав.";
        }

        var layout = SelectedLayout();

        try
        {
            await _sharedLayout.SaveAsync(layout);
            await _audit.WriteAsync(
                _session.Current.AccountName,
                "Общий вид адресной книги",
                "все сотрудники",
                string.Join(", ", layout));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return ex.Message;
        }

        // Сам администратор сразу видит то, что применил.
        _store.Current.AddressBookFields = [];
        _store.Save();

        RebuildColumns();
        Rebuild();
        return null;
    }

    /// <summary>Готовит диалог правки для выбранного сотрудника.</summary>
    public void PrepareEdit(EmployeeCard employee)
    {
        EditTarget = employee;
        OnPropertyChanged(nameof(WriteTargetNotice));
        OnPropertyChanged(nameof(EditTitle));

        EditFields.Clear();
        foreach (var attribute in _settings.EditableAttributes)
        {
            EditFields.Add(new EditableField(attribute, DirectoryFields.TitleOf(attribute), employee.Get(attribute)));
        }
    }

    /// <summary>Записывает изменённые атрибуты. Возвращает текст ошибки или null при успехе.</summary>
    public async Task<string?> SaveEditAsync()
    {
        if (EditTarget is not { } employee)
        {
            return "Сотрудник не выбран.";
        }

        var changes = EditFields
            .Where(field => field.IsChanged)
            .ToDictionary(field => field.Attribute, field => (string?)field.Value.Trim());

        if (changes.Count == 0)
        {
            return null;
        }

        try
        {
            await _writer.UpdateAttributesAsync(employee.DistinguishedName, changes);
            await _audit.WriteAsync(
                _session.Current.AccountName,
                "Правка атрибутов",
                employee.DisplayName,
                string.Join("; ", EditFields.Where(f => f.IsChanged).Select(f => $"{f.Attribute}: '{f.OriginalValue}' -> '{f.Value}'")));
        }
        catch (Exception ex)
        {
            return ex.Message;
        }

        await _cache.RefreshAsync();
        return null;
    }

    /// <summary>Отключает или включает учётную запись.</summary>
    public async Task<string?> SetBlockedAsync(EmployeeCard employee, bool isBlocked)
    {
        try
        {
            await _writer.SetEnabledAsync(employee.DistinguishedName, !isBlocked);
            await _audit.WriteAsync(
                _session.Current.AccountName,
                isBlocked ? "Блокировка учётной записи" : "Разблокировка учётной записи",
                Describe(employee),
                employee.DistinguishedName);
        }
        catch (Exception ex)
        {
            return ex.Message;
        }

        await _cache.RefreshAsync();
        return null;
    }

    /// <summary>Увольнение: отключение учётной записи и снятие групп.</summary>
    public async Task<string?> DismissAsync(EmployeeCard employee)
    {
        try
        {
            var removed = await _writer.DismissAsync(employee.DistinguishedName);

            // Снятые группы записываем полностью: только по ним увольнение можно откатить.
            // Учётная запись обязательна — по одному ФИО тёзок в каталоге не различить.
            var groups = removed.Count > 0
                ? $"сняты группы: {string.Join("; ", removed)}"
                : "групп не было";

            await _audit.WriteAsync(
                _session.Current.AccountName,
                "Увольнение",
                Describe(employee),
                $"{groups}; {employee.DistinguishedName}");
        }
        catch (Exception ex)
        {
            return ex.Message;
        }

        await _cache.RefreshAsync();
        return null;
    }
}
