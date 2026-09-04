using Circus.Users.Core.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Circus.Users.Core.Settings;

/// <summary>
/// Журнал операций в текстовом файле на общей шаре.
/// </summary>
/// <remarks>
/// Лежит рядом с общими настройками, чтобы записи всех администраторов собирались вместе.
/// Если шара недоступна, запись уходит в резервный каталог, переданный при создании.
/// </remarks>
public sealed class FileAuditLog : IAuditLog
{
    private const string FileName = "audit.log";

    private readonly StorageSettings _settings;
    private readonly string _fallbackDirectory;
    private readonly ILogger<FileAuditLog> _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public FileAuditLog(IOptions<AppSettings> settings, string fallbackDirectory, ILogger<FileAuditLog> logger)
    {
        _settings = settings.Value.Storage;
        _fallbackDirectory = fallbackDirectory;
        _logger = logger;
    }

    public async Task WriteAsync(
        string actor,
        string action,
        string target,
        string details,
        CancellationToken cancellationToken = default)
    {
        var line = string.Join('\t',
            DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            actor,
            action,
            target,
            details.ReplaceLineEndings(" "));

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            if (!await TryAppendAsync(_settings.SharedSettingsPath, line, cancellationToken))
            {
                await TryAppendAsync(_fallbackDirectory, line, cancellationToken);
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task<bool> TryAppendAsync(string? directory, string line, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return false;
        }

        try
        {
            Directory.CreateDirectory(directory);
            await File.AppendAllTextAsync(Path.Combine(directory, FileName), line + Environment.NewLine, cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Не удалось записать в журнал операций в {Directory}", directory);
            return false;
        }
    }
}
