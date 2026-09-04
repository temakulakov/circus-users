using System.DirectoryServices;
using Circus.Users.Core.Abstractions;
using Circus.Users.Core.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Circus.Users.Core.ActiveDirectory;

/// <summary>
/// Вносит изменения в учётные записи Active Directory.
/// </summary>
/// <remarks>
/// Пишет от имени вошедшего пользователя: права проверяет сам каталог, приложение
/// чужих учётных данных не хранит. Набор изменяемых атрибутов ограничен белым списком
/// из конфигурации — это защита от опечатки в коде, а не замена правам в каталоге.
/// </remarks>
public sealed class AdDirectoryWriter : IDirectoryWriter
{
    /// <summary>Бит «учётная запись отключена» в userAccountControl.</summary>
    private const int AccountDisabledFlag = 0x0002;

    private readonly ActiveDirectorySettings _settings;
    private readonly ILogger<AdDirectoryWriter> _logger;

    public AdDirectoryWriter(IOptions<AppSettings> settings, ILogger<AdDirectoryWriter> logger)
    {
        _settings = settings.Value.ActiveDirectory;
        _logger = logger;
    }

    public Task UpdateAttributesAsync(
        string distinguishedName,
        IReadOnlyDictionary<string, string?> changes,
        CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            var forbidden = changes.Keys
                .Where(name => !_settings.EditableAttributes.Contains(name, StringComparer.OrdinalIgnoreCase))
                .ToList();

            if (forbidden.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Эти атрибуты запрещено изменять: {string.Join(", ", forbidden)}.");
            }

            using var entry = Bind(distinguishedName);

            foreach (var (attribute, value) in changes)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    // Пустое значение означает «очистить»: присвоение пустой строки каталог не примет.
                    entry.Properties[attribute].Clear();
                }
                else
                {
                    entry.Properties[attribute].Value = value;
                }
            }

            entry.CommitChanges();
            _logger.LogInformation("Изменены атрибуты {Attributes} у {Dn}", string.Join(", ", changes.Keys), distinguishedName);
        }, cancellationToken);

    public Task SetEnabledAsync(string distinguishedName, bool isEnabled, CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            using var entry = Bind(distinguishedName);
            SetEnabled(entry, isEnabled);
            entry.CommitChanges();

            _logger.LogInformation("Учётная запись {Dn} переведена в состояние включена={IsEnabled}", distinguishedName, isEnabled);
        }, cancellationToken);

    public Task<IReadOnlyList<string>> DismissAsync(string distinguishedName, CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyList<string>>(() =>
        {
            using var entry = Bind(distinguishedName);

            // Сначала запоминаем группы: после снятия членства список уже не восстановить.
            var groups = entry.Properties["memberOf"]
                .Cast<object>()
                .Select(value => value.ToString() ?? string.Empty)
                .Where(value => value.Length > 0)
                .ToList();

            SetEnabled(entry, isEnabled: false);
            entry.CommitChanges();

            var removed = new List<string>();

            foreach (var groupDn in groups)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    using var group = Bind(groupDn);
                    group.Properties["member"].Remove(distinguishedName);
                    group.CommitChanges();
                    removed.Add(groupDn);
                }
                catch (Exception ex) when (ex is DirectoryServicesCOMException or UnauthorizedAccessException)
                {
                    // Одна недоступная группа не должна отменять всё увольнение:
                    // учётная запись уже отключена, остальные группы снимаем дальше.
                    _logger.LogWarning(ex, "Не удалось снять {Dn} с группы {Group}", distinguishedName, groupDn);
                }
            }

            _logger.LogInformation("Уволен {Dn}, снято групп: {Count}", distinguishedName, removed.Count);
            return removed;
        }, cancellationToken);

    private static void SetEnabled(DirectoryEntry entry, bool isEnabled)
    {
        var current = (int)(entry.Properties["userAccountControl"].Value ?? 0);

        entry.Properties["userAccountControl"].Value = isEnabled
            ? current & ~AccountDisabledFlag
            : current | AccountDisabledFlag;
    }

    private DirectoryEntry Bind(string distinguishedName)
    {
        var server = string.IsNullOrWhiteSpace(_settings.Server)
            ? string.IsNullOrWhiteSpace(_settings.DomainFqdn) ? string.Empty : _settings.DomainFqdn + "/"
            : _settings.Server + "/";

        return new DirectoryEntry($"LDAP://{server}{distinguishedName}", null, null, AuthenticationTypes.Secure);
    }
}
