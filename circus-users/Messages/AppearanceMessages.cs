namespace Circus.Users.App.Messages;

/// <summary>Настройки навигации изменились — оболочке нужно перестроить панель.</summary>
internal sealed record NavigationLayoutChangedMessage;

/// <summary>Настройки подложки окна изменились.</summary>
internal sealed record BackdropChangedMessage;

/// <summary>Сменился пользователь сеанса — нужно пересобрать доступные страницы.</summary>
internal sealed record SessionChangedMessage;
