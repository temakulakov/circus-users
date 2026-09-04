using Circus.Users.Core.Abstractions;

namespace Circus.Users.App.Services;

/// <summary>
/// Направляет запись в боевой каталог или в заглушку в зависимости от режима.
/// </summary>
/// <remarks>
/// Выбор делается на каждый вызов, а не один раз при создании: переключатель тестовых
/// данных работает без перезапуска приложения.
/// </remarks>
internal sealed class DirectoryWriterProxy : IDirectoryWriter
{
    private readonly IPreferencesStore _preferences;
    private readonly Func<bool, IDirectoryWriter> _resolve;

    public DirectoryWriterProxy(IPreferencesStore preferences, Func<bool, IDirectoryWriter> resolve)
    {
        _preferences = preferences;
        _resolve = resolve;
    }

    private IDirectoryWriter Current => _resolve(_preferences.Current.UseFakeData);

    public Task UpdateAttributesAsync(
        string distinguishedName,
        IReadOnlyDictionary<string, string?> changes,
        CancellationToken cancellationToken = default) =>
        Current.UpdateAttributesAsync(distinguishedName, changes, cancellationToken);

    public Task SetEnabledAsync(string distinguishedName, bool isEnabled, CancellationToken cancellationToken = default) =>
        Current.SetEnabledAsync(distinguishedName, isEnabled, cancellationToken);

    public Task<IReadOnlyList<string>> DismissAsync(string distinguishedName, CancellationToken cancellationToken = default) =>
        Current.DismissAsync(distinguishedName, cancellationToken);
}
