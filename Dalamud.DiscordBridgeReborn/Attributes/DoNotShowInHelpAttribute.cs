using System;

namespace Dalamud.DiscordBridge.Attributes
{
    [AttributeUsage(AttributeTargets.Method)]
    public class DoNotShowInHelpAttribute : Attribute
    {
    }
}