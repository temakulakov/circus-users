namespace Circus.Users.Core.Abstractions;

/// <summary>Изменение учётных записей в каталоге.</summary>
public interface IDirectoryWriter
{
    /// <summary>
    /// Обновляет атрибуты сотрудника. Пустое значение очищает атрибут.
    /// </summary>
    Task UpdateAttributesAsync(
        string distinguishedName,
        IReadOnlyDictionary<string, string?> changes,
        CancellationToken cancellationToken = default);

    /// <summary>Включает или отключает учётную запись.</summary>
    Task SetEnabledAsync(string distinguishedName, bool isEnabled, CancellationToken cancellationToken = default);

    /// <summary>
    /// Увольнение: отключает учётную запись и снимает членство в группах.
    /// Возвращает снятые группы, чтобы их можно было записать в журнал и при нужде вернуть.
    /// </summary>
    Task<IReadOnlyList<string>> DismissAsync(string distinguishedName, CancellationToken cancellationToken = default);
}
