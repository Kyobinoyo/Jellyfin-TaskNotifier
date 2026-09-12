using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.HomeAssistantTaskNotifier;

/// <summary>
/// Registers <see cref="TaskNotifierService"/> as a hosted background service so it
/// starts with the server and can subscribe to <c>ITaskManager</c> events.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddHostedService<TaskNotifierService>();
    }
}
