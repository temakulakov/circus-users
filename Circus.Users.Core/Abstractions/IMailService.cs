using Circus.Users.Core.Models;

namespace Circus.Users.Core.Abstractions;

/// <summary>Работа с корпоративной почтой.</summary>
public interface IMailService
{
    /// <summary>Настроен ли доступ: есть ли токен и идентификатор организации.</summary>
    bool IsConfigured { get; }

    /// <summary>Проверяет доступ. Возвращает описание ошибки или null, если всё в порядке.</summary>
    Task<string?> TestConnectionAsync(CancellationToken cancellationToken = default);

    /// <summary>Все занятые почтовые логины организации.</summary>
    Task<IReadOnlyList<string>> GetLoginsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MailGroup>> GetGroupsAsync(CancellationToken cancellationToken = default);

    Task<MailAccount> CreateMailboxAsync(NewMailboxRequest request, CancellationToken cancellationToken = default);

    Task SetEnabledAsync(string userId, bool isEnabled, CancellationToken cancellationToken = default);
}
