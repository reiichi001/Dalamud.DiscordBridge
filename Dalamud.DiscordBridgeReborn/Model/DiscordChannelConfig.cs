using System.Collections.Generic;
using Dalamud.Game.Text;

namespace Dalamud.DiscordBridge.Model
{
    public class DiscordChannelConfig
    {
        public List<XivChatType> ChatTypes { get; set; } = new List<XivChatType>();
        public ulong WebhookId { get; set; }
        public bool IsBidirectional { get; set; }
        public bool IsContentFinder { get; set; }

        public void SetUnique(XivChatType type)
        {
            SetUnique(new []{type});
        }

        public void SetUnique(IEnumerable<XivChatType> types)
        {
            foreach (var xivChatType in types)
            {
                if (!ChatTypes.Contains(xivChatType))
                    ChatTypes.Add(xivChatType);
            }
        }

        public void UnsetUnique(XivChatType type)
        {
            UnsetUnique(new []{type});
        }

        public void UnsetUnique(IEnumerable<XivChatType> types)
        {
            foreach (var xivChatType in types)
            {
                ChatTypes.Remove(xivChatType);
            }
        }
    }
}
