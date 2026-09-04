namespace Circus.Users.Core.Abstractions;

/// <summary>Журнал изменений, вносимых приложением в каталог.</summary>
public interface IAuditLog
{
    /// <summary>
    /// Записывает выполненное действие.
    /// </summary>
    /// <param name="actor">Кто выполнил.</param>
    /// <param name="action">Что сделал.</param>
    /// <param name="target">Над кем.</param>
    /// <param name="details">Подробности, по которым действие можно откатить вручную.</param>
    Task WriteAsync(string actor, string action, string target, string details, CancellationToken cancellationToken = default);

    /// <summary>
    /// Читает последние записи, новые сверху. Журнал только дописывается,
    /// поэтому хвост файла — это и есть свежие события.
    /// </summary>
    /// <param name="maxEntries">Сколько записей вернуть.</param>
    Task<AuditReadResult> ReadAsync(int maxEntries, CancellationToken cancellationToken = default);
}

/// <summary>Одна строка журнала.</summary>
/// <param name="When">Когда произошло; null, если строка старого формата.</param>
/// <param name="Actor">Кто выполнил.</param>
/// <param name="Action">Что сделал.</param>
/// <param name="Target">Над кем.</param>
/// <param name="Details">Подробности.</param>
public sealed record AuditEntry(DateTimeOffset? When, string Actor, string Action, string Target, string Details);

/// <summary>Прочитанный журнал вместе с тем, откуда он взят.</summary>
/// <param name="Entries">Записи, новые сверху.</param>
/// <param name="Source">Файл, из которого прочитано, или null, если журнала ещё нет.</param>
/// <param name="Error">Почему прочитать не удалось, или null.</param>
public sealed record AuditReadResult(IReadOnlyList<AuditEntry> Entries, string? Source, string? Error);
