using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Events;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.HomeAssistantTaskNotifier;

/// <summary>
/// Subscribes to <see cref="ITaskManager"/> and keeps a "something is running" sensor
/// in sync in Home Assistant: on while at least one tracked scheduled task is running,
/// off again once none are. Optionally also fires a per-task start/finish event.
/// </summary>
public sealed class TaskNotifierService : IHostedService
{
    private readonly ITaskManager _taskManager;
    private readonly ILogger<TaskNotifierService> _logger;
    private readonly HomeAssistantNotifier _notifier;
    private readonly HashSet<string> _runningTaskNames = new();
    private readonly object _lock = new();
    private CancellationTokenSource? _refreshCts;
    private Task? _refreshLoop;

    /// <summary>
    /// Initializes a new instance of the <see cref="TaskNotifierService"/> class.
    /// </summary>
    /// <param name="taskManager">Jellyfin's scheduled task manager.</param>
    /// <param name="logger">Logger.</param>
    public TaskNotifierService(ITaskManager taskManager, ILogger<TaskNotifierService> logger)
    {
        _taskManager = taskManager;
        _logger = logger;
        _notifier = new HomeAssistantNotifier(logger);
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _taskManager.TaskExecuting += OnTaskExecuting;
        _taskManager.TaskCompleted += OnTaskCompleted;
        _refreshCts = new CancellationTokenSource();
        _refreshLoop = Task.Run(() => RefreshLoopAsync(_refreshCts.Token), CancellationToken.None);
        _logger.LogInformation("Home Assistant Task Notifier is now watching scheduled tasks.");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _taskManager.TaskExecuting -= OnTaskExecuting;
        _taskManager.TaskCompleted -= OnTaskCompleted;

        if (_refreshCts is not null)
        {
            await _refreshCts.CancelAsync().ConfigureAwait(false);
            if (_refreshLoop is not null)
            {
                await _refreshLoop.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            _refreshCts.Dispose();
            _refreshCts = null;
        }
    }

    /// <summary>
    /// Periodically re-sends the sensor state, derived from the tasks that are actually
    /// running right now. This corrects state lost through missed events or failed
    /// requests, and recreates a REST-pushed entity after a Home Assistant restart.
    /// The interval is re-read from the configuration every cycle, so changes apply
    /// without restarting Jellyfin.
    /// </summary>
    private async Task RefreshLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var minutes = Plugin.Instance?.Configuration.StatusRefreshIntervalMinutes ?? 0;
            try
            {
                // When disabled, just check back every minute whether it got enabled.
                await Task.Delay(TimeSpan.FromMinutes(minutes > 0 ? minutes : 1), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (minutes <= 0)
            {
                continue;
            }

            try
            {
                await RefreshStatusAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Periodic Home Assistant status refresh failed");
            }
        }
    }

    private Task RefreshStatusAsync()
    {
        var running = _taskManager.ScheduledTasks
            .Where(w => w.State == TaskState.Running && IsTracked(w.ScheduledTask.Key))
            .Select(w => w.Name)
            .ToList();

        List<string> snapshot;
        lock (_lock)
        {
            _runningTaskNames.Clear();
            _runningTaskNames.UnionWith(running);
            snapshot = _runningTaskNames.ToList();
        }

        _logger.LogDebug("Periodic status refresh: {Count} tracked task(s) running", snapshot.Count);
        return _notifier.SetSensorStateAsync(snapshot.Count > 0, snapshot);
    }

    private void OnTaskExecuting(object? sender, GenericEventArgs<IScheduledTaskWorker> e)
    {
        var worker = e.Argument;
        var key = worker.ScheduledTask.Key;
        if (!IsTracked(key))
        {
            return;
        }

        bool becameActive;
        List<string> snapshot;
        lock (_lock)
        {
            _runningTaskNames.Add(worker.Name);
            becameActive = _runningTaskNames.Count == 1;
            snapshot = _runningTaskNames.ToList();
        }

        FireAndForget(_notifier.FireTaskEventAsync(key, worker.Name, "started", null));
        if (becameActive)
        {
            FireAndForget(_notifier.SetSensorStateAsync(true, snapshot));
        }
    }

    private void OnTaskCompleted(object? sender, TaskCompletionEventArgs e)
    {
        var worker = e.Task;
        var key = worker.ScheduledTask.Key;
        if (!IsTracked(key))
        {
            return;
        }

        bool becameIdle;
        List<string> snapshot;
        lock (_lock)
        {
            _runningTaskNames.Remove(worker.Name);
            becameIdle = _runningTaskNames.Count == 0;
            snapshot = _runningTaskNames.ToList();
        }

        FireAndForget(_notifier.FireTaskEventAsync(key, worker.Name, "completed", e.Result.Status.ToString()));
        if (becameIdle)
        {
            FireAndForget(_notifier.SetSensorStateAsync(false, snapshot));
        }
    }

    private bool IsTracked(string taskKey)
    {
        var included = Plugin.Instance?.Configuration.IncludedTaskKeys;
        if (string.IsNullOrWhiteSpace(included))
        {
            return true;
        }

        return included
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Contains(taskKey, StringComparer.OrdinalIgnoreCase);
    }

    private void FireAndForget(Task task)
    {
        _ = task.ContinueWith(
            t => _logger.LogError(t.Exception, "Unhandled error notifying Home Assistant"),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
    }
}
