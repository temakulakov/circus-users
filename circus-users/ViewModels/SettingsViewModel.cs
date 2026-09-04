using System.Collections.ObjectModel;
using Circus.Users.App.Messages;
using Circus.Users.App.Services;
using Circus.Users.Core.Abstractions;
using Circus.Users.Core.ActiveDirectory;
using Circus.Users.Core.Settings;
using Circus.Users.Core.Yandex;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Windows.UI;

namespace Circus.Users.App.ViewModels;

/// <summary>Страница настроек оформления. Изменения применяются сразу и сохраняются локально.</summary>
internal sealed partial class SettingsViewModel : ObservableObject
{
    /// <summary>Готовая палитра — подмножество системных акцентов Windows.</summary>
    private static readonly string[] BuiltInPresets =
    [
        "#FFB900", "#F7630C", "#CA5010", "#DA3B01",
        "#D13438", "#E81123", "#E3008C", "#C239B3",
        "#0078D4", "#0063B1", "#6B69D6", "#8764B8",
        "#0099BC", "#00B7C3", "#00B294", "#10893E",
        "#498205", "#7A7574", "#68768A", "#515C6B",
    ];

    private readonly IPreferencesStore _store;
    private readonly ThemeService _theme;
    private readonly BackdropService _backdrop;
    private readonly SoundService _sound;
    private readonly SessionService _session;
    private readonly DirectoryCache _cache;
    private readonly IMailService _mailService;
    private readonly ISecretStore _secrets;
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();

    /// <summary>Идёт первичная загрузка значений — реагировать на изменения не нужно.</summary>
    private bool _isLoading;

    /// <summary>Пересчёт акцента уже поставлен в очередь — второй раз не нужно.</summary>
    private bool _isAccentUpdateQueued;

    [ObservableProperty]
    private int _themeIndex;

    [ObservableProperty]
    private BackdropOption? _selectedBackdrop;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAutoHideAvailable))]
    private int _navigationIndex;

    [ObservableProperty]
    private bool _autoHidePane;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PaletteVisibility))]
    private bool _useSystemAccent;

    [ObservableProperty]
    private Color _accentColor;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SpatialSoundVisibility))]
    private bool _enableSounds;

    [ObservableProperty]
    private bool _enableSpatialSound;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FakeAdminVisibility))]
    private bool _useFakeData;

    [ObservableProperty]
    private bool _fakeUserIsAdmin;

    [ObservableProperty]
    private string _mailToken = string.Empty;

    [ObservableProperty]
    private string _mailCheckResult = string.Empty;

    [ObservableProperty]
    private bool _isCheckingMail;

    public SettingsViewModel(
        IPreferencesStore store,
        ThemeService theme,
        BackdropService backdrop,
        SoundService sound,
        SessionService session,
        DirectoryCache cache,
        IMailService mailService,
        ISecretStore secrets)
    {
        _store = store;
        _theme = theme;
        _backdrop = backdrop;
        _sound = sound;
        _session = session;
        _cache = cache;
        _mailService = mailService;
        _secrets = secrets;

        Presets = [];
        BackdropOptions = [.. backdrop.AvailableModes.Select(mode => new BackdropOption(mode))];

        LoadFromPreferences();
    }

    public ObservableCollection<AccentPreset> Presets { get; }

    /// <summary>Только те материалы, которые эта версия Windows действительно умеет.</summary>
    public ObservableCollection<BackdropOption> BackdropOptions { get; }

    /// <summary>Палитра нужна, только если акцент не берётся из системы.</summary>
    public Visibility PaletteVisibility => UseSystemAccent ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>Раздел разработки собирается только в отладочной конфигурации.</summary>
    public Visibility DevelopmentVisibility =>
#if DEBUG
        Visibility.Visible;
#else
        Visibility.Collapsed;
#endif

    /// <summary>Выбор роли имеет смысл только при включённых тестовых данных.</summary>
    public Visibility FakeAdminVisibility => UseFakeData ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>У верхней панели нет режима скрытия.</summary>
    public bool IsAutoHideAvailable => NavigationIndex != (int)NavigationPosition.Top;

    /// <summary>Пространственное звучание показывается только при включённых звуках.</summary>
    public Visibility SpatialSoundVisibility => EnableSounds ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Раздел почты видит только администратор: токен даёт доступ ко всей организации.</summary>
    public Visibility MailSectionVisibility =>
        _session.Current.IsAdmin ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Состояние подключения к почте — показывается под полем токена.</summary>
    public string MailStatus => _mailService.IsConfigured
        ? "Токен сохранён. Нажмите «Проверить», чтобы убедиться в доступе."
        : "Токен не задан — почтовые ящики создаваться не будут.";

    private void LoadFromPreferences()
    {
        _isLoading = true;
        try
        {
            var preferences = _store.Current;

            ThemeIndex = (int)preferences.Theme;
            SelectedBackdrop = BackdropOptions.FirstOrDefault(option => option.Mode == preferences.Backdrop)
                               ?? BackdropOptions[0];
            NavigationIndex = (int)preferences.NavigationPosition;
            AutoHidePane = preferences.AutoHideNavigationPane;
            UseSystemAccent = preferences.UseSystemAccent;
            AccentColor = _theme.EffectiveAccentColor;
            EnableSounds = preferences.EnableSounds;
            EnableSpatialSound = preferences.EnableSpatialSound;
            UseFakeData = preferences.UseFakeData;
            FakeUserIsAdmin = preferences.FakeUserIsAdmin;

            RebuildPresets(preferences);
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void RebuildPresets(UserPreferences preferences)
    {
        Presets.Clear();

        foreach (var hex in BuiltInPresets)
        {
            if (ColorHex.TryParse(hex, out var color))
            {
                Presets.Add(new AccentPreset(color, isCustom: false));
            }
        }

        foreach (var hex in preferences.AccentPresets)
        {
            if (ColorHex.TryParse(hex, out var color))
            {
                Presets.Add(new AccentPreset(color, isCustom: true));
            }
        }

        SyncPresetSelection();
    }

    /// <summary>Отмечает в палитре цвет, который сейчас используется.</summary>
    private void SyncPresetSelection()
    {
        foreach (var preset in Presets)
        {
            preset.IsSelected = !UseSystemAccent
                                && preset.Color.R == AccentColor.R
                                && preset.Color.G == AccentColor.G
                                && preset.Color.B == AccentColor.B;
        }
    }

    partial void OnThemeIndexChanged(int value)
    {
        if (_isLoading)
        {
            return;
        }

        _store.Current.Theme = (AppTheme)value;
        _store.Save();

        _theme.ApplyTheme();

        // Если фактическая тема не изменилась, событие ActualThemeChanged не придёт,
        // поэтому пересчитываем палитру явно — вызов идемпотентный.
        _theme.ApplyAccent();
    }

    partial void OnSelectedBackdropChanged(BackdropOption? value)
    {
        if (_isLoading || value is null)
        {
            return;
        }

        _store.Current.Backdrop = value.Mode;
        _store.Save();

        _backdrop.Apply();
    }

    partial void OnNavigationIndexChanged(int value)
    {
        if (_isLoading)
        {
            return;
        }

        _store.Current.NavigationPosition = (NavigationPosition)value;
        _store.Save();

        WeakReferenceMessenger.Default.Send(new NavigationLayoutChangedMessage());
    }

    partial void OnAutoHidePaneChanged(bool value)
    {
        if (_isLoading)
        {
            return;
        }

        _store.Current.AutoHideNavigationPane = value;
        _store.Save();

        WeakReferenceMessenger.Default.Send(new NavigationLayoutChangedMessage());
    }

    /// <remarks>
    /// Переключатель только запоминает выбор. Выбранный вручную цвет намеренно не трогаем:
    /// выключив переключатель обратно, пользователь получит тот же цвет, что и раньше.
    /// </remarks>
    partial void OnUseSystemAccentChanged(bool value)
    {
        if (_isLoading)
        {
            return;
        }

        _store.Current.UseSystemAccent = value;
        _store.Save();

        RequestAccentUpdate();
    }

    partial void OnAccentColorChanged(Color value)
    {
        if (_isLoading)
        {
            return;
        }

        _store.Current.AccentColor = ColorHex.ToHex(value);
        _store.Save();

        RequestAccentUpdate();
    }

    /// <summary>
    /// Откладывает пересчёт акцента до конца текущего цикла ввода.
    /// </summary>
    /// <remarks>
    /// Пересчёт трогает три десятка кистей и перебирает палитру. Если делать это прямо
    /// в обработчике переключателя, контрол не успевает доиграть анимацию, и быстрые щелчки
    /// выглядят как несработавшие. Заодно подряд идущие изменения (например, перетаскивание
    /// ползунка в палитре) схлопываются в один пересчёт.
    /// </remarks>
    private void RequestAccentUpdate()
    {
        if (_isAccentUpdateQueued)
        {
            return;
        }

        _isAccentUpdateQueued = true;
        _dispatcher.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            _isAccentUpdateQueued = false;
            _theme.ApplyAccent();
            SyncPresetSelection();
        });
    }

    partial void OnEnableSoundsChanged(bool value)
    {
        if (_isLoading)
        {
            return;
        }

        _store.Current.EnableSounds = value;
        _store.Save();

        _sound.Apply();
    }

    partial void OnEnableSpatialSoundChanged(bool value)
    {
        if (_isLoading)
        {
            return;
        }

        _store.Current.EnableSpatialSound = value;
        _store.Save();

        _sound.Apply();
    }

    partial void OnUseFakeDataChanged(bool value)
    {
        if (_isLoading)
        {
            return;
        }

        _store.Current.UseFakeData = value;
        _store.Save();

        RefreshSession();
    }

    partial void OnFakeUserIsAdminChanged(bool value)
    {
        if (_isLoading)
        {
            return;
        }

        _store.Current.FakeUserIsAdmin = value;
        _store.Save();

        RefreshSession();
    }

    private void RefreshSession()
    {
        _session.Refresh();
        WeakReferenceMessenger.Default.Send(new SessionChangedMessage());

        // Источник справочника сменился вместе с режимом — перечитываем его.
        _ = _cache.RefreshAsync();
    }

    /// <summary>Выбирает цвет из палитры. Вызывается из обработчика нажатия на образец.</summary>
    public void SelectPreset(AccentPreset preset) => AccentColor = preset.Color;

    /// <summary>
    /// Сохраняет токен в диспетчере учётных данных. В настройках приложения
    /// и в его файлах он не появляется.
    /// </summary>
    [RelayCommand]
    private async Task SaveMailTokenAsync()
    {
        var token = MailToken.Trim();

        if (token.Length == 0)
        {
            MailCheckResult = "Введите токен.";
            return;
        }

        try
        {
            _secrets.Write(Yandex360MailService.TokenKey, token);
            MailToken = string.Empty;
            OnPropertyChanged(nameof(MailStatus));

            await CheckMailAsync();
        }
        catch (Exception ex)
        {
            MailCheckResult = $"Не удалось сохранить токен: {ex.Message}";
        }
    }

    [RelayCommand]
    private void DeleteMailToken()
    {
        try
        {
            _secrets.Delete(Yandex360MailService.TokenKey);
            MailCheckResult = "Токен удалён.";
            OnPropertyChanged(nameof(MailStatus));
        }
        catch (Exception ex)
        {
            MailCheckResult = $"Не удалось удалить токен: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task CheckMailAsync()
    {
        IsCheckingMail = true;
        try
        {
            var error = await _mailService.TestConnectionAsync();
            MailCheckResult = error ?? "Связь с Yandex 360 есть, токен принят.";
        }
        finally
        {
            IsCheckingMail = false;
        }
    }

    [RelayCommand]
    private void SaveCurrentColorAsPreset()
    {
        var hex = ColorHex.ToHex(AccentColor);
        if (_store.Current.AccentPresets.Contains(hex) || BuiltInPresets.Contains(hex))
        {
            return;
        }

        _store.Current.AccentPresets.Add(hex);
        _store.Save();

        Presets.Add(new AccentPreset(AccentColor, isCustom: true));
        SyncPresetSelection();
    }

    [RelayCommand]
    private void RemovePreset(AccentPreset? preset)
    {
        if (preset is not { IsCustom: true })
        {
            return;
        }

        _store.Current.AccentPresets.Remove(preset.Hex);
        _store.Save();

        Presets.Remove(preset);
    }

    [RelayCommand]
    private void ResetAppearance()
    {
        var defaults = new UserPreferences();
        var current = _store.Current;

        current.Theme = defaults.Theme;
        current.Backdrop = defaults.Backdrop;
        current.UseSystemAccent = defaults.UseSystemAccent;
        current.AccentColor = defaults.AccentColor;
        current.NavigationPosition = defaults.NavigationPosition;
        current.AutoHideNavigationPane = defaults.AutoHideNavigationPane;
        current.NavigationPaneWidth = defaults.NavigationPaneWidth;
        current.EnableSounds = defaults.EnableSounds;
        current.EnableSpatialSound = defaults.EnableSpatialSound;
        // Сохранённые пользователем цвета намеренно не трогаем.
        _store.Save();

        LoadFromPreferences();

        _theme.ApplyTheme();
        _theme.ApplyAccent();
        _backdrop.Apply();
        _sound.Apply();
        WeakReferenceMessenger.Default.Send(new NavigationLayoutChangedMessage());
    }
}
