using System.Globalization;
using Windows.UI;

namespace Circus.Users.App.Services;

/// <summary>Преобразование цвета в строку вида #RRGGBB и обратно — формат хранения настроек.</summary>
internal static class ColorHex
{
    public static string ToHex(Color color) =>
        string.Create(CultureInfo.InvariantCulture, $"#{color.R:X2}{color.G:X2}{color.B:X2}");

    public static bool TryParse(string? hex, out Color color)
    {
        color = default;

        if (string.IsNullOrWhiteSpace(hex))
        {
            return false;
        }

        var span = hex.AsSpan().Trim();
        if (span.Length > 0 && span[0] == '#')
        {
            span = span[1..];
        }

        if (span.Length != 6 ||
            !byte.TryParse(span[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r) ||
            !byte.TryParse(span[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g) ||
            !byte.TryParse(span[4..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
        {
            return false;
        }

        color = Color.FromArgb(255, r, g, b);
        return true;
    }
}
