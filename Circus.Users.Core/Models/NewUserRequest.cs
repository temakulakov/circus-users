namespace Circus.Users.Core.Models;

/// <summary>Что нужно для создания сотрудника.</summary>
public sealed class NewUserRequest
{
    public required string DepartmentDn { get; init; }

    public required string DisplayName { get; init; }

    public required string FirstName { get; init; }

    public required string LastName { get; init; }

    public string? MiddleName { get; init; }

    public required string AccountName { get; init; }

    public required string Email { get; init; }

    public string? Title { get; init; }

    public string? IpPhone { get; init; }

    /// <summary>Значения из шаблона подразделения.</summary>
    public string? Company { get; init; }

    public string? TelephoneNumber { get; init; }

    public string? City { get; init; }

    public string? StreetAddress { get; init; }

    public string? Office { get; init; }

    /// <summary>Группы безопасности AD по sAMAccountName.</summary>
    public IReadOnlyList<string> AdGroups { get; init; } = [];

    /// <summary>Группы рассылки Yandex — понадобятся почтовому модулю.</summary>
    public IReadOnlyList<string> MailGroups { get; init; } = [];
}

/// <summary>Созданная учётная запись. Пароли живут только в памяти и показываются один раз.</summary>
/// <param name="AccountName">Логин в домене.</param>
/// <param name="DomainPassword">Пароль доменной учётной записи.</param>
/// <param name="Email">Почтовый адрес.</param>
/// <param name="MailPassword">Пароль от почты; null, пока почтовый модуль не подключён.</param>
/// <param name="DistinguishedName">Различающееся имя созданного объекта.</param>
/// <param name="Warnings">Что не удалось доделать — например, добавить в отдельные группы.</param>
public sealed record ProvisionedUser(
    string AccountName,
    string DomainPassword,
    string Email,
    string? MailPassword,
    string DistinguishedName,
    IReadOnlyList<string> Warnings);
