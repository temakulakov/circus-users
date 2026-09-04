namespace Circus.Users.App.Services;

/// <summary>
/// Аварийный журнал: пишет необработанные исключения в файл рядом с настройками.
/// Нужен потому, что упавшее приложение уносит с собой и логгер, и окно с сообщением.
/// </summary>
internal static class CrashLog
{
    private static readonly object Gate = new();

    public static string FilePath { get; } = Path.Combine(AppPaths.LocalSettingsDirectory, "crash.log");

    public static void Write(string context, Exception exception)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(AppPaths.LocalSettingsDirectory);
                File.AppendAllText(
                    FilePath,
                    $"""

                     [{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}] {context}
                     {exception}

                     """);
            }
        }
        catch (Exception)
        {
            // Записать не удалось — падать из-за журнала нельзя.
        }
    }
}
