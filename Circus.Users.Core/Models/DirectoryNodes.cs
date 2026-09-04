namespace Circus.Users.Core.Models;

/// <summary>Подразделение каталога.</summary>
/// <param name="DistinguishedName">Различающееся имя OU.</param>
/// <param name="Name">Имя подразделения без пути.</param>
/// <param name="ParentDistinguishedName">Родительский контейнер.</param>
public sealed record OrganizationalUnit(string DistinguishedName, string Name, string ParentDistinguishedName);

/// <summary>Группа безопасности или рассылки в каталоге.</summary>
/// <param name="DistinguishedName">Различающееся имя группы.</param>
/// <param name="Name">Имя для показа.</param>
/// <param name="AccountName">sAMAccountName — по нему группа записывается в шаблон подразделения.</param>
public sealed record DirectoryGroup(string DistinguishedName, string Name, string AccountName);
