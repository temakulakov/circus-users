using Circus.Users.Core.Abstractions;
using Circus.Users.Core.Models;

namespace Circus.Users.Core.Fakes;

/// <summary>
/// Подставной пользователь для разработки: позволяет посмотреть приложение
/// глазами администратора и обычного сотрудника, не трогая домен.
/// </summary>
public sealed class FakeCurrentUserResolver : ICurrentUserResolver
{
    /// <summary>Выдавать ли тестовому пользователю права администратора.</summary>
    public bool IsAdmin { get; set; } = true;

    public CurrentUser Resolve() => IsAdmin
        ? new CurrentUser(@"CIRCUS\t.admin", "Тестов Администратор Тестович", "S-1-5-21-0-0-0-1001", IsAdmin: true, IsFake: true)
        : new CurrentUser(@"CIRCUS\t.user", "Тестов Сотрудник Тестович", "S-1-5-21-0-0-0-1002", IsAdmin: false, IsFake: true);
}
