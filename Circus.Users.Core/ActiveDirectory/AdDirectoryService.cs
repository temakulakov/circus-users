using System.DirectoryServices;
using System.Text.RegularExpressions;
using Circus.Users.Core.Abstractions;
using Circus.Users.Core.Models;
using Circus.Users.Core.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Circus.Users.Core.ActiveDirectory;

/// <summary>
/// Читает сотрудников из Active Directory по протоколу LDAP.
/// </summary>
/// <remarks>
/// Подключение идёт от имени вошедшего пользователя (<see cref="AuthenticationTypes.Secure"/>),
/// пароли приложение не хранит и не спрашивает. Только чтение.
/// </remarks>
public sealed partial class AdDirectoryService : IDirectoryService
{
    /// <summary>
    /// Включённые учётные записи людей: бит 2 в userAccountControl — «отключена».
    /// Имена, оканчивающиеся на $, отсекают учётные записи компьютеров и доверий.
    /// </summary>
    private const string EmployeeFilter =
        "(&(objectCategory=person)(objectClass=user)" +
        "(!(userAccountControl:1.2.840.113556.1.4.803:=2))" +
        "(!(sAMAccountName=*$)))";

    private readonly ActiveDirectorySettings _settings;
    private readonly ILogger<AdDirectoryService> _logger;

    public AdDirectoryService(IOptions<AppSettings> settings, ILogger<AdDirectoryService> logger)
    {
        _settings = settings.Value.ActiveDirectory;
        _logger = logger;
    }

    public Task<IReadOnlyList<EmployeeCard>> GetEmployeesAsync(CancellationToken cancellationToken = default) =>
        // System.DirectoryServices синхронна, поэтому уводим её с потока пользовательского интерфейса.
        Task.Run(() => Load(cancellationToken), cancellationToken);

    /// <summary>Имя ближайшего подразделения в различающемся имени.</summary>
    [GeneratedRegex(",OU=([^,]+)", RegexOptions.IgnoreCase)]
    private static partial Regex ImmediateOuRegex();

    private IReadOnlyList<EmployeeCard> Load(CancellationToken cancellationToken)
    {
        var path = ResolveSearchRoot();
        _logger.LogInformation("Чтение справочника из {Path}", path);

        using var root = new DirectoryEntry(path, null, null, AuthenticationTypes.Secure);
        using var searcher = new DirectorySearcher(root)
        {
            Filter = EmployeeFilter,
            PageSize = 500,
            SearchScope = SearchScope.Subtree,
        };

        searcher.PropertiesToLoad.Add("distinguishedName");
        searcher.PropertiesToLoad.Add("cn");

        // Подразделение не запрашиваем: у пользователя атрибута ou нет, он вычисляется из DN.
        foreach (var field in DirectoryFields.All)
        {
            if (field.Attribute != DirectoryFields.DepartmentAttribute)
            {
                searcher.PropertiesToLoad.Add(field.Attribute);
            }
        }

        var employees = new List<EmployeeCard>();

        using var results = searcher.FindAll();
        foreach (SearchResult result in results)
        {
            cancellationToken.ThrowIfCancellationRequested();
            employees.Add(ToEmployee(result));
        }

        _logger.LogInformation("Прочитано сотрудников: {Count}", employees.Count);
        return employees;
    }

    private static EmployeeCard ToEmployee(SearchResult result)
    {
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (string name in result.Properties.PropertyNames)
        {
            if (result.Properties[name] is { Count: > 0 } values && values[0] is not null)
            {
                attributes[name] = values[0]!.ToString() ?? string.Empty;
            }
        }

        // Руководитель приходит различающимся именем — показываем только его CN.
        if (attributes.TryGetValue("manager", out var manager))
        {
            attributes["manager"] = CommonNameOf(manager);
        }

        var distinguishedName = attributes.GetValueOrDefault("distinguishedName", string.Empty);

        if (ImmediateOuRegex().Match(distinguishedName) is { Success: true } ou)
        {
            attributes[DirectoryFields.DepartmentAttribute] = ou.Groups[1].Value;
        }

        return new EmployeeCard(distinguishedName, attributes);
    }

    public Task<IReadOnlyList<OrganizationalUnit>> GetDepartmentsAsync(CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyList<OrganizationalUnit>>(() =>
        {
            using var root = new DirectoryEntry(ResolveDepartmentsRoot(), null, null, AuthenticationTypes.Secure);
            using var searcher = new DirectorySearcher(root)
            {
                Filter = "(objectCategory=organizationalUnit)",
                PageSize = 500,
                SearchScope = SearchScope.Subtree,
            };

            searcher.PropertiesToLoad.Add("distinguishedName");
            searcher.PropertiesToLoad.Add("ou");

            var departments = new List<OrganizationalUnit>();

            using var results = searcher.FindAll();
            foreach (SearchResult result in results)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var dn = First(result, "distinguishedName");
                if (dn.Length == 0)
                {
                    continue;
                }

                departments.Add(new OrganizationalUnit(dn, First(result, "ou"), ParentOf(dn)));
            }

            _logger.LogInformation("Прочитано подразделений: {Count}", departments.Count);
            return departments;
        }, cancellationToken);

    public Task<IReadOnlyList<DirectoryGroup>> GetGroupsAsync(CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyList<DirectoryGroup>>(() =>
        {
            using var root = new DirectoryEntry(ResolveSearchRoot(), null, null, AuthenticationTypes.Secure);
            using var searcher = new DirectorySearcher(root)
            {
                Filter = "(objectCategory=group)",
                PageSize = 500,
                SearchScope = SearchScope.Subtree,
            };

            searcher.PropertiesToLoad.Add("distinguishedName");
            searcher.PropertiesToLoad.Add("cn");
            searcher.PropertiesToLoad.Add("sAMAccountName");

            var groups = new List<DirectoryGroup>();

            using var results = searcher.FindAll();
            foreach (SearchResult result in results)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var account = First(result, "sAMAccountName");
                if (account.Length == 0)
                {
                    continue;
                }

                groups.Add(new DirectoryGroup(First(result, "distinguishedName"), First(result, "cn"), account));
            }

            _logger.LogInformation("Прочитано групп: {Count}", groups.Count);
            return groups;
        }, cancellationToken);

    private static string First(SearchResult result, string property) =>
        result.Properties[property] is { Count: > 0 } values ? values[0]?.ToString() ?? string.Empty : string.Empty;

    /// <summary>Отбрасывает первый элемент различающегося имени, оставляя родителя.</summary>
    private static string ParentOf(string distinguishedName)
    {
        var separator = distinguishedName.IndexOf(',');
        return separator >= 0 ? distinguishedName[(separator + 1)..] : string.Empty;
    }

    /// <summary>Корень дерева подразделений. Пусто в настройках — тот же корень, что и у пользователей.</summary>
    private string ResolveDepartmentsRoot()
    {
        if (string.IsNullOrWhiteSpace(_settings.DepartmentsRootDn))
        {
            return ResolveSearchRoot();
        }

        var server = string.IsNullOrWhiteSpace(_settings.Server)
            ? string.IsNullOrWhiteSpace(_settings.DomainFqdn) ? string.Empty : _settings.DomainFqdn + "/"
            : _settings.Server + "/";

        return $"LDAP://{server}{_settings.DepartmentsRootDn}";
    }

    /// <summary>Вытаскивает CN=... из различающегося имени.</summary>
    private static string CommonNameOf(string distinguishedName)
    {
        const string prefix = "CN=";

        if (!distinguishedName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return distinguishedName;
        }

        var end = distinguishedName.IndexOf(',');
        return end > prefix.Length
            ? distinguishedName[prefix.Length..end]
            : distinguishedName[prefix.Length..];
    }

    /// <summary>Куда подключаться. Пустые настройки означают «определить по текущему домену».</summary>
    private string ResolveSearchRoot()
    {
        var server = string.IsNullOrWhiteSpace(_settings.Server)
            ? string.IsNullOrWhiteSpace(_settings.DomainFqdn) ? string.Empty : _settings.DomainFqdn + "/"
            : _settings.Server + "/";

        var baseDn = string.IsNullOrWhiteSpace(_settings.UsersBaseDn)
            ? ResolveDefaultNamingContext(server)
            : _settings.UsersBaseDn;

        return $"LDAP://{server}{baseDn}";
    }

    private static string ResolveDefaultNamingContext(string server)
    {
        using var rootDse = new DirectoryEntry($"LDAP://{server}RootDSE", null, null, AuthenticationTypes.Secure);
        var context = rootDse.Properties["defaultNamingContext"].Value?.ToString();

        if (string.IsNullOrWhiteSpace(context))
        {
            throw new InvalidOperationException("Не удалось определить корень каталога: RootDSE не вернул defaultNamingContext.");
        }

        return context;
    }
}
