using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Circus.Users.Core.Abstractions;
using Circus.Users.Core.Models;
using Circus.Users.Core.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Circus.Users.Core.Security;

/// <summary>
/// Определяет пользователя по маркеру доступа текущего процесса Windows.
/// </summary>
/// <remarks>
/// Права администратора — это членство в группе из настроек. Проверяем по списку групп
/// в маркере: он уже содержит вложенные группы, отдельно обходить дерево не нужно.
/// Обращений на запись в каталог здесь нет, только чтение локального маркера.
/// </remarks>
public sealed class WindowsCurrentUserResolver : ICurrentUserResolver
{
    /// <summary>Формат EXTENDED_NAME_FORMAT.NameDisplay.</summary>
    private const int NameDisplay = 3;

    private readonly ActiveDirectorySettings _settings;
    private readonly ILogger<WindowsCurrentUserResolver> _logger;

    public WindowsCurrentUserResolver(
        IOptions<AppSettings> settings,
        ILogger<WindowsCurrentUserResolver> logger)
    {
        _settings = settings.Value.ActiveDirectory;
        _logger = logger;
    }

    public CurrentUser Resolve()
    {
        using var identity = WindowsIdentity.GetCurrent();

        var accountName = identity.Name ?? string.Empty;
        var isAdmin = IsMemberOf(identity, _settings.AdminGroup);

        return new CurrentUser(
            AccountName: accountName,
            DisplayName: GetDisplayName() ?? ShortName(accountName),
            Sid: identity.User?.Value,
            IsAdmin: isAdmin,
            IsFake: false);
    }

    private bool IsMemberOf(WindowsIdentity identity, string groupName)
    {
        if (string.IsNullOrWhiteSpace(groupName))
        {
            _logger.LogWarning("Группа администраторов не задана в настройках — права не выданы никому");
            return false;
        }

        if (identity.Groups is not { } groups)
        {
            return false;
        }

        try
        {
            // Пакетный перевод: forceSuccess = false, чтобы неразрешимые идентификаторы
            // просто вернулись строкой, а не роняли весь список исключением.
            var names = groups.Translate(typeof(NTAccount), forceSuccess: false);

            foreach (var name in names)
            {
                if (string.Equals(ShortName(name.Value), groupName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch (Exception ex) when (ex is SystemException)
        {
            _logger.LogWarning(ex, "Не удалось получить список групп пользователя");
        }

        return false;
    }

    /// <summary>Отбрасывает часть DOMAIN\ у имени учётной записи или группы.</summary>
    private static string ShortName(string value)
    {
        var separator = value.LastIndexOf('\\');
        return separator >= 0 ? value[(separator + 1)..] : value;
    }

    /// <summary>Отображаемое имя из каталога. Вне домена возвращает null.</summary>
    private string? GetDisplayName()
    {
        try
        {
            uint size = 256;
            var buffer = new StringBuilder((int)size);

            if (GetUserNameEx(NameDisplay, buffer, ref size))
            {
                var value = buffer.ToString();
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            _logger.LogDebug(ex, "Отображаемое имя недоступно");
        }

        return null;
    }

    [DllImport("secur32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetUserNameEx(int nameFormat, StringBuilder nameBuffer, ref uint size);
}
