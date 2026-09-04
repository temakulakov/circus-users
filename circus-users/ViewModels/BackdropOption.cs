using Circus.Users.Core.Settings;

namespace Circus.Users.App.ViewModels;

/// <summary>Пункт списка «Прозрачность окна». Показываются только режимы, доступные на этом компьютере.</summary>
public sealed class BackdropOption
{
    internal BackdropOption(BackdropMode mode)
    {
        Mode = mode;
        Title = mode switch
        {
            BackdropMode.FollowSystem => "Как в системе",
            BackdropMode.Mica => "Mica",
            BackdropMode.MicaAlt => "Mica Alt",
            BackdropMode.Acrylic => "Акрил",
            _ => "Выключена",
        };
    }

    internal BackdropMode Mode { get; }

    public string Title { get; }
}
