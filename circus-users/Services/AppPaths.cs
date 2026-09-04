using Windows.Storage;

namespace Circus.Users.App;

/// <summary>Пути, по которым приложение хранит свои файлы на локальной машине.</summary>
internal static class AppPaths
{
    /// <summary>
    /// Каталог персональных настроек. В упакованном виде это LocalFolder пакета,
    /// при запуске без пакета — обычный LocalAppData.
    /// </summary>
    public static string LocalSettingsDirectory { get; } = ResolveLocalSettingsDirectory();

    private static string ResolveLocalSettingsDirectory()
    {
        try
        {
            return ApplicationData.Current.LocalFolder.Path;
        }
        catch (InvalidOperationException)
        {
            // Приложение запущено без MSIX-пакета.
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "circus-users");
        }
    }
}
