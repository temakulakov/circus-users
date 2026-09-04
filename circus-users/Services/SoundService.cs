using Circus.Users.Core.Abstractions;
using Microsoft.UI.Xaml;

namespace Circus.Users.App.Services;

/// <summary>
/// Звуковое сопровождение действий. По умолчанию WinUI звуки не проигрывает,
/// поэтому включаем их явно из настроек пользователя.
/// </summary>
internal sealed class SoundService
{
    private readonly IPreferencesStore _preferences;

    public SoundService(IPreferencesStore preferences) => _preferences = preferences;

    public void Apply()
    {
        var preferences = _preferences.Current;

        ElementSoundPlayer.State = preferences.EnableSounds
            ? ElementSoundPlayerState.On
            : ElementSoundPlayerState.Off;

        // Пространственный звук имеет смысл только вместе с включёнными звуками.
        ElementSoundPlayer.SpatialAudioMode = preferences.EnableSounds && preferences.EnableSpatialSound
            ? ElementSpatialAudioMode.On
            : ElementSpatialAudioMode.Off;
    }
}
