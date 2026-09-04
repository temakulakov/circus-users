using Circus.Users.Core.Models;

namespace Circus.Users.Core.Abstractions;

/// <summary>Шаблоны подразделений: что подставлять новому сотруднику.</summary>
public interface IDepartmentTemplateStore
{
    /// <summary>Читает шаблон подразделения. Возвращает пустой шаблон, если он ещё не задан.</summary>
    Task<DepartmentTemplate> LoadAsync(string organizationalUnitDn, CancellationToken cancellationToken = default);

    Task SaveAsync(string organizationalUnitDn, DepartmentTemplate template, CancellationToken cancellationToken = default);
}
