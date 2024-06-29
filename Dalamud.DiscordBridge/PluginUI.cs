using System.Diagnostics;
using System.Numerics;
using Dalamud.Plugin.Services;
using ImGuiNET;

namespace Dalamud.DiscordBridge
{
    public class PluginUI(DiscordBridgePlugin plugin)
    {
        static readonly IPluginLog Logger = Service.Logger;
        private readonly DiscordBridgePlugin Plugin = plugin;
        private bool isVisible;

        private string token;
        private string username;

        private static Vector4 errorColor = new(1f, 0f, 0f, 1f);
        private static Vector4 fineColor = new(0.337f, 1f, 0.019f, 1f);

        public void Show()
        {
            this.token = this.Plugin.Config.DiscordToken;
            this.username = this.Plugin.Config.DiscordOwnerName;

            this.isVisible = true;
        }

        public void Draw()
        {
            if (!this.isVisible)
                return;

            ImGui.Begin("Discord Bridge Setup", ref this.isVisible);

            ImGui.Text("In this window, you can set up the XIVLauncher Discord Bridge.\n\n" +
                       "To begin, enter your discord bot token and username or user ID number below, then click \"Save\".\n" +
                       "As soon as the red text says \"connected\", click the \"Join my server\" button and add the bot to one of your personal servers.\n" +
                       $"You can then use the {this.Plugin.Config.DiscordBotPrefix}help command in your discord server to specify channels.");

            ImGui.Dummy(new Vector2(10, 10));

            ImGui.InputText("Enter your bot token", ref this.token, 100);
            ImGui.InputText("Enter your Username(e.g. user#0000)", ref this.username, 50);

            ImGui.Dummy(new Vector2(10, 10));

            ImGui.Text("Status: ");
            ImGui.SameLine();

            var message = this.Plugin.Discord.State switch
            {
                DiscordState.None => "Not started",
                DiscordState.Ready => "Connected!",
                DiscordState.TokenInvalid => "Token empty or invalid.",
                _ => "Unknown"
            };

            ImGui.TextColored(this.Plugin.Discord.State == DiscordState.Ready ? fineColor : errorColor, message);
            if (this.Plugin.Discord.State == DiscordState.Ready && ImGui.Button("Join my server"))
            {
                Process.Start(
                    new ProcessStartInfo { 
                        FileName = $"https://discordapp.com/oauth2/authorize?client_id={this.Plugin.Discord.UserId}&scope=bot&permissions=2684742720", UseShellExecute = true 
                    } 
                );
            }

            ImGui.Dummy(new Vector2(10, 10));

            if (ImGui.Button("How does this work?"))
            {
                Process.Start(
                    new ProcessStartInfo
                    {
                        FileName = Constant.HelpLink,
                        UseShellExecute = true
                    } 
                );
            }

            ImGui.SameLine();

            if (ImGui.Button("Save"))
            {
                Logger.Verbose("Reloading Discord...");

                this.Plugin.Config.DiscordToken = this.token;
                this.Plugin.Config.DiscordOwnerName = this.username;
                this.Plugin.Config.Save();

                this.Plugin.Discord.Dispose();
                this.Plugin.Discord = new DiscordHandler(this.Plugin);
                _ = this.Plugin.Discord.Start();
            }
        }
    }
}
