using System.Security.Cryptography;
using System.Text;

namespace Circus.Users.Core.Naming;

/// <summary>
/// Пароль для новой учётной записи.
/// </summary>
/// <remarks>
/// Похожие символы (0, O, o, 1, l, I) исключены: пароль диктуют голосом и переписывают
/// с экрана. Из каждого класса символов гарантированно берётся хотя бы один, иначе
/// случайная строка может не пройти доменную политику сложности.
/// </remarks>
public static class PasswordGenerator
{
    private const string UpperCase = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string LowerCase = "abcdefghijkmnpqrstuvwxyz";
    private const string Digits = "23456789";
    private const string Symbols = "!@#$%*+-=?";

    public static string Generate(int length = 14)
    {
        if (length < 8)
        {
            throw new ArgumentOutOfRangeException(nameof(length), "Пароль короче восьми символов недопустим.");
        }

        var alphabet = UpperCase + LowerCase + Digits + Symbols;
        var password = new List<char>(length)
        {
            Pick(UpperCase),
            Pick(LowerCase),
            Pick(Digits),
            Pick(Symbols),
        };

        while (password.Count < length)
        {
            password.Add(Pick(alphabet));
        }

        Shuffle(password);
        return new string(password.ToArray());
    }

    private static char Pick(string alphabet) => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];

    /// <summary>Перемешивание Фишера — Йетса на криптографическом источнике случайности.</summary>
    private static void Shuffle(List<char> values)
    {
        for (var i = values.Count - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (values[i], values[j]) = (values[j], values[i]);
        }
    }
}
