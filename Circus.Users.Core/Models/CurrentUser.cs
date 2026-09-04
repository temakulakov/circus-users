namespace Circus.Users.Core.Models;

/// <summary>Кто работает в приложении и что ему доступно.</summary>
/// <param name="AccountName">Учётная запись в виде DOMAIN\user.</param>
/// <param name="DisplayName">Отображаемое имя; при недоступности — имя учётной записи.</param>
/// <param name="Sid">Идентификатор безопасности.</param>
/// <param name="IsAdmin">Состоит в группе администраторов приложения.</param>
/// <param name="IsFake">Данные из режима тестовых данных, а не из домена.</param>
public sealed record CurrentUser(
    string AccountName,
    string DisplayName,
    string? Sid,
    bool IsAdmin,
    bool IsFake)
{
    public string RoleTitle => IsAdmin ? "Администратор" : "Сотрудник";
}
