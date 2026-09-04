using Circus.Users.Core.Abstractions;
using Circus.Users.Core.Models;
using Circus.Users.Core.Naming;
using Circus.Users.Core.Settings;
using Microsoft.Extensions.Options;

namespace Circus.Users.Core.Fakes;

/// <summary>
/// Почта в тестовом режиме: отвечает как настоящая, но ничего не создаёт.
/// Занятые логины совпадают с тестовым справочником, чтобы проверка занятости
/// вела себя так же, как на боевых данных.
/// </summary>
public sealed class FakeMailService : IMailService
{
    // l.lebedev намеренно есть только здесь: по нему видно, что проверка занятости
    // заглядывает и в почту, а не только в каталог.
    private static readonly string[] Logins = ["i.ivanov", "o.kuznetsova", "m.petrova", "a.sokolova", "l.lebedev"];

    private readonly MailSettings _settings;

    public FakeMailService(IOptions<AppSettings> settings) => _settings = settings.Value.Mail;

    public bool IsConfigured => true;

    public Task<string?> TestConnectionAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(null);

    public Task<IReadOnlyList<string>> GetLoginsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<string>>(Logins);

    public Task<IReadOnlyList<MailGroup>> GetGroupsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<MailGroup>>(
        [
            new MailGroup(1, "Все сотрудники", $"all@{_settings.Domain}"),
            new MailGroup(2, "Бухгалтерия", $"buh@{_settings.Domain}"),
            new MailGroup(3, "Отдел кадров", $"hr@{_settings.Domain}"),
        ]);

    public Task<MailAccount> CreateMailboxAsync(NewMailboxRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(new MailAccount(
            "fake-user-id",
            $"{request.Nickname}@{_settings.Domain}",
            PasswordGenerator.Generate(),
            ["Тестовый режим: почтовый ящик не создан."]));

    public Task SetEnabledAsync(string userId, bool isEnabled, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
