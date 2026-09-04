using System.Collections.Concurrent;
using Circus.Users.Core.Abstractions;
using Circus.Users.Core.Models;

namespace Circus.Users.Core.Fakes;

/// <summary>
/// Шаблоны подразделений в памяти: позволяют пройти весь сценарий настройки,
/// ничего не записывая в каталог. Живут до закрытия приложения.
/// </summary>
public sealed class FakeDepartmentTemplateStore : IDepartmentTemplateStore
{
    private readonly ConcurrentDictionary<string, DepartmentTemplate> _templates = new(StringComparer.OrdinalIgnoreCase);

    public Task<DepartmentTemplate> LoadAsync(string organizationalUnitDn, CancellationToken cancellationToken = default) =>
        Task.FromResult(_templates.TryGetValue(organizationalUnitDn, out var template)
            ? Clone(template)
            : new DepartmentTemplate());

    public Task SaveAsync(
        string organizationalUnitDn,
        DepartmentTemplate template,
        CancellationToken cancellationToken = default)
    {
        _templates[organizationalUnitDn] = Clone(template);
        return Task.CompletedTask;
    }

    /// <summary>Копия, чтобы правки в форме не меняли уже сохранённое.</summary>
    private static DepartmentTemplate Clone(DepartmentTemplate source) => new()
    {
        Company = source.Company,
        TelephoneNumber = source.TelephoneNumber,
        City = source.City,
        StreetAddress = source.StreetAddress,
        Office = source.Office,
        ManagerAccount = source.ManagerAccount,
        AdGroups = [.. source.AdGroups],
        MailGroups = [.. source.MailGroups],
    };
}
