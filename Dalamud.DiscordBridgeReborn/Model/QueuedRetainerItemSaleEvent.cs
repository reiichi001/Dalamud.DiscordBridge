using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Dalamud.DiscordBridge.XivApi;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;

namespace Dalamud.DiscordBridge.Model
{
    public class QueuedRetainerItemSaleEvent : QueuedXivEvent
    {
        public SeString Message { get; set; }
        public SeString Sender { get; set; }
        public XivChatType ChatType { get; set; }
    }
}
