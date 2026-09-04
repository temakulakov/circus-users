namespace Circus.Users.Core.Models;

/// <summary>Поле карточки сотрудника: атрибут каталога, название для человека и ширина колонки.</summary>
public sealed record DirectoryField(string Attribute, string Title, double Width);

/// <summary>Набор атрибутов, которые приложение умеет показывать в адресной книге.</summary>
public static class DirectoryFields
{
    /// <summary>
    /// Подразделение берётся не из атрибута, а из подразделения каталога, в котором лежит учётная
    /// запись: атрибут department заполнен меньше чем у трети сотрудников, а OU есть у всех.
    /// Ключ совпадает с именем LDAP-атрибута ou, у пользователей он не занят.
    /// </summary>
    public const string DepartmentAttribute = "ou";

    public static IReadOnlyList<DirectoryField> All { get; } =
    [
        new("displayName", "ФИО", 260),
        new(DepartmentAttribute, "Подразделение", 300),
        new("title", "Должность", 240),
        new("ipPhone", "Добавочный", 120),
        new("mail", "Электронная почта", 240),
        new("telephoneNumber", "Городской телефон", 170),
        new("mobile", "Мобильный телефон", 170),
        new("physicalDeliveryOfficeName", "Кабинет", 110),
        new("department", "Отдел по атрибуту", 220),
        new("company", "Организация", 200),
        new("manager", "Руководитель", 220),
        new("sAMAccountName", "Учётная запись", 160),
        new("description", "Примечание", 240),
    ];

    /// <summary>Колонки, которые показываются, пока пользователь не настроил вид под себя.</summary>
    public static IReadOnlyList<string> DefaultLayout { get; } =
    [
        "displayName",
        "title",
        "ipPhone",
        "mail",
        "physicalDeliveryOfficeName",
    ];

    public static DirectoryField? Find(string attribute) =>
        All.FirstOrDefault(field => field.Attribute == attribute);

    public static string TitleOf(string attribute) => Find(attribute)?.Title ?? attribute;
}
