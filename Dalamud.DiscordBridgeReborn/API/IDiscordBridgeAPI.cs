namespace Dalamud.DiscordBridge.API
{
    /// <summary>
    /// Interface to communicate with DiscordBridge.
    /// </summary>
    public interface IDiscordBridgeAPI
    {
        /// <summary>
        /// Gets api version.
        /// </summary>
        public int APIVersion { get; }

        /// <summary>
        /// Send discord message.
        /// </summary>
        /// <param name="message">message to send.</param>
        /// <param name="pluginName">plugin / assembly name.</param>
        /// <param name="avatarUrl">avatar url.</param>
        public void SendMessage(string pluginName, string avatarUrl, string message);
    }
}
