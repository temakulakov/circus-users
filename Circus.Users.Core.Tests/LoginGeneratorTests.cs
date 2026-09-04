using Circus.Users.Core.Naming;
using Xunit;

namespace Circus.Users.Core.Tests;

public class TransliteratorTests
{
    [Theory]
    [InlineData("Иванов", "ivanov")]
    [InlineData("Щербаков", "scherbakov")]
    [InlineData("Хохлов", "khokhlov")]
    [InlineData("Цыганков", "tsygankov")]
    [InlineData("Ёлкин", "elkin")]
    [InlineData("Юрьев", "yurev")]
    [InlineData("Объедков", "obedkov")]
    [InlineData("Салтыков-Щедрин", "saltykov-schedrin")]
    public void ПереводитФамилииВЛатиницу(string source, string expected) =>
        Assert.Equal(expected, Transliterator.Translate(source));

    [Fact]
    public void ОтбрасываетПосторонниеСимволы() =>
        Assert.Equal("ivanov", Transliterator.Translate("  Ива нов!  "));
}

public class RussianFullNameTests
{
    [Fact]
    public void РазбираетФамилиюИмяОтчество()
    {
        var name = RussianFullName.Parse("иванов иван иванович");

        Assert.Equal("Иванов", name.Surname);
        Assert.Equal("Иван", name.GivenName);
        Assert.Equal("Иванович", name.Patronymic);
        Assert.Equal("Иванов Иван Иванович", name.DisplayName);
    }

    [Fact]
    public void СчитаетНеполнымИмяБезФамилииИлиИмени()
    {
        Assert.False(RussianFullName.Parse("Иванов").IsComplete);
        Assert.True(RussianFullName.Parse("Иванов Иван").IsComplete);
    }

    [Fact]
    public void СклеиваетСоставноеОтчество() =>
        Assert.Equal("Оглы Мамедов", RussianFullName.Parse("Алиев Рашид Оглы Мамедов").Patronymic);
}

public class LoginGeneratorTests
{
    private static LoginSuggestion Generate(string fullName, params string[] taken) =>
        LoginGenerator.Generate(
            RussianFullName.Parse(fullName),
            login => taken.Contains(login, StringComparer.OrdinalIgnoreCase));

    [Fact]
    public void БерётПервуюБуквуИмениКогдаСвободно()
    {
        var result = Generate("Иванов Иван Иванович");

        Assert.Equal("i.ivanov", result.Login);
        Assert.True(result.IsResolved);
        Assert.Empty(result.Taken);
    }

    [Fact]
    public void НаращиваетБуквыИмениПриЗанятости()
    {
        // Игорь Иванов уже занял i.ivanov — Ивану достаётся iv.ivanov.
        var result = Generate("Иванов Иван Иванович", "i.ivanov");

        Assert.Equal("iv.ivanov", result.Login);
        Assert.Equal(["i.ivanov"], result.Taken);
    }

    [Fact]
    public void ПеречисляетВсеЗанятыеВарианты()
    {
        var result = Generate("Иванов Иван Иванович", "i.ivanov", "iv.ivanov");

        Assert.Equal("iva.ivanov", result.Login);
        Assert.Equal(["i.ivanov", "iv.ivanov"], result.Taken);
    }

    [Fact]
    public void СообщаетОПолномТёзкеВместоЧисловогоСуффикса()
    {
        var result = Generate("Иванов Иван Иванович", "i.ivanov", "iv.ivanov", "iva.ivanov", "ivan.ivanov");

        Assert.False(result.IsResolved);
        Assert.Empty(result.Login);
        Assert.NotNull(result.Problem);
        Assert.Contains("вручную", result.Problem);
    }

    [Fact]
    public void УкладываетсяВПределДлиныУчётнойЗаписи()
    {
        // Фамилия в латинице длиннее предела, поэтому сокращается именно она,
        // а приставка от имени остаётся: она различает однофамильцев.
        var result = Generate("Верхнеконстантинопольский Владимир Владимирович");

        Assert.Equal(LoginGenerator.MaxAccountNameLength, result.Login.Length);
        Assert.True(result.WasTruncated);
        Assert.StartsWith("v.", result.Login);
    }

    [Fact]
    public void НичегоНеПредлагаетПоНеполномуИмени()
    {
        var result = Generate("Иванов");

        Assert.Empty(result.Login);
        Assert.Null(result.Problem);
    }
}

public class PasswordGeneratorTests
{
    [Fact]
    public void СодержитВсеКлассыСимволов()
    {
        var password = PasswordGenerator.Generate();

        Assert.Equal(14, password.Length);
        Assert.Contains(password, char.IsUpper);
        Assert.Contains(password, char.IsLower);
        Assert.Contains(password, char.IsDigit);
        Assert.Contains(password, symbol => !char.IsLetterOrDigit(symbol));
    }

    [Fact]
    public void НеСодержитПохожихСимволов()
    {
        // Пароль диктуют голосом, поэтому 0/O и 1/l/I исключены.
        for (var attempt = 0; attempt < 200; attempt++)
        {
            Assert.DoesNotContain(PasswordGenerator.Generate(), symbol => "0Oo1lI".Contains(symbol));
        }
    }

    [Fact]
    public void НеДопускаетСлишкомКороткийПароль() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => PasswordGenerator.Generate(6));
}
