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
}
