using System;
using System.Threading.Tasks;
using Dalamud.DiscordBridge.API;
using Dalamud.DiscordBridge.Attributes;
using Dalamud.DiscordBridge.Model;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Lumina.Excel.GeneratedSheets;

namespace Dalamud.DiscordBridge
{
    public class DiscordBridgePlugin : IDalamudPlugin
    {
        public static DiscordBridgePlugin Plugin { get; private set; }
        private readonly PluginCommandManager<DiscordBridgePlugin> commandManager;
        private readonly PluginUI ui;

        public DiscordHandler Discord;
        public Configuration Config;
        public DiscordBridgeProvider DiscordBridgeProvider;

        static readonly IPluginLog Logger = Service.Logger;

        public IPlayerCharacter cachedLocalPlayer;
        private bool startedFromConstructor = false;


        public DiscordBridgePlugin(IDalamudPluginInterface pluginInterface, ICommandManager command)
        {
            Plugin = this;
            pluginInterface.Create<Service>();

            this.Config = (Configuration)pluginInterface.GetPluginConfig() ?? new Configuration();
            this.Config.Initialize(pluginInterface);

            pluginInterface.UiBuilder.OpenConfigUi += this.OpenConfigUi;

            // sanity check - ensure there are no invalid types leftover from past versions.
            foreach (DiscordChannelConfig config in this.Config.ChannelConfigs.Values)
            {
                for (int i = 0; i < config.ChatTypes.Count; i++)
                {
                    XivChatType xct = config.ChatTypes[i];
                    if ((int)xct > 127)
                    {
                        config.ChatTypes[i] = (XivChatType)((int)xct & 0x7F);
                        this.Config.Save();
                    }
                    try
                    {
                        xct.GetInfo();
                    }
                    catch (ArgumentException)
                    {
                        Logger.Error($"Removing invalid chat type before it could cause problems ({(int)xct}){xct}.");
                        config.ChatTypes.RemoveAt(i--);
                        this.Config.Save();
                    }
                }
            }

            
            this.DiscordBridgeProvider = new DiscordBridgeProvider(pluginInterface, new DiscordBridgeAPI(this));
            this.Discord = new DiscordHandler(this);
            // Task t = this.Discord.Start(); // bot won't start if we just have this
            
            
            Task.Run(async () => // makes the bot actually start
            {
                await this.Discord.Start();
                startedFromConstructor = true;
            });
            



            this.ui = new PluginUI(this);
            pluginInterface.UiBuilder.Draw += this.ui.Draw;

            Service.Chat.ChatMessage += ChatOnOnChatMessage;
            Service.State.CfPop += ClientStateOnCfPop;
            Service.State.Login += OnLoginEvent;
            Service.State.Logout += OnLogoutEvent;
            Service.Framework.Update += OnFrameworkUpdate;
            

            this.commandManager = new PluginCommandManager<DiscordBridgePlugin>(this, command);

            if (string.IsNullOrEmpty(this.Config.DiscordToken))
            {
                Service.Chat.PrintError("The Discord Bridge plugin was installed successfully." +
                                                              "Please use the \"/pdiscord\" command to set it up.");
            }
        }

        private async void OnFrameworkUpdate(IFramework framework)
        {
            // I don't like this, but I saw users state that localplayer was coming back null when it shouldn't.
            // So I'll just update the cache every tick even though that feels excessive.
            cachedLocalPlayer = await framework.RunOnFrameworkThread(() => Service.State.LocalPlayer);
        }

        private async void OnLoginEvent()
        {
            // Since I'm pulling this on Framework updates now, this might not be needed anymore.
            // But I'll keep it for now just in case.
            cachedLocalPlayer = await Service.Framework.RunOnFrameworkThread(() => Service.State.LocalPlayer);

            /* 
            // I'll disable the bot start/stop on login/logout logic. Seems busted.
            if (!startedFromConstructor)
            {
                await this.Discord.Start();
            }
            */
        }

        private void OnLogoutEvent()
        {
            cachedLocalPlayer = null;
            // this.Discord.Dispose();
            // this.Discord = new DiscordHandler(this);
            //startedFromConstructor = false;
        }

        private void ClientStateOnCfPop(ContentFinderCondition e)
        {
            this.Discord.MessageQueue.Enqueue(new QueuedContentFinderEvent
            {
                ContentFinderCondition = e
            });
        }

        private void ChatOnOnChatMessage(XivChatType type, int timestamp, ref SeString sender, ref SeString message, ref bool ishandled)
        {
            if (ishandled) return; // don't process a message that's been handled.

            if (type == XivChatType.RetainerSale)
            {
                this.Discord.MessageQueue.Enqueue(new QueuedRetainerItemSaleEvent 
                {
                    ChatType = type,
                    Message = message,
                    Sender = sender
                });
            }
            else
            {
                this.Discord.MessageQueue.Enqueue(new QueuedChatEvent
                {
                    ChatType = (XivChatType)((int)type & 0x7F), // strip off the sender mask subtype
                    Message = message,
                    Sender = sender
                });
            }
            
        }

        private void OpenConfigUi()
        {
            this.ui.Show();
        }

        
        [Command("/pdiscord")]
        [HelpMessage("Show settings for the discord bridge plugin.")]
        public void OpenSettingsCommand(string command, string args)
        {
            this.ui.Show();
        }

        
        [Command("/ddebug")]
        [HelpMessage("Send a debug message using a particular chat type")]
        [DoNotShowInHelp]
        public void DebugCommand(string command, string args)
        {
            string[] commandArgs = args.Split(' ');
            this.Discord.MessageQueue.Enqueue(new QueuedChatEvent
            {
                ChatType = XivChatTypeExtensions.GetBySlug(commandArgs?[0] ?? "e"),
                Message = new SeString(new Payload[]{new TextPayload("Test Message"), }),
                Sender = new SeString(new Payload[]{new TextPayload("Test Sender"), })
            });
        }
        
        [Command("/dsaledebug")]
        [HelpMessage("Send a fake item sale chat payload.")]
        [DoNotShowInHelp]
        public void SaleDebugCommand(string command, string args)
        {
            // make a sample sale message. This is using Titanium Ore for an item
            Item sampleitem = Service.Data.GetExcelSheet<Item>().GetRow(12537);
            SeString sameplesale = new(new Payload[] { new TextPayload("The "), new ItemPayload(sampleitem.RowId, true), new TextPayload(sampleitem.Name), new TextPayload(" you put up for sale in the Crystarium markets has sold for 777 gil (after fees).") });

            // Logger.Information($"Trying to make a fake sale: {sameplesale.TextValue}");

            this.Discord.MessageQueue.Enqueue(new QueuedRetainerItemSaleEvent
            {
                ChatType = XivChatType.RetainerSale,
                Message = sameplesale,
                Sender = new SeString(new Payload[] { new TextPayload("Test Sender"), })
            });

            Service.Chat.Print(new XivChatEntry
            {
                Message = sameplesale,
                Type = XivChatType.Echo
            });

        }

        
        [Command("/dprintlist")]
        [HelpMessage("Dump all plugin config to chat box.")]
        [DoNotShowInHelp]
        public void ListCommand(string command, string args)
        {
            foreach (var keyValuePair in XivChatTypeExtensions.TypeInfoDict)
            {
                 Service.Chat.Print($"({(int)keyValuePair.Key}) {keyValuePair.Key.GetSlug()} - {keyValuePair.Key.GetFancyName()}");
            }
        }
        

        #region IDisposable Support
        protected virtual void Dispose(bool disposing)
        {
            if (!disposing) return;

            this.DiscordBridgeProvider.Dispose();
            
            this.Discord.Dispose();

            this.commandManager.Dispose();

            Service.Interface.UiBuilder.OpenConfigUi -= this.OpenConfigUi;

            Service.Interface.SavePluginConfig(this.Config);

            Service.Interface.UiBuilder.Draw -= this.ui.Draw;

            Service.State.CfPop -= this.ClientStateOnCfPop;
            
            Service.State.Login -= OnLoginEvent;
            
            Service.State.Logout -= OnLogoutEvent;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        #endregion
    }
}
