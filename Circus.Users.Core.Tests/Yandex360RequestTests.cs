using System.Net;
using System.Text;
using Circus.Users.Core.Abstractions;
using Circus.Users.Core.Models;
using Circus.Users.Core.Settings;
using Circus.Users.Core.Yandex;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Circus.Users.Core.Tests;

/// <summary>
/// Что именно уходит в сеть при заведении ящика. Проверяем не модель, а готовый HTTP-запрос:
/// расхождение между полями объекта и телом запроса уже однажды стоило нескольких часов.
/// </summary>
public class Yandex360RequestTests
{
    [Fact]
    public async Task ТелоЗапросаСодержитПодразделение()
    {
        var handler = new CapturingHandler();
        var service = CreateService(handler);

        await service.CreateMailboxAsync(new NewMailboxRequest
        {
            Nickname = "i.ivanov",
            FirstName = "Иван",
            LastName = "Иванов",
        });

        Assert.NotNull(handler.LastBody);
        Assert.Contains("\"departmentId\":1", handler.LastBody);
        Assert.Contains("\"nickname\":\"i.ivanov\"", handler.LastBody);
    }

    [Fact]
    public async Task ЗапросИдётПоАдресуОрганизации()
    {
        var handler = new CapturingHandler();
        var service = CreateService(handler);

        await service.CreateMailboxAsync(new NewMailboxRequest
        {
            Nickname = "i.ivanov",
            FirstName = "Иван",
            LastName = "Иванов",
        });

        Assert.Equal(
            "https://api360.yandex.net/directory/v1/org/358123/users",
            handler.LastUri?.ToString());
    }

    /// <summary>
    /// Главная причина отказов API: тело без Content-Length уходит порциями,
    /// и шлюз Yandex 360 его не читает — запрос выглядит для него пустым.
    /// </summary>
    [Fact]
    public async Task ТелоЗапросаУходитОднимБлокомСИзвестнойДлиной()
    {
        var handler = new CapturingHandler();
        var service = CreateService(handler);

        await service.CreateMailboxAsync(new NewMailboxRequest
        {
            Nickname = "i.ivanov",
            FirstName = "Иван",
            LastName = "Иванов",
        });

        Assert.NotNull(handler.LastContentLength);
        Assert.Equal(Encoding.UTF8.GetByteCount(handler.LastBody!), handler.LastContentLength);
    }

    [Fact]
    public async Task ОтключениеСотрудникаТожеУходитСДлиной()
    {
        var handler = new CapturingHandler();
        var service = CreateService(handler);

        await service.SetEnabledAsync("1130000000000001", isEnabled: false);

        Assert.NotNull(handler.LastContentLength);
        Assert.Contains("\"isEnabled\":false", handler.LastBody);
    }

    /// <summary>
    /// Диагностика отказа уходит в журнал на общей шаре, поэтому пароля в ней быть не должно.
    /// Проверяем на пароле со знаком «плюс»: сериализатор экранирует его как \u002B,
    /// и вырезание пароля по тексту такой случай пропускало.
    /// </summary>
    [Fact]
    public async Task ПарольНеПопадаетВТекстОшибки()
    {
        var handler = new CapturingHandler { StatusCode = HttpStatusCode.BadRequest };
        var service = CreateService(handler);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateMailboxAsync(new NewMailboxRequest
            {
                Nickname = "i.ivanov",
                FirstName = "Иван",
                LastName = "Иванов",
            }));

        var sentPassword = ExtractPassword(handler.LastBody!);

        Assert.Contains("\"password\":\"***\"", error.Message);
        Assert.DoesNotContain(sentPassword, error.Message, StringComparison.Ordinal);
    }

    /// <summary>Достаёт пароль из отправленного тела — он генерируется внутри службы.</summary>
    private static string ExtractPassword(string body)
    {
        using var document = System.Text.Json.JsonDocument.Parse(body);
        return document.RootElement.GetProperty("password").GetString()!;
    }

    private static Yandex360MailService CreateService(CapturingHandler handler)

    {
        var settings = Options.Create(new AppSettings
        {
            Mail = new MailSettings { Domain = "circus.ru", OrgId = "358123", DefaultDepartmentId = 1 },
        });

        return new Yandex360MailService(
            new SingleClientFactory(handler),
            new FixedSecretStore(),
            settings,
            NullLogger<Yandex360MailService>.Instance);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string? LastBody { get; private set; }

        public Uri? LastUri { get; private set; }

        public long? LastContentLength { get; private set; }

        /// <summary>Каким кодом отвечать. По умолчанию — успех.</summary>
        public HttpStatusCode StatusCode { get; init; } = HttpStatusCode.OK;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastUri = request.RequestUri;
            LastContentLength = request.Content?.Headers.ContentLength;
            LastBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(StatusCode)
            {
                Content = new StringContent(
                    """{"id":"1","nickname":"i.ivanov","email":"i.ivanov@circus.ru","aliases":[]}""",
                    Encoding.UTF8,
                    "application/json"),
            };
        }
    }

    private sealed class SingleClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public SingleClientFactory(HttpMessageHandler handler) => _handler = handler;

        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private sealed class FixedSecretStore : ISecretStore
    {
        public string? Read(string key) => "test-token";

        public void Write(string key, string secret)
        {
        }

        public void Delete(string key)
        {
        }
    }
}
