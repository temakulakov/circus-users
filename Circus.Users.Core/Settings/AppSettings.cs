namespace Circus.Users.Core.Settings;

/// <summary>Корень конфигурации приложения (appsettings.json). Одинаков для всех пользователей.</summary>
public sealed class AppSettings
{
    public ActiveDirectorySettings ActiveDirectory { get; set; } = new();

    public MailSettings Mail { get; set; } = new();

    public StorageSettings Storage { get; set; } = new();
}

public sealed class ActiveDirectorySettings
{
    /// <summary>FQDN домена. Пусто — определяем автоматически по домену текущего компьютера.</summary>
    public string? DomainFqdn { get; set; }

    /// <summary>Конкретный контроллер домена. Пусто — выбирает Windows.</summary>
    public string? Server { get; set; }

    /// <summary>Базовый DN для поиска пользователей. Пусто — defaultNamingContext из RootDSE.</summary>
    public string? UsersBaseDn { get; set; }

    /// <summary>DN корневого OU, внутри которого лежат подразделения. Пусто — тот же базовый DN.</summary>
    public string? DepartmentsRootDn { get; set; }

    /// <summary>Имя AD-группы, членство в которой даёт права администратора приложения.</summary>
    public string AdminGroup { get; set; } = string.Empty;

    /// <summary>Имя contact-объекта внутри OU, в котором хранится шаблон подразделения.</summary>
    public string DepartmentTemplateObjectName { get; set; } = "_DepartmentTemplate";

    /// <summary>Период обновления кэша справочника, в минутах.</summary>
    public int CacheRefreshMinutes { get; set; } = 5;

    /// <summary>
    /// Атрибуты, которые администратору разрешено править из адресной книги.
    /// Ограничение на стороне приложения; права в каталоге оно не заменяет.
    /// </summary>
    public List<string> EditableAttributes { get; set; } =
    [
        "title",
        "department",
        "ipPhone",
        "telephoneNumber",
        "mobile",
        "physicalDeliveryOfficeName",
        "description",
    ];
}

public sealed class MailSettings
{
    /// <summary>Почтовый домен, который подставляется к логину.</summary>
    public string Domain { get; set; } = "circus.ru";

    /// <summary>Идентификатор организации в Yandex 360.</summary>
    public string OrgId { get; set; } = string.Empty;

    /// <summary>
    /// Подразделение в Yandex 360, куда попадает сотрудник, если шаблон не задал своё.
    /// API требует это поле при создании; 1 — корневое «Все сотрудники».
    /// </summary>
    public long DefaultDepartmentId { get; set; } = 1;
}

public sealed class StorageSettings
{
    /// <summary>Каталог с общими настройками (макет адресной книги «для всех»).</summary>
    public string SharedSettingsPath { get; set; } = string.Empty;
}
