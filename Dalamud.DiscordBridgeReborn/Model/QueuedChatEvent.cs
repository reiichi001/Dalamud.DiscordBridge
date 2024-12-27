using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;

namespace Dalamud.DiscordBridge.Model
{
    public class QueuedChatEvent : QueuedXivEvent
    {
        public SeString Message { get; set; }
        public SeString Sender { get; set; }
        public XivChatType ChatType { get; set; }
        public string AvatarUrl { get; set; }
    }
}
