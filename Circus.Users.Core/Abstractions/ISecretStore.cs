namespace Circus.Users.Core.Abstractions;

/// <summary>Хранилище секретов приложения: токенов и ключей.</summary>
public interface ISecretStore
{
    /// <summary>Читает секрет или возвращает null, если он не задан.</summary>
    string? Read(string key);

    void Write(string key, string secret);

    void Delete(string key);
}
