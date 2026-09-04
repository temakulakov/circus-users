using System.Text.Json;
using Circus.Users.Core.Yandex;
using Xunit;

namespace Circus.Users.Core.Tests;

/// <summary>
/// Форма тела запроса на создание сотрудника. API отклоняет запрос без departmentId,
/// а увидеть это можно только на боевой организации — поэтому проверяем здесь.
/// </summary>
public class Yandex360PayloadTests
{
    private static string Serialize(long departmentId) =>
        JsonSerializer.Serialize(
            new Yandex360MailService.CreateUserBody
            {
                Nickname = "i.ivanov",
                Password = "не-важно",
                DepartmentId = departmentId,
                Name = new Yandex360MailService.NameBody { First = "Иван", Last = "Иванов" },
            },
            Yandex360MailService.Json);

    [Fact]
    public void ТелоЗапросаСодержитОбязательноеПодразделение()
    {
        var json = Serialize(1);

        Assert.Contains("\"departmentId\":1", json);
    }

    [Fact]
    public void ПоляНазываютсяТакЖеКакВДокументацииApi()
    {
        var json = Serialize(42);

        Assert.Contains("\"nickname\":\"i.ivanov\"", json);
        Assert.Contains("\"departmentId\":42", json);
        Assert.Contains("\"first\":", json);
        Assert.Contains("\"last\":", json);
    }

    [Fact]
    public void ОтчествоНеПопадаетВЗапросЕслиЕгоНет()
    {
        var json = Serialize(1);

        // Пустое отчество не отправляем: API считает пустую строку значением.
        Assert.DoesNotContain("middle", json);
    }
}
