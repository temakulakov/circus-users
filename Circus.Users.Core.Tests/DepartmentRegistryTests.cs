using Circus.Users.Core.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Circus.Users.Core.Tests;

/// <summary>
/// Реестр подразделений: отметка «не отдел» лежит в общих настройках,
/// и от неё зависит, можно ли заводить сотрудников в каталоге.
/// </summary>
public class DepartmentRegistryTests : IDisposable
{
    private const string Dn = "OU=Архив,OU=RGC,DC=Circus,DC=local";

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "circus-users-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void НеизвестныйКаталогСчитаетсяПодразделением()
    {
        var registry = Create();

        // Недоступная или пустая шара не должна запрещать обычную работу.
        Assert.True(registry.IsDepartment(Dn));
    }

    [Fact]
    public async Task ОтметкаСохраняетсяИЧитаетсяЗаново()
    {
        var registry = Create();
        await registry.SetIsDepartmentAsync(Dn, "Архив", isDepartment: false, "CIRCUS\\a.kulakov");

        var reopened = Create();
        await reopened.LoadAsync();

        Assert.False(reopened.IsDepartment(Dn));
        Assert.Equal("Архив", reopened.Find(Dn)?.Name);
        Assert.Equal("CIRCUS\\a.kulakov", reopened.Find(Dn)?.UpdatedBy);
    }

    [Fact]
    public async Task ОтметкаСнимаетсяОбратно()
    {
        var registry = Create();
        await registry.SetIsDepartmentAsync(Dn, "Архив", isDepartment: false, "admin");
        await registry.SetIsDepartmentAsync(Dn, "Архив", isDepartment: true, "admin");

        Assert.True(registry.IsDepartment(Dn));
    }

    [Fact]
    public async Task РазличающеесяИмяСравниваетсяБезУчётаРегистра()
    {
        var registry = Create();
        await registry.SetIsDepartmentAsync(Dn, "Архив", isDepartment: false, "admin");

        var reopened = Create();
        await reopened.LoadAsync();

        // Каталог отдаёт различающиеся имена в разном написании в зависимости от способа чтения.
        Assert.False(reopened.IsDepartment(Dn.ToUpperInvariant()));
    }

    [Fact]
    public async Task ОтметкаДругогоАдминистратораНеЗатирается()
    {
        var first = Create();
        var second = Create();

        await first.SetIsDepartmentAsync("OU=A,DC=Circus", "A", isDepartment: false, "admin1");

        // Второй экземпляр реестра не читал файл после чужой записи — но перечитает перед своей.
        await second.SetIsDepartmentAsync("OU=B,DC=Circus", "B", isDepartment: false, "admin2");

        var reopened = Create();
        await reopened.LoadAsync();

        Assert.False(reopened.IsDepartment("OU=A,DC=Circus"));
        Assert.False(reopened.IsDepartment("OU=B,DC=Circus"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private FileDepartmentRegistry Create() =>
        new(
            Options.Create(new AppSettings { Storage = new StorageSettings { SharedSettingsPath = _directory } }),
            NullLogger<FileDepartmentRegistry>.Instance);
}
