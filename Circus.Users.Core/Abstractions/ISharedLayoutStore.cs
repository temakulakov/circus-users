namespace Circus.Users.Core.Abstractions;

/// <summary>
/// Общий вид адресной книги, заданный администратором для всех сотрудников.
/// </summary>
/// <remarks>
/// Персональные настройки имеют приоритет: общий вид применяется только к тем,
/// кто не настраивал колонки под себя.
/// </remarks>
public interface ISharedLayoutStore
{
    /// <summary>Последний загруженный общий вид или null, если он не задан или недоступен.</summary>
    IReadOnlyList<string>? Layout { get; }

    /// <summary>Причина, по которой общий вид недоступен, или null.</summary>
    string? LastError { get; }

    Task LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Сохраняет вид для всех. Доступно только администратору.</summary>
    Task SaveAsync(IReadOnlyList<string> layout, CancellationToken cancellationToken = default);
}
