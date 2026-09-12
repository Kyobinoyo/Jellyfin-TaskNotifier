using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Security;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.HomeAssistantTaskNotifier;

/// <summary>
/// Talks to the Home Assistant REST API: pushes the running/idle sensor state and,
/// optionally, a discrete event per scheduled task start/finish.
/// </summary>
public sealed class HomeAssistantNotifier
{
    // Shared across instances: the certificate callback reads the *current* plugin
    // configuration on every call, so toggling "allow invalid certificate" in the
    // config page takes effect immediately without recreating the handler/client.
    private static readonly HttpClientHandler Handler = new()
    {
        ServerCertificateCustomValidationCallback = (_, _, _, errors) =>
            errors == SslPolicyErrors.None || (Plugin.Instance?.Configuration.AllowInvalidHttpsCertificate ?? false)
    };

    private static readonly HttpClient SharedClient = new(Handler)
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    private readonly ILogger _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="HomeAssistantNotifier"/> class.
    /// </summary>
    /// <param name="logger">Logger to report delivery failures to.</param>
    public HomeAssistantNotifier(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Sets the configured sensor entity to "on" or "off". If the entity id belongs to
    /// the <c>input_boolean</c> domain (a manually created Home Assistant helper), its
    /// <c>turn_on</c>/<c>turn_off</c> service is called instead of overwriting the raw
    /// state directly, since that's the correct way to drive a helper. Any other domain
    /// (e.g. <c>binary_sensor</c>) is pushed straight into the state machine, including
    /// the list of currently running (tracked) task names as an attribute.
    /// </summary>
    public Task SetSensorStateAsync(bool isRunning, IReadOnlyCollection<string> runningTaskNames)
    {
        var config = Plugin.Instance?.Configuration;
        if (config is null || string.IsNullOrWhiteSpace(config.HomeAssistantUrl)
            || string.IsNullOrWhiteSpace(config.AccessToken) || string.IsNullOrWhiteSpace(config.SensorEntityId))
        {
            return Task.CompletedTask;
        }

        var entityId = config.SensorEntityId.Trim();
        var domain = entityId.Split('.', 2)[0];

        if (string.Equals(domain, "input_boolean", StringComparison.OrdinalIgnoreCase))
        {
            var service = isRunning ? "turn_on" : "turn_off";
            var serviceUrl = $"{config.HomeAssistantUrl.TrimEnd('/')}/api/services/input_boolean/{service}";
            return SendAsync(HttpMethod.Post, serviceUrl, config.AccessToken, new { entity_id = entityId });
        }

        var payload = new
        {
            state = isRunning ? "on" : "off",
            attributes = new
            {
                friendly_name = "Jellyfin scheduled task running",
                device_class = "running",
                running_tasks = runningTaskNames,
                updated_at = DateTime.UtcNow.ToString("o")
            }
        };

        var statesUrl = $"{config.HomeAssistantUrl.TrimEnd('/')}/api/states/{entityId}";
        return SendAsync(HttpMethod.Post, statesUrl, config.AccessToken, payload);
    }

    /// <summary>
    /// Fires a Home Assistant event describing a single task's start or completion,
    /// when <see cref="Configuration.PluginConfiguration.FireTaskEvents"/> is enabled.
    /// </summary>
    public Task FireTaskEventAsync(string taskKey, string taskName, string phase, string? status)
    {
        var config = Plugin.Instance?.Configuration;
        if (config is null || !config.FireTaskEvents || string.IsNullOrWhiteSpace(config.HomeAssistantUrl)
            || string.IsNullOrWhiteSpace(config.AccessToken) || string.IsNullOrWhiteSpace(config.EventType))
        {
            return Task.CompletedTask;
        }

        var payload = new
        {
            task_key = taskKey,
            task_name = taskName,
            phase,
            status,
            timestamp = DateTime.UtcNow.ToString("o")
        };

        var url = $"{config.HomeAssistantUrl.TrimEnd('/')}/api/events/{config.EventType}";
        return SendAsync(HttpMethod.Post, url, config.AccessToken, payload);
    }

    private async Task SendAsync(HttpMethod method, string url, string accessToken, object payload)
    {
        try
        {
            using var request = new HttpRequestMessage(method, url)
            {
                Content = JsonContent.Create(payload)
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using var response = await SharedClient.SendAsync(request).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                _logger.LogWarning(
                    "Home Assistant request to {Url} failed with {StatusCode}: {Body}",
                    url,
                    response.StatusCode,
                    body);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reach Home Assistant at {Url}", url);
        }
    }
}
