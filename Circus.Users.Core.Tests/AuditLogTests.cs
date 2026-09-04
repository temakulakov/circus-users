using System.Text;
using Circus.Users.Core.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Circus.Users.Core.Tests;

/// <summary>
/// Журнал операций. Файл дописывают несколько администраторов сразу,
/// поэтому проверяется и разбор строк, и порядок, и чтение занятого файла.
/// </summary>
public class AuditLogTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "circus-users-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ПустойЖурналНеСчитаетсяОшибкой()
    {
        var result = await Create().ReadAsync(100);

        Assert.Empty(result.Entries);
        Assert.Null(result.Error);
        Assert.Null(result.Source);
    }

    [Fact]
    public async Task ЗаписьЧитаетсяОбратноПоПолям()
    {
        var log = Create();
        await log.WriteAsync("CIRCUS\\a.kulakov", "Создание сотрудника", "Иванов Иван", "i.ivanov");

        var entry = Assert.Single((await log.ReadAsync(100)).Entries);

        Assert.Equal("CIRCUS\\a.kulakov", entry.Actor);
        Assert.Equal("Создание сотрудника", entry.Action);
        Assert.Equal("Иванов Иван", entry.Target);
        Assert.Equal("i.ivanov", entry.Details);
        Assert.NotNull(entry.When);
    }

    [Fact]
    public async Task НовыеЗаписиИдутСверху()
    {
        var log = Create();
        await log.WriteAsync("admin", "Первое", "цель", string.Empty);
        await log.WriteAsync("admin", "Второе", "цель", string.Empty);

        var entries = (await log.ReadAsync(100)).Entries;

        Assert.Equal("Второе", entries[0].Action);
        Assert.Equal("Первое", entries[1].Action);
    }

    [Fact]
    public async Task ВозвращаетсяНеБольшеЗапрошенного()
    {
        var log = Create();
        for (var i = 0; i < 5; i++)
        {
            await log.WriteAsync("admin", $"Действие {i}", "цель", string.Empty);
        }

        var entries = (await log.ReadAsync(2)).Entries;

        Assert.Equal(2, entries.Count);
        // Обрезается начало истории, а не конец: нужны последние события.
        Assert.Equal("Действие 4", entries[0].Action);
    }

    [Fact]
    public async Task ПереносыВПодробностяхНеЛомаютРазбор()
    {
        var log = Create();
        await log.WriteAsync("admin", "Ящик не создан", "Иванов", "строка один\nстрока два");

        var entry = Assert.Single((await log.ReadAsync(100)).Entries);

        Assert.Equal("строка один строка два", entry.Details);
    }

    [Fact]
    public async Task СтрокаЧужогоФорматаНеТеряется()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(
            Path.Combine(_directory, "audit.log"),
            "что-то совсем другое" + Environment.NewLine,
            Encoding.UTF8);

        var entry = Assert.Single((await Create().ReadAsync(100)).Entries);

        Assert.Null(entry.When);
        Assert.Equal("что-то совсем другое", entry.Details);
    }

    [Fact]
    public async Task ЖурналЧитаетсяПокаОнОткрытНаЗапись()
    {
        var log = Create();
        await log.WriteAsync("admin", "Действие", "цель", string.Empty);

        var path = Path.Combine(_directory, "audit.log");

        // Другой администратор держит файл открытым — это не должно мешать чтению.
        await using var holder = new FileStream(
            path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);

        Assert.Single((await log.ReadAsync(100)).Entries);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private FileAuditLog Create() =>
        new(
            Options.Create(new AppSettings { Storage = new StorageSettings { SharedSettingsPath = _directory } }),
            _directory,
            NullLogger<FileAuditLog>.Instance);
}
