using System.Text.Json;
using System.Text.Json.Serialization;
using Circus.Users.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace Circus.Users.Core.Settings;

/// <summary>
/// Хранит персональные настройки в JSON-файле на локальной машине.
/// Запись отложенная: подряд идущие изменения (например, перетаскивание ползунка
/// в палитре) схлопываются в одно обращение к диску.
/// </summary>
public sealed class JsonPreferencesStore : IPreferencesStore, IAsyncDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(400);

    private readonly string _filePath;
    private readonly ILogger<JsonPreferencesStore> _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    private CancellationTokenSource? _pendingSave;

    public JsonPreferencesStore(string filePath, ILogger<JsonPreferencesStore> logger)
    {
        _filePath = filePath;
        _logger = logger;
    }

    public UserPreferences Current { get; private set; } = new();

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                _logger.LogInformation("Файл настроек не найден, используем значения по умолчанию: {Path}", _filePath);
                return;
            }

            await using var stream = File.OpenRead(_filePath);
            var loaded = await JsonSerializer.DeserializeAsync<UserPreferences>(stream, SerializerOptions, cancellationToken);
            if (loaded is not null)
            {
                Current = loaded;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Настройки — не критичные данные: при повреждении файла молча откатываемся к умолчанию.
            _logger.LogWarning(ex, "Не удалось прочитать настройки из {Path}, применяем значения по умолчанию", _filePath);
            Current = new UserPreferences();
        }
    }

    /// <remarks>
    /// Источник отмены освобождает та же задача, которая им пользуется. Освобождать его
    /// здесь, сразу после <c>Cancel</c>, нельзя: отложенная запись в этот момент ещё держит
    /// на нём подписку, и при частых вызовах (быстрые щелчки по переключателю) это приводит
    /// к обращению к освобождённому объекту.
    /// </remarks>
    public void Save()
    {
        var cts = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _pendingSave, cts);
        previous?.Cancel();

        _ = DelayedSaveAsync(cts);
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        _pendingSave?.Cancel();
        await WriteAsync(cancellationToken);
    }

    private async Task DelayedSaveAsync(CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(SaveDelay, cts.Token);
            await WriteAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Пришло более свежее изменение — эта запись больше не нужна.
        }
        finally
        {
            Interlocked.CompareExchange(ref _pendingSave, null, cts);
            cts.Dispose();
        }
    }

    private async Task WriteAsync(CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Пишем во временный файл и подменяем, чтобы аварийное завершение
            // не оставило пользователя с обрезанным JSON.
            var tempPath = _filePath + ".tmp";
            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, Current, SerializerOptions, cancellationToken);
            }

            File.Move(tempPath, _filePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Не удалось сохранить настройки в {Path}", _filePath);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await FlushAsync();
        _writeLock.Dispose();
    }
}
