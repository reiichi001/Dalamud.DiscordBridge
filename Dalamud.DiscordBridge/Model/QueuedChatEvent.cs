using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;

namespace Dalamud.DiscordBridge.Model
{
    public class QueuedChatEvent : QueuedXivEvent
    {
        public required SeString Message { get; set; }
        public required SeString Sender { get; set; }
        public required XivChatType ChatType { get; set; }
        public required string AvatarUrl { get; set; }
    }
}
