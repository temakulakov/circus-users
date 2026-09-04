namespace Circus.Users.Core.Models;

/// <summary>Группа рассылки в почтовой организации.</summary>
/// <param name="Id">Идентификатор группы в Yandex 360.</param>
/// <param name="Name">Название для показа.</param>
/// <param name="Email">Адрес группы.</param>
public sealed record MailGroup(long Id, string Name, string Email);

/// <summary>Что нужно для заведения ящика.</summary>
public sealed class NewMailboxRequest
{
    public required string Nickname { get; init; }

    public required string FirstName { get; init; }

    public required string LastName { get; init; }

    public string? MiddleName { get; init; }

    /// <summary>Группы рассылки: имена или адреса из шаблона подразделения.</summary>
    public IReadOnlyList<string> Groups { get; init; } = [];

    /// <summary>
    /// Подразделение в Yandex 360. Пусто — берётся значение по умолчанию из настроек:
    /// API отклоняет создание без этого поля.
    /// </summary>
    public long? DepartmentId { get; init; }
}

/// <summary>Заведённый ящик. Пароль живёт только в памяти и показывается один раз.</summary>
/// <param name="UserId">Идентификатор сотрудника в почтовой организации.</param>
/// <param name="Email">Адрес ящика.</param>
/// <param name="Password">Пароль от ящика.</param>
/// <param name="Warnings">Что не удалось доделать — например, добавить в отдельную рассылку.</param>
public sealed record MailAccount(string UserId, string Email, string Password, IReadOnlyList<string> Warnings);
