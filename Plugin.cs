using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Plugin.HomeAssistantTaskNotifier.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.HomeAssistantTaskNotifier;

/// <summary>
/// The main plugin entry point. Registers the config page; the actual work happens in
/// <see cref="TaskNotifierService"/>, wired up by <see cref="PluginServiceRegistrator"/>.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
    /// <param name="xmlSerializer">Instance of the <see cref="IXmlSerializer"/> interface.</param>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    /// <inheritdoc />
    public override string Name => "Home Assistant Task Notifier";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("e9a7a45f-d10a-49fb-8ca0-4ce05c79f874");

    /// <inheritdoc />
    public override string Description =>
        "Toggles a Home Assistant sensor while a Jellyfin scheduled task runs and optionally fires an event per task.";

    /// <summary>
    /// Gets the current plugin instance.
    /// </summary>
    public static Plugin? Instance { get; private set; }

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return
        [
            new PluginPageInfo
            {
                Name = "HomeAssistantTaskNotifier",
                EmbeddedResourcePath = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}.Configuration.configPage.html",
                    GetType().Namespace)
            }
        ];
    }
}
