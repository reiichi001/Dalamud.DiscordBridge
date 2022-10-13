using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.DiscordBridge.Model;
using Dalamud.DiscordBridge.XivApi;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Logging;
using Dalamud.Plugin;
using Lumina;

namespace Dalamud.DiscordBridge
{
    public class DiscordMessageQueue
    {
        private volatile bool runQueue = true;

        private readonly Plugin plugin;
        private readonly Thread runnerThread;

        private readonly ConcurrentQueue<QueuedXivEvent> eventQueue = new ConcurrentQueue<QueuedXivEvent>();

        private readonly Dictionary<ClientLanguage, Regex[]> retainerSaleRegexes = new Dictionary<ClientLanguage, Regex[]>() { {
                ClientLanguage.Japanese, new Regex[] {
                    new Regex(@"^(?:.+)マーケットに(?<origValue>[\d,.]+)ギルで出品した(?<item>.*)×(?<count>[\d,.]+)が売れ、(?<value>[\d,.]+)ギルを入手しました。$", RegexOptions.Compiled),
                    new Regex(@"^(?:.+)マーケットに(?<origValue>[\d,.]+)ギルで出品した(?<item>.*)が売れ、(?<value>[\d,.]+)ギルを入手しました。$", RegexOptions.Compiled) }
            }, {
                ClientLanguage.English, new Regex[] {
                    new Regex(@"^(?<item>.+) you put up for sale in the (?:.+) markets (?:have|has) sold for (?<value>[\d,.]+) gil \(after fees\)\.$", RegexOptions.Compiled)
                }
            }, {
                ClientLanguage.German, new Regex[] {
                    new Regex(@"^Dein Gehilfe hat (?<item>.+) auf dem Markt von (?:.+) für (?<value>[\d,.]+) Gil verkauft\.$", RegexOptions.Compiled),
                    new Regex(@"^Dein Gehilfe hat (?<item>.+) auf dem Markt von (?:.+) verkauft und (?<value>[\d,.]+) Gil erhalten\.$", RegexOptions.Compiled)
                }
            }, {
                ClientLanguage.French, new Regex[] {
                    new Regex(@"^Un servant a vendu (?<item>.+) pour (?<value>[\d,.]+) gil à (?:.+)\.$", RegexOptions.Compiled)
                }
            }
        };


        public DiscordMessageQueue(Plugin plugin)
        {
            this.plugin = plugin;
            this.runnerThread = new Thread(RunMessageQueue);
        }

        public void Start()
        {
            this.runQueue = true;
            this.runnerThread.Start();
        }

        public void Stop()
        {
            this.runQueue = false;

            if(this.runnerThread.IsAlive)
                this.runnerThread.Join();
        }

        public void Enqueue(QueuedXivEvent @event) => this.eventQueue.Enqueue(@event);

        private async void RunMessageQueue()
        {
            while (this.runQueue)
            {
                if (this.eventQueue.TryDequeue(out var resultEvent))
                {
                    try
                    {

                        if (resultEvent is QueuedRetainerItemSaleEvent retainerSaleEvent)
                        {
                            try
                            {

                                //foreach (var regex in retainerSaleRegexes[this.plugin.Interface.ClientState.ClientLanguage])
                                {
                                    //var matchInfo = regex.Match(retainerSaleEvent.Message.TextValue);

                                    var itemLink =
                                    retainerSaleEvent.Message.Payloads.First(x => x.Type == PayloadType.Item) as ItemPayload;

                                    var avatarUrl = Constant.LogoLink;

                                    if (itemLink == null)
                                    {
                                        PluginLog.Error("itemLink was null. Msg: {0}", BitConverter.ToString(retainerSaleEvent.Message.Encode()));
                                        break;
                                    }
                                    else
                                    {

                                        // XIVAPI wants these padded with 0s in the front if under 6 digits
                                        // at least if Titanium Ore testing is to be believed. 
                                        var iconFolder = $"{itemLink.Item.Icon / 1000 * 1000}".PadLeft(6,'0');
                                        var iconFile = $"{itemLink.Item.Icon}".PadLeft(6, '0');

                                        avatarUrl = $"https://xivapi.com" + $"/i/{iconFolder}/{iconFile}.png";
                                        /* 
                                        // we don't need this anymore because the above should work
                                        // but it doesn't hurt to have it commented out as a fallback for the future
                                        try
                                        {
                                            ItemResult res = XivApiClient.GetItem(itemLink.Item.RowId).GetAwaiter().GetResult();
                                            avatarUrl = $"https://xivapi.com{res.Icon}";
                                        }
                                        catch (Exception ex)
                                        {
                                            PluginLog.Error(ex, "Cannot fetch XIVAPI item search.");
                                        }
                                        */
                                    }

                                    //var valueInfo = matchInfo.Groups["value"];
                                    // not sure if using a culture here would work correctly, so just strip symbols instead
                                    //if (!valueInfo.Success || !int.TryParse(valueInfo.Value.Replace(",", "").Replace(".", ""), out var itemValue))
                                    //    continue;

                                    //SendItemSaleEvent(uint itemId, int amount, bool isHq, string message, XivChatType chatType)

                                    await this.plugin.Discord.SendItemSaleEvent(itemLink.Item.Name, avatarUrl, itemLink.Item.RowId, retainerSaleEvent.Message.TextValue, retainerSaleEvent.ChatType);
                                }
                            }
                            catch (Exception e)
                            {
                                PluginLog.Error(e, "Could not send discord message.");
                            }
                        }

                        if (resultEvent is QueuedChatEvent chatEvent)
                        {
                            var senderName = (chatEvent.ChatType == XivChatType.TellOutgoing || chatEvent.ChatType == XivChatType.Echo)
                                ? this.plugin.State.LocalPlayer.Name
                                : chatEvent.Sender.ToString();
                            var senderWorld = string.Empty;

                            // for debugging. Make sure to comment this out for releases.
                            /*
                            PluginLog.Debug($"Type: {chatEvent.ChatType} Sender: {chatEvent.Sender.TextValue} "
                                + $"Message: {chatEvent.Message.TextValue}");
                            */

                            try
                            {
                                if (this.plugin.State.LocalPlayer != null)
                                {
                                    var playerLink = chatEvent.Sender.Payloads.FirstOrDefault(x => x.Type == PayloadType.Player) as PlayerPayload;

                                    if (playerLink == null)
                                    {
                                        // chat messages from the local player do not include a player link, and are just the raw name
                                        // but we should still track other instances to know if this is ever an issue otherwise

                                        // Special case 2 - When the local player talks in party/alliance, the name comes through as raw text,
                                        // but prefixed by their position number in the party (which for local player may always be 1)
                                        if (chatEvent.Sender.TextValue.EndsWith(this.plugin.State.LocalPlayer.Name.TextValue))
                                        {
                                            senderName = this.plugin.State.LocalPlayer.Name;
                                        }
                                        else
                                        {
                                            // Franz is really tired of getting playerlink is null when there shouldn't be a player link for certain things
                                            switch (chatEvent.ChatType)
                                            {
                                                case XivChatType.Debug:
                                                    break;
                                                case XivChatType.Urgent:
                                                    break;
                                                case XivChatType.Notice:
                                                    break;
                                                case XivChatType.TellOutgoing:
                                                    senderName = this.plugin.State.LocalPlayer.Name;
                                                    // senderWorld = this.plugin.Interface.ClientState.LocalPlayer.HomeWorld.GameData.Name;
                                                    break;
                                                case XivChatType.StandardEmote:
                                                    playerLink = chatEvent.Message.Payloads.FirstOrDefault(x => x.Type == PayloadType.Player) as PlayerPayload;
                                                    senderName = playerLink.PlayerName;
                                                    senderWorld = playerLink.World.Name;
                                                    // we need to get the world here because cross-world people will be assumed local player's otherwise.
                                                    /*
                                                    senderWorld = chatEvent.Message.TextValue.TrimStart(senderName.ToCharArray()).Split(' ')[0];
                                                    if (senderWorld.EndsWith("'s")) // fuck having to do this
                                                        senderWorld = senderWorld.Substring(0, senderWorld.Length - 2);
                                                    */
                                                    break;
                                                case XivChatType.Echo:
                                                    senderName = this.plugin.State.LocalPlayer.Name;
                                                    // senderWorld = this.plugin.Interface.ClientState.LocalPlayer.HomeWorld.GameData.Name;
                                                    break;
                                                case (XivChatType)61: // NPC Talk
                                                    senderName = chatEvent.Sender.TextValue;
                                                    senderWorld = "NPC";
                                                    break;
                                                case (XivChatType)68: // NPC Announcement
                                                    senderName = chatEvent.Sender.TextValue;
                                                    senderWorld = "NPC";
                                                    break;
                                                default:
                                                    if ((int)chatEvent.ChatType >= 41 && (int)chatEvent.ChatType <= 55) //ignore a bunch of non-chat messages
                                                        break;
                                                    if ((int)chatEvent.ChatType >= 57 && (int)chatEvent.ChatType <= 70) //ignore a bunch of non-chat messages
                                                        break;
                                                    if ((int)chatEvent.ChatType >= 72 && (int)chatEvent.ChatType <= 100) // ignore a bunch of non-chat messages
                                                        break;
                                                    if ((int)chatEvent.ChatType > 107) // don't handle anything past CWLS8 for now
                                                        break;
                                                    PluginLog.Error($"playerLink was null.\nChatType: {chatEvent.ChatType} ({(int)chatEvent.ChatType}) Sender: {chatEvent.Sender.TextValue} Message: {chatEvent.Message.TextValue}");
                                                    senderName = chatEvent.Sender.TextValue;
                                                    break;
                                            }
                                            

                                            
                                        }

                                        // only if we still need one
                                        if (senderWorld.Equals(string.Empty))
                                            senderWorld = this.plugin.State.LocalPlayer.HomeWorld.GameData.Name;

                                        // PluginLog.Information($"FRANZDEBUGGINGNULL Playerlink is null: {senderName}＠{senderWorld}");
                                    }
                                    else
                                    {
                                        senderName = chatEvent.ChatType == XivChatType.TellOutgoing
                                            ? this.plugin.State.LocalPlayer.Name
                                            : playerLink.PlayerName;
                                        senderWorld = chatEvent.ChatType == XivChatType.TellOutgoing
                                            ? this.plugin.State.LocalPlayer.HomeWorld.GameData.Name
                                            : playerLink.World.Name;
                                        // PluginLog.Information($"FRANZDEBUGGING Playerlink was not null: {senderName}＠{senderWorld}");
                                    }
                                }
                                else
                                {
                                    // only do this one if it's debug
                                    /*
                                    PluginLog.Debug($"Plugin interface LocalPlayer was null.\n"
                                        + $"ChatType: {chatEvent.ChatType} ({(int)chatEvent.ChatType}) Sender: {chatEvent.Sender.TextValue} Message: {chatEvent.Message.TextValue}");
                                    */
                                    senderName = string.Empty;
                                    senderWorld = string.Empty;
                                }
                            }
                            catch(Exception ex)
                            {
                                PluginLog.Error(ex, "Could not deduce player name.");
                            }

                            string messagetext = "";
                            chatEvent.Message.Payloads.ForEach(x =>
                            {
                                if (x.Type == PayloadType.EmphasisItalic)
                                {
                                    messagetext += "_";
                                }
                                else if (x.Type == PayloadType.UIGlow)
                                {
                                    messagetext += "**";
                                }
                                else if (x is ITextProvider)
                                {
                                    messagetext += ((ITextProvider)x).Text;
                                }
                            });
                            // This indicates whether the message should be relayed to discord. By default, it should be
                            bool messageShouldBeSent = true;
                            // First check the blocking regex. If this regex matches, the message should not be sent
                            if (this.plugin.Config.BlockRegexes.ContainsKey(chatEvent.ChatType))
                            {
                                try
                                {
                                    // The message should only be sent if the blocking regex does not find a match
                                    messageShouldBeSent = !this.plugin.Config.BlockRegexes[chatEvent.ChatType].Match(messagetext).Success;
                                }
                                catch (Exception e)
                                {
                                    PluginLog.Error(e, "Failed to match message using regex.");
                                }
                            }
                            // Next check the allowing regex. If this regex matches, the message should not be sent
                            // We only check this if the blocklist has not already blocked the message
                            if (messageShouldBeSent && this.plugin.Config.AllowRegexes.ContainsKey(chatEvent.ChatType))
                            {
                                // If an allowing regex exists, we should disallow the message to be sent by default
                                messageShouldBeSent = false;
                                try
                                {
                                    // If the allowlist finds a match, the message should be sent
                                    messageShouldBeSent = this.plugin.Config.AllowRegexes[chatEvent.ChatType].Match(messagetext).Success;
                                }
                                catch (Exception e)
                                {
                                    PluginLog.Error(e, "Failed to match message using regex.");
                                }
                            }
                            // Finally, we send the message if it's allowed
                            if (messageShouldBeSent)
                            {
                                try
                                {
                                    await this.plugin.Discord.SendChatEvent(messagetext, senderName.TextValue, senderWorld, chatEvent.ChatType, chatEvent.AvatarUrl);
                                }
                                catch (Exception e)
                                {
                                    PluginLog.Error(e, "Could not send discord message.");
                                }
                            }
                        }

                        if (resultEvent is QueuedContentFinderEvent cfEvent)
                            try
                            {
                                await this.plugin.Discord.SendContentFinderEvent(cfEvent);
                            }
                            catch (Exception e)
                            {
                                PluginLog.Error(e, "Could not send discord message.");
                            }

                        
                    }
                    catch (Exception e)
                    {
                        PluginLog.Error(e, "Could not process event.");
                    }
                }

                Thread.Yield();
            }
        }
    }
}
