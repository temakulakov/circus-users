namespace Circus.Users.Core.Settings;

/// <summary>
/// Персональные настройки пользователя. Хранятся локально на машине,
/// в общий доступ не попадают.
/// </summary>
public sealed class UserPreferences
{
    public AppTheme Theme { get; set; } = AppTheme.System;

    public BackdropMode Backdrop { get; set; } = BackdropMode.FollowSystem;

    /// <summary>Брать акцентный цвет из системных настроек Windows.</summary>
    public bool UseSystemAccent { get; set; } = true;

    /// <summary>Свой акцентный цвет в формате #RRGGBB. Учитывается, если <see cref="UseSystemAccent"/> выключен.</summary>
    public string? AccentColor { get; set; }

    /// <summary>Сохранённые пользователем цвета-пресеты в формате #RRGGBB.</summary>
    public List<string> AccentPresets { get; set; } = [];

    public NavigationPosition NavigationPosition { get; set; } = NavigationPosition.Left;

    /// <summary>Сворачивать панель навигации, оставляя только кнопку-гамбургер.</summary>
    public bool AutoHideNavigationPane { get; set; }

    /// <summary>Ширина развёрнутой панели навигации. 0 — подобрать по ширине подписей.</summary>
    public double NavigationPaneWidth { get; set; }

    /// <summary>Звуковое сопровождение действий (ElementSoundPlayer).</summary>
    public bool EnableSounds { get; set; }

    /// <summary>Пространственное звучание: звук идёт со стороны нажатого элемента.</summary>
    public bool EnableSpatialSound { get; set; } = true;

    /// <summary>
    /// Поля карточки адресной книги в порядке показа. Пустой список — вид по умолчанию.
    /// </summary>
    public List<string> AddressBookFields { get; set; } = [];

    /// <summary>Работать на тестовых данных вместо домена. Раздел разработки.</summary>
    public bool UseFakeData { get; set; }

    /// <summary>Выдавать ли тестовому пользователю права администратора.</summary>
    public bool FakeUserIsAdmin { get; set; } = true;
}
