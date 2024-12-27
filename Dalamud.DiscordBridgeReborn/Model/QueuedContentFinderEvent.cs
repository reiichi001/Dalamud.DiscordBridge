using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Lumina.Excel.Sheets;

namespace Dalamud.DiscordBridge.Model
{
    public class QueuedContentFinderEvent : QueuedXivEvent
    {
        public ContentFinderCondition ContentFinderCondition { get; set; }
    }
}
