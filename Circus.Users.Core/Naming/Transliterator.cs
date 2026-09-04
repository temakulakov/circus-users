using System.Text;

namespace Circus.Users.Core.Naming;

/// <summary>
/// Перевод кириллицы в латиницу для логинов и почтовых адресов.
/// </summary>
/// <remarks>
/// Таблица соответствует практике, принятой в корпоративных каталогах:
/// х→kh, ц→ts, щ→sch, ъ и ь опускаются. Она намеренно зафиксирована здесь,
/// а не берётся из системной локали: логин сотрудника не должен зависеть
/// от настроек машины, на которой его создавали.
/// </remarks>
public static class Transliterator
{
    private static readonly Dictionary<char, string> Map = new()
    {
        ['а'] = "a", ['б'] = "b", ['в'] = "v", ['г'] = "g", ['д'] = "d",
        ['е'] = "e", ['ё'] = "e", ['ж'] = "zh", ['з'] = "z", ['и'] = "i",
        ['й'] = "y", ['к'] = "k", ['л'] = "l", ['м'] = "m", ['н'] = "n",
        ['о'] = "o", ['п'] = "p", ['р'] = "r", ['с'] = "s", ['т'] = "t",
        ['у'] = "u", ['ф'] = "f", ['х'] = "kh", ['ц'] = "ts", ['ч'] = "ch",
        ['ш'] = "sh", ['щ'] = "sch", ['ъ'] = "", ['ы'] = "y", ['ь'] = "",
        ['э'] = "e", ['ю'] = "yu", ['я'] = "ya",
    };

    /// <summary>
    /// Переводит слово в латиницу. Дефисы в двойных фамилиях сохраняются,
    /// остальные посторонние символы отбрасываются.
    /// </summary>
    public static string Translate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var result = new StringBuilder(value.Length * 2);

        foreach (var symbol in value.Trim().ToLowerInvariant())
        {
            if (Map.TryGetValue(symbol, out var replacement))
            {
                result.Append(replacement);
            }
            else if (symbol is >= 'a' and <= 'z' or >= '0' and <= '9' || symbol == '-')
            {
                result.Append(symbol);
            }
        }

        return result.ToString();
    }
}
