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

    public async Task<AuditReadResult> ReadAsync(int maxEntries, CancellationToken cancellationToken = default)
    {
        // Смотрим туда же, куда пишем: сначала общая шара, потом резервный каталог.
        foreach (var directory in new[] { _settings.SharedSettingsPath, _fallbackDirectory })
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                continue;
            }

            var path = Path.Combine(directory, FileName);
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                // Журнал открыт на дозапись у других администраторов, поэтому читаем
                // с разделением доступа, а не File.ReadAllLines.
                var lines = await ReadLinesAsync(path, cancellationToken);

                var entries = lines
                    .AsEnumerable()
                    .Reverse()
                    .Where(line => !string.IsNullOrWhiteSpace(line))
                    .Take(maxEntries)
                    .Select(Parse)
                    .ToList();

                return new AuditReadResult(entries, path, null);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Не удалось прочитать журнал операций из {Path}", path);
                return new AuditReadResult([], path, ex.Message);
            }
        }

        return new AuditReadResult([], null, null);
    }

    private static async Task<List<string>> ReadLinesAsync(string path, CancellationToken cancellationToken)
    {
        var lines = new List<string>();

        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            lines.Add(line);
        }

        return lines;
    }

    /// <summary>Разбирает строку журнала. Неразобранная строка не теряется — она видна целиком.</summary>
    private static AuditEntry Parse(string line)
    {
        var parts = line.Split('\t');

        if (parts.Length < 5)
        {
            return new AuditEntry(null, string.Empty, string.Empty, string.Empty, line);
        }

        DateTimeOffset? when = DateTimeOffset.TryParse(parts[0], out var parsed) ? parsed : null;

        return new AuditEntry(when, parts[1], parts[2], parts[3], string.Join('\t', parts[4..]));
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
