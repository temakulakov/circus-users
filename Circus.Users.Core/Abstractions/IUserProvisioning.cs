using Circus.Users.Core.Models;

namespace Circus.Users.Core.Abstractions;

/// <summary>Создание сотрудника в каталоге.</summary>
public interface IUserProvisioning
{
    Task<ProvisionedUser> CreateAsync(NewUserRequest request, CancellationToken cancellationToken = default);
}
