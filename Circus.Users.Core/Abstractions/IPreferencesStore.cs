using Circus.Users.Core.Settings;

namespace Circus.Users.Core.Abstractions;

/// <summary>Хранилище персональных настроек пользователя.</summary>
public interface IPreferencesStore
{
    /// <summary>Текущие настройки. Объект живой: правим свойства и вызываем <see cref="Save"/>.</summary>
    UserPreferences Current { get; }

    /// <summary>Загружает настройки с диска. При отсутствии или повреждении файла возвращает значения по умолчанию.</summary>
    Task LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Планирует сохранение. Частые вызовы схлопываются в одну запись.</summary>
    void Save();

    /// <summary>Немедленно сохраняет настройки (например, при закрытии приложения).</summary>
    Task FlushAsync(CancellationToken cancellationToken = default);
}
