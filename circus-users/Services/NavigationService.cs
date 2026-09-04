using Circus.Users.App.Views;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace Circus.Users.App.Services;

/// <summary>Навигация по страницам оболочки. Страницы адресуются строковым тегом из NavigationView.</summary>
internal sealed class NavigationService
{
    /// <summary>Теги страниц. Совпадают со значениями Tag у элементов NavigationView.</summary>
    public static class Pages
    {
        public const string AddUser = "add-user";
        public const string AddressBook = "address-book";
        public const string Departments = "departments";
        public const string Journal = "journal";
        public const string Settings = "settings";
    }

    private static readonly Dictionary<string, Type> PageTypes = new(StringComparer.Ordinal)
    {
        [Pages.AddUser] = typeof(AddUserPage),
        [Pages.AddressBook] = typeof(AddressBookPage),
        [Pages.Departments] = typeof(DepartmentsPage),
        [Pages.Journal] = typeof(JournalPage),
        [Pages.Settings] = typeof(SettingsPage),
    };

    private Frame? _frame;

    public string? CurrentTag { get; private set; }

    public void Initialize(Frame frame) => _frame = frame;

    /// <summary>Переходит на страницу по тегу. Повторный переход на текущую страницу игнорируется.</summary>
    public bool NavigateTo(string tag)
    {
        if (_frame is null || !PageTypes.TryGetValue(tag, out var pageType))
        {
            return false;
        }

        if (CurrentTag == tag)
        {
            return false;
        }

        var navigated = _frame.Navigate(pageType, null, new DrillInNavigationTransitionInfo());
        if (navigated)
        {
            CurrentTag = tag;
        }

        return navigated;
    }
}
