using Circus.Users.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace Circus.Users.Core.Fakes;

/// <summary>
/// Изменения в режиме тестовых данных: ничего не пишет, только сообщает в журнал.
/// Позволяет пройти весь сценарий правки и увольнения, не трогая боевой каталог.
/// </summary>
public sealed class FakeDirectoryWriter : IDirectoryWriter
{
    private readonly ILogger<FakeDirectoryWriter> _logger;

    public FakeDirectoryWriter(ILogger<FakeDirectoryWriter> logger) => _logger = logger;

    public Task UpdateAttributesAsync(
        string distinguishedName,
        IReadOnlyDictionary<string, string?> changes,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Тестовый режим: у {Dn} были бы изменены {Changes}",
            distinguishedName,
            string.Join(", ", changes.Select(change => $"{change.Key}={change.Value}")));

        return Task.CompletedTask;
    }

    public Task SetEnabledAsync(string distinguishedName, bool isEnabled, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Тестовый режим: {Dn} была бы переведена в включена={IsEnabled}", distinguishedName, isEnabled);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> DismissAsync(string distinguishedName, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Тестовый режим: {Dn} был бы уволен", distinguishedName);
        return Task.FromResult<IReadOnlyList<string>>(["CN=Тестовая группа,OU=Тест,DC=circus,DC=local"]);
    }
}
