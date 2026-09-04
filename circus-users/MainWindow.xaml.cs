using Circus.Users.App.Messages;
using Circus.Users.App.Services;
using Circus.Users.Core.Abstractions;
using Circus.Users.Core.Settings;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;

namespace Circus.Users.App;

/// <summary>Главное окно: заголовок, панель навигации и область страниц.</summary>
public sealed partial class MainWindow : Window
{
    private const double MinPaneWidth = 150;
    private const double MaxPaneWidth = 460;

    private readonly IPreferencesStore _preferences;
    private readonly NavigationService _navigation;
    private readonly SessionService _session;

    internal MainWindow(
        IPreferencesStore preferences,
        ThemeService theme,
        BackdropService backdrop,
        NavigationService navigation,
        SoundService sound,
        SessionService session)
    {
        _preferences = preferences;
        _navigation = navigation;
        _session = session;

        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        theme.AttachRoot(RootGrid);
        backdrop.Attach(this, SolidBackground);
        sound.Apply();
        _navigation.Initialize(ContentFrame);

        ApplyNavigationLayout();
        ApplySession();

        var messenger = WeakReferenceMessenger.Default;
        messenger.Register<NavigationLayoutChangedMessage>(this, (_, _) => ApplyNavigationLayout());
        messenger.Register<BackdropChangedMessage>(this, (_, _) => backdrop.Apply());
        messenger.Register<SessionChangedMessage>(this, (_, _) => ApplySession());
    }

    /// <summary>Показывает вошедшего пользователя и оставляет только доступные ему страницы.</summary>
    private void ApplySession()
    {
        var user = _session.Current;

        CurrentUserText.Text = user.IsFake ? $"{user.DisplayName} (тестовые данные)" : user.DisplayName;
        RoleText.Text = user.RoleTitle;

        var adminOnly = user.IsAdmin ? Visibility.Visible : Visibility.Collapsed;
        AddUserItem.Visibility = adminOnly;
        DepartmentsItem.Visibility = adminOnly;
        JournalItem.Visibility = adminOnly;

        // Если открытая страница только что стала недоступна, уводим на общедоступную.
        var selected = NavView.SelectedItem as NavigationViewItem;
        if (selected is null || selected.Visibility == Visibility.Collapsed)
        {
            NavView.SelectedItem = AddressBookItem;
        }
    }

    /// <summary>Приводит панель навигации в соответствие с настройками пользователя.</summary>
    private void ApplyNavigationLayout()
    {
        var preferences = _preferences.Current;
        var isAutoHidden = preferences.AutoHideNavigationPane;

        NavView.PaneDisplayMode = preferences.NavigationPosition switch
        {
            // LeftMinimal прячет панель за кнопку-гамбургер и раскрывает её поверх
            // содержимого со штатной анимацией Fluent.
            NavigationPosition.Left or NavigationPosition.LeftCompact when isAutoHidden =>
                NavigationViewPaneDisplayMode.LeftMinimal,
            NavigationPosition.Left => NavigationViewPaneDisplayMode.Left,
            NavigationPosition.LeftCompact => NavigationViewPaneDisplayMode.LeftCompact,
            NavigationPosition.Top => NavigationViewPaneDisplayMode.Top,
            _ => NavigationViewPaneDisplayMode.Left,
        };

        var width = preferences.NavigationPaneWidth > 0
            ? Math.Clamp(preferences.NavigationPaneWidth, MinPaneWidth, MaxPaneWidth)
            : MeasureAutoPaneWidth();

        NavView.OpenPaneLength = width;

        // Тянуть за край имеет смысл только у постоянно раскрытой боковой панели.
        var isResizable = preferences.NavigationPosition == NavigationPosition.Left && !isAutoHidden;
        PaneGrip.Visibility = isResizable ? Visibility.Visible : Visibility.Collapsed;
        PaneGrip.Margin = new Thickness(width - PaneGrip.Width / 2, 0, 0, 0);
    }

    /// <summary>Подбирает ширину панели по самой длинной подписи пункта меню.</summary>
    private double MeasureAutoPaneWidth()
    {
        var widest = 0.0;

        foreach (var item in NavView.MenuItems.Concat(NavView.FooterMenuItems).OfType<NavigationViewItem>())
        {
            if (item.Content is not string text)
            {
                continue;
            }

            var probe = new TextBlock { Text = text, FontSize = 14 };
            probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            widest = Math.Max(widest, probe.DesiredSize.Width);
        }

        // 48 px — колонка значка, ещё 44 px — отступы пункта и запас на округление шрифта.
        return Math.Clamp(widest + 92, MinPaneWidth, MaxPaneWidth);
    }

    private void SetPaneWidth(double width)
    {
        width = Math.Clamp(width, MinPaneWidth, MaxPaneWidth);

        NavView.OpenPaneLength = width;
        PaneGrip.Margin = new Thickness(width - PaneGrip.Width / 2, 0, 0, 0);
    }

    private void OnPaneGripManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e) =>
        SetPaneWidth(NavView.OpenPaneLength + e.Delta.Translation.X);

    private void OnPaneGripManipulationCompleted(object sender, ManipulationCompletedRoutedEventArgs e)
    {
        _preferences.Current.NavigationPaneWidth = NavView.OpenPaneLength;
        _preferences.Save();
    }

    private void OnPaneGripDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        // Двойной щелчок возвращает автоматическую ширину — по самой длинной подписи.
        _preferences.Current.NavigationPaneWidth = 0;
        _preferences.Save();

        SetPaneWidth(MeasureAutoPaneWidth());
    }

    /// <summary>
    /// Переход по номеру страницы: Ctrl+1 — первый доступный пункт меню и так далее.
    /// Скрытые для обычного пользователя пункты в счёт не идут, поэтому номера
    /// совпадают с тем, что человек видит на экране.
    /// </summary>
    private void OnNavigateAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;

        var index = sender.Key - Windows.System.VirtualKey.Number1;

        var visible = NavView.MenuItems
            .Concat(NavView.FooterMenuItems)
            .OfType<NavigationViewItem>()
            .Where(item => item.Visibility == Visibility.Visible)
            .ToList();

        if (index >= 0 && index < visible.Count)
        {
            NavView.SelectedItem = visible[index];
        }
    }

    private void OnNavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem { Tag: string tag })
        {
            _navigation.NavigateTo(tag);
        }
    }
}
