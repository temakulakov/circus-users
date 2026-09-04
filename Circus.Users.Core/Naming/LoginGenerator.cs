namespace Circus.Users.Core.Naming;

/// <summary>Результат подбора логина.</summary>
/// <param name="Login">Предложенный логин или пустая строка, если подобрать не удалось.</param>
/// <param name="Taken">Кандидаты, которые оказались заняты — их показываем подсказкой под полем.</param>
/// <param name="WasTruncated">Фамилию пришлось сократить из-за предела в 20 символов.</param>
/// <param name="Problem">Причина, по которой логин не подобран, или null.</param>
public sealed record LoginSuggestion(
    string Login,
    IReadOnlyList<string> Taken,
    bool WasTruncated,
    string? Problem)
{
    public bool IsResolved => Login.Length > 0 && Problem is null;
}

/// <summary>
/// Подбирает учётную запись вида «первые буквы имени».«фамилия».
/// </summary>
/// <remarks>
/// Ивановым Иваном займётся i.ivanov; если он занят Игорем, следующим кандидатом
/// станет iv.ivanov и так далее по буквам имени. Когда буквы имени кончились —
/// значит, полный тёзка уже есть, и логин должен назначить администратор вручную:
/// придумывать за него числовые суффиксы приложение не будет.
/// </remarks>
public static class LoginGenerator
{
    /// <summary>Предел sAMAccountName в Active Directory.</summary>
    public const int MaxAccountNameLength = 20;

    /// <param name="isTaken">Проверка занятости: и в каталоге, и в почте.</param>
    public static LoginSuggestion Generate(RussianFullName name, Func<string, bool> isTaken)
    {
        if (!name.IsComplete)
        {
            return new LoginSuggestion(string.Empty, [], false, null);
        }

        var surname = Transliterator.Translate(name.Surname);
        var given = Transliterator.Translate(name.GivenName);

        if (surname.Length == 0 || given.Length == 0)
        {
            return new LoginSuggestion(string.Empty, [], false, "Не удалось перевести ФИО в латиницу.");
        }

        var taken = new List<string>();

        for (var prefixLength = 1; prefixLength <= given.Length; prefixLength++)
        {
            var (candidate, wasTruncated) = Compose(given[..prefixLength], surname);

            if (!isTaken(candidate))
            {
                return new LoginSuggestion(candidate, taken, wasTruncated, null);
            }

            taken.Add(candidate);
        }

        return new LoginSuggestion(
            string.Empty,
            taken,
            false,
            $"Все варианты от {taken[0]} до {taken[^1]} заняты — похоже, полный тёзка уже есть. Задайте логин вручную.");
    }

    /// <summary>
    /// Собирает логин и укладывает его в предел длины.
    /// Сокращается фамилия, а не приставка: приставка отличает однофамильцев.
    /// </summary>
    private static (string Login, bool WasTruncated) Compose(string prefix, string surname)
    {
        var available = MaxAccountNameLength - prefix.Length - 1;

        if (surname.Length <= available)
        {
            return ($"{prefix}.{surname}", false);
        }

        return ($"{prefix}.{surname[..Math.Max(1, available)]}", true);
    }
}
