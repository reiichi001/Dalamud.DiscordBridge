using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

using Discord.WebSocket;

using Dalamud.Logging;
using Dalamud.Utility;

namespace Dalamud.DiscordBridge
{
    /// <summary>
    /// Dedupes already posted messages whenever a message is received.
    /// </summary>
    public class DuplicateFilter
    {
        /// <summary>
        /// Constructs a <see cref="DuplicateFilter"/> to dedupe messages for the given socket client.
        /// </summary>
        /// <param name="plugin"></param>
        /// <param name="client">The socket client to monitor for duplicate messages.</param>
        public DuplicateFilter(Plugin plugin, DiscordSocketClient client)
        {
            this.plugin = plugin;
            
            client.MessageReceived += OnMessageReceived;
        }
        
        #region Methods

        /// <summary>
        /// Check if an in-game chat was already sent to the server, either by this client or another.
        /// </summary>
        /// <param name="channel">The channel to be the destination.</param>
        /// <param name="slug">The identifier for the chat type.</param>
        /// <param name="displayName">The name of the player who sent the chat message.</param>
        /// <param name="chatText">The relevant text of the chat message.</param>
        /// <returns>Whether the chat was recently sent and should be prevented.</returns>
        public bool CheckAlreadySent(SocketChannel channel, string slug, string displayName, string chatText)
        {
            // Get the first message that duplicates the name and text for consideration.
            var recentMsg = recentMessages.FirstOrDefault(msg =>
            {
                var contentParser = new ContentParser(msg.Content);
                
                return msg.Channel.Id == channel.Id &&
                       msg.Author.Username == displayName &&
                       contentParser.Slug == slug &&
                       contentParser.Text == chatText;
            });

            if (recentMsg == null)
            {
                return false;
            }
            
            long msgDiff = GetAgeMs(recentMsg);
            
            if (msgDiff < plugin.Config.DuplicateCheckMS)
            {
                PluginLog.LogVerbose($"[FILTER] Filtered outgoing message as duplicate. Diff: {msgDiff}ms, Threshold: {plugin.Config.DuplicateCheckMS}ms");

                return true;
            }

            return false;
        }

        #endregion
        
        #region Private Functions

        private async Task OnMessageReceived(SocketMessage message)
        {
            AddRecent(message);
            
            await Dedupe();
        }

        private void AddRecent(SocketMessage message)
        {
            // The message should be from a webhook and have a content string. 
            if (!message.Author.IsWebhook || message.Content.IsNullOrEmpty())
            {
                return;
            }
            
            // Add the message if it's not already in the recent messages list.
            recentMessages.Add(message);
        }
        
        private async Task Dedupe()
        {
            // Remove for consideration any message that's over the age threshold.
            var filtered = recentMessages
                .Where(m => GetAgeMs(m) < plugin.Config.DuplicateCheckMS)
                .ToArray(); // Convert to array to avoid multiple enumeration below.

            // Holds deleted messages so that they can be removed from recents later.
            var deleted = new HashSet<SocketMessage>();
            
            // Compare every message to every other message to check for duplicates.
            // - there's probably a linq way to do this, but would it really be better
            for (var outerIdx = 0; outerIdx < filtered.Length; outerIdx++)
            {
                var recent = filtered[outerIdx];

                for (var innerIdx = outerIdx + 1; innerIdx < filtered.Length; innerIdx++)
                {
                    var other = filtered[innerIdx];
                    
                    if (IsDuplicate(recent, other))
                    {
                        SocketMessage mostRecent = await DeleteMostRecent(recent, other);

                        deleted.Add(mostRecent);
                    }
                }
            }

            // Rebuild recent messages to exclude old messages and deleted messages.
            recentMessages = new(filtered.Except(deleted));
        }
        
        /// <returns>The most recent of the two messages.</returns>
        private async Task<SocketMessage> DeleteMostRecent(SocketMessage left, SocketMessage right)
        {
            bool leftIsNewer = left.Timestamp.Offset > right.Timestamp.Offset;
            
            SocketMessage target = leftIsNewer ? left : right;
            
            _ = await TryDeleteAsync(target);
            
            return target;
        }

        /// <returns>Whether the message existed on the server and was deleted.</returns>
        private static async Task<bool> TryDeleteAsync(SocketMessage message)
        {
            try
            {
                await message.DeleteAsync();
                //await message.AddReactionAsync(new Emoji("💥")); // useful for testing
                
                return true;
            }
            catch (Discord.Net.HttpException ex)
            {
                // Rethrow unless 404 came back.
                // 404 is expected if the message was already deleted.
                if (ex.HttpCode != HttpStatusCode.NotFound)
                {
                    PluginLog.LogError($"[FILTER] Unexpected exception when attempting to delete a message.");
                    
                    throw;
                }
            }
            
            return false;
        }
        
        private static bool IsDuplicate(SocketMessage left, SocketMessage right)
        {
            var leftContent = new ContentParser(left.Content);
            var rightContent = new ContentParser(right.Content);
            
            return left.Channel.Id == right.Channel.Id &&
                   leftContent.Slug == rightContent.Slug &&
                   left.Author.Username == right.Author.Username &&
                   leftContent.Text == rightContent.Text;
        }

        private static long GetAgeMs(SocketMessage message)
        {
            return GetElapsedMs(message.Timestamp);
        }
        
        private static long GetElapsedMs(DateTimeOffset timestamp)
        {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - timestamp.ToUnixTimeMilliseconds();
        }
        
        #endregion
        
        #region Private Data

        private readonly Plugin plugin;
        
        private HashSet<SocketMessage> recentMessages = new();

        #endregion
    }
}