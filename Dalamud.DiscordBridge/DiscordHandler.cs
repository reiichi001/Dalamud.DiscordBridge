using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using Dalamud.DiscordBridge.Model;
using Dalamud.Game.Text;
using Dalamud.Plugin.Services;
using Discord;
using Discord.Webhook;
using Discord.WebSocket;
using Lumina.Text;
using NetStone;
using NetStone.Model.Parseables.Character;
using NetStone.Search.Character;

namespace Dalamud.DiscordBridge
{
    public class DiscordHandler : IDisposable
    {
        static readonly IPluginLog Logger = Service.Logger;

        private readonly DuplicateFilter duplicateFilter;
        
        private readonly DiscordSocketClient socketClient;
        private readonly SpecialCharsHandler specialChars;

        public bool IsConnected => this.socketClient.ConnectionState == ConnectionState.Connected;
        public ulong UserId => this.socketClient.CurrentUser.Id;

        private static readonly ConcurrentDictionary<string, LodestoneCharacter> CachedResponses = new();

        /// <summary>
        /// Defines if the bot has connected and verified that it has the correct permissions
        /// </summary>
        public DiscordState State { get; private set; } = DiscordState.None;

        private readonly DiscordBridgePlugin plugin;

        /// <summary>
        /// Chat types that are set when used the "all" setting.
        /// </summary>
        private static readonly XivChatType[] DefaultChatTypes =
        [
            XivChatType.Say,
            XivChatType.Shout,
            XivChatType.Yell,
            XivChatType.Party,
            XivChatType.CrossParty,
            XivChatType.PvPTeam,
            XivChatType.TellIncoming,
            XivChatType.Alliance,
            XivChatType.FreeCompany,
            XivChatType.Ls1,
            XivChatType.Ls2,
            XivChatType.Ls3,
            XivChatType.Ls4,
            XivChatType.Ls5,
            XivChatType.Ls6,
            XivChatType.Ls7,
            XivChatType.Ls8,
            XivChatType.NoviceNetwork,
            XivChatType.CustomEmote,
            XivChatType.StandardEmote,
            XivChatType.CrossLinkShell1,
            XivChatType.CrossLinkShell2,
            XivChatType.CrossLinkShell3,
            XivChatType.CrossLinkShell4,
            XivChatType.CrossLinkShell5,
            XivChatType.CrossLinkShell6,
            XivChatType.CrossLinkShell7,
            XivChatType.CrossLinkShell8,
            XivChatType.Echo,
            XivChatType.SystemMessage,
        ];

        /// <summary>
        /// Embed color signalling that everything is fine.
        /// </summary>
        private const int EmbedColorFine = 0x478CFF;

        /// <summary>
        /// Embed color signalling that everything is bad.
        /// </summary>
        private const int EmbedColorError = 0xD10303;

        /// <summary>
        /// The asynchronous message queue that is responsible for sending messages in order.
        /// </summary>
        public readonly DiscordMessageQueue MessageQueue;

        private LodestoneClient lodestoneClient;

        public DiscordHandler(DiscordBridgePlugin plugin)
        {
            this.plugin = plugin;

            this.specialChars = new SpecialCharsHandler();

            this.MessageQueue = new DiscordMessageQueue(this.plugin);

            Logger.Debug("BEFORE DiscordSocketClient");
            this.socketClient = new DiscordSocketClient(new DiscordSocketConfig
            {
                MessageCacheSize = 20, // hold onto the last 20 messages per channel in cache for duplicate checks
                GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.GuildMessages | GatewayIntents.GuildWebhooks | GatewayIntents.MessageContent,
            });
            Logger.Debug("AFTER DiscordSocketClient");
            this.socketClient.Ready += SocketClientOnReady;
            this.socketClient.MessageReceived += SocketClientOnMessageReceived;
            
            this.duplicateFilter = new DuplicateFilter(this.plugin, this.socketClient);
        }

        public async Task Start()
        {

            if (string.IsNullOrEmpty(this.plugin.Config.DiscordToken))
            {
                this.State = DiscordState.TokenInvalid;

                Logger.Error("Token empty, cannot start bot.");
                return;
            }

            try
            {
                await this.socketClient.LoginAsync(TokenType.Bot, this.plugin.Config.DiscordToken);
                await this.socketClient.StartAsync();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Token invalid, cannot start bot.");
            }

            this.MessageQueue.Start();

            lodestoneClient = await LodestoneClient.GetClientAsync();

            Logger.Debug("DiscordHandler START!!");
        }

        private Task SocketClientOnReady()
        {
            this.State = DiscordState.Ready;
            this.specialChars.TryFindEmote(this.socketClient);

            Logger.Verbose("DiscordHandler READY!!");
            
            return Task.CompletedTask;
        }

        private async Task SocketClientOnMessageReceived(SocketMessage message)
        {
            if (message.Author.IsBot || message.Author.IsWebhook)
                return;

            var args = message.Content.Split();

            // if it doesn't start with the bot prefix, ignore it.
            if (!args[0].StartsWith(this.plugin.Config.DiscordBotPrefix))
                return;

            /*
            // this is only needed for debugging purposes.
            foreach (var s in args)
            {
                Logger.Verbose(s);
            }
            */

            Logger.Verbose("Received command: {0}", args[0]);

            try
            {
                if (args[0] == this.plugin.Config.DiscordBotPrefix + "setchannel" &&
                    await EnsureOwner(message.Author, message.Channel))
                {
                    // Are there parameters?
                    if (args.Length == 1)
                    {
                        await SendGenericEmbed(message.Channel,
                            $"You need to specify some chat kinds to use.\nCheck the ``{this.plugin.Config.DiscordBotPrefix}help`` command for more information.",
                            "Error", EmbedColorError);

                        return;
                    }

                    var kinds = args[1].Split(',').Select(x => x.ToLower());

                    // Is there any chat type that's not recognized?
                    if (kinds
                        .Any(x =>
                        XivChatTypeExtensions.TypeInfoDict.All(y => y.Value.Slug != x) && x != "any"))
                    {
                        Logger.Verbose("Could not find kinds");
                        await SendGenericEmbed(message.Channel,
                            $"One or more of the chat kinds you specified could not be found.\nCheck the ``{this.plugin.Config.DiscordBotPrefix}help`` command for more information.",
                            "Error", EmbedColorError);

                        return;
                    }

                    if (!this.plugin.Config.ChannelConfigs.TryGetValue(message.Channel.Id, out var config))
                        config = new DiscordChannelConfig();

                    foreach (var selectedKind in kinds)
                    {
                        Logger.Verbose(selectedKind);

                        if (selectedKind == "any")
                        {
                            config.SetUnique(DefaultChatTypes);
                        }
                        else if (selectedKind == "tell")
                        {
                            config.SetUnique(XivChatType.TellOutgoing);
                            config.SetUnique(XivChatType.TellIncoming);
                        }
                        else if (selectedKind == "p")
                        {
                            config.SetUnique(XivChatType.Party);
                            config.SetUnique(XivChatType.CrossParty);
                        }
                        else
                        {
                            var chatType = XivChatTypeExtensions.GetBySlug(selectedKind);
                            config.SetUnique(chatType);
                        }
                    }

                    this.plugin.Config.ChannelConfigs[message.Channel.Id] = config;
                    this.plugin.Config.Save();

                    await SendGenericEmbed(message.Channel,
                        $"OK! This channel has been set to receive the following chat kinds:\n\n```\n{config.ChatTypes.Select(x => $"{x.GetFancyName()}").Aggregate((x, y) => x + "\n" + y)}```",
                        "Chat kinds set", EmbedColorFine);

                    return;
                }

                if (args[0] == this.plugin.Config.DiscordBotPrefix + "unsetchannel" &&
                    await EnsureOwner(message.Author, message.Channel))
                {
                    // Are there parameters?
                    if (args.Length == 1)
                    {
                        await SendGenericEmbed(message.Channel,
                            $"You need to specify some chat kinds to use.\nCheck the ``{this.plugin.Config.DiscordBotPrefix}help`` command for more information.",
                            "Error", EmbedColorError);

                        return;
                    }

                    var kinds = args[1].Split(',').Select(x => x.ToLower());

                    // Is there any chat type that's not recognized?
                    if (kinds.Any(x =>
                        XivChatTypeExtensions.TypeInfoDict.All(y => y.Value.Slug != x) && x != "any"))
                    {
                        await SendGenericEmbed(message.Channel,
                            $"One or more of the chat kinds you specified could not be found.\nCheck the ``{this.plugin.Config.DiscordBotPrefix}help`` command for more information.",
                            "Error", EmbedColorError);

                        return;
                    }

                    if (!this.plugin.Config.ChannelConfigs.TryGetValue(message.Channel.Id, out var config))
                        config = new DiscordChannelConfig();

                    foreach (var selectedKind in kinds)
                    {
                        if (selectedKind == "any")
                        {
                            config.UnsetUnique(DefaultChatTypes);
                        }
                        else if (selectedKind == "tell")
                        {
                            config.UnsetUnique(XivChatType.TellOutgoing);
                            config.UnsetUnique(XivChatType.TellIncoming);
                        }
                        else if (selectedKind == "p")
                        {
                            config.UnsetUnique(XivChatType.Party);
                            config.UnsetUnique(XivChatType.CrossParty);
                        }
                        else
                        {
                            var chatType = XivChatTypeExtensions.GetBySlug(selectedKind);
                            config.UnsetUnique(chatType);
                        }
                    }

                    this.plugin.Config.ChannelConfigs[message.Channel.Id] = config;
                    this.plugin.Config.Save();

                    if (config.ChatTypes.Count == 0)
                    {
                        await SendGenericEmbed(message.Channel,
                        $"All chat kinds have been removed from this channel.",
                        "Chat Kinds unset", EmbedColorFine);
                    }
                    await SendGenericEmbed(message.Channel,
                        $"OK! This channel will still receive the following chat kinds:\n\n```\n{config.ChatTypes.Select(x => $"{x.GetSlug()} - {x.GetFancyName()}").Aggregate((x, y) => x + "\n" + y)}```",
                        "Chat kinds unset", EmbedColorFine);

                    return;
                }

                if (args[0] == this.plugin.Config.DiscordBotPrefix + "setprefix" &&
                    await EnsureOwner(message.Author, message.Channel))
                {
                    // Are there parameters?
                    if (args.Length < 3)
                    {
                        await SendGenericEmbed(message.Channel,
                            $"You need to specify some chat kinds and a prefix to use.\nCheck the ``{this.plugin.Config.DiscordBotPrefix}help`` command for more information.",
                            "Error", EmbedColorError);

                        return;
                    }

                    var kinds = args[1].Split(',').Select(x => x.ToLower());

                    // Is there any chat type that's not recognized?
                    if (kinds.Any(x =>
                        XivChatTypeExtensions.TypeInfoDict.All(y => y.Value.Slug != x) && x != "any"))
                    {
                        await SendGenericEmbed(message.Channel,
                            $"One or more of the chat kinds you specified could not be found.\nCheck the ``{this.plugin.Config.DiscordBotPrefix}help`` command for more information.",
                            "Error", EmbedColorError);

                        return;
                    }

                    if (args[2] == "none")
                        args[2] = string.Empty;

                    foreach (var selectedKind in kinds)
                    {
                        // Special handling for chat types that share a type
                        if (selectedKind == "tell")
                        {
                            this.plugin.Config.PrefixConfigs[XivChatType.TellOutgoing] = args[2];
                            this.plugin.Config.PrefixConfigs[XivChatType.TellIncoming] = args[2];
                        }
                        else if (selectedKind == "p")
                        {
                            this.plugin.Config.PrefixConfigs[XivChatType.Party] = args[2];
                            this.plugin.Config.PrefixConfigs[XivChatType.CrossParty] = args[2];
                        }
                        else
                        {
                            var type = XivChatTypeExtensions.GetBySlug(selectedKind);
                            this.plugin.Config.PrefixConfigs[type] = args[2];
                        }

                    }

                    this.plugin.Config.Save();


                    await SendGenericEmbed(message.Channel,
                        $"OK! The following prefixes are set:\n\n```\n{this.plugin.Config.PrefixConfigs.Select(x => $"{x.Key.GetFancyName()} - {x.Value}").Aggregate((x, y) => x + "\n" + y)}```",
                        "Prefix set", EmbedColorFine);

                    return;
                }



                if (args[0] == this.plugin.Config.DiscordBotPrefix + "unsetprefix" &&
                    await EnsureOwner(message.Author, message.Channel))
                {
                    // Are there parameters?
                    if (args.Length < 2)
                    {
                        await SendGenericEmbed(message.Channel,
                            $"You need to specify some chat kinds and a prefix to use.\nCheck the ``{this.plugin.Config.DiscordBotPrefix}help`` command for more information.",
                            "Error", EmbedColorError);

                        return;
                    }

                    var kinds = args[1].Split(',').Select(x => x.ToLower());

                    // Is there any chat type that's not recognized?
                    if (kinds.Any(x =>
                        XivChatTypeExtensions.TypeInfoDict.All(y => y.Value.Slug != x) && x != "any"))
                    {
                        await SendGenericEmbed(message.Channel,
                            $"One or more of the chat kinds you specified could not be found.\nCheck the ``{this.plugin.Config.DiscordBotPrefix}help`` command for more information.",
                            "Error", EmbedColorError);

                        return;
                    }

                    foreach (var selectedKind in kinds)
                    {
                        var type = XivChatTypeExtensions.GetBySlug(selectedKind);
                        // Special handling for chat types that share a type
                        if (selectedKind == "tell")
                        {
                            this.plugin.Config.PrefixConfigs.Remove(XivChatType.TellOutgoing);
                            this.plugin.Config.PrefixConfigs.Remove(XivChatType.TellIncoming);
                        }
                        else if (selectedKind == "p")
                        {
                            this.plugin.Config.PrefixConfigs.Remove(XivChatType.Party);
                            this.plugin.Config.PrefixConfigs.Remove(XivChatType.CrossParty);
                        }
                        else
                        {
                            this.plugin.Config.PrefixConfigs.Remove(type);
                        }
                        
                    }

                    this.plugin.Config.Save();

                    if (this.plugin.Config.PrefixConfigs.Count == 0 )
                    {
                        await SendGenericEmbed(message.Channel,
                        $"All prefixes have been removed.",
                        "Prefix unset", EmbedColorFine);
                    }
                    else // this doesn't seem to trigger when there's only one entry left. I don't know why.
                    {
                        await SendGenericEmbed(message.Channel,
                        $"OK! The prefix for {XivChatTypeExtensions.GetBySlug(args[2])} has been removed.\n\n"
                        + $"The following prefixes are still set:\n\n```\n{this.plugin.Config.PrefixConfigs.Select(x => $"{x.Key.GetFancyName()} - {x.Value}").Aggregate((x, y) => x + "\n" + y)}```",
                        "Prefix unset", EmbedColorFine);
                    }

                    return;
                }

                if (args[0] == this.plugin.Config.DiscordBotPrefix + "setchattypename" &&
                    await EnsureOwner(message.Author, message.Channel))
                {
                    // Are there parameters?
                    if (args.Length < 3)
                    {
                        await SendGenericEmbed(message.Channel,
                            $"You need to specify one or more chat kinds and a custom name.\nCheck the ``{this.plugin.Config.DiscordBotPrefix}help`` command for more information.",
                            "Error", EmbedColorError);

                        return;
                    }

                    

                    var kinds = args[1].Split(',').Select(x => x.ToLower());
                    var chatChannelOverride = string.Join(" ", args.Skip(2)).Trim('"');

                    // Logger.Information($"arg1: {args[1]}; arg2: {chatChannelOverride}");

                    // Is there any chat type that's not recognized?
                    if (kinds.Any(x =>
                        XivChatTypeExtensions.TypeInfoDict.All(y => y.Value.Slug != x) && x != "any"))
                    {
                        await SendGenericEmbed(message.Channel,
                            $"One or more of the chat kinds you specified could not be found.\nCheck the ``{this.plugin.Config.DiscordBotPrefix}help`` command for more information.",
                            "Error", EmbedColorError);

                        return;
                    }

                    if (chatChannelOverride == "none")
                    {
                        foreach (var selectedKind in kinds)
                        {
                            var type = XivChatTypeExtensions.GetBySlug(selectedKind);
                            this.plugin.Config.CustomSlugsConfigs[type] = type.GetSlug();
                        }

                        await SendGenericEmbed(message.Channel,
                        $"OK! The following custom chat type names have been set:\n\n```\n{this.plugin.Config.CustomSlugsConfigs.Select(x => $"{x.Key.GetFancyName()} - {x.Value}").Aggregate((x, y) => x + "\n" + y)}```",
                        "Custom chat type set", EmbedColorFine);
                    }
                    else
                    {
                        foreach (var selectedKind in kinds)
                        {
                            // Special handling for chat types that share a type
                            if (selectedKind == "tell")
                            {
                                this.plugin.Config.CustomSlugsConfigs[XivChatType.TellOutgoing] = chatChannelOverride;
                                this.plugin.Config.CustomSlugsConfigs[XivChatType.TellIncoming] = chatChannelOverride;
                            }
                            else if (selectedKind == "p")
                            {
                                this.plugin.Config.CustomSlugsConfigs[XivChatType.Party] = chatChannelOverride;
                                this.plugin.Config.CustomSlugsConfigs[XivChatType.CrossParty] = chatChannelOverride;
                            }
                            else
                            {
                                var type = XivChatTypeExtensions.GetBySlug(selectedKind);
                                this.plugin.Config.CustomSlugsConfigs[type] = chatChannelOverride;
                            }
                        }

                        await SendGenericEmbed(message.Channel,
                        $"OK! The following custom chat type names have been set:\n\n```\n{this.plugin.Config.CustomSlugsConfigs.Select(x => $"{x.Key.GetFancyName()} - {x.Value}").Aggregate((x, y) => x + "\n" + y)}```",
                        "Custom chat type set", EmbedColorFine);
                    }

                    this.plugin.Config.Save();

                    return;
                }

                if (args[0] == this.plugin.Config.DiscordBotPrefix + "unsetchattypename" &&
                    await EnsureOwner(message.Author, message.Channel))
                {
                    // Are there parameters?
                    if (args.Length < 2)
                    {
                        await SendGenericEmbed(message.Channel,
                            $"One or more of the chat kinds you specified could not be found.\nCheck the ``{this.plugin.Config.DiscordBotPrefix}help`` command for more information.",
                            "Error", EmbedColorError);

                        return;
                    }



                    var kinds = args[1].Split(',').Select(x => x.ToLower());

                    Logger.Information($"Unsetting custom type name for arg1: {args[1]}");

                    // Is there any chat type that's not recognized?
                    if (kinds.Any(x =>
                        XivChatTypeExtensions.TypeInfoDict.All(y => y.Value.Slug != x) && x != "any"))
                    {
                        await SendGenericEmbed(message.Channel,
                            $"One or more of the chat kinds you specified could not be found.\nCheck the ``{this.plugin.Config.DiscordBotPrefix}help`` command for more information.",
                            "Error", EmbedColorError);

                        return;
                    }


                    foreach (var selectedKind in kinds)
                    {
                        var type = XivChatTypeExtensions.GetBySlug(selectedKind);
                        this.plugin.Config.CustomSlugsConfigs[type] = type.GetSlug();
                    }

                    await SendGenericEmbed(message.Channel,
                    $"OK! The following custom chat type names have been set:\n\n```\n{this.plugin.Config.CustomSlugsConfigs.Select(x => $"{x.Key.GetFancyName()} - {x.Value}").Aggregate((x, y) => x + "\n" + y)}```",
                    "Custom chat type unset", EmbedColorFine);


                    this.plugin.Config.Save();

                    return;
                }

                if (args[0] == this.plugin.Config.DiscordBotPrefix + "setduplicatems" &&
                    await EnsureOwner(message.Author, message.Channel))
                {
                    // Are there parameters?
                    if (args.Length != 2)
                    {
                        await SendGenericEmbed(message.Channel,
                            $"You need to specify a number in milliseconds to use.\nCheck the ``{this.plugin.Config.DiscordBotPrefix}help`` command for more information.",
                            "Error", EmbedColorError);

                        return;
                    }

                    // Make sure that it's a number (or assume it is)
                    if (!int.TryParse(args[1], out int newDelay))
                    {
                        await SendGenericEmbed(message.Channel,
                            $"You need to specify a positive number in milliseconds to use, or 0 to turn the feature off.\nCheck the ``{this.plugin.Config.DiscordBotPrefix}help`` command for more information.",
                            "Error", EmbedColorError);

                        return;
                    }


                    if (args[1].ToLower() == "none")
                        newDelay = 0;

                    if (newDelay < 0)
                    {
                        await SendGenericEmbed(message.Channel,
                            $"You need to specify a positive number in milliseconds to use, or 0 to turn the feature off.\nCheck the ``{this.plugin.Config.DiscordBotPrefix}help`` command for more information.",
                            "Error", EmbedColorError);

                        return;
                    }

                    this.plugin.Config.DuplicateCheckMS = newDelay;
                    this.plugin.Config.Save();

                    if (newDelay == 0)
                    {
                        await SendGenericEmbed(message.Channel,
                            $"OK! The duplicate chat removal feature has been disabled.", "Duplicate Message Check", EmbedColorFine);
                    }
                    else
                    {
                        await SendGenericEmbed(message.Channel,
                            $"OK! Any messages with the same content within the last **{newDelay}** milliseconds will be skipped, preventing duplicate posts.", "Duplicate Message Check", EmbedColorFine);
                    }

                    return;
                }

                if (args[0] == this.plugin.Config.DiscordBotPrefix + "toggledf" &&
                    await EnsureOwner(message.Author, message.Channel))
                {
                    if (!this.plugin.Config.ChannelConfigs.TryGetValue(message.Channel.Id, out var config))
                        config = new DiscordChannelConfig();

                    config.IsContentFinder = !config.IsContentFinder;

                    this.plugin.Config.ChannelConfigs[message.Channel.Id] = config;
                    this.plugin.Config.Save();

                    await SendGenericEmbed(message.Channel,
                        $"OK! This channel has been {(config.IsContentFinder ? "enabled" : "disabled")} from receiving Duty Finder notifications.",
                        "Duty Finder set", EmbedColorFine);

                    return;
                }

                if (args[0] == this.plugin.Config.DiscordBotPrefix + "toggleembed" &&
                    await EnsureOwner(message.Author, message.Channel))
                {
                    this.plugin.Config.ForceEmbedFallbackMode = !this.plugin.Config.ForceEmbedFallbackMode;
                    this.plugin.Config.Save();

                    await SendGenericEmbed(message.Channel,
                        $"OK! Sending relayed messages in embed fallback mode have been {(this.plugin.Config.ForceEmbedFallbackMode ? "**enabled**." : "**disabled**.\n\nPlease make sure Discord Chat Bridge is allowed to manage webhooks in your channels as needed.")}",
                        "Embed Fallback Mode set", EmbedColorFine);

                    return;
                }

                if (args[0] == this.plugin.Config.DiscordBotPrefix + "toggledefaultnameavatar" &&
                    await EnsureOwner(message.Author, message.Channel))
                {
                    this.plugin.Config.ForceDefaultNameAvatar = !this.plugin.Config.ForceDefaultNameAvatar;
                    this.plugin.Config.Save();

                    await SendGenericEmbed(message.Channel,
                        $"OK! Sending relayed messages in embed fallback mode have been {(this.plugin.Config.ForceDefaultNameAvatar ? "**enabled**.\n\nPlease make sure you have toggled sender names on, or you won't know who said which chat messages!" : "**disabled**.")}",
                        "Default Name and Avatar Mode set", EmbedColorFine);

                    if (this.plugin.Config.ForceDefaultNameAvatar && this.socketClient.CurrentUser.Username.ToLower().Contains("discord"))
                    {
                        await SendGenericEmbed(message.Channel, "Bot username cannot contain Discord. Using a fallback value until this is changed.", "ERROR - Bot username cannot contain Discord", EmbedColorError);
                    }

                    return;
                }
                

                if (args[0] == this.plugin.Config.DiscordBotPrefix + "togglesender" &&
                    await EnsureOwner(message.Author, message.Channel))
                {
                    this.plugin.Config.SenderInMessage = !this.plugin.Config.SenderInMessage;
                    this.plugin.Config.Save();

                    await SendGenericEmbed(message.Channel,
                        $"OK! Discord Chat Bridge **{(this.plugin.Config.SenderInMessage ? "will" : "will not")}** include the sender name in all messages (where possible).",
                        "Sender In Message Mode set", EmbedColorFine);

                    return;
                }

                if (args[0] == this.plugin.Config.DiscordBotPrefix + "setcfprefix" &&
                    await EnsureOwner(message.Author, message.Channel))
                {
                    // Are there parameters?
                    if (args.Length < 2)
                    {
                        await SendGenericEmbed(message.Channel,
                            $"You need to specify a prefix to use, or type \"none\" if you want to remove it.\nCheck the ``{this.plugin.Config.DiscordBotPrefix}help`` command for more information.",
                            "Error", EmbedColorError);

                        return;
                    }

                    if (args[1] == "none")
                        args[1] = string.Empty;

                    this.plugin.Config.CFPrefixConfig = args[1];

                    this.plugin.Config.Save();


                    await SendGenericEmbed(message.Channel,
                        $"OK! The following prefix was set:\n\n```\n{this.plugin.Config.CFPrefixConfig}```",
                        "Prefix set", EmbedColorFine);

                    return;
                }

                if (args[0] == this.plugin.Config.DiscordBotPrefix + "listchannel" &&
                    await EnsureOwner(message.Author, message.Channel))
                {
                    if (!this.plugin.Config.ChannelConfigs.TryGetValue(message.Channel.Id, out var config))
                    {
                        await SendGenericEmbed(message.Channel,
                            $"You didn't set up any channel kinds for this channel yet.\nPlease use the ``{this.plugin.Config.DiscordBotPrefix}setchannel`` command to do this.",
                            "Error", EmbedColorError);
                        return;
                    }

                    if (config == null || config.ChatTypes.Count == 0) 
                    {
                        await SendGenericEmbed(message.Channel,
                            $"There are no channel kinds set for this channel right now.\nPlease use the ``{this.plugin.Config.DiscordBotPrefix}setchannel`` command to do this.",
                            "Error", EmbedColorFine);
                        return;
                    }

                    await SendGenericEmbed(message.Channel,
                        $"OK! This channel has been set to receive the following chat kinds:\n\n```\n{config.ChatTypes.Select(x => $"{x.GetFancyName()}").Aggregate((x, y) => x + "\n" + y)}```",
                        "Chat kinds set", EmbedColorFine);

                    return;
                }

                if (args[0] == this.plugin.Config.DiscordBotPrefix + "setavatar")
                {
                    // Are there parameters?
                    if (args.Length != 3)
                    {
                        await SendGenericEmbed(message.Channel,
                            $"You need to specify one or more chat kinds and a custom avatar url.\nCheck the ``{this.plugin.Config.DiscordBotPrefix}help`` command for more information.",
                            "Error", EmbedColorError);

                        return;
                    }

                    var kinds = args[1].Split(',').Select(x => x.ToLower());
                    var avatarURL = args[2].Replace("<", "").Replace(">", "").Trim();


                    if (args[1] == "default" && avatarURL == "none")
                    {
                        this.plugin.Config.DefaultAvatarURL = Constant.LogoLink;

                        await SendGenericEmbed(message.Channel,
                        $"OK! The default/fallback avatar has been reset to default.",
                        "Custom fallback avatar reset", EmbedColorFine);

                        this.plugin.Config.Save();
                        return;
                    }
                    if (args[1] == "default")
                    {
                        this.plugin.Config.DefaultAvatarURL = avatarURL;

                        await SendGenericEmbed(message.Channel,
                        $"OK! The default/fallback avatar has been set to ``{avatarURL}``",
                        "Custom fallback avatar set", EmbedColorFine);

                        this.plugin.Config.Save();
                        return;
                    }
                    

                    // Is there any chat type that's not recognized?
                    if (kinds.Any(x =>
                        XivChatTypeExtensions.TypeInfoDict.All(y => y.Value.Slug != x) && x != "any"))
                    {
                        await SendGenericEmbed(message.Channel,
                            $"One or more of the chat kinds you specified could not be found.\nCheck the ``{this.plugin.Config.DiscordBotPrefix}help`` command for more information.",
                            "Error", EmbedColorError);

                        return;
                    }

                    if (avatarURL == "none")
                    {
                        foreach (var selectedKind in kinds)
                        {
                            var type = XivChatTypeExtensions.GetBySlug(selectedKind);
                            this.plugin.Config.ChatTypeAvatarURL[type] = Constant.LogoLink;
                        }

                        await SendGenericEmbed(message.Channel,
                        $"OK! The following custom chat type names have been set:\n\n```\n{this.plugin.Config.CustomSlugsConfigs.Select(x => $"{x.Key.GetFancyName()} - {x.Value}").Aggregate((x, y) => x + "\n" + y)}```",
                        "Custom avatar set", EmbedColorFine);
                    }
                    else
                    {
                        foreach (var selectedKind in kinds)
                        {
                            // Special handling for chat types that share a type
                            if (selectedKind == "tell")
                            {
                                this.plugin.Config.ChatTypeAvatarURL[XivChatType.TellOutgoing] = avatarURL;
                                this.plugin.Config.ChatTypeAvatarURL[XivChatType.TellIncoming] = avatarURL;
                            }
                            else if (selectedKind == "p")
                            {
                                this.plugin.Config.ChatTypeAvatarURL[XivChatType.Party] = avatarURL;
                                this.plugin.Config.ChatTypeAvatarURL[XivChatType.CrossParty] = avatarURL;
                            }
                            else
                            {
                                var type = XivChatTypeExtensions.GetBySlug(selectedKind);
                                this.plugin.Config.ChatTypeAvatarURL[type] = avatarURL;
                            }
                        }

                        await SendGenericEmbed(message.Channel,
                        $"OK! The following custom chat type names have been set:\n\n```\n{this.plugin.Config.ChatTypeAvatarURL.Select(x => $"{x.Key.GetFancyName()} - {x.Value}").Aggregate((x, y) => x + "\n" + y)}```",
                        "Custom chat type set", EmbedColorFine);
                    }

                    this.plugin.Config.Save();

                    return;
                }

                if (args[0] == this.plugin.Config.DiscordBotPrefix + "unsetavatar")
                {
                    if (args.Length != 2)
                    {
                        await SendGenericEmbed(message.Channel,
                            $"You have entered this command incorrectly. Please try again.",
                            "Error", EmbedColorError);
                    }

                    if (args[1] == "default")
                    {
                        this.plugin.Config.DefaultAvatarURL = Constant.LogoLink;

                        await SendGenericEmbed(message.Channel,
                        $"OK! The default/fallback avatar has been reset.",
                        "Custom fallback avatar reset", EmbedColorFine);

                        this.plugin.Config.Save();
                        return;
                    }

                    var kinds = args[1].Split(',').Select(x => x.ToLower());

                    // Logger.Information($"Looking for `{chatTypeSlug}`");

                    // Is there any chat type that's not recognized?
                    if (kinds.Any(x =>
                        XivChatTypeExtensions.TypeInfoDict.All(y => y.Value.Slug != x) && x != "any"))
                    {
                        await SendGenericEmbed(message.Channel,
                            $"One or more of the chat kinds you specified could not be found.\nCheck the ``{this.plugin.Config.DiscordBotPrefix}help`` command for more information.",
                            "Error", EmbedColorError);

                        return;
                    }

                    foreach (var selectedKind in kinds)
                    {
                        // Special handling for chat types that share a type
                        if (selectedKind == "tell")
                        {
                            this.plugin.Config.ChatTypeAvatarURL.Remove(XivChatType.TellOutgoing);
                            this.plugin.Config.ChatTypeAvatarURL.Remove(XivChatType.TellIncoming);
                        }
                        else if (selectedKind == "p")
                        {
                            this.plugin.Config.ChatTypeAvatarURL.Remove(XivChatType.Party);
                            this.plugin.Config.ChatTypeAvatarURL.Remove(XivChatType.CrossParty);
                        }
                        else
                        {
                            var type = XivChatTypeExtensions.GetBySlug(selectedKind);
                            this.plugin.Config.ChatTypeAvatarURL.Remove(type);
                        }
                    }

                    if (this.plugin.Config.ChatTypeAvatarURL.Count == 0)
                    {
                        await SendGenericEmbed(message.Channel,
                            $"OK! There are no custom avatar overrides set.",
                            "Custom chat type unset", EmbedColorFine);
                    }
                    else
                    {
                        await SendGenericEmbed(message.Channel,
                            $"OK! The following custom chat type names are set:\n\n```\n{this.plugin.Config.ChatTypeAvatarURL.Select(x => $"{x.Key.GetFancyName()} - {x.Value}").Aggregate((x, y) => x + "\n" + y)}```",
                            "Custom chat type unset", EmbedColorFine);
                    }
                    

                    plugin.Config.Save();
                    return;
                    
                }

                if (args[0] == this.plugin.Config.DiscordBotPrefix + "help")
                {
                    Logger.Verbose("Help time");

                    var builder = new EmbedBuilder()
                        .WithTitle("Discord Bridge Help")
                        .WithDescription("You can use the following commands to set up the Discord bridge.")
                        .WithColor(new Color(EmbedColorFine))
                        .AddField($"{this.plugin.Config.DiscordBotPrefix}setchannel", "Select, which kinds of chat should arrive in this channel.\n" +
                                                 $"Format: ``{this.plugin.Config.DiscordBotPrefix}setchannel <kind1,kind2,...>``\n\n" +
                                                 $"See [this link for a list of all available chat kinds]({Constant.KindListLink}) or type ``any`` to enable it for all regular chat messages.")
                        //$"The following chat kinds are available:\n```all - All regular chat\n{XivChatTypeExtensions.TypeInfoDict.Select(x => $"{x.Value.Slug} - {x.Value.FancyName}").Aggregate((x, y) => x + "\n" + y)}```")
                        .AddField($"{this.plugin.Config.DiscordBotPrefix}unsetchannel", "Works like the previous command, but removes kinds of chat from the list of kinds that are sent to this channel.")
                        .AddField($"{this.plugin.Config.DiscordBotPrefix}listchannel", "List all chat kinds that are sent to this channel.")
                        .AddField($"{this.plugin.Config.DiscordBotPrefix}toggledefaultnameavatar","Enable or disable sending webhook messages using your configured bot's name and the default bot avatar.\n**WARNING:**This should be combined with enabling the `togglesender` function or you will be removing any distinction between different player messages.")
                        .AddField($"{this.plugin.Config.DiscordBotPrefix}toggledf", "Enable or disable sending duty finder updates to this channel.")
                        .AddField($"{this.plugin.Config.DiscordBotPrefix}toggleembed", "Enable or disable sending messages as Webhooks (default) or Embeds (Fallback mode)")
                        .AddField($"{this.plugin.Config.DiscordBotPrefix}togglesender", "Enable or disable sending messages with the sender name in the message.")
                        .AddField($"{this.plugin.Config.DiscordBotPrefix}setduplicatems", "Set time in milliseconds that the bot will check to see if any past messages were the same. Default is 0 ms.")
                        .AddField($"{this.plugin.Config.DiscordBotPrefix}setprefix", "Set a prefix for chat kinds. "
                            + $"This can be an emoji or a string that will be prepended to every chat message that will arrive with this chat kind. "
                            + $"You can also set it to `none` if you want to remove it.\n" 
                            + $"Format: ``{this.plugin.Config.DiscordBotPrefix}setprefix <kind1,kind2,...> <prefix>``")
                        .AddField($"{this.plugin.Config.DiscordBotPrefix}setcfprefix", "Set a prefix for duty finder posts. "
                            + $"You can also set it to `none` if you want to remove it.\n" 
                            + $"Format: ``{this.plugin.Config.DiscordBotPrefix}setcfprefix <prefix>``")
                        .AddField($"{this.plugin.Config.DiscordBotPrefix}setchattypename ", "Set custom text for chat kinds. "
                            + $"This can be an emoji or a string that will replace the short name of a chat kind for every chat message that will arrive with this chat kind. "
                            + $"You can also set it to `none` if you want to remove it.\n" 
                            + $"Format: ``{this.plugin.Config.DiscordBotPrefix}setchattypename  <kind1,kind2,...> <custom text>``")
                        .AddField($"{this.plugin.Config.DiscordBotPrefix}unsetprefix", "Remove prefix set for a chat kind. \n"
                            + $"Format: ``{this.plugin.Config.DiscordBotPrefix}unsetprefix <kind>``")
                        .AddField($"{this.plugin.Config.DiscordBotPrefix}unsetchattypename", "Remove custom name for a chat kind. \n"
                            + $"Format: ``{this.plugin.Config.DiscordBotPrefix}unsetchattypename <kind>``")
                        .AddField($"{this.plugin.Config.DiscordBotPrefix}setavatar <kind> <url>", "Set custom fallback avator for a chat kind. "
                            + "Use ``default`` as the fallback for any unconfigured overrides.\n"
                            + "__NOTE__: Upload the icon to Discord first if you don't have a URL already.\n"
                            + $"Format: ``{this.plugin.Config.DiscordBotPrefix}setavatar <kind> <url>``")
                        .AddField($"{this.plugin.Config.DiscordBotPrefix}unsetavatar <kind>", "Unset custom fallback avator for a chat kind. "
                            + "Use ``default`` to reset the fallback for any unconfigured overrides.\n"
                            + $"Format: ``{this.plugin.Config.DiscordBotPrefix}unsetavatar <kind>``")
                        .AddField("Need more help?",
                            $"You can [read the full step-by-step guide]({Constant.HelpLink}) or [join our Discord server]({Constant.DiscordJoinLink}) to ask for help.")
                        .WithFooter(footer =>
                        {
                            footer
                                .WithText("Dalamud Chat Bridge")
                                .WithIconUrl(Constant.LogoLink);
                        })
                        .WithThumbnailUrl(Constant.LogoLink);
                    var embed = builder.Build();

                    var m = await message.Channel.SendMessageAsync(
                            null,
                            embed: embed)
                        .ConfigureAwait(false);
                    ;
                    Logger.Verbose(m.Id.ToString());

                    return;
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Could not handle incoming Discord message.");
            }
        }

        private static async Task SendGenericEmbed(ISocketMessageChannel channel, string message, string title, uint color)
        {
            var builder = new EmbedBuilder()
                .WithTitle(title)
                .WithDescription(message)
                .WithColor(new Color(color))
                .WithFooter(footer => {
                    footer
                        .WithText("Dalamud Chat Bridge")
                        .WithIconUrl(Constant.LogoLink);
                })
                .WithThumbnailUrl(Constant.LogoLink);
                
            var embed = builder.Build();
            await channel.SendMessageAsync(
                    null,
                    embed: embed)
                .ConfigureAwait(false);
        }

        private static async Task SendPrettyEmbed(ISocketMessageChannel channel, string message, string title, string iconurl, uint color)
        {
            var builder = new EmbedBuilder()
                .WithTitle(title)
                .WithDescription(message)
                .WithColor(new Color(color))
                .WithFooter(footer => {
                    footer
                        .WithText("Dalamud Chat Bridge")
                        .WithIconUrl(Constant.LogoLink);
                })
                .WithThumbnailUrl(iconurl);

            var embed = builder.Build();
            await channel.SendMessageAsync(
                    null,
                    embed: embed)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Check if the sender of this message is set as the owner of this plugin, and send an error message to the specified channel if not null.
        /// </summary>
        /// <param name="user">User in question.</param>
        /// <param name="errorMessageChannel">Channel for error message.</param>
        /// <returns>True if the user is the owner of this plugin.</returns>
        private async Task<bool> EnsureOwner(IUser user, ISocketMessageChannel errorMessageChannel = null)
        {
            Logger.Verbose("EnsureOwner: " + user.Username + "#" + user.Discriminator);
            if (user.Username + "#" + user.Discriminator == this.plugin.Config.DiscordOwnerName) 
                return true;

            // bandaid for Discord's username changes.
            if (user.Username == this.plugin.Config.DiscordOwnerName && user.Discriminator == "0000")
                return true;

            if (ulong.TryParse(this.plugin.Config.DiscordOwnerName, out ulong parsed))
                if (user.Id == parsed)
                    return true;

            if (errorMessageChannel == null) 
                return false;

            await SendGenericEmbed(errorMessageChannel, "You are not allowed to run commands for this bot.\n\nIf this is your bot, please use the \"/pdiscord\" command in-game to enter your username or Discord User ID number.", "Error", EmbedColorError);

            return false;
        }

        public async Task SendItemSaleEvent(SeString name, string iconurl, uint itemId, string message, XivChatType chatType)
        {
            var applicableChannels =
                this.plugin.Config.ChannelConfigs.Where(x => x.Value.ChatTypes.Contains(chatType));

            if (!applicableChannels.Any())
                return;

            message = this.specialChars.TransformToUnicode(message);

            
            Logger.Information($"Retainer sold itemID: {itemId} with iconurl: {iconurl}");

            this.plugin.Config.PrefixConfigs.TryGetValue(chatType, out var prefix);

            foreach (var channelConfig in applicableChannels)
            {
                var socketChannel = this.socketClient.GetChannel(channelConfig.Key);

                if (socketChannel == null)
                {
                    Logger.Error("Could not find channel {0} for {1}", channelConfig.Key, chatType);
                    continue;
                }

                // add handling for webhook vs embed here
                IGuildChannel guildChannel = (IGuildChannel)socketChannel;
                IGuildUser guildUser = await guildChannel.Guild.GetUserAsync(this.socketClient.CurrentUser.Id);
                bool hasManageWebHooks = guildUser.GetPermissions(guildChannel).Has(ChannelPermission.ManageWebhooks);

                if (socketChannel is SocketDMChannel)
                {
                    var DMChannel = await this.socketClient.GetDMChannelAsync(channelConfig.Key);
                    await SendPrettyEmbed((ISocketMessageChannel)DMChannel, message, $"Retainer sold {name}", iconurl, EmbedColorFine);
                }
                else if (!hasManageWebHooks)
                {
                    Logger.Debug("FALLBACKMODE - Unable to create WebHook - No permission\n");
                    await SendPrettyEmbed((ISocketMessageChannel)socketChannel, $"FALLBACKMODE\n\nMissing ManageWebHooks permission.\n\n{message}", $"Retainer sold {name}", iconurl, EmbedColorError);
                }
                else
                {
                    var webhookClient = await GetOrCreateWebhookClient(socketChannel);
                    if (webhookClient != null)
                    {
                        await webhookClient.SendMessageAsync($"{prefix} {message}",
                        username: $"Retainer sold {name}", avatarUrl: iconurl);
                    }
                    else
                    {
                        Logger.Debug("FALLBACKMODE - Unable to create WebHook\n");
                        await SendPrettyEmbed((ISocketMessageChannel)socketChannel, $"FALLBACKMODE\nUnable to create WebHook\n\n{message}", $"Retainer sold {name}", iconurl, EmbedColorError);
                    }
                    
                }
                    
            }

        }

        public async Task SendChatEvent(string message, string senderName, string senderWorld, XivChatType chatType, string avatarUrl = "")
        {
            // set fields for true chat messages or custom via ipc
            if (chatType != XivChatTypeExtensions.IpcChatType)
            {
                // Special case for outgoing tells, these should be sent under Incoming tells
                if (chatType == XivChatType.TellOutgoing) {
                    chatType = XivChatType.TellIncoming;
                }
            }
            else
            {
                senderWorld = null;
            }

            // default avatar url to logo link if empty
            if (string.IsNullOrEmpty(avatarUrl))
            {
                
                if (!plugin.Config.ChatTypeAvatarURL.TryGetValue(chatType, out avatarUrl))
                {
                    avatarUrl = plugin.Config.DefaultAvatarURL;
                }
                
            }

            var applicableChannels =
                this.plugin.Config.ChannelConfigs.Where(x => x.Value.ChatTypes.Contains(chatType));

            if (!applicableChannels.Any())
                return;

            message = this.specialChars.TransformToUnicode(message);

            bool characterSearchFailed = false;
            
            try
            {
                switch (chatType)
                {
                    case XivChatType.Echo:
                        break;
                    case (XivChatType)61: // npc talk
                        break;
                    case (XivChatType)68: // npc announce
                        break;
                    default:
                        // don't even bother searching if it's gonna be invalid
                        bool doSearch = true;
                        
                        if (string.IsNullOrEmpty(senderName))
                        {
                            Logger.Debug($"Sender Name was null or empty");
                            senderName = $"FFXIV Bridge Worker {Service.State.LocalPlayer?.Name}";
                            senderWorld = "";
                            doSearch = false;
                        }
                        if (string.IsNullOrEmpty(senderWorld))
                        {
                            Logger.Debug($"Sender World was null or empty: {senderWorld}");
                            doSearch = false;
                        }
                        
                        // special cases for things that aren't coming from FFXIV directly.
                        if (senderName == "Sonar")
                        {
                            Logger.Debug($"Sender Name was {senderName}");
                            doSearch = false;
                        }
                        else if (!senderName.Contains(' '))
                        {
                            Logger.Debug($"Sender Name invalid: {senderName}");
                            doSearch = false;
                        }


                        if (doSearch)
                        {
                            var playerCacheName = $"{senderName}＠{senderWorld}";
                            Logger.Debug($"Searching for {playerCacheName}");
                            
                            if (CachedResponses.TryGetValue(playerCacheName, out LodestoneCharacter lschar))
                            {
                                Logger.Debug($"Retrived cached data for {lschar.Name} {lschar.Avatar}");
                                avatarUrl = lschar.Avatar.ToString();
                            }
                            else
                            {
                                Logger.Debug($"Searching lodestone for {playerCacheName}");

                                var searchPage = await lodestoneClient.SearchCharacter(new CharacterSearchQuery
                                {
                                    CharacterName = senderName,
                                    World = senderWorld,
                                });

                                var matchingEntry = searchPage.Results.FirstOrDefault(result => result.Name == senderName);
                                if (matchingEntry == null)
                                {
                                    break;
                                }
                                
                                lschar = await matchingEntry.GetCharacter();

                                CachedResponses.TryAdd(playerCacheName, lschar);
                                Logger.Debug($"Adding cached data for {lschar.Name} {lschar.Avatar}");
                                avatarUrl = lschar.Avatar.ToString();
                            }

                            // avatarUrl = (await XivApiClient.GetCharacterSearch(senderName, senderWorld)).AvatarUrl;
                        }
                        
                        break;
                }                    
            }
            catch (Exception ex)
            {
                if (string.IsNullOrEmpty(senderName))
                {
                    Logger.Error($"senderName was null or empty. How did we get this far?");
                    senderName = "Bridge Error - sendername";
                }
                else
                {
                    Logger.Error(ex, $"Cannot fetch XIVAPI character search for {senderName} on {senderWorld}");
                }
                
                characterSearchFailed = true;
            }

            var displayName = senderName + (string.IsNullOrEmpty(senderWorld) || string.IsNullOrEmpty(senderName)
                ? ""
                : $"＠{senderWorld}");

            this.plugin.Config.PrefixConfigs.TryGetValue(chatType, out var prefix);

            bool senderInMessage = this.plugin.Config.SenderInMessage;

            var chatTypeText = this.plugin.Config.CustomSlugsConfigs.TryGetValue(chatType, out var x) ? x : chatType.GetSlug();
            

            foreach (var channelConfig in applicableChannels)
            {
                var socketChannel = this.socketClient.GetChannel(channelConfig.Key);

                if (socketChannel == null)
                {
                    Logger.Error("Could not find channel {0} for {1}", channelConfig.Key, chatType);

                    // try one more time just in case.
                    socketChannel = this.socketClient.GetChannel(channelConfig.Key);

                    if (socketChannel is null && !characterSearchFailed)
                    {
                        var channelConfigs = this.plugin.Config.ChannelConfigs;
                        channelConfigs.Remove(channelConfig.Key);
                        this.plugin.Config.ChannelConfigs = channelConfigs;
                        
                        Logger.Info("Removing channel {0}'s config because it no longer exists or cannot be accessed.", channelConfig.Key);
                        this.plugin.Config.Save();
                    }
                    
                    continue;
                }

                var messageContent = senderInMessage ? $"{displayName}: {message}" : message;

                messageContent = chatType != XivChatTypeExtensions.IpcChatType ? $"{prefix}**[{chatTypeText}]** {messageContent}" : $"{prefix} {messageContent}";


                // add handling for webhook vs embed here
                IGuildChannel guildChannel = (IGuildChannel)socketChannel;
                IGuildUser guildUser = await guildChannel.Guild.GetUserAsync(this.socketClient.CurrentUser.Id);
                bool hasManageWebHooks = guildUser.GetPermissions(guildChannel).Has(ChannelPermission.ManageWebhooks);

                if (socketChannel is SocketDMChannel)
                {
                    var DMChannel = await this.socketClient.GetDMChannelAsync(channelConfig.Key);
                    await SendPrettyEmbed((ISocketMessageChannel)DMChannel, messageContent, displayName, avatarUrl, EmbedColorFine);
                    Logger.Debug("SendChatEvent sent to DMs.");
                }
                else if (this.plugin.Config.ForceEmbedFallbackMode)
                {
                    await SendPrettyEmbed((ISocketMessageChannel)socketChannel, $"{messageContent}", $"{displayName}", avatarUrl, EmbedColorFine);
                }
                else if (!hasManageWebHooks)
                {
                    Logger.Debug("FALLBACKMODE - Unable to create WebHook - No Permission\n");
                    await SendPrettyEmbed((ISocketMessageChannel)socketChannel, $"FALLBACKMODE\n\nMissing ManageWebHooks permission.\n\n{messageContent}", $"{displayName}", avatarUrl, EmbedColorError);
                }
                else
                {
                    var webhookClient = await GetOrCreateWebhookClient(socketChannel);

                    if (duplicateFilter.CheckAlreadySent(socketChannel, slug: chatTypeText, displayName, chatText: message))
                    {
                        continue;
                    }

                    if (webhookClient != null)
                    {
                        if (this.plugin.Config.ForceDefaultNameAvatar)
                        {
                            displayName = this.socketClient.CurrentUser.Username.ToLower().Contains("discord") ? "Dalamud Chat Bridge" : this.socketClient.CurrentUser.Username;
                            avatarUrl = plugin.Config.DefaultAvatarURL;
                        }
                        
                        await webhookClient.SendMessageAsync(
                            messageContent, username: displayName, avatarUrl: avatarUrl,
                            allowedMentions: new AllowedMentions(AllowedMentionTypes.Roles | AllowedMentionTypes.Users | AllowedMentionTypes.None)
                        );
                        Logger.Debug("SendChatEvent sent to WebHook.");
                    }
                    else
                    {
                        Logger.Debug("FALLBACKMODE - Unable to create WebHook - Unknown failure\n");
                        await SendPrettyEmbed((ISocketMessageChannel)socketChannel, $"FALLBACKMODE\n\nUnable to create WebHook\n\n{messageContent}", $"{displayName}", avatarUrl, EmbedColorError);
                    }
                }
            }
        }

        public async Task SendContentFinderEvent(QueuedContentFinderEvent cfEvent)
        {
            var applicableChannels =
                this.plugin.Config.ChannelConfigs.Where(x => x.Value.IsContentFinder);

            if (!applicableChannels.Any())
                return;

            var iconFolder = cfEvent.ContentFinderCondition.Image / 1000 * 1000;

            var embedBuilder = new EmbedBuilder()
                .WithCurrentTimestamp()
                .WithColor(0x297c00)
                .WithTitle("Duty is ready: " + cfEvent.ContentFinderCondition.Name)
                .WithImageUrl("https://xivapi.com" + $"/i/{iconFolder}/{cfEvent.ContentFinderCondition.Image}.png")
                .WithFooter(footer =>
                {
                    footer
                        .WithText("For: " + Service.State.LocalPlayer?.Name)
                        .WithIconUrl(Constant.LogoLink);
                });

            foreach (var channelConfig in applicableChannels)
            {
                var socketChannel = this.socketClient.GetChannel(channelConfig.Key);

                if (socketChannel == null)
                {
                    Logger.Error("Could not find channel {0} for cfc", channelConfig.Key);
                    continue;
                }

                var prefix = this.plugin.Config.CFPrefixConfig ?? "";

                // add handling for webhook vs embed here
                IGuildChannel guildChannel = (IGuildChannel)socketChannel;
                IGuildUser guildUser = await guildChannel.Guild.GetUserAsync(this.socketClient.CurrentUser.Id);
                bool hasManageWebHooks = guildUser.GetPermissions(guildChannel).Has(ChannelPermission.ManageWebhooks);

                if (socketChannel is SocketDMChannel)
                {
                    embedBuilder.WithAuthor(new EmbedAuthorBuilder {Name = "Dalamud Chat Bridge", IconUrl = Constant.LogoLink});
                    var DMChannel = await this.socketClient.GetDMChannelAsync(channelConfig.Key);
                    await DMChannel.SendMessageAsync($"{prefix}", embed: embedBuilder.Build());
                }
                else if (!hasManageWebHooks)
                {
                    Logger.Debug("FALLBACKMODE - Unable to create WebHook - No Permission\n");
                    embedBuilder
                        .WithAuthor(new EmbedAuthorBuilder { Name = "Dalamud Chat Bridge", IconUrl = Constant.LogoLink })
                        .WithDescription("FALLBACKMODE - Unable to create WebHook - Missing ManageWebHook permission.");
                    await ((ISocketMessageChannel)socketChannel).SendMessageAsync($"{prefix}", embed: embedBuilder.Build());
                }
                else
                {
                    var webhookClient = await GetOrCreateWebhookClient(socketChannel);
                    

                    if (webhookClient != null)
                    {
                        await webhookClient.SendMessageAsync($"{prefix}", embeds: [embedBuilder.Build()],
                    username: "Dalamud Chat Bridge", avatarUrl: Constant.LogoLink);
                    }
                    else
                    {
                        Logger.Debug("FALLBACKMODE - Unable to create WebHook - Unknown error\n");
                        embedBuilder
                            .WithAuthor(new EmbedAuthorBuilder { Name = "Dalamud Chat Bridge", IconUrl = Constant.LogoLink })
                            .WithDescription("FALLBACKMODE - Unable to create WebHook - Unknown failure");
                        await ((ISocketMessageChannel)socketChannel).SendMessageAsync($"{prefix}", embed: embedBuilder.Build());
                    }
                }

                
            }
        }

        /// <summary>
        /// Get the webhook for the respective channel, or create one if it doesn't exist.
        /// </summary>
        /// <param name="channel">The channel to get the webhook for</param>
        /// <returns><see cref="IWebhook"/> for the respective channel.</returns>
        private async Task<DiscordWebhookClient> GetOrCreateWebhookClient(SocketChannel channel)
        {
            if (channel is SocketTextChannel textChannel)
            {
                if (!this.plugin.Config.ChannelConfigs.TryGetValue(channel.Id, out var channelConfig))
                    throw new ArgumentException("No configuration for channel.", nameof(channel));

                IWebhook hook;
                if (channelConfig.WebhookId != 0)
                    hook = await textChannel.GetWebhookAsync(channelConfig.WebhookId) ?? await textChannel.CreateWebhookAsync("FFXIV Bridge Worker");
                else
                {
                    try
                    {
                        hook = await textChannel.CreateWebhookAsync("FFXIV Bridge Worker");
                    }
                    catch (Discord.Net.HttpException e)
                    {
                        Logger.Error("Unable to get or create webhook", e.StackTrace);
                        return null;
                    }
                }


                this.plugin.Config.ChannelConfigs[channel.Id].WebhookId = hook.Id;
                this.plugin.Config.Save();

                Logger.Verbose("Webhook for {0} OK!! {1}", channel.Id, hook.Id);

                return new DiscordWebhookClient(hook);
            }

            throw new ArgumentNullException(nameof(channel));
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                Logger.Verbose("Discord DISPOSE!!");
                this.MessageQueue?.Stop();
                this.socketClient?.LogoutAsync().GetAwaiter().GetResult();
                this.socketClient?.Dispose();
            }
        }
    }
}
