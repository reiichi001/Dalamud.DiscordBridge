using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.Text;

namespace Dalamud.DiscordBridge
{
    public static class XivChatTypeExtensions
    {
        public class XivChatTypeInfo
        {
            public string Slug { get; set; }
            public string FancyName { get; set; }
        }

        public static XivChatType IpcChatType => (XivChatType)99; // using dummy code that isn't used by game

        public static readonly IReadOnlyDictionary<XivChatType, XivChatTypeInfo> TypeInfoDict =
            new Dictionary<XivChatType, XivChatTypeInfo>
            {
                {
                    XivChatType.None, new XivChatTypeInfo
                    {
                        Slug = "none",
                        FancyName = "No Chat Type"
                    }
                },
                {
                    XivChatType.Debug, new XivChatTypeInfo
                    {
                        Slug = "debug",
                        FancyName = "Debug Messages"
                    }
                },
                {
                    XivChatType.Urgent, new XivChatTypeInfo
                    {
                        Slug = "urgent",
                        FancyName = "Urgent Messages"
                    }
                },
                {
                    XivChatType.Notice, new XivChatTypeInfo
                    {
                        Slug = "notice",
                        FancyName = "Server notices"
                    }
                },
                {
                    XivChatType.Say, new XivChatTypeInfo
                    {
                        Slug = "say",
                        FancyName = "Say"
                    }
                },
                {
                    XivChatType.Shout, new XivChatTypeInfo
                    {
                        Slug = "shout",
                        FancyName = "Shout"
                    }
                },
                {
                    XivChatType.TellOutgoing, new XivChatTypeInfo // Tells need special handling
                    {
                        Slug = "tell",
                        FancyName = "Tell (Outgoing)"
                    }
                },
                {
                    XivChatType.TellIncoming, new XivChatTypeInfo // Tells need special handling, outgoing Tells are TellOutgoing
                    {
                        Slug = "tell",
                        FancyName = "Tell (Incoming)"
                    }
                },
                {
                    XivChatType.Party, new XivChatTypeInfo
                    {
                        Slug = "p",
                        FancyName = "Party Chat"
                    }
                },
                {
                    XivChatType.Alliance, new XivChatTypeInfo
                    {
                        Slug = "alliance",
                        FancyName = "Alliance"
                    }
                },
                {
                    XivChatType.Ls1, new XivChatTypeInfo
                    {
                        Slug = "ls1",
                        FancyName = "Linkshell 1"
                    }
                },
                {
                    XivChatType.Ls2, new XivChatTypeInfo
                    {
                        Slug = "ls2",
                        FancyName = "Linkshell 2"
                    }
                },
                {
                    XivChatType.Ls3, new XivChatTypeInfo
                    {
                        Slug = "ls3",
                        FancyName = "Linkshell 3"
                    }
                },
                {
                    XivChatType.Ls4, new XivChatTypeInfo
                    {
                        Slug = "ls4",
                        FancyName = "Linkshell 4"
                    }
                },
                {
                    XivChatType.Ls5, new XivChatTypeInfo
                    {
                        Slug = "ls5",
                        FancyName = "Linkshell 5"
                    }
                },
                {
                    XivChatType.Ls6, new XivChatTypeInfo
                    {
                        Slug = "ls6",
                        FancyName = "Linkshell 6"
                    }
                },
                {
                    XivChatType.Ls7, new XivChatTypeInfo
                    {
                        Slug = "ls7",
                        FancyName = "Linkshell 7"
                    }
                },
                {
                    XivChatType.Ls8, new XivChatTypeInfo
                    {
                        Slug = "ls8",
                        FancyName = "Linkshell 8"
                    }
                },
                {
                    XivChatType.FreeCompany, new XivChatTypeInfo
                    {
                        Slug = "fc",
                        FancyName = "Free Company"
                    }
                },
                {
                    XivChatType.NoviceNetwork, new XivChatTypeInfo
                    {
                        Slug = "nn",
                        FancyName = "Novice Network"
                    }
                },
                {
                    XivChatType.CustomEmote, new XivChatTypeInfo
                    {
                        Slug = "customemote",
                        FancyName = "Custom Emote"
                    }
                },
                {
                    XivChatType.StandardEmote, new XivChatTypeInfo
                    {
                        Slug = "standardemote",
                        FancyName = "Standard Emote"
                    }
                },
                {
                    XivChatType.Yell, new XivChatTypeInfo
                    {
                        Slug = "yell",
                        FancyName = "Yell"
                    }
                },
                {
                    XivChatType.CrossParty, new XivChatTypeInfo // This should logically be the same as party, but it's handled separately internally
                    {
                        Slug = "p",
                        FancyName = "Cross-World Party"
                    }
                },
                {
                    XivChatType.PvPTeam, new XivChatTypeInfo
                    {
                        Slug = "pvpt",
                        FancyName = "PvP Team"
                    }
                },
                {
                    XivChatType.CrossLinkShell1, new XivChatTypeInfo
                    {
                        Slug = "cwls1",
                        FancyName = "Cross-World Linkshell 1"
                    }
                },
                {
                    XivChatType.CrossLinkShell2, new XivChatTypeInfo
                    {
                        Slug = "cwls2",
                        FancyName = "Cross-World Linkshell 2"
                    }
                },
                {
                    XivChatType.CrossLinkShell3, new XivChatTypeInfo
                    {
                        Slug = "cwls3",
                        FancyName = "Cross-World Linkshell 3"
                    }
                },
                {
                    XivChatType.CrossLinkShell4, new XivChatTypeInfo
                    {
                        Slug = "cwls4",
                        FancyName = "Cross-World Linkshell 4"
                    }
                },
                {
                    XivChatType.CrossLinkShell5, new XivChatTypeInfo
                    {
                        Slug = "cwls5",
                        FancyName = "Cross-World Linkshell 5"
                    }
                },
                {
                    XivChatType.CrossLinkShell6, new XivChatTypeInfo
                    {
                        Slug = "cwls6",
                        FancyName = "Cross-World Linkshell 6"
                    }
                },
                {
                    XivChatType.CrossLinkShell7, new XivChatTypeInfo
                    {
                        Slug = "cwls7",
                        FancyName = "Cross-World Linkshell 7"
                    }
                },
                {
                    XivChatType.CrossLinkShell8, new XivChatTypeInfo
                    {
                        Slug = "cwls8",
                        FancyName = "Cross-World Linkshell 8"
                    }
                },
                {
                    XivChatType.Echo, new XivChatTypeInfo
                    {
                        Slug = "e",
                        FancyName = "Echo"
                    }
                },
                {
                    XivChatType.SystemMessage, new XivChatTypeInfo
                    {
                        Slug = "sysmsg",
                        FancyName = "SystemMessage"
                    }
                },
                {
                    XivChatType.SystemError, new XivChatTypeInfo
                    {
                        Slug = "syserror",
                        FancyName = "SystemError"
                    }
                },
                {
                    XivChatType.GatheringSystemMessage, new XivChatTypeInfo
                    {
                        Slug = "gathersysmsg",
                        FancyName = "GatheringSystemMessage"
                    }
                },
                {
                    XivChatType.ErrorMessage, new XivChatTypeInfo
                    {
                        Slug = "errmsg",
                        FancyName = "ErrorMessage"
                    }
                },
                // TODO: fellowships and shit, need dalamud update

                // Custom types not defined in Dalamud
                {
                    IpcChatType, new XivChatTypeInfo
                    {
                        Slug = "ipc",
                        FancyName = "IPC"
                    }
                },
                {
                    (XivChatType)55, new XivChatTypeInfo
                    {
                        Slug = "alarm",
                        FancyName = "Alarm"
                    }
                },
                /* This is no longer needed. It's part of SystemMessage.
                {
                    (XivChatType)57, new XivChatTypeInfo
                    {
                        Slug = "partyadd",
                        FancyName = "Added to Party"
                    }
                },
                */
                {
                    (XivChatType)61, new XivChatTypeInfo
                    {
                        Slug = "npctalk",
                        FancyName = "NPC Talk"
                    }
                },
                {
                    (XivChatType)66, new XivChatTypeInfo
                    {
                        Slug = "synthmsg",
                        FancyName = "Synthesis Message"
                    }
                },
                {
                    (XivChatType)68, new XivChatTypeInfo
                    {
                        Slug = "npcannounce",
                        FancyName = "NPC Announcement"
                    }
                },
                {
                    (XivChatType)69, new XivChatTypeInfo
                    {
                        Slug = "fcannounce",
                        FancyName = "Free Company Announcement"
                    }
                },
                {
                    (XivChatType)70, new XivChatTypeInfo
                    {
                        Slug = "fclogin",
                        FancyName = "Free Company Login/Logout"
                    }
                },
                {
                    XivChatType.RetainerSale, new XivChatTypeInfo
                    {
                        Slug = "retainersale",
                        FancyName = "Retainer Sale"
                    }
                },
                {
                    (XivChatType)73, new XivChatTypeInfo
                    {
                        Slug = "sign",
                        FancyName = "Sign"
                    }
                },
                {
                    (XivChatType)74, new XivChatTypeInfo
                    {
                        Slug = "random",
                        FancyName = "Random Number"
                    }
                },
                {
                    (XivChatType)75, new XivChatTypeInfo
                    {
                        Slug = "nnn",
                        FancyName = "Novice Network Notifications"
                    }
                },
                // Special handling for GM types
                {
                    (XivChatType)80, new XivChatTypeInfo
                    {
                        Slug = "gmtell",
                        FancyName = "GM Tell"
                    }
                },
                {
                    (XivChatType)81, new XivChatTypeInfo
                    {
                        Slug = "gmsay",
                        FancyName = "Say"
                    }
                },
                {
                    (XivChatType)82, new XivChatTypeInfo
                    {
                        Slug = "gmshout",
                        FancyName = "GM Shout"
                    }
                },
                {
                    (XivChatType)83, new XivChatTypeInfo
                    {
                        Slug = "gmyell",
                        FancyName = "GM Yell"
                    }
                },
                {
                    (XivChatType)84, new XivChatTypeInfo
                    {
                        Slug = "gmp",
                        FancyName = "GM Party Chat"
                    }
                },
                {
                    (XivChatType)85, new XivChatTypeInfo
                    {
                        Slug = "gmfc",
                        FancyName = "GM Free Company"
                    }
                },
                {
                    (XivChatType)86, new XivChatTypeInfo
                    {
                        Slug = "gmls1",
                        FancyName = "GM Linkshell 1"
                    }
                },
                {
                    (XivChatType)87, new XivChatTypeInfo
                    {
                        Slug = "gmls2",
                        FancyName = "GM Linkshell 2"
                    }
                },
                {
                    (XivChatType)88, new XivChatTypeInfo
                    {
                        Slug = "gmls3",
                        FancyName = "GM Linkshell 3"
                    }
                },
                {
                    (XivChatType)89, new XivChatTypeInfo
                    {
                        Slug = "gmls4",
                        FancyName = "GM Linkshell 4"
                    }
                },
                {
                    (XivChatType)90, new XivChatTypeInfo
                    {
                        Slug = "gmls5",
                        FancyName = "GM Linkshell 5"
                    }
                },
                {
                    (XivChatType)91, new XivChatTypeInfo
                    {
                        Slug = "gmls6",
                        FancyName = "GM Linkshell 6"
                    }
                },
                {
                    (XivChatType)92, new XivChatTypeInfo
                    {
                        Slug = "gmls7",
                        FancyName = "GM Linkshell 7"
                    }
                },
                {
                    (XivChatType)93, new XivChatTypeInfo
                    {
                        Slug = "gmls8",
                        FancyName = "GM Linkshell 8"
                    }
                },
                {
                    (XivChatType)94, new XivChatTypeInfo
                    {
                        Slug = "gmnn",
                        FancyName = "GM Novice Network"
                    }
                }
            };

        public static XivChatTypeInfo GetInfo(this XivChatType type)
        {
            // if (type == XivChatType.TellOutgoing)
            //     type = XivChatType.TellIncoming;

            if (TypeInfoDict.TryGetValue((XivChatType)((int)type & 0x7F), out var info))
                return info;

            throw new ArgumentException($"No info mapping for chat type. {nameof(type)} ({(int)type}){type}");
        }

        public static string GetSlug(this XivChatType type)
        {
            return type.GetInfo().Slug;
        }

        public static string GetFancyName(this XivChatType type)
        {
            return type.GetInfo().FancyName;
        }

        public static XivChatType GetBySlug(string slug) => TypeInfoDict.First(x => x.Value.Slug == slug).Key;
        public static XivChatType GetByFancyName(string fancyname) => TypeInfoDict.First(x => x.Value.FancyName == fancyname).Key;

    }
}
