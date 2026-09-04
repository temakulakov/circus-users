using Circus.Users.Core.Abstractions;
using Circus.Users.Core.Models;

namespace Circus.Users.App.Services;

/// <summary>
/// Направляет создание сотрудника в каталог или в заглушку в зависимости от режима.
/// Выбор делается на каждый вызов, чтобы переключатель тестовых данных работал сразу.
/// </summary>
internal sealed class UserProvisioningProxy : IUserProvisioning
{
    private readonly IPreferencesStore _preferences;
    private readonly Func<bool, IUserProvisioning> _resolve;

    public UserProvisioningProxy(IPreferencesStore preferences, Func<bool, IUserProvisioning> resolve)
    {
        _preferences = preferences;
        _resolve = resolve;
    }

    public Task<ProvisionedUser> CreateAsync(NewUserRequest request, CancellationToken cancellationToken = default) =>
        _resolve(_preferences.Current.UseFakeData).CreateAsync(request, cancellationToken);
}
