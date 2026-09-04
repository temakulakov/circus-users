using System.Text.Json.Serialization;

namespace Circus.Users.Core.Models;

/// <summary>
/// Значения, которые подставляются новому сотруднику подразделения.
/// </summary>
/// <remarks>
/// Имена свойств в JSON сокращены намеренно: шаблон хранится в атрибуте каталога
/// с ограничением в 1024 символа, и каждый лишний байт приближает к нему.
/// Группы хранятся именами учётных записей, а не различающимися именами, по той же причине.
/// </remarks>
public sealed class DepartmentTemplate
{
    [JsonPropertyName("co")]
    public string? Company { get; set; }

    [JsonPropertyName("ph")]
    public string? TelephoneNumber { get; set; }

    [JsonPropertyName("ci")]
    public string? City { get; set; }

    [JsonPropertyName("ad")]
    public string? StreetAddress { get; set; }

    [JsonPropertyName("of")]
    public string? Office { get; set; }

    /// <summary>Руководитель: sAMAccountName, чтобы не хранить длинное различающееся имя.</summary>
    [JsonPropertyName("mg")]
    public string? ManagerAccount { get; set; }

    /// <summary>Группы безопасности AD по sAMAccountName.</summary>
    [JsonPropertyName("gr")]
    public List<string> AdGroups { get; set; } = [];

    /// <summary>Группы рассылки Yandex 360. До этапа почты заполняются вручную.</summary>
    [JsonPropertyName("ml")]
    public List<string> MailGroups { get; set; } = [];

    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(Company)
        && string.IsNullOrWhiteSpace(TelephoneNumber)
        && string.IsNullOrWhiteSpace(City)
        && string.IsNullOrWhiteSpace(StreetAddress)
        && string.IsNullOrWhiteSpace(Office)
        && string.IsNullOrWhiteSpace(ManagerAccount)
        && AdGroups.Count == 0
        && MailGroups.Count == 0;
}
