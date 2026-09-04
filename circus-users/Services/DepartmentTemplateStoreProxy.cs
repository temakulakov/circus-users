using Circus.Users.Core.Abstractions;
using Circus.Users.Core.Models;

namespace Circus.Users.App.Services;

/// <summary>
/// Направляет работу с шаблонами в каталог или в память в зависимости от режима.
/// Выбор делается на каждый вызов, чтобы переключатель тестовых данных работал сразу.
/// </summary>
internal sealed class DepartmentTemplateStoreProxy : IDepartmentTemplateStore
{
    private readonly IPreferencesStore _preferences;
    private readonly Func<bool, IDepartmentTemplateStore> _resolve;

    public DepartmentTemplateStoreProxy(IPreferencesStore preferences, Func<bool, IDepartmentTemplateStore> resolve)
    {
        _preferences = preferences;
        _resolve = resolve;
    }

    private IDepartmentTemplateStore Current => _resolve(_preferences.Current.UseFakeData);

    public Task<DepartmentTemplate> LoadAsync(string organizationalUnitDn, CancellationToken cancellationToken = default) =>
        Current.LoadAsync(organizationalUnitDn, cancellationToken);

    public Task SaveAsync(string organizationalUnitDn, DepartmentTemplate template, CancellationToken cancellationToken = default) =>
        Current.SaveAsync(organizationalUnitDn, template, cancellationToken);
}
