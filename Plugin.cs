using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Plugin.UltimateStrm.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.UltimateStrm;

/// <summary>
/// The Ultimate STRM plugin entry point. Combines what used to be three separate
/// plugins (Strm Manager, yt2strm, Custom Iframe Player) into one assembly with a
/// single tabbed configuration page and one shared SQLite database.
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
    public override string Name => "UltimateSTRM";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("f029e2b3-8dc0-47de-a13d-4df77020485a");

    /// <inheritdoc />
    public override string Description => "Strm Manager, yt2strm and Custom Iframe Player combined into a single tabbed plugin with one shared database.";

    /// <summary>
    /// Gets the current plugin instance.
    /// </summary>
    public static Plugin Instance { get; private set; }

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return
        [
            new PluginPageInfo
            {
                Name = Name,
                EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Configuration.configPage.html", GetType().Namespace),
                EnableInMainMenu = true
            }
        ];
    }
}
