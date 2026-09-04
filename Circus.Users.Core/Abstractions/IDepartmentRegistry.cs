using Circus.Users.Core.Models;

namespace Circus.Users.Core.Abstractions;

/// <summary>
/// Реестр подразделений: какие каталоги приложение считает отделами.
/// </summary>
/// <remarks>
/// Отметка живёт вне Active Directory намеренно. Каталог, помеченный «не отдел», не должен
/// получать никаких объектов приложения — в том числе служебный контакт с шаблоном,
/// поэтому хранить признак внутри самого каталога противоречило бы его смыслу.
/// Сейчас это JSON на общей шаре; интерфейс описан так, чтобы замена на таблицу в базе
/// не задела вызывающий код.
/// </remarks>
public interface IDepartmentRegistry
{
    /// <summary>Реестр перечитан или изменён.</summary>
    event EventHandler? Changed;

    /// <summary>Причина, по которой реестр недоступен, или null.</summary>
    string? LastError { get; }

    Task LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Считается ли каталог подразделением. Неизвестный каталог — считается:
    /// недоступная шара не должна запрещать работу с подразделениями.
    /// </summary>
    bool IsDepartment(string organizationalUnitDn);

    /// <summary>Запись реестра или null, если каталог в нём не упомянут.</summary>
    DepartmentRecord? Find(string organizationalUnitDn);

    /// <summary>Помечает каталог отделом или не-отделом.</summary>
    Task SetIsDepartmentAsync(
        string organizationalUnitDn,
        string name,
        bool isDepartment,
        string actor,
        CancellationToken cancellationToken = default);
}
