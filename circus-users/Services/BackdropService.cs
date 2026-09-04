using Circus.Users.Core.Abstractions;
using Circus.Users.Core.Settings;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI.ViewManagement;

namespace Circus.Users.App.Services;

/// <summary>
/// Включает и переключает материал подложки окна (Mica / Акрил / без прозрачности).
/// </summary>
/// <remarks>
/// Mica доступна только на Windows 11: на Windows 10 её варианты не предлагаются вовсе,
/// см. <see cref="AvailableModes"/>.
/// </remarks>
internal sealed class BackdropService
{
    private readonly IPreferencesStore _preferences;
    private readonly UISettings _uiSettings = new();

    private DispatcherQueue? _dispatcher;
    private Window? _window;
    private FrameworkElement? _solidBackground;

    public BackdropService(IPreferencesStore preferences)
    {
        _preferences = preferences;
        _uiSettings.AdvancedEffectsEnabledChanged += OnAdvancedEffectsChanged;
    }

    /// <summary>Mica поддерживается начиная с Windows 11.</summary>
    public bool IsMicaSupported => MicaController.IsSupported();

    /// <summary>Акрил поддерживается на Windows 10 1809 и новее.</summary>
    public bool IsAcrylicSupported => DesktopAcrylicController.IsSupported();

    /// <summary>Системный тумблер «Эффекты прозрачности».</summary>
    public bool AreSystemEffectsEnabled => _uiSettings.AdvancedEffectsEnabled;

    /// <summary>Материал, который фактически применён с учётом возможностей системы.</summary>
    public BackdropMode EffectiveMode => Resolve(_preferences.Current.Backdrop);

    /// <summary>Режимы, которые имеет смысл показывать пользователю на этом компьютере.</summary>
    public IReadOnlyList<BackdropMode> AvailableModes
    {
        get
        {
            var modes = new List<BackdropMode> { BackdropMode.FollowSystem };

            if (IsMicaSupported)
            {
                modes.Add(BackdropMode.Mica);
                modes.Add(BackdropMode.MicaAlt);
            }

            if (IsAcrylicSupported)
            {
                modes.Add(BackdropMode.Acrylic);
            }

            modes.Add(BackdropMode.None);
            return modes;
        }
    }

    /// <param name="solidBackground">
    /// Слой сплошного фона под содержимым окна. Показывается, когда материал выключен:
    /// без него окно осталось бы прозрачным и рисовало бы чёрный фон.
    /// </param>
    public void Attach(Window window, FrameworkElement solidBackground)
    {
        _window = window;
        _solidBackground = solidBackground;
        _dispatcher = window.DispatcherQueue;

        Apply();
    }

    /// <summary>Применяет режим подложки из настроек.</summary>
    public void Apply()
    {
        if (_window is null)
        {
            return;
        }

        var mode = Resolve(_preferences.Current.Backdrop);
        _window.SystemBackdrop = CreateBackdrop(mode);

        if (_solidBackground is not null)
        {
            _solidBackground.Visibility = mode == BackdropMode.None ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    /// <summary>Во что фактически превращается выбранный режим на этой машине.</summary>
    private BackdropMode Resolve(BackdropMode mode)
    {
        if (mode == BackdropMode.FollowSystem)
        {
            if (!AreSystemEffectsEnabled)
            {
                return BackdropMode.None;
            }

            mode = IsMicaSupported ? BackdropMode.Mica : BackdropMode.Acrylic;
        }

        if (mode is BackdropMode.Mica or BackdropMode.MicaAlt && !IsMicaSupported)
        {
            mode = BackdropMode.Acrylic;
        }

        if (mode == BackdropMode.Acrylic && !IsAcrylicSupported)
        {
            mode = BackdropMode.None;
        }

        return mode;
    }

    /// <remarks>
    /// Только штатные материалы. Свой наследник <c>SystemBackdrop</c> с настройкой тона
    /// давал более заметную прозрачность, но ронял процесс в нативном коде WinUI
    /// (нарушение доступа в Microsoft.UI.Xaml.dll), поэтому от него отказались.
    /// </remarks>
    private static SystemBackdrop? CreateBackdrop(BackdropMode mode) => mode switch
    {
        BackdropMode.Mica => new MicaBackdrop { Kind = MicaKind.Base },
        BackdropMode.MicaAlt => new MicaBackdrop { Kind = MicaKind.BaseAlt },
        BackdropMode.Acrylic => new DesktopAcrylicBackdrop(),
        _ => null,
    };

    private void OnAdvancedEffectsChanged(UISettings sender, object args)
    {
        // Событие приходит не из UI-потока.
        _dispatcher?.TryEnqueue(() =>
        {
            if (_preferences.Current.Backdrop == BackdropMode.FollowSystem)
            {
                Apply();
            }
        });
    }
}
