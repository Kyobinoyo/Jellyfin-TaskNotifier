using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.HomeAssistantTaskNotifier.Configuration;

/// <summary>
/// Plugin configuration.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets the base URL of the Home Assistant instance, e.g. http://homeassistant.local:8123.
    /// </summary>
    public string HomeAssistantUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the Home Assistant long-lived access token.
    /// </summary>
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the entity id of the sensor that is switched on while a tracked
    /// scheduled task is running and off again once none are running anymore.
    /// </summary>
    public string SensorEntityId { get; set; } = "binary_sensor.jellyfin_task_running";

    /// <summary>
    /// Gets or sets a value indicating whether a discrete Home Assistant event should
    /// additionally be fired for every task start/finish (useful for per-task automations).
    /// </summary>
    public bool FireTaskEvents { get; set; } = true;

    /// <summary>
    /// Gets or sets the Home Assistant event type used when <see cref="FireTaskEvents"/> is enabled.
    /// </summary>
    public string EventType { get; set; } = "jellyfin_scheduled_task";

    /// <summary>
    /// Gets or sets a comma separated list of scheduled task keys to track.
    /// Empty means every scheduled task is tracked.
    /// </summary>
    public string IncludedTaskKeys { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether an invalid/self-signed HTTPS certificate
    /// on the Home Assistant side should be accepted.
    /// </summary>
    public bool AllowInvalidHttpsCertificate { get; set; }
}
