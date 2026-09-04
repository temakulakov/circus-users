namespace Circus.Users.Core.Naming;

/// <summary>
/// Разобранное ФИО. Порядок ввода — «Фамилия Имя Отчество», как в кадровых документах.
/// </summary>
public sealed record RussianFullName(string Surname, string GivenName, string Patronymic)
{
    public static RussianFullName Empty { get; } = new(string.Empty, string.Empty, string.Empty);

    /// <summary>Есть ли из чего строить логин.</summary>
    public bool IsComplete => Surname.Length > 0 && GivenName.Length > 0;

    /// <summary>Выводимое имя: то, что было введено, но без лишних пробелов.</summary>
    public string DisplayName => string.Join(' ', new[] { Surname, GivenName, Patronymic }.Where(part => part.Length > 0));

    public static RussianFullName Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Empty;
        }

        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return new RussianFullName(
            Surname: parts.Length > 0 ? Capitalize(parts[0]) : string.Empty,
            GivenName: parts.Length > 1 ? Capitalize(parts[1]) : string.Empty,
            // Всё, что после отчества, приклеивается к нему: встречаются составные отчества.
            Patronymic: parts.Length > 2 ? string.Join(' ', parts[2..].Select(Capitalize)) : string.Empty);
    }

    private static string Capitalize(string part) =>
        part.Length == 0 ? part : char.ToUpperInvariant(part[0]) + part[1..].ToLowerInvariant();
}
