namespace Circus.Users.Core.Settings;

/// <summary>Тема оформления, выбранная пользователем.</summary>
public enum AppTheme
{
    /// <summary>Следовать системной теме Windows.</summary>
    System,
    Light,
    Dark,
}

/// <summary>Режим подложки окна (материал фона).</summary>
public enum BackdropMode
{
    /// <summary>Включать материал только если в системе разрешены эффекты прозрачности.</summary>
    FollowSystem,
    Mica,
    MicaAlt,
    Acrylic,
    /// <summary>Непрозрачный фон.</summary>
    None,
}

/// <summary>Расположение панели навигации.</summary>
public enum NavigationPosition
{
    Left,
    LeftCompact,
    Top,
}
