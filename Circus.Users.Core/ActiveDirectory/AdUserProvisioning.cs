using System.DirectoryServices;
using Circus.Users.Core.Abstractions;
using Circus.Users.Core.Models;
using Circus.Users.Core.Naming;
using Circus.Users.Core.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Circus.Users.Core.ActiveDirectory;

/// <summary>
/// Создаёт учётную запись сотрудника в Active Directory.
/// </summary>
/// <remarks>
/// Порядок важен: объект создаётся отключённым, затем задаётся пароль и только после этого
/// учётная запись включается. Иначе домен отвергнет включение записи без пароля.
/// Ошибка добавления в отдельную группу не отменяет создание: сотрудник уже заведён,
/// и такие промахи возвращаются предупреждениями, чтобы администратор довёл их руками.
/// </remarks>
public sealed class AdUserProvisioning : IUserProvisioning
{
    /// <summary>NORMAL_ACCOUNT — обычная включённая учётная запись.</summary>
    private const int NormalEnabledAccount = 0x0200;

    private readonly ActiveDirectorySettings _settings;
    private readonly MailSettings _mail;
    private readonly ILogger<AdUserProvisioning> _logger;

    public AdUserProvisioning(IOptions<AppSettings> settings, ILogger<AdUserProvisioning> logger)
    {
        _settings = settings.Value.ActiveDirectory;
        _mail = settings.Value.Mail;
        _logger = logger;
    }

    public Task<ProvisionedUser> CreateAsync(NewUserRequest request, CancellationToken cancellationToken = default) =>
        Task.Run(() => Create(request, cancellationToken), cancellationToken);

    private ProvisionedUser Create(NewUserRequest request, CancellationToken cancellationToken)
    {
        var password = PasswordGenerator.Generate();
        var warnings = new List<string>();

        using var container = Bind(request.DepartmentDn);
        var user = container.Children.Add($"CN={Escape(request.DisplayName)}", "user");

        try
        {
            SetIfPresent(user, "sAMAccountName", request.AccountName);
            SetIfPresent(user, "userPrincipalName", $"{request.AccountName}@{DomainSuffix()}");
            SetIfPresent(user, "givenName", request.FirstName);
            SetIfPresent(user, "sn", request.LastName);
            SetIfPresent(user, "middleName", request.MiddleName);
            SetIfPresent(user, "displayName", request.DisplayName);
            SetIfPresent(user, "mail", request.Email);
            SetIfPresent(user, "title", request.Title);
            SetIfPresent(user, "ipPhone", request.IpPhone);
            SetIfPresent(user, "company", request.Company);
            SetIfPresent(user, "telephoneNumber", request.TelephoneNumber);
            SetIfPresent(user, "l", request.City);
            SetIfPresent(user, "streetAddress", request.StreetAddress);
            SetIfPresent(user, "physicalDeliveryOfficeName", request.Office);

            user.CommitChanges();
            cancellationToken.ThrowIfCancellationRequested();

            user.Invoke("SetPassword", password);
            user.Properties["userAccountControl"].Value = NormalEnabledAccount;
            user.CommitChanges();

            var distinguishedName = user.Properties["distinguishedName"].Value?.ToString() ?? string.Empty;
            AddToGroups(request.AdGroups, distinguishedName, warnings, cancellationToken);

            _logger.LogInformation("Создана учётная запись {Account} в {Ou}", request.AccountName, request.DepartmentDn);

            return new ProvisionedUser(
                request.AccountName,
                password,
                request.Email,
                // Почтовый ящик заводится отдельным модулем — он появится позже.
                MailPassword: null,
                distinguishedName,
                warnings);
        }
        finally
        {
            user.Dispose();
        }
    }

    private void AddToGroups(
        IReadOnlyList<string> groupAccounts,
        string userDn,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        if (groupAccounts.Count == 0 || userDn.Length == 0)
        {
            return;
        }

        using var root = new DirectoryEntry(SearchRoot(), null, null, AuthenticationTypes.Secure);

        foreach (var account in groupAccounts)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using var searcher = new DirectorySearcher(root)
                {
                    Filter = $"(&(objectCategory=group)(sAMAccountName={Escape(account)}))",
                    SearchScope = SearchScope.Subtree,
                };

                using var group = searcher.FindOne()?.GetDirectoryEntry();

                if (group is null)
                {
                    warnings.Add($"Группа «{account}» не найдена — добавьте вручную.");
                    continue;
                }

                group.Properties["member"].Add(userDn);
                group.CommitChanges();
            }
            catch (Exception ex) when (ex is DirectoryServicesCOMException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Не удалось добавить {Dn} в группу {Group}", userDn, account);
                warnings.Add($"Не удалось добавить в группу «{account}»: {ex.Message}");
            }
        }
    }

    private static void SetIfPresent(DirectoryEntry entry, string attribute, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            entry.Properties[attribute].Value = value;
        }
    }

    /// <summary>Экранирует символы, ломающие фильтр LDAP и различающееся имя.</summary>
    private static string Escape(string value) => value
        .Replace("\\", "\\5c")
        .Replace("(", "\\28")
        .Replace(")", "\\29")
        .Replace("*", "\\2a")
        .Replace(",", "\\2c");

    /// <summary>Домен для userPrincipalName. Почтовый домен и доменный суффикс могут не совпадать.</summary>
    private string DomainSuffix() =>
        string.IsNullOrWhiteSpace(_settings.DomainFqdn) ? _mail.Domain : _settings.DomainFqdn;

    private string SearchRoot()
    {
        var baseDn = string.IsNullOrWhiteSpace(_settings.UsersBaseDn) ? string.Empty : _settings.UsersBaseDn;
        return $"LDAP://{ServerPrefix()}{baseDn}";
    }

    private DirectoryEntry Bind(string distinguishedName) =>
        new($"LDAP://{ServerPrefix()}{distinguishedName}", null, null, AuthenticationTypes.Secure);

    private string ServerPrefix() =>
        string.IsNullOrWhiteSpace(_settings.Server)
            ? string.IsNullOrWhiteSpace(_settings.DomainFqdn) ? string.Empty : _settings.DomainFqdn + "/"
            : _settings.Server + "/";
}
