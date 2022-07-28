using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;
using Dalamud.DiscordBridge.Model;
using Dalamud.DiscordBridge.XivApi;
using Dalamud.Game.Text;
using Dalamud.Logging;
using Dalamud.Utility;
using Discord;
using Discord.Net.Providers.WS4Net;
using Discord.Webhook;
using Discord.WebSocket;
using Lumina.Text;
using NetStone;
using NetStone.Model.Parseables.Character;
using NetStone.Model.Parseables.Search.Character;
using NetStone.Search.Character;

namespace Dalamud.DiscordBridge
{
    public class DiscordHandler : IDisposable
    {
        private readonly DiscordSocketClient socketClient;
        private readonly SpecialCharsHandler specialChars;

        public bool IsConnected => this.socketClient.ConnectionState == ConnectionState.Connected;
        public ulong UserId => this.socketClient.CurrentUser.Id;

        private static readonly ConcurrentDictionary<string, LodestoneCharacter> CachedResponses = new ConcurrentDictionary<string, LodestoneCharacter>();

        /// <summary>
        /// Defines if the bot has connected and verified that it has the correct permissions
        /// </summary>
        public DiscordState State { get; private set; } = DiscordState.None;

        private readonly Plugin plugin;

        /// <summary>
        /// Chat types that are set when used the "all" setting.
        /// </summary>
        private static readonly XivChatType[] DefaultChatTypes = new[]
        {
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
        };

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

        public DiscordHandler(Plugin plugin)
        {
            this.plugin = plugin;

            this.specialChars = new SpecialCharsHandler();

            this.MessageQueue = new DiscordMessageQueue(this.plugin);

            this.socketClient = new DiscordSocketClient(new DiscordSocketConfig
            {
                WebSocketProvider = WS4NetProvider.Instance,
                MessageCacheSize = 20, // hold onto the last 20 messages per channel in cache for duplicate checks
            });
            this.socketClient.Ready += SocketClientOnReady;
            this.socketClient.MessageReceived += SocketClientOnMessageReceived;
        }

        public async Task Start()
        {

            if (string.IsNullOrEmpty(this.plugin.Config.DiscordToken))
            {
                this.State = DiscordState.TokenInvalid;

                PluginLog.Error("Token empty, cannot start bot.");
                return;
            }

            try
            {
                await this.socketClient.LoginAsync(TokenType.Bot, this.plugin.Config.DiscordToken);
                await this.socketClient.StartAsync();
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "Token invalid, cannot start bot.");
            }

            this.MessageQueue.Start();

            lodestoneClient = await LodestoneClient.GetClientAsync();

            PluginLog.Verbose("DiscordHandler START!!");
        }

        private async Task SocketClientOnReady()
        {
            this.State = DiscordState.Ready;
            await this.specialChars.TryFindEmote(this.socketClient);

            PluginLog.Verbose("DiscordHandler READY!!");
        }

        private async Task SocketClientOnMessageReceived(SocketMessage message)
        {
	        if (message.Channel is SocketChannel socketChannel)
	        {
		        await Dedupe(socketChannel);
	        }

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
                PluginLog.Verbose(s);
            }
            */

            PluginLog.Verbose("Received command: {0}", args[0]);

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
                        PluginLog.Verbose("Could not find kinds");
                        await SendGenericEmbed(message.Channel,
                            $"One or more of the chat kinds you specified could not be found.\nCheck the ``{this.plugin.Config.DiscordBotPrefix}help`` command for more information.",
                            "Error", EmbedColorError);

                        return;
                    }

                    if (!this.plugin.Config.ChannelConfigs.TryGetValue(message.Channel.Id, out var config))
                        config = new DiscordChannelConfig();

                    foreach (var selectedKind in kinds)
                    {
                        PluginLog.Verbose(selectedKind);

                        if (selectedKind == "any")
                        {
                            config.SetUnique(DefaultChatTypes);
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
                        else
                        {
                            var chatType = XivChatTypeExtensions.GetBySlug(selectedKind);
                            config.UnsetUnique(chatType);
                        }
                    }

                    this.plugin.Config.ChannelConfigs[message.Channel.Id] = config;
                    this.plugin.Config.Save();

                    if (config.ChatTypes.Count() == 0)
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
                        var type = XivChatTypeExtensions.GetBySlug(selectedKind);
                        this.plugin.Config.PrefixConfigs[type] = args[2];
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
                        this.plugin.Config.PrefixConfigs.Remove(type);
                    }

                    this.plugin.Config.Save();

                    if (this.plugin.Config.PrefixConfigs.Count() == 0 )
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

                    // PluginLog.Information($"arg1: {args[1]}; arg2: {chatChannelOverride}");

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
                            var type = XivChatTypeExtensions.GetBySlug(selectedKind);
                            this.plugin.Config.CustomSlugsConfigs[type] = chatChannelOverride;
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

                    PluginLog.Information($"Unsetting custom type name for arg1: {args[1]}");

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
                    if (args.Length == 1)
                    {
                        await SendGenericEmbed(message.Channel,
                            $"You need to specify a number in milliseconds to use.\nCheck the ``{this.plugin.Config.DiscordBotPrefix}help`` command for more information.",
                            "Error", EmbedColorError);

                        return;
                    }

                    var kinds = args[1].Split(',').Select(x => x.ToLower());

                    // Make sure that it's a number (or assume it is)
                    int newDelay = int.Parse(args[1]);

                    this.plugin.Config.DuplicateCheckMS = newDelay;
                    this.plugin.Config.Save();

                    await SendGenericEmbed(message.Channel,
                        $"OK! Any messages with the same content within the last **{newDelay}** milliseconds will be skipped, preventing duplicate posts.",
                        "Duplicate Message Check", EmbedColorFine);

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

                if (args[0] == this.plugin.Config.DiscordBotPrefix + "help")
                {
                    PluginLog.Verbose("Help time");

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
                        .AddField($"{this.plugin.Config.DiscordBotPrefix}toggledf", "Enable or disable sending duty finder updates to this channel.")
                        .AddField($"{this.plugin.Config.DiscordBotPrefix}setduplicatems", "Set time in milliseconds that the bot will check to see if any past messages were the same. Default is 0 ms.")
                        .AddField($"{this.plugin.Config.DiscordBotPrefix}setprefix", "Set a prefix for chat kinds. "
                            + $"This can be an emoji or a string that will be prepended to every chat message that will arrive with this chat kind. "
                            + $"You can also set it to `none` if you want to remove it.\n" 
                            + $"Format: ``{this.plugin.Config.DiscordBotPrefix}setchannel <kind1,kind2,...> <prefix>``")
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
                        .AddField("Need more help?",
                            $"You can [read the full step-by-step guide]({Constant.HelpLink}) or [join our Discord server]({Constant.DiscordJoinLink}) to ask for help.")
                        .WithFooter(footer =>
                        {
                            footer
                                .WithText("Dalamud Discord Bridge")
                                .WithIconUrl(Constant.LogoLink);
                        })
                        .WithThumbnailUrl(Constant.LogoLink);
                    var embed = builder.Build();

                    var m = await message.Channel.SendMessageAsync(
                            null,
                            embed: embed)
                        .ConfigureAwait(false);
                    ;
                    PluginLog.Verbose(m.Id.ToString());

                    return;
                }
            }
            catch (Exception ex)
            {
                PluginLog.Error(ex, "Could not handle incoming Discord message.");
            }
        }

        private async Task SendGenericEmbed(ISocketMessageChannel channel, string message, string title, uint color)
        {
            var builder = new EmbedBuilder()
                .WithTitle(title)
                .WithDescription(message)
                .WithColor(new Color(color))
                .WithFooter(footer => {
                    footer
                        .WithText("Dalamud Discord Bridge")
                        .WithIconUrl(Constant.LogoLink);
                })
                .WithThumbnailUrl(Constant.LogoLink);
                
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
            PluginLog.Verbose("EnsureOwner: " + user.Username + "#" + user.Discriminator);
            if (user.Username + "#" + user.Discriminator == this.plugin.Config.DiscordOwnerName) 
                return true;

            if (ulong.TryParse(this.plugin.Config.DiscordOwnerName, out ulong parsed))
                if (user.Id == parsed)
                    return true;

            if (errorMessageChannel == null) 
                return false;

            await SendGenericEmbed(errorMessageChannel, "You are not allowed to run commands for this bot.\n\nIf this is your bot, please use the \"/pdiscord\" command in-game to enter your username.", "Error", EmbedColorError);

            return false;
        }

        public async Task SendItemSaleEvent(SeString name, string iconurl, uint itemId, string message, XivChatType chatType)
        {
            var applicableChannels =
                this.plugin.Config.ChannelConfigs.Where(x => x.Value.ChatTypes.Contains(chatType));

            if (!applicableChannels.Any())
                return;

            message = this.specialChars.TransformToUnicode(message);

            
            PluginLog.Information($"Retainer sold itemID: {itemId} with iconurl: {iconurl}");

            this.plugin.Config.PrefixConfigs.TryGetValue(chatType, out var prefix);

            foreach (var channelConfig in applicableChannels)
            {
                var socketChannel = this.socketClient.GetChannel(channelConfig.Key);

                if (socketChannel == null)
                {
                    PluginLog.Error("Could not find channel {0} for {1}", channelConfig.Key, chatType);
                    continue;
                }
                
                var webhookClient = await GetOrCreateWebhookClient(socketChannel);
                await webhookClient.SendMessageAsync($"{prefix} {message}",
                    username: $"Retainer sold {name}", avatarUrl: iconurl);
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
                avatarUrl = Constant.LogoLink;
            }

            var applicableChannels =
                this.plugin.Config.ChannelConfigs.Where(x => x.Value.ChatTypes.Contains(chatType));

            if (!applicableChannels.Any())
                return;

            message = this.specialChars.TransformToUnicode(message);

            
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
                            PluginLog.Verbose($"Sender Name was null or empty: {senderName}");
                            doSearch = false;
                        }
                        if (string.IsNullOrEmpty(senderWorld))
                        {
                            PluginLog.Verbose($"Sender World was null or empty: {senderWorld}");
                            doSearch = false;
                        }
                        if (senderName == "Sonar" || !senderName.Contains(" "))
                        {
                            PluginLog.Verbose($"Sender Name was a plugin or invalid: {senderName}");
                            doSearch = false;
                        }
                        if (doSearch)
                        {
                            var playerCacheName = $"{senderName}@{senderWorld}";
                            PluginLog.Verbose($"Searching for {playerCacheName}");
                            
                            if (CachedResponses.TryGetValue(playerCacheName, out LodestoneCharacter lschar))
                            {
                                PluginLog.Verbose($"Retrived cached data for {lschar.Name} {lschar.Avatar.ToString()}");
                                avatarUrl = lschar.Avatar.ToString();
                            }
                            else
                            {
                                PluginLog.Verbose($"Searching lodestone for {playerCacheName}");
                                lschar = await lodestoneClient.SearchCharacter(new CharacterSearchQuery()
                                {
                                    CharacterName = senderName,
                                    World = senderWorld,
                                }).Result.Results.FirstOrDefault(result => result.Name == senderName).GetCharacter();

                                CachedResponses.TryAdd(playerCacheName, lschar);
                                PluginLog.Verbose($"Adding cached data for {lschar.Name} {lschar.Avatar}");
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
                    PluginLog.Error($"senderName was null or empty. How did we get this far?");
                    senderName = "Bridge Error - sendername";
                }
                else
                {
                    PluginLog.Error(ex, $"Cannot fetch XIVAPI character search for {senderName} on {senderWorld}");
                }
                    
            }

            var displayName = senderName + (string.IsNullOrEmpty(senderWorld) || string.IsNullOrEmpty(senderName)
                ? ""
                : $"@{senderWorld}");

            this.plugin.Config.PrefixConfigs.TryGetValue(chatType, out var prefix);

            var chatTypeText = this.plugin.Config.CustomSlugsConfigs.TryGetValue(chatType, out var x) ? x : chatType.GetSlug();
            

            foreach (var channelConfig in applicableChannels)
            {
                var socketChannel = this.socketClient.GetChannel(channelConfig.Key);

                if (socketChannel == null)
                {
                    PluginLog.Error("Could not find channel {0} for {1}", channelConfig.Key, chatType);

                    var channelConfigs = this.plugin.Config.ChannelConfigs;
                    channelConfigs.Remove(channelConfig.Key);
                    this.plugin.Config.ChannelConfigs = channelConfigs;


                    PluginLog.Log("Removing channel {0}'s config because it no longer exists or cannot be accessed.", channelConfig.Key);
                    this.plugin.Config.Save();
                    
                    continue;
                }

                var webhookClient = await GetOrCreateWebhookClient(socketChannel);
                var messageContent = chatType != XivChatTypeExtensions.IpcChatType ? $"{prefix}**[{chatTypeText}]** {message}" : $"{prefix} {message}";


                // check for duplicates before sending
                // straight up copied from the previous bot, but I have no way to test this myself.
                var cachedMessages = (socketChannel as SocketTextChannel).GetCachedMessages();
                var recentMsg = cachedMessages.FirstOrDefault(msg => msg.Content == messageContent);

                
                //if (this.plugin.Config.DuplicateCheckMS > 0 && recentMsg != null)
                if (recentMsg != null)
                {
                    long msgDiff = GetElapsedMs(recentMsg);
                    
                    //if (msgDiff < this.plugin.Config.DuplicateCheckMS)
                    if (msgDiff < 0)
                    {
                        PluginLog.Log($"[IN TESTING]\n DIFF:{msgDiff}ms Skipping duplicate message: {messageContent}");
                        return;
                    }
                        
                }

                await webhookClient.SendMessageAsync(
                    messageContent,username: displayName, avatarUrl: avatarUrl, 
                    allowedMentions: new AllowedMentions(AllowedMentionTypes.Roles | AllowedMentionTypes.Users | AllowedMentionTypes.None)
                );


                // await Dedupe(socketChannel);

                // the message to a list of recently sent messages. 
                // If someone else sent the same thing at the same time
                // both will need to be checked and the earlier timestamp kept
                // while the newer one is removed
                // refer to https://discord.com/channels/581875019861328007/684745859497590843/791207648619266060
            }
        }

        private async Task Dedupe(SocketChannel socketChannel)
        {
	        if (socketChannel is not SocketTextChannel socketTextChannel)
	        {
		        return;
	        }

	        var cachedMessages = socketTextChannel.GetCachedMessages();

	        var recentMessages = cachedMessages.Where(m => GetElapsedMs(m) < 10000);
	        var socketMessages = recentMessages as SocketMessage[] ?? recentMessages.ToArray();
	        var content = socketMessages.Select(m => m.Content);

	        PluginLog.LogInformation($"Total: {cachedMessages.Count()}");
	        PluginLog.LogInformation($"Recent: {socketMessages.Count()}");
	        PluginLog.LogInformation($"Content: {string.Join(", ", content)}");

	        for (var i = 0; i < socketMessages.Length; i++)
	        {
		        var recent = socketMessages[i];

		        for (var j = 0; j < socketMessages.Length; j++)
		        {
			        if (i != j)
			        {
				        var other = socketMessages[j];

				        if (IsDuplicate(recent, other))
				        {
					        await DeleteMostRecent(recent, other);
				        }
			        }
		        }
	        }
        }

        private static long GetElapsedMs(SocketMessage message)
        {
	        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - message.Timestamp.ToUnixTimeMilliseconds();
        }

        private async Task DeleteMostRecent(SocketMessage recent, SocketMessage other)
        {
	        if (recent.Timestamp > other.Timestamp)
	        {
		        await TryDeleteAsync(recent);
	        }
	        else
	        {
		        await TryDeleteAsync(other);
	        }
        }

        private static async Task TryDeleteAsync(SocketMessage message)
        {
	        PluginLog.LogInformation($"[IN TESTING]\n TRY DELETE: ({message.Author.Username}) {message.Content}");

	        // if (message.Author.IsBot)
	        // {
		       //  PluginLog.LogInformation($"AUTHOR IS BOT");
		       //  return;
	        // }

	        try
	        {
		        await message.DeleteAsync();
	        }
	        catch (Discord.Net.HttpException)
	        {
		        PluginLog.LogInformation($"Message could not be deleted");
	        }
        }

        private bool IsDuplicate(SocketMessage recent, SocketMessage other)
        {
	        string left = recent.Content;
	        string right = other.Content;

	        bool notEmptyString = !(recent.Content.IsNullOrEmpty() && other.Content.IsNullOrEmpty());

	        bool bothWebhook = recent.Author.IsWebhook && other.Author.IsWebhook;

	        bool sameUser = recent.Author.Username == other.Author.Username;
	        //bool sameUser = true;

	        bool differentId = recent.Id != other.Id;

	        return notEmptyString && bothWebhook && sameUser && differentId && (left.Contains(right) || right.Contains(left));
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
                        .WithText("For: " + this.plugin.State.LocalPlayer?.Name)
                        .WithIconUrl(Constant.LogoLink);
                });

            foreach (var channelConfig in applicableChannels)
            {
                var socketChannel = this.socketClient.GetChannel(channelConfig.Key);

                if (socketChannel == null)
                {
                    PluginLog.Error("Could not find channel {0} for cfc", channelConfig.Key);
                    continue;
                }

                var prefix = this.plugin.Config.CFPrefixConfig ?? "";

                var webhookClient = await GetOrCreateWebhookClient(socketChannel);
                await webhookClient.SendMessageAsync($"{prefix}", embeds: new[] {embedBuilder.Build()},
                    username: "Dalamud Discord Bridge", avatarUrl: Constant.LogoLink);
            }
        }

        /// <summary>
        /// Get the webhook for the respective channel, or create one if it doesn't exist.
        /// </summary>
        /// <param name="channel">The channel to get the webhook for</param>
        /// <returns><see cref="IWebhook"/> for the respective channel.</returns>
        private async Task<DiscordWebhookClient> GetOrCreateWebhookClient(SocketChannel channel)
        {
            if (!(channel is SocketTextChannel textChannel))
                throw new ArgumentNullException(nameof(textChannel));

            if (!this.plugin.Config.ChannelConfigs.TryGetValue(channel.Id, out var channelConfig))
                throw new ArgumentException("No configuration for channel.", nameof(channel));

            IWebhook hook;
            if (channelConfig.WebhookId != 0)
                hook = await textChannel.GetWebhookAsync(channelConfig.WebhookId) ?? await textChannel.CreateWebhookAsync("FFXIV Bridge Worker");
            else
                hook = await textChannel.CreateWebhookAsync("FFXIV Bridge Worker");
            
            this.plugin.Config.ChannelConfigs[channel.Id].WebhookId = hook.Id;
            this.plugin.Config.Save();

            PluginLog.Verbose("Webhook for {0} OK!! {1}", channel.Id, hook.Id);

            return new DiscordWebhookClient(hook);
        }

        public void Dispose()
        {
            PluginLog.Verbose("Discord DISPOSE!!");
            this.MessageQueue?.Stop();
            this.socketClient?.LogoutAsync().GetAwaiter().GetResult();
            this.socketClient?.Dispose();
        }
    }
}
