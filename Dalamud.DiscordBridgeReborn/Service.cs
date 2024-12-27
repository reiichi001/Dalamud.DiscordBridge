using Dalamud.IoC;
using Dalamud.Plugin.Services;
using Dalamud.Plugin;

namespace Dalamud.DiscordBridge
{
    internal class Service
    {
        [PluginService] internal static IDalamudPluginInterface Interface { get; private set; } = null!;
        [PluginService] internal static IClientState State { get; private set; } = null!;
        [PluginService] internal static IChatGui Chat { get; private set; } = null!;
        [PluginService] internal static IDataManager Data { get; private set; } = null!;
        [PluginService] internal static IPluginLog Logger { get; private set; } = null!;
        [PluginService] internal static IFramework Framework { get; private set; } = null!;
    }
}
