using System.Collections.ObjectModel;
using Circus.Users.Core.Abstractions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;

namespace Circus.Users.App.ViewModels;

/// <summary>Строка журнала в том виде, в каком её показывают.</summary>
public sealed class JournalRow
{
    public JournalRow(AuditEntry entry)
    {
        Entry = entry;

        When = entry.When is { } when ? when.ToString("dd.MM.yyyy HH:mm") : string.Empty;
        Actor = entry.Actor;
        Action = string.IsNullOrEmpty(entry.Action) ? "Запись журнала" : entry.Action;
        Target = entry.Target;
        Details = entry.Details;

        // Строка для поиска считается один раз: перебирать поля на каждое нажатие клавиши дорого.
        SearchText = string.Join(' ', When, Actor, Action, Target, Details).ToLowerInvariant();
    }

    public AuditEntry Entry { get; }

    public string When { get; }

    public string Actor { get; }

    public string Action { get; }

    public string Target { get; }

    public string Details { get; }

    public string SearchText { get; }

    public Visibility DetailsVisibility =>
        string.IsNullOrWhiteSpace(Details) ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>Одной строкой — для чтения с экрана и для копирования.</summary>
    public string Summary =>
        string.Join(", ", new[] { When, Actor, Action, Target, Details }.Where(part => part.Length > 0));
}

/// <summary>
/// Журнал операций: что приложение меняло в каталоге и в почте.
/// </summary>
/// <remarks>
/// Журнал только читается. Записи копятся в файле на общей шаре, поэтому здесь
/// показывается лишь хвост: администратору нужны последние события, а не вся история.
/// </remarks>
internal sealed partial class JournalViewModel : ObservableObject
{
    /// <summary>Сколько последних записей забирать из файла.</summary>
    private const int MaxEntries = 2000;

    private readonly IAuditLog _audit;

    private IReadOnlyList<JournalRow> _all = [];

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _statusText = "Журнал ещё не прочитан";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isEmpty = true;

    [ObservableProperty]
    private string _emptyText = string.Empty;

    [ObservableProperty]
    private string _errorText = string.Empty;

    public JournalViewModel(IAuditLog audit) => _audit = audit;

    public ObservableCollection<JournalRow> Rows { get; } = [];

    public Visibility EmptyVisibility => IsEmpty && !IsBusy ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ErrorVisibility =>
        string.IsNullOrEmpty(ErrorText) ? Visibility.Collapsed : Visibility.Visible;

    [RelayCommand]
    public async Task RefreshAsync()
    {
        IsBusy = true;
        ErrorText = string.Empty;

        try
        {
            var result = await _audit.ReadAsync(MaxEntries);

            _all = [.. result.Entries.Select(entry => new JournalRow(entry))];

            if (result.Error is { } error)
            {
                ErrorText = $"Журнал недоступен: {error}";
                StatusText = "Не удалось прочитать журнал";
            }
            else if (result.Source is null)
            {
                StatusText = "Журнал ещё не создан — записи появятся после первого изменения";
            }
            else
            {
                StatusText = _all.Count >= MaxEntries
                    ? $"Показаны последние {MaxEntries} записей · {result.Source}"
                    : $"Записей: {_all.Count} · {result.Source}";
            }
        }
        catch (Exception ex)
        {
            _all = [];
            ErrorText = $"Журнал недоступен: {ex.Message}";
            StatusText = "Не удалось прочитать журнал";
        }
        finally
        {
            IsBusy = false;
        }

        Rebuild();
    }

    private void Rebuild()
    {
        var query = _all.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var needle = SearchText.Trim().ToLowerInvariant();
            query = query.Where(row => row.SearchText.Contains(needle, StringComparison.Ordinal));
        }

        Rows.Clear();
        foreach (var row in query)
        {
            Rows.Add(row);
        }

        IsEmpty = Rows.Count == 0;
        EmptyText = _all.Count == 0
            ? "Записей пока нет"
            : $"Ничего не найдено по запросу «{SearchText.Trim()}»";
    }

    partial void OnSearchTextChanged(string value) => Rebuild();

    partial void OnIsEmptyChanged(bool value) => OnPropertyChanged(nameof(EmptyVisibility));

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(EmptyVisibility));

    partial void OnErrorTextChanged(string value) => OnPropertyChanged(nameof(ErrorVisibility));
}
