using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Circus.Users.Core.Abstractions;
using Circus.Users.Core.Models;
using Circus.Users.Core.Naming;
using Circus.Users.Core.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Circus.Users.Core.Yandex;

/// <summary>
/// Корпоративная почта через Yandex 360 Directory API.
/// </summary>
/// <remarks>
/// Токен берётся из диспетчера учётных данных при каждом запросе, а не кэшируется в поле:
/// администратор может заменить его в настройках, и перезапуск для этого не нужен.
/// Ответы разбираются по узкому набору полей — организация может добавлять свои,
/// и лишние ключи не должны ломать разбор.
/// </remarks>
public sealed class Yandex360MailService : IMailService
{
    /// <summary>Ключ, под которым токен лежит в диспетчере учётных данных Windows.</summary>
    public const string TokenKey = "circus-users:yandex360";

    private const string BaseAddress = "https://api360.yandex.net/directory/v1/";
    private const int PageSize = 100;

    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IHttpClientFactory _clients;
    private readonly ISecretStore _secrets;
    private readonly MailSettings _settings;
    private readonly ILogger<Yandex360MailService> _logger;

    public Yandex360MailService(
        IHttpClientFactory clients,
        ISecretStore secrets,
        IOptions<AppSettings> settings,
        ILogger<Yandex360MailService> logger)
    {
        _clients = clients;
        _secrets = secrets;
        _settings = settings.Value.Mail;
        _logger = logger;
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_settings.OrgId) && !string.IsNullOrWhiteSpace(_secrets.Read(TokenKey));

    public async Task<string?> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.OrgId))
        {
            return "В настройках приложения не указан идентификатор организации.";
        }

        if (string.IsNullOrWhiteSpace(_secrets.Read(TokenKey)))
        {
            return "Токен доступа не задан.";
        }

        try
        {
            using var client = CreateClient();
            using var response = await client.GetAsync($"users?perPage=1&page=1", cancellationToken);

            return response.IsSuccessStatusCode
                ? null
                : await DescribeFailureAsync(response, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return $"Не удалось связаться с Yandex 360: {ex.Message}";
        }
    }

    public async Task<IReadOnlyList<string>> GetLoginsAsync(CancellationToken cancellationToken = default)
    {
        var logins = new List<string>();

        await foreach (var user in ReadUsersAsync(cancellationToken))
        {
            if (!string.IsNullOrWhiteSpace(user.Nickname))
            {
                logins.Add(user.Nickname);
            }

            // Псевдонимы занимают адрес так же, как основной логин.
            logins.AddRange(user.Aliases.Where(alias => !string.IsNullOrWhiteSpace(alias)));
        }

        return logins;
    }

    public async Task<IReadOnlyList<MailGroup>> GetGroupsAsync(CancellationToken cancellationToken = default)
    {
        using var client = CreateClient();

        var groups = new List<MailGroup>();
        var page = 1;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var payload = await GetAsync<GroupPage>(client, $"groups?perPage={PageSize}&page={page}", cancellationToken);

            groups.AddRange(payload.Groups.Select(group => new MailGroup(group.Id, group.Name ?? string.Empty, group.Email ?? string.Empty)));

            if (page >= payload.Pages || payload.Groups.Count == 0)
            {
                break;
            }

            page++;
        }

        return groups;
    }

    public async Task<MailAccount> CreateMailboxAsync(
        NewMailboxRequest request,
        CancellationToken cancellationToken = default)
    {
        using var client = CreateClient();

        var password = PasswordGenerator.Generate();

        var body = new CreateUserBody
        {
            Nickname = request.Nickname,
            Password = password,
            DepartmentId = request.DepartmentId ?? _settings.DefaultDepartmentId,
            Name = new NameBody
            {
                First = request.FirstName,
                Last = request.LastName,
                Middle = string.IsNullOrWhiteSpace(request.MiddleName) ? null : request.MiddleName,
            },
        };

        // Тело сериализуется явно: так тот же текст уходит в сеть и попадает
        // в диагностику отказа, и расхождение между ними невозможно.
        var json = JsonSerializer.Serialize(body, Json);

        using var content = JsonBody(json);
        using var response = await client.PostAsync("users", content, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            // Диагностика уходит в журнал на общей шаре, поэтому пароль в неё не попадает.
            // Заменяется не текст пароля в готовом JSON, а само поле: сериализатор
            // экранирует символы, и подстроки пароля в JSON может просто не оказаться.
            var sent = JsonSerializer.Serialize(body with { Password = "***" }, Json);

            _logger.LogWarning("Yandex 360 отклонил создание ящика ({Sent})", sent);

            throw new InvalidOperationException(
                $"{await DescribeFailureAsync(response, cancellationToken)} Отправлено: {sent}");
        }

        var created = await response.Content.ReadFromJsonAsync<UserDto>(Json, cancellationToken)
                      ?? throw new InvalidOperationException("Yandex 360 вернул пустой ответ на создание сотрудника.");

        var warnings = new List<string>();
        await AddToGroupsAsync(client, created.Id, request.Groups, warnings, cancellationToken);

        _logger.LogInformation("Создан почтовый ящик {Nickname}", request.Nickname);

        return new MailAccount(created.Id, created.Email ?? $"{request.Nickname}@{_settings.Domain}", password, warnings);
    }

    public async Task SetEnabledAsync(string userId, bool isEnabled, CancellationToken cancellationToken = default)
    {
        using var client = CreateClient();
        using var content = JsonBody(JsonSerializer.Serialize(new { isEnabled }, Json));
        using var response = await client.PatchAsync($"users/{userId}", content, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(await DescribeFailureAsync(response, cancellationToken));
        }
    }

    /// <summary>
    /// Добавляет в группы рассылки. Промах по одной группе не отменяет заведение ящика:
    /// он уже создан, и такие случаи возвращаются предупреждениями.
    /// </summary>
    private async Task AddToGroupsAsync(
        HttpClient client,
        string userId,
        IReadOnlyList<string> groupNames,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        if (groupNames.Count == 0)
        {
            return;
        }

        var groups = await GetGroupsAsync(cancellationToken);

        foreach (var name in groupNames)
        {
            var group = groups.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase)
                || string.Equals(candidate.Email, name, StringComparison.OrdinalIgnoreCase));

            if (group is null)
            {
                warnings.Add($"Группа рассылки «{name}» не найдена — добавьте вручную.");
                continue;
            }

            try
            {
                using var member = JsonBody(JsonSerializer.Serialize(new { type = "user", id = userId }, Json));
                using var response = await client.PostAsync(
                    $"groups/{group.Id}/members",
                    member,
                    cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    warnings.Add($"Не удалось добавить в «{name}»: {await DescribeFailureAsync(response, cancellationToken)}");
                }
            }
            catch (HttpRequestException ex)
            {
                warnings.Add($"Не удалось добавить в «{name}»: {ex.Message}");
            }
        }
    }

    private async IAsyncEnumerable<UserDto> ReadUsersAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var client = CreateClient();
        var page = 1;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var payload = await GetAsync<UserPage>(client, $"users?perPage={PageSize}&page={page}", cancellationToken);

            foreach (var user in payload.Users)
            {
                yield return user;
            }

            if (page >= payload.Pages || payload.Users.Count == 0)
            {
                break;
            }

            page++;
        }
    }

    private async Task<T> GetAsync<T>(HttpClient client, string path, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(path, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(await DescribeFailureAsync(response, cancellationToken));
        }

        return await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken)
               ?? throw new InvalidOperationException($"Yandex 360 вернул пустой ответ на запрос {path}.");
    }

    /// <summary>
    /// Тело запроса с явной длиной.
    /// </summary>
    /// <remarks>
    /// Штатные PostAsJsonAsync и PatchAsJsonAsync сериализуют прямо в поток и потому
    /// отправляют тело порциями, без Content-Length. Шлюз Yandex 360 такое тело не читает:
    /// запрос доходит, но выглядит для него пустым, и API отвечает, что обязательные
    /// поля не заполнены. Поэтому весь JSON готовится заранее и уходит одним блоком.
    /// </remarks>
    private static StringContent JsonBody(string json) => new(json, Encoding.UTF8, "application/json");

    /// <summary>Превращает ответ об ошибке в текст, понятный администратору.</summary>
    private static async Task<string> DescribeFailureAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var reason = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "токен недействителен или истёк",
            HttpStatusCode.Forbidden => "у токена нет прав на управление сотрудниками",
            HttpStatusCode.NotFound => "организация не найдена — проверьте orgId",
            HttpStatusCode.Conflict => "такой логин уже занят",
            HttpStatusCode.UnprocessableEntity => "запрос отклонён — проверьте подразделение и пароль",
            HttpStatusCode.TooManyRequests => "превышен лимит запросов, повторите позже",
            _ => $"код ответа {(int)response.StatusCode}",
        };

        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        // Отдельные коды API приходят кодом 400 и различаются только текстом.
        // Самый частый из них — исчерпанные места в организации; объясняем его словами.
        if (body.Contains("users_limit_error", StringComparison.OrdinalIgnoreCase))
        {
            return "Yandex 360: в организации закончились места для сотрудников. "
                   + "Освободите или докупите места в админке, затем повторите создание ящика.";
        }

        var details = body.Length > 200 ? body[..200] : body;

        return details.Length > 0 ? $"Yandex 360: {reason}. {details}" : $"Yandex 360: {reason}.";
    }

    private HttpClient CreateClient()
    {
        var token = _secrets.Read(TokenKey)
                    ?? throw new InvalidOperationException("Токен доступа к Yandex 360 не задан. Укажите его в настройках.");

        var client = _clients.CreateClient(nameof(Yandex360MailService));
        client.BaseAddress = new Uri($"{BaseAddress}org/{_settings.OrgId}/");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("OAuth", token);

        return client;
    }

    private sealed class UserPage
    {
        public List<UserDto> Users { get; init; } = [];

        public int Pages { get; init; } = 1;
    }

    private sealed class UserDto
    {
        public string Id { get; init; } = string.Empty;

        public string? Nickname { get; init; }

        public string? Email { get; init; }

        public List<string> Aliases { get; init; } = [];
    }

    private sealed class GroupPage
    {
        public List<GroupDto> Groups { get; init; } = [];

        public int Pages { get; init; } = 1;
    }

    private sealed class GroupDto
    {
        public long Id { get; init; }

        public string? Name { get; init; }

        public string? Email { get; init; }
    }

    internal sealed record CreateUserBody
    {
        public required string Nickname { get; init; }

        public required string Password { get; init; }

        /// <summary>Обязательное поле API: без него создание отклоняется.</summary>
        public required long DepartmentId { get; init; }

        public required NameBody Name { get; init; }
    }

    internal sealed record NameBody
    {
        public required string First { get; init; }

        public required string Last { get; init; }

        public string? Middle { get; init; }
    }
}
