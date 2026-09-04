using Circus.Users.Core.Abstractions;
using Circus.Users.Core.Fakes;
using Circus.Users.Core.Models;
using Circus.Users.Core.Security;
using Microsoft.Extensions.Logging;

namespace Circus.Users.App.Services;

/// <summary>
/// Сеанс работы: кто вошёл и какие у него права.
/// </summary>
/// <remarks>
/// Приложение не спрашивает логин и пароль — пользователь уже вошёл в домен,
/// его личность берётся из маркера доступа Windows.
/// </remarks>
internal sealed class SessionService
{
    private readonly IPreferencesStore _preferences;
    private readonly WindowsCurrentUserResolver _windowsResolver;
    private readonly FakeCurrentUserResolver _fakeResolver;
    private readonly ILogger<SessionService> _logger;

    public SessionService(
        IPreferencesStore preferences,
        WindowsCurrentUserResolver windowsResolver,
        FakeCurrentUserResolver fakeResolver,
        ILogger<SessionService> logger)
    {
        _preferences = preferences;
        _windowsResolver = windowsResolver;
        _fakeResolver = fakeResolver;
        _logger = logger;

        Current = Resolve();
    }

    public CurrentUser Current { get; private set; }

    /// <summary>Перечитывает пользователя — например, после переключения режима тестовых данных.</summary>
    public void Refresh() => Current = Resolve();

    private CurrentUser Resolve()
    {
        var preferences = _preferences.Current;

        if (preferences.UseFakeData)
        {
            _fakeResolver.IsAdmin = preferences.FakeUserIsAdmin;
            var fake = _fakeResolver.Resolve();
            _logger.LogInformation("Режим тестовых данных: {Account}, администратор: {IsAdmin}", fake.AccountName, fake.IsAdmin);
            return fake;
        }

        try
        {
            var user = _windowsResolver.Resolve();
            _logger.LogInformation("Вход: {Account}, администратор: {IsAdmin}", user.AccountName, user.IsAdmin);
            return user;
        }
        catch (Exception ex)
        {
            // Без личности пользователя приложение всё равно должно открыться —
            // но только с общедоступными страницами.
            _logger.LogError(ex, "Не удалось определить текущего пользователя");
            return new CurrentUser("—", "Неизвестный пользователь", Sid: null, IsAdmin: false, IsFake: false);
        }
    }
}
