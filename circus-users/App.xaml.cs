using Circus.Users.App.Services;
using Circus.Users.App.ViewModels;
using Circus.Users.Core.Abstractions;
using Circus.Users.Core.ActiveDirectory;
using Circus.Users.Core.Fakes;
using Circus.Users.Core.Security;
using Circus.Users.Core.Settings;
using Circus.Users.Core.Yandex;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.UI.Xaml;

namespace Circus.Users.App;

/// <summary>Точка входа приложения: поднимает контейнер зависимостей и главное окно.</summary>
public partial class App : Application
{
    private IHost? _host;
    private Window? _window;

    public App()
    {
        InitializeComponent();

        UnhandledException += (_, e) =>
        {
            CrashLog.Write("Необработанное исключение", e.Exception);
        };
    }

    /// <summary>
    /// Дескриптор главного окна. Нужен системным диалогам — выбору файла и подобным:
    /// без владельца они не показываются.
    /// </summary>
    public static nint MainWindowHandle { get; private set; }

    /// <summary>Контейнер приложения. Страницы достают из него свои ViewModel.</summary>
    public static IServiceProvider Services =>
        ((App)Current)._host?.Services
        ?? throw new InvalidOperationException("Контейнер зависимостей ещё не создан.");

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            _host = BuildHost();

            // Настройки нужны до создания окна: тема и подложка применяются сразу,
            // чтобы пользователь не видел вспышку светлой темы.
            var preferences = _host.Services.GetRequiredService<IPreferencesStore>();
            await preferences.LoadAsync();

            // Общий вид адресной книги нужен до создания страницы.
            await _host.Services.GetRequiredService<ISharedLayoutStore>().LoadAsync();

            // Реестр подразделений нужен до первой отрисовки списков:
            // по нему решается, какие каталоги вообще предлагать.
            await _host.Services.GetRequiredService<IDepartmentRegistry>().LoadAsync();

            _host.Services.GetRequiredService<ThemeService>().InstallResources();

            _window = _host.Services.GetRequiredService<MainWindow>();
            MainWindowHandle = WinRT.Interop.WindowNative.GetWindowHandle(_window);
            _window.Activate();

            // Справочник читается в фоне, окно его не ждёт.
            _host.Services.GetRequiredService<DirectoryCache>().Start();
        }
        catch (Exception ex)
        {
            // Без окна показать ошибку негде — пишем её в журнал и выходим.
            CrashLog.Write("Сбой при запуске", ex);
            throw;
        }
    }

    private static IHost BuildHost()
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Configuration
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            // Настройки организации лежат отдельно и перекрывают значения по умолчанию:
            // в репозитории им не место, а без них приложение всё равно должно собираться.
            .AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);

        builder.Services.Configure<AppSettings>(builder.Configuration);

        builder.Logging.ClearProviders();
        builder.Logging.AddDebug();

        builder.Services.AddSingleton<IPreferencesStore>(sp => new JsonPreferencesStore(
            Path.Combine(AppPaths.LocalSettingsDirectory, "preferences.json"),
            sp.GetRequiredService<ILogger<JsonPreferencesStore>>()));

        builder.Services.AddSingleton<WindowsCurrentUserResolver>();
        builder.Services.AddSingleton<FakeCurrentUserResolver>();
        builder.Services.AddSingleton<SessionService>();

        builder.Services.AddSingleton<AdDirectoryService>();
        builder.Services.AddSingleton<FakeDirectoryService>();
        builder.Services.AddSingleton<AdDirectoryWriter>();
        builder.Services.AddSingleton<FakeDirectoryWriter>();

        // Запись направляется в тестовый или боевой каталог на каждый вызов,
        // поэтому переключатель режима работает без перезапуска.
        builder.Services.AddSingleton<IDirectoryWriter>(sp => new DirectoryWriterProxy(
            sp.GetRequiredService<IPreferencesStore>(),
            useFake => useFake
                ? sp.GetRequiredService<FakeDirectoryWriter>()
                : sp.GetRequiredService<AdDirectoryWriter>()));

        builder.Services.AddHttpClient();
        builder.Services.AddSingleton<ISecretStore, WindowsCredentialStore>();
        builder.Services.AddSingleton<Yandex360MailService>();
        builder.Services.AddSingleton<FakeMailService>();
        builder.Services.AddSingleton<IMailService>(sp => new MailServiceProxy(
            sp.GetRequiredService<IPreferencesStore>(),
            useFake => useFake
                ? sp.GetRequiredService<FakeMailService>()
                : sp.GetRequiredService<Yandex360MailService>()));

        builder.Services.AddSingleton<AdUserProvisioning>();
        builder.Services.AddSingleton<FakeUserProvisioning>();
        builder.Services.AddSingleton<IUserProvisioning>(sp => new UserProvisioningProxy(
            sp.GetRequiredService<IPreferencesStore>(),
            useFake => useFake
                ? sp.GetRequiredService<FakeUserProvisioning>()
                : sp.GetRequiredService<AdUserProvisioning>()));

        builder.Services.AddSingleton<AdDepartmentTemplateStore>();
        builder.Services.AddSingleton<FakeDepartmentTemplateStore>();
        builder.Services.AddSingleton<IDepartmentTemplateStore>(sp => new DepartmentTemplateStoreProxy(
            sp.GetRequiredService<IPreferencesStore>(),
            useFake => useFake
                ? sp.GetRequiredService<FakeDepartmentTemplateStore>()
                : sp.GetRequiredService<AdDepartmentTemplateStore>()));

        builder.Services.AddSingleton<ISharedLayoutStore, FileSharedLayoutStore>();
        builder.Services.AddSingleton<IDepartmentRegistry, FileDepartmentRegistry>();
        builder.Services.AddSingleton<IAuditLog>(sp => new FileAuditLog(
            sp.GetRequiredService<IOptions<AppSettings>>(),
            AppPaths.LocalSettingsDirectory,
            sp.GetRequiredService<ILogger<FileAuditLog>>()));
        builder.Services.AddSingleton(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<AppSettings>>().Value.ActiveDirectory;
            var preferences = sp.GetRequiredService<IPreferencesStore>();

            return new DirectoryCache(
                () => preferences.Current.UseFakeData
                    ? sp.GetRequiredService<FakeDirectoryService>()
                    : sp.GetRequiredService<AdDirectoryService>(),
                TimeSpan.FromMinutes(Math.Max(1, settings.CacheRefreshMinutes)),
                sp.GetRequiredService<ILogger<DirectoryCache>>());
        });

        builder.Services.AddSingleton<ThemeService>();
        builder.Services.AddSingleton<BackdropService>();
        builder.Services.AddSingleton<SoundService>();
        builder.Services.AddSingleton<NavigationService>();

        // Явная фабрика вместо AddSingleton<MainWindow>(): у окна internal-конструктор
        // (его параметры — внутренние сервисы), а контейнер умеет искать только публичные.
        builder.Services.AddSingleton(sp => new MainWindow(
            sp.GetRequiredService<IPreferencesStore>(),
            sp.GetRequiredService<ThemeService>(),
            sp.GetRequiredService<BackdropService>(),
            sp.GetRequiredService<NavigationService>(),
            sp.GetRequiredService<SoundService>(),
            sp.GetRequiredService<SessionService>()));

        builder.Services.AddTransient<SettingsViewModel>();

        // Одиночка: страница пересоздаётся при каждом переходе, а подписка на кэш
        // и выбранный сотрудник должны переживать навигацию.
        builder.Services.AddSingleton<AddressBookViewModel>();
        builder.Services.AddSingleton<DepartmentsViewModel>();
        builder.Services.AddSingleton<JournalViewModel>();
        builder.Services.AddSingleton<AddUserViewModel>();

        return builder.Build();
    }
}
