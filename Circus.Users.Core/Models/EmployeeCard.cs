namespace Circus.Users.Core.Models;

/// <summary>Сотрудник из каталога. Значения хранятся как есть, набор колонок задаётся настройками вида.</summary>
public sealed class EmployeeCard
{
    private readonly Dictionary<string, string> _attributes;

    public EmployeeCard(string distinguishedName, Dictionary<string, string> attributes)
    {
        DistinguishedName = distinguishedName;
        _attributes = attributes;

        DisplayName = Get("displayName") ?? Get("cn") ?? Get("sAMAccountName") ?? "Без имени";

        // Подразделение — это OU, в котором лежит учётная запись; атрибут department
        // остаётся запасным вариантом, он заполнен далеко не у всех.
        Department = Get(DirectoryFields.DepartmentAttribute) ?? Get("department") ?? "Без подразделения";

        // Строка для поиска считается один раз: перебирать словарь на каждое нажатие клавиши дорого.
        SearchText = string.Join(' ', attributes.Values).ToLowerInvariant();
    }

    public string DistinguishedName { get; }

    public string DisplayName { get; }

    public string Department { get; }

    public string SearchText { get; }

    /// <summary>Значение атрибута или null, если он пуст.</summary>
    public string? Get(string attribute) =>
        _attributes.TryGetValue(attribute, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    /// <summary>Инициалы для кружка в первой колонке.</summary>
    public string Initials
    {
        get
        {
            var parts = DisplayName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length >= 2
                ? $"{parts[0][0]}{parts[1][0]}".ToUpperInvariant()
                : DisplayName[..Math.Min(2, DisplayName.Length)].ToUpperInvariant();
        }
    }
}
