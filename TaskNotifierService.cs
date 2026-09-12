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
        _logger.LogInformation("Home Assistant Task Notifier is now watching scheduled tasks.");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _taskManager.TaskExecuting -= OnTaskExecuting;
        _taskManager.TaskCompleted -= OnTaskCompleted;
        return Task.CompletedTask;
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
