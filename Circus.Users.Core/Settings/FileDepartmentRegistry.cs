using System.Text.Json;
using Circus.Users.Core.Abstractions;
using Circus.Users.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Circus.Users.Core.Settings;

/// <summary>Реестр подразделений в JSON-файле на общей шаре.</summary>
/// <remarks>
/// Запись идёт по схеме «перечитать — изменить — записать»: администраторов несколько,
/// и отметка одного не должна затирать отметку другого, сделанную минутой раньше.
/// Файл подменяется через временный, чтобы обрыв связи с шарой не оставил всех
/// с наполовину записанным реестром.
/// </remarks>
public sealed class FileDepartmentRegistry : IDepartmentRegistry
{
    private const string FileName = "departments.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly StorageSettings _settings;
    private readonly ILogger<FileDepartmentRegistry> _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    private DepartmentRegistryFile _file = new();

    public FileDepartmentRegistry(IOptions<AppSettings> settings, ILogger<FileDepartmentRegistry> logger)
    {
        _settings = settings.Value.Storage;
        _logger = logger;
    }

    public event EventHandler? Changed;

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
            _file = await ReadAsync(cancellationToken);
            LastError = null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Недоступная шара не должна мешать работе: все каталоги останутся отделами.
            _logger.LogWarning(ex, "Не удалось прочитать реестр подразделений из {Path}", FilePath);
            LastError = ex.Message;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public bool IsDepartment(string organizationalUnitDn) =>
        !_file.Records.TryGetValue(organizationalUnitDn, out var record) || record.IsDepartment;

    public DepartmentRecord? Find(string organizationalUnitDn) =>
        _file.Records.TryGetValue(organizationalUnitDn, out var record) ? record : null;

    public async Task SetIsDepartmentAsync(
        string organizationalUnitDn,
        string name,
        bool isDepartment,
        string actor,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.SharedSettingsPath))
        {
            throw new InvalidOperationException("Путь к общим настройкам не задан в конфигурации.");
        }

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var file = await ReadAsync(cancellationToken);

            file.Records[organizationalUnitDn] = new DepartmentRecord
            {
                Name = name,
                IsDepartment = isDepartment,
                UpdatedBy = actor,
                UpdatedAt = DateTimeOffset.Now,
            };

            Directory.CreateDirectory(_settings.SharedSettingsPath);

            var tempPath = FilePath + ".tmp";
            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, file, SerializerOptions, cancellationToken);
            }

            File.Move(tempPath, FilePath, overwrite: true);

            _file = file;
            LastError = null;
            _logger.LogInformation(
                "Каталог {Dn} помечен как {State}",
                organizationalUnitDn,
                isDepartment ? "подразделение" : "не подразделение");
        }
        finally
        {
            _writeLock.Release();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private async Task<DepartmentRegistryFile> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(FilePath))
        {
            // Пустой реестр — не ошибка: до первой отметки файла просто нет.
            return new DepartmentRegistryFile();
        }

        await using var stream = File.OpenRead(FilePath);
        var file = await JsonSerializer.DeserializeAsync<DepartmentRegistryFile>(
            stream, SerializerOptions, cancellationToken);

        if (file is null)
        {
            return new DepartmentRegistryFile();
        }

        // Различающиеся имена сравниваются без учёта регистра: каталог отдаёт их
        // в разном написании в зависимости от способа чтения.
        file.Records = new Dictionary<string, DepartmentRecord>(file.Records, StringComparer.OrdinalIgnoreCase);
        return file;
    }
}
