namespace Circus.Users.Core.Models;

/// <summary>
/// Что приложение знает о каталоге сверх того, что лежит в самом каталоге.
/// </summary>
/// <remarks>
/// Одна запись — один OU, ключ — различающееся имя. Набор полей намеренно плоский:
/// при переезде на базу данных запись становится строкой таблицы без переработки модели.
/// </remarks>
public sealed class DepartmentRecord
{
    /// <summary>Имя подразделения на момент записи — чтобы файл читался глазами.</summary>
    public string? Name { get; set; }

    /// <summary>
    /// Считается ли каталог подразделением. Если нет — в нём не заводят ни сотрудников,
    /// ни служебный контакт с шаблоном.
    /// </summary>
    public bool IsDepartment { get; set; } = true;

    public string? UpdatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }
}

/// <summary>Файл реестра целиком.</summary>
/// <remarks>
/// Версия нужна для будущих переносов: по ней читатель поймёт формат, даже если
/// реестр к тому времени переедет в базу и файл останется как выгрузка.
/// </remarks>
public sealed class DepartmentRegistryFile
{
    public int Version { get; set; } = 1;

    /// <summary>Записи по различающемуся имени OU.</summary>
    public Dictionary<string, DepartmentRecord> Records { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
