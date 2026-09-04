using System.DirectoryServices;
using System.Text.Json;
using Circus.Users.Core.Abstractions;
using Circus.Users.Core.Models;
using Circus.Users.Core.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Circus.Users.Core.ActiveDirectory;

/// <summary>
/// Хранит шаблон подразделения в contact-объекте внутри самого подразделения.
/// </summary>
/// <remarks>
/// Шаблон едет вместе с каталогом: не нужна ни база, ни доступная всем шара, а права
/// на изменение шаблона — это обычные права на объект в OU.
/// Атрибут <c>info</c> ограничен 1024 символами, поэтому JSON пишется без отступов
/// и с сокращёнными именами полей, а длина проверяется перед записью.
/// </remarks>
public sealed class AdDepartmentTemplateStore : IDepartmentTemplateStore
{
    /// <summary>Предел атрибута info в схеме Active Directory.</summary>
    private const int InfoAttributeLimit = 1024;

    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = false };

    private readonly ActiveDirectorySettings _settings;
    private readonly ILogger<AdDepartmentTemplateStore> _logger;

    public AdDepartmentTemplateStore(IOptions<AppSettings> settings, ILogger<AdDepartmentTemplateStore> logger)
    {
        _settings = settings.Value.ActiveDirectory;
        _logger = logger;
    }

    public Task<DepartmentTemplate> LoadAsync(string organizationalUnitDn, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            using var container = Bind(organizationalUnitDn);
            using var contact = FindTemplate(container);

            if (contact is null)
            {
                return new DepartmentTemplate();
            }

            var json = contact.Properties["info"].Value?.ToString();
            if (string.IsNullOrWhiteSpace(json))
            {
                return new DepartmentTemplate();
            }

            try
            {
                return JsonSerializer.Deserialize<DepartmentTemplate>(json, SerializerOptions) ?? new DepartmentTemplate();
            }
            catch (JsonException ex)
            {
                // Испорченный шаблон не должен мешать открыть подразделение:
                // администратор увидит пустую форму и сможет заполнить её заново.
                _logger.LogWarning(ex, "Шаблон подразделения {Dn} повреждён", organizationalUnitDn);
                return new DepartmentTemplate();
            }
        }, cancellationToken);

    public Task SaveAsync(
        string organizationalUnitDn,
        DepartmentTemplate template,
        CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            var json = JsonSerializer.Serialize(template, SerializerOptions);

            if (json.Length > InfoAttributeLimit)
            {
                throw new InvalidOperationException(
                    $"Шаблон не помещается в атрибут каталога: {json.Length} символов из {InfoAttributeLimit}. "
                    + "Уберите часть групп или сократите адрес.");
            }

            using var container = Bind(organizationalUnitDn);
            var contact = FindTemplate(container);

            try
            {
                if (contact is null)
                {
                    // Контакта в подразделении ещё нет — заводим его при первом сохранении.
                    try
                    {
                        contact = container.Children.Add($"CN={_settings.DepartmentTemplateObjectName}", "contact");
                        contact.CommitChanges();
                        _logger.LogInformation(
                            "В подразделении {Dn} создан контакт {Name} для шаблона",
                            organizationalUnitDn,
                            _settings.DepartmentTemplateObjectName);
                    }
                    catch (DirectoryServicesCOMException ex)
                    {
                        throw new InvalidOperationException(
                            $"Не удалось создать контакт «{_settings.DepartmentTemplateObjectName}» "
                            + $"в подразделении: {ex.Message} Проверьте право на создание объектов в этом OU.",
                            ex);
                    }
                }

                contact.Properties["info"].Value = json;
                contact.CommitChanges();
            }
            finally
            {
                contact?.Dispose();
            }

            _logger.LogInformation("Шаблон подразделения {Dn} сохранён, {Length} символов", organizationalUnitDn, json.Length);
        }, cancellationToken);

    private DirectoryEntry? FindTemplate(DirectoryEntry container)
    {
        using var searcher = new DirectorySearcher(container)
        {
            Filter = $"(&(objectClass=contact)(cn={_settings.DepartmentTemplateObjectName}))",
            SearchScope = SearchScope.OneLevel,
        };

        return searcher.FindOne()?.GetDirectoryEntry();
    }

    private DirectoryEntry Bind(string distinguishedName)
    {
        var server = string.IsNullOrWhiteSpace(_settings.Server)
            ? string.IsNullOrWhiteSpace(_settings.DomainFqdn) ? string.Empty : _settings.DomainFqdn + "/"
            : _settings.Server + "/";

        return new DirectoryEntry($"LDAP://{server}{distinguishedName}", null, null, AuthenticationTypes.Secure);
    }
}
