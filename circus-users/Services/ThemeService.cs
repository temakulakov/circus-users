using Circus.Users.Core.Abstractions;
using Circus.Users.Core.Settings;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace Circus.Users.App.Services;

/// <summary>
/// Управляет темой и акцентным цветом приложения.
/// </summary>
/// <remarks>
/// Кисти акцента подменяются на уровне <see cref="Application.Resources"/> ещё до создания окна,
/// а дальше меняется только свойство <see cref="SolidColorBrush.Color"/> у тех же экземпляров —
/// поэтому смена акцента видна мгновенно, без перезапуска и перехода на другую страницу.
/// Ключей много намеренно: WinUI не переиспользует одну кисть акцента, у переключателя,
/// флажка и ползунка она своя, и подмена только AccentFillColorDefaultBrush их не затрагивает.
/// </remarks>
internal sealed class ThemeService
{
    /// <summary>Основной оттенок акцента: заливка контролов в обычном состоянии.</summary>
    private static readonly string[] FillKeys =
    [
        "AccentFillColorDefaultBrush",
        "AccentButtonBackground",
        "ToggleSwitchFillOn",
        "ToggleSwitchStrokeOn",
        "CheckBoxCheckBackgroundFillChecked",
        "CheckBoxCheckBackgroundStrokeChecked",
        "RadioButtonOuterEllipseCheckedFill",
        "SliderTrackValueFill",
        "ProgressBarForeground",
        "NavigationViewSelectionIndicatorForeground",
        "TextControlSelectionHighlightColor",
        "TextControlBorderBrushFocused",
        "SystemControlHighlightAccentBrush",
        "SystemControlBackgroundAccentBrush",
        "SystemControlForegroundAccentBrush",
    ];

    /// <summary>Наведение указателя: тот же цвет, чуть приглушённый.</summary>
    private static readonly string[] PointerOverKeys =
    [
        "AccentFillColorSecondaryBrush",
        "AccentButtonBackgroundPointerOver",
        "ToggleSwitchFillOnPointerOver",
        "ToggleSwitchStrokeOnPointerOver",
        "CheckBoxCheckBackgroundFillCheckedPointerOver",
        "RadioButtonOuterEllipseCheckedFillPointerOver",
        "SliderTrackValueFillPointerOver",
    ];

    /// <summary>Нажатие.</summary>
    private static readonly string[] PressedKeys =
    [
        "AccentFillColorTertiaryBrush",
        "AccentButtonBackgroundPressed",
        "ToggleSwitchFillOnPressed",
        "ToggleSwitchStrokeOnPressed",
        "CheckBoxCheckBackgroundFillCheckedPressed",
        "RadioButtonOuterEllipseCheckedFillPressed",
        "SliderTrackValueFillPressed",
    ];

    /// <summary>Акцентный текст и ссылки.</summary>
    private static readonly string[] TextKeys =
    [
        "AccentTextFillColorPrimaryBrush",
        "HyperlinkButtonForeground",
    ];

    private readonly IPreferencesStore _preferences;
    private readonly UISettings _uiSettings = new();
    private readonly Dictionary<string, SolidColorBrush> _brushes = new(StringComparer.Ordinal);

    private DispatcherQueue? _dispatcher;
    private FrameworkElement? _root;

    public ThemeService(IPreferencesStore preferences)
    {
        _preferences = preferences;
        _uiSettings.ColorValuesChanged += OnSystemColorValuesChanged;
    }

    /// <summary>Акцентный цвет, заданный в системных настройках Windows.</summary>
    public Color SystemAccentColor => _uiSettings.GetColorValue(UIColorType.Accent);

    /// <summary>Цвет, который приложение использует как акцент прямо сейчас.</summary>
    public Color EffectiveAccentColor
    {
        get
        {
            var preferences = _preferences.Current;
            if (!preferences.UseSystemAccent && ColorHex.TryParse(preferences.AccentColor, out var custom))
            {
                return custom;
            }

            return SystemAccentColor;
        }
    }

    /// <summary>
    /// Регистрирует кисти акцента в ресурсах приложения. Вызывать один раз при старте,
    /// до создания первого окна: контролы, которые уже разрешили кисть, новую запись не подхватят.
    /// </summary>
    public void InstallResources()
    {
        foreach (var key in FillKeys.Concat(PointerOverKeys).Concat(PressedKeys).Concat(TextKeys))
        {
            var brush = new SolidColorBrush();
            _brushes[key] = brush;
            Application.Current.Resources[key] = brush;
        }

        ApplyAccent();
    }

    /// <summary>Привязывает сервис к корневому элементу окна и применяет сохранённые настройки.</summary>
    public void AttachRoot(FrameworkElement root)
    {
        _dispatcher = root.DispatcherQueue;
        _root = root;
        _root.ActualThemeChanged += OnActualThemeChanged;

        ApplyTheme();
        ApplyAccent();
    }

    /// <summary>Применяет тему из настроек к корневому элементу.</summary>
    public void ApplyTheme()
    {
        if (_root is null)
        {
            return;
        }

        _root.RequestedTheme = _preferences.Current.Theme switch
        {
            AppTheme.Light => ElementTheme.Light,
            AppTheme.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
    }

    /// <summary>Пересчитывает палитру акцента под текущую тему и обновляет кисти.</summary>
    public void ApplyAccent()
    {
        var palette = AccentPalette.From(EffectiveAccentColor);
        var isDark = IsDarkTheme();

        // Windows берёт под заливку тёмный оттенок в светлой теме и светлый — в тёмной,
        // чтобы контраст с фоном оставался в норме.
        var fill = isDark ? palette.Light2 : palette.Dark1;
        var text = isDark ? palette.Light3 : palette.Dark2;

        SetBrushColors(FillKeys, fill);
        SetBrushColors(PointerOverKeys, WithAlpha(fill, 0xE6));
        SetBrushColors(PressedKeys, WithAlpha(fill, 0xCC));
        SetBrushColors(TextKeys, text);

        SetResourceColor("SystemAccentColor", palette.Base);
        SetResourceColor("SystemAccentColorLight1", palette.Light1);
        SetResourceColor("SystemAccentColorLight2", palette.Light2);
        SetResourceColor("SystemAccentColorLight3", palette.Light3);
        SetResourceColor("SystemAccentColorDark1", palette.Dark1);
        SetResourceColor("SystemAccentColorDark2", palette.Dark2);
        SetResourceColor("SystemAccentColorDark3", palette.Dark3);
    }

    /// <summary>
    /// Кладёт цвет в ресурсы приложения, только если он действительно изменился:
    /// запись в <see cref="Application.Resources"/> заставляет пересматривать ресурсы
    /// по всему дереву, и делать это вхолостую дорого.
    /// </summary>
    private static void SetResourceColor(string key, Color color)
    {
        var resources = Application.Current.Resources;

        if (resources.TryGetValue(key, out var existing) && existing is Color current && current == color)
        {
            return;
        }

        resources[key] = color;
    }

    private bool IsDarkTheme()
    {
        if (_root is not null)
        {
            return _root.ActualTheme == ElementTheme.Dark;
        }

        return Application.Current.RequestedTheme == ApplicationTheme.Dark;
    }

    private void SetBrushColors(string[] keys, Color color)
    {
        foreach (var key in keys)
        {
            if (_brushes.TryGetValue(key, out var brush))
            {
                brush.Color = color;
            }
        }
    }

    private static Color WithAlpha(Color color, byte alpha) => Color.FromArgb(alpha, color.R, color.G, color.B);

    private void OnActualThemeChanged(FrameworkElement sender, object args) => ApplyAccent();

    private void OnSystemColorValuesChanged(UISettings sender, object args)
    {
        // Событие приходит не из UI-потока.
        _dispatcher?.TryEnqueue(() =>
        {
            if (_preferences.Current.UseSystemAccent)
            {
                ApplyAccent();
            }
        });
    }
}
