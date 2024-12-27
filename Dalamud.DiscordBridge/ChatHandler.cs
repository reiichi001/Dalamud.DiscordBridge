using System;
using Dalamud.DiscordBridge.Helper;
using Dalamud.Game;
using Dalamud.Plugin.Services;

namespace Dalamud.DiscordBridge
{
public class ChatHandler
{
   static readonly IPluginLog Logger = Service.Logger;
   private readonly MessageRelay messageRelay;

   public ChatHandler(ISigScanner scanner)
   {
      messageRelay = new MessageRelay(scanner);
   }

   public void HandleMessage(string channel, string message)
   {
      Logger.Error($"[ChatHandler] Channel: {channel}, Message: {message}");
      try
      {
         string formattedMessage = $"{channel} {message}";
         Service.Framework.RunOnFrameworkThread(
            () => messageRelay.SendMessage(formattedMessage));
      }
      catch (Exception ex)
      {
         Logger.Error($"Failed to send message: {ex.Message}");
      }
   }
}
}