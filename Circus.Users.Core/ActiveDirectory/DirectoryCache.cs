using Circus.Users.Core.Abstractions;
using Circus.Users.Core.Models;
using Microsoft.Extensions.Logging;

namespace Circus.Users.Core.ActiveDirectory;

/// <summary>
/// Держит справочник сотрудников в памяти и обновляет его по расписанию.
/// </summary>
/// <remarks>
/// При нескольких сотнях учётных записей полное перечитывание раз в несколько минут дешевле
/// и надёжнее, чем разностная синхронизация по uSNChanged. Событие <see cref="Changed"/>
/// приходит из фонового потока — подписчику нужно самому переходить в свой поток.
/// </remarks>
public sealed class DirectoryCache : IAsyncDisposable
{
    private readonly Func<IDirectoryService> _serviceFactory;
    private readonly TimeSpan _refreshInterval;
    private readonly ILogger<DirectoryCache> _logger;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly CancellationTokenSource _stopping = new();

    private Task? _loop;

    public DirectoryCache(
        Func<IDirectoryService> serviceFactory,
        TimeSpan refreshInterval,
        ILogger<DirectoryCache> logger)
    {
        _serviceFactory = serviceFactory;
        _refreshInterval = refreshInterval;
        _logger = logger;
    }

    /// <summary>Последний успешно прочитанный список сотрудников.</summary>
    public IReadOnlyList<EmployeeCard> Employees { get; private set; } = [];

    /// <summary>Дерево подразделений на момент последнего обновления.</summary>
    public IReadOnlyList<OrganizationalUnit> Departments { get; private set; } = [];

    /// <summary>Группы каталога — нужны редактору шаблонов подразделений.</summary>
    public IReadOnlyList<DirectoryGroup> Groups { get; private set; } = [];

    public DateTimeOffset? LastUpdated { get; private set; }

    /// <summary>Текст последней ошибки чтения или null, если всё в порядке.</summary>
    public string? LastError { get; private set; }

    public bool IsRefreshing { get; private set; }

    public event EventHandler? Changed;

    /// <summary>Запускает первое чтение и последующие обновления по расписанию.</summary>
    public void Start() => _loop ??= Task.Run(RefreshLoopAsync);

    /// <summary>Перечитывает справочник немедленно.</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (!await _refreshLock.WaitAsync(0, cancellationToken))
        {
            // Обновление уже идёт — второе параллельное не нужно.
            return;
        }

        try
        {
            IsRefreshing = true;
            Changed?.Invoke(this, EventArgs.Empty);

            var service = _serviceFactory();

            // Три чтения подряд, а не параллельно: параллельные обходы каталога
            // нагружают контроллер домена без выигрыша во времени.
            var employees = await service.GetEmployeesAsync(cancellationToken);
            var departments = await service.GetDepartmentsAsync(cancellationToken);
            var groups = await service.GetGroupsAsync(cancellationToken);

            Employees = employees;
            Departments = departments;
            Groups = groups;
            LastUpdated = DateTimeOffset.Now;
            LastError = null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Прошлые данные остаются доступными: лучше показать их с пометкой, чем пустой список.
            _logger.LogError(ex, "Не удалось обновить справочник");
            LastError = ex.Message;
        }
        finally
        {
            IsRefreshing = false;
            _refreshLock.Release();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task RefreshLoopAsync()
    {
        var token = _stopping.Token;

        try
        {
            await RefreshAsync(token);

            using var timer = new PeriodicTimer(_refreshInterval);
            while (await timer.WaitForNextTickAsync(token))
            {
                await RefreshAsync(token);
            }
        }
        catch (OperationCanceledException)
        {
            // Приложение закрывается.
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync();

        if (_loop is not null)
        {
            try
            {
                await _loop;
            }
            catch (OperationCanceledException)
            {
                // Ожидаемо при остановке.
            }
        }

        _stopping.Dispose();
        _refreshLock.Dispose();
    }
}
