using System.Text.Json;
using Circus.Users.Core.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Circus.Users.Core.Settings;

/// <summary>Общий вид адресной книги в JSON-файле на сетевой шаре.</summary>
public sealed class FileSharedLayoutStore : ISharedLayoutStore
{
    private const string FileName = "address-book-layout.json";

    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly StorageSettings _settings;
    private readonly ILogger<FileSharedLayoutStore> _logger;

    public FileSharedLayoutStore(IOptions<AppSettings> settings, ILogger<FileSharedLayoutStore> logger)
    {
        _settings = settings.Value.Storage;
        _logger = logger;
    }

    public IReadOnlyList<string>? Layout { get; private set; }

    public string? LastError { get; private set; }

    private string FilePath => Path.Combine(_settings.SharedSettingsPath, FileName);

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.SharedSettingsPath))
        {
            LastError = "Путь к общим настройкам не задан";
            return;
        }

        try
        {
            if (!File.Exists(FilePath))
            {
                // Это не ошибка: администратор мог ещё не применять вид для всех.
                Layout = null;
                LastError = null;
                return;
            }

            await using var stream = File.OpenRead(FilePath);
            Layout = await JsonSerializer.DeserializeAsync<List<string>>(stream, SerializerOptions, cancellationToken);
            LastError = null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Недоступная шара не должна мешать работе: останется вид по умолчанию.
            _logger.LogWarning(ex, "Не удалось прочитать общий вид адресной книги из {Path}", FilePath);
            LastError = ex.Message;
        }
    }

    public async Task SaveAsync(IReadOnlyList<string> layout, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.SharedSettingsPath))
        {
            throw new InvalidOperationException("Путь к общим настройкам не задан в конфигурации.");
        }

        Directory.CreateDirectory(_settings.SharedSettingsPath);

        // Пишем во временный файл и подменяем: обрыв связи с шарой не должен
        // оставить всех пользователей с испорченным видом.
        var tempPath = FilePath + ".tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, layout, SerializerOptions, cancellationToken);
        }

        File.Move(tempPath, FilePath, overwrite: true);

        Layout = layout;
        LastError = null;
        _logger.LogInformation("Общий вид адресной книги сохранён в {Path}", FilePath);
    }
}
