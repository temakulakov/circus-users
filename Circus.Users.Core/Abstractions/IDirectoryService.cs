using Circus.Users.Core.Models;

namespace Circus.Users.Core.Abstractions;

/// <summary>Чтение справочника: сотрудники, подразделения и группы.</summary>
public interface IDirectoryService
{
    Task<IReadOnlyList<EmployeeCard>> GetEmployeesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrganizationalUnit>> GetDepartmentsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DirectoryGroup>> GetGroupsAsync(CancellationToken cancellationToken = default);
}
