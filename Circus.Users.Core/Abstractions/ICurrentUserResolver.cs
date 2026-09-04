using Circus.Users.Core.Models;

namespace Circus.Users.Core.Abstractions;

/// <summary>Определяет, кто сейчас работает в приложении.</summary>
public interface ICurrentUserResolver
{
    CurrentUser Resolve();
}
