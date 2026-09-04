using Circus.Users.Core.Abstractions;
using Circus.Users.Core.Models;

namespace Circus.Users.Core.Fakes;

/// <summary>Тестовый справочник: позволяет разрабатывать и показывать адресную книгу без домена.</summary>
public sealed class FakeDirectoryService : IDirectoryService
{
    private static readonly (string Name, string Title, string Department, string Room, string Extension)[] People =
    [
        ("Иванов Иван Иванович", "Ведущий инженер", "16.2-Отдел инфраструктуры", "312", "1201"),
        ("Иванов Игорь Петрович", "Системный администратор", "16.2-Отдел инфраструктуры", "312", "1202"),
        ("Кузнецова Ольга Сергеевна", "Начальник отдела", "16.2-Отдел инфраструктуры", "310", "1200"),
        ("Смирнов Алексей Викторович", "Специалист технической поддержки", "16.2-Отдел инфраструктуры", "314", "1203"),
        ("Петрова Мария Андреевна", "Главный бухгалтер", "04-Бухгалтерия", "205", "1101"),
        ("Соколова Анна Дмитриевна", "Бухгалтер", "04-Бухгалтерия", "205", "1102"),
        ("Морозов Дмитрий Олегович", "Экономист", "04-Бухгалтерия", "206", "1103"),
        ("Волкова Екатерина Игоревна", "Специалист по кадрам", "02-Отдел кадров", "108", "1010"),
        ("Зайцев Николай Павлович", "Начальник отдела кадров", "02-Отдел кадров", "107", "1011"),
        ("Медведев Сергей Львович", "Художественный руководитель", "01-Дирекция", "101", "1001"),
        ("Орлова Татьяна Борисовна", "Помощник директора", "01-Дирекция", "102", "1002"),
        ("Лебедев Артём Юрьевич", "Инспектор манежа", "07-Манеж", "020", "1301"),
    ];

    public Task<IReadOnlyList<EmployeeCard>> GetEmployeesAsync(CancellationToken cancellationToken = default)
    {
        var employees = People.Select(Create).ToList();
        return Task.FromResult<IReadOnlyList<EmployeeCard>>(employees);
    }

    private static EmployeeCard Create((string Name, string Title, string Department, string Room, string Extension) person)
    {
        var login = Login(person.Name);

        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["displayName"] = person.Name,
            ["title"] = person.Title,
            ["department"] = person.Department,
            [DirectoryFields.DepartmentAttribute] = person.Department,
            ["mail"] = $"{login}@circus.ru",
            ["sAMAccountName"] = login,
            ["ipPhone"] = person.Extension,
            ["telephoneNumber"] = "+7 (495) 000-00-00",
            ["physicalDeliveryOfficeName"] = person.Room,
            ["company"] = "Цирк",
        };

        return new EmployeeCard($"CN={person.Name},OU=Тест,DC=circus,DC=local", attributes);
    }

    /// <summary>Грубая транслитерация — настоящая появится на этапе создания пользователя.</summary>
    private static string Login(string fullName)
    {
        var parts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var surname = Translit(parts[0]);
        var initial = parts.Length > 1 ? Translit(parts[1][..1]) : string.Empty;
        return $"{initial}.{surname}".ToLowerInvariant();
    }

    private static string Translit(string value)
    {
        const string source = "абвгдеёжзийклмнопрстуфхцчшщъыьэюя";
        string[] target =
        [
            "a", "b", "v", "g", "d", "e", "e", "zh", "z", "i", "y", "k", "l", "m", "n", "o", "p",
            "r", "s", "t", "u", "f", "kh", "ts", "ch", "sh", "sch", "", "y", "", "e", "yu", "ya",
        ];

        var result = new System.Text.StringBuilder(value.Length);
        foreach (var symbol in value.ToLowerInvariant())
        {
            var index = source.IndexOf(symbol);
            result.Append(index >= 0 ? target[index] : symbol.ToString());
        }

        return result.ToString();
    }

    public Task<IReadOnlyList<OrganizationalUnit>> GetDepartmentsAsync(CancellationToken cancellationToken = default)
    {
        const string root = "OU=Тест,DC=circus,DC=local";

        var departments = People
            .Select(person => person.Department)
            .Distinct()
            .Order(StringComparer.CurrentCulture)
            .Select(name => new OrganizationalUnit($"OU={name},{root}", name, root))
            .ToList();

        departments.Insert(0, new OrganizationalUnit(root, "Тест", "DC=circus,DC=local"));
        return Task.FromResult<IReadOnlyList<OrganizationalUnit>>(departments);
    }

    public Task<IReadOnlyList<DirectoryGroup>> GetGroupsAsync(CancellationToken cancellationToken = default)
    {
        string[] names = ["Все сотрудники", "Бухгалтерия", "Инфраструктура", "Отдел кадров", "Дирекция"];

        var groups = names
            .Select((name, index) => new DirectoryGroup(
                $"CN={name},OU=Группы,DC=circus,DC=local",
                name,
                $"grp{index + 1:00}"))
            .ToList();

        return Task.FromResult<IReadOnlyList<DirectoryGroup>>(groups);
    }
}
