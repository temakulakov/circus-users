using Circus.Users.Core.Abstractions;
using Circus.Users.Core.Models;
using Circus.Users.Core.Naming;
using Microsoft.Extensions.Logging;

namespace Circus.Users.Core.Fakes;

/// <summary>
/// Создание сотрудника в тестовом режиме: ничего не записывает, но возвращает
/// такой же результат, чтобы можно было пройти форму и диалог с паролями целиком.
/// </summary>
public sealed class FakeUserProvisioning : IUserProvisioning
{
    private readonly ILogger<FakeUserProvisioning> _logger;

    public FakeUserProvisioning(ILogger<FakeUserProvisioning> logger) => _logger = logger;

    public Task<ProvisionedUser> CreateAsync(NewUserRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Тестовый режим: был бы создан {Account} в {Ou}, групп: {Groups}",
            request.AccountName,
            request.DepartmentDn,
            request.AdGroups.Count);

        return Task.FromResult(new ProvisionedUser(
            request.AccountName,
            PasswordGenerator.Generate(),
            request.Email,
            PasswordGenerator.Generate(),
            $"CN={request.DisplayName},{request.DepartmentDn}",
            ["Тестовый режим: в каталоге ничего не создано."]));
    }
}
