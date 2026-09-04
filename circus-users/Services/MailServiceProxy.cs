using Circus.Users.Core.Abstractions;
using Circus.Users.Core.Models;

namespace Circus.Users.App.Services;

/// <summary>
/// Направляет почтовые операции в Yandex 360 или в заглушку в зависимости от режима.
/// Выбор делается на каждый вызов, чтобы переключатель тестовых данных работал сразу.
/// </summary>
internal sealed class MailServiceProxy : IMailService
{
    private readonly IPreferencesStore _preferences;
    private readonly Func<bool, IMailService> _resolve;

    public MailServiceProxy(IPreferencesStore preferences, Func<bool, IMailService> resolve)
    {
        _preferences = preferences;
        _resolve = resolve;
    }

    private IMailService Current => _resolve(_preferences.Current.UseFakeData);

    public bool IsConfigured => Current.IsConfigured;

    public Task<string?> TestConnectionAsync(CancellationToken cancellationToken = default) =>
        Current.TestConnectionAsync(cancellationToken);

    public Task<IReadOnlyList<string>> GetLoginsAsync(CancellationToken cancellationToken = default) =>
        Current.GetLoginsAsync(cancellationToken);

    public Task<IReadOnlyList<MailGroup>> GetGroupsAsync(CancellationToken cancellationToken = default) =>
        Current.GetGroupsAsync(cancellationToken);

    public Task<MailAccount> CreateMailboxAsync(NewMailboxRequest request, CancellationToken cancellationToken = default) =>
        Current.CreateMailboxAsync(request, cancellationToken);

    public Task SetEnabledAsync(string userId, bool isEnabled, CancellationToken cancellationToken = default) =>
        Current.SetEnabledAsync(userId, isEnabled, cancellationToken);
}
