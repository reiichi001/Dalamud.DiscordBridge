using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord.WebSocket;

namespace Dalamud.DiscordBridge
{
    class SpecialCharsHandler
    {
        private Dictionary<char, string> charsDict;

        private readonly object dictLock = new();

        private string hqEmote;
        private string atlEmote;
        private string atrEmote;


        public SpecialCharsHandler()
        {
            this.charsDict = BuildDict();
        }

        public string TransformToUnicode(string input)
        {
            var returnString = new StringBuilder(input);

            lock (this.dictLock)
            {
                foreach (var thisChar in input.Where(thisChar => this.charsDict.ContainsKey(thisChar)))
                {
                    returnString.Replace(thisChar.ToString(), this.charsDict[thisChar]);
                }
            }

            return returnString.ToString();
        }

        public void TryFindEmote(DiscordSocketClient socketClient)
        {
            foreach (var guild in socketClient.Guilds)
            {
                foreach (var guildEmote in guild.Emotes)
                {
                    switch (guildEmote.Name)
                    {
                        case "xivAtl": this.atlEmote = $"<:xivAtl:{guildEmote.Id}>";
                            return;
                        case "xivAtr": this.atrEmote = $"<:xivAtr:{guildEmote.Id}>";
                            return;
                        case "xivHq": this.hqEmote = $"<:xivHq:{guildEmote.Id}>";
                            return;
                    }
                }
            }

            lock (this.dictLock)
            {
                this.charsDict = BuildDict();
            }
        }

        private Dictionary<char, string> BuildDict()
        {
            var atlSymbol = string.IsNullOrEmpty(this.atlEmote) ? "🟩" : this.atlEmote;
            var atrSymbol = string.IsNullOrEmpty(this.atrEmote) ? "🟥" : this.atrEmote;
            var hqSymbol = string.IsNullOrEmpty(this.hqEmote) ? "❇" : this.hqEmote;

            var mappedChars = new Dictionary<char, string>
            {
                {'\uE020', "あ"}, // hiragana
                {'\uE021', "ア"}, // katakana
                {'\uE022', "🇪\u200B"}, // english
                {'\uE023', "_ｧ"}, // half-width katakana
                {'\uE024', "_ᴀ"}, // half-width english
                {'\uE025', "가"}, // ka / korean
                {'\uE026', "中"}, // chu / china probably
                {'\uE027', "英"}, // english?
                {'\uE028', "ₘ"}, // tiny m
                {'\uE029', "分"}, // cut/divide?

                {'\uE031', "⏰"}, // clock
                {'\uE032', "⇟"}, // some kind of down arrow sundered armor thing
                {'\uE033', "🟉"}, // item level icon
                {'\uE034', "🌱"}, // new adventurer / sprout icon
                {'\uE035', "🠗"}, // down arrow
                {'\uE039', "💲"}, // first strange s
                {'\uE03a', "🇪🇺\u200B"}, // eureka light level
                {'\uE03b', "➕"}, // thicc + (glamored icon)
                {'\uE03c', hqSymbol}, //hq marker
                {'\uE03d', "📦"}, // collectable box icon
                {'\uE03e', "⚂"}, // die with 3 facing
                {'\uE03f', "·"}, // bold period or dot or something

                {'\uE040', atlSymbol}, //autotranslate Left
                {'\uE041', atrSymbol}, //autotranslate Right
                {'\uE042', "⬡"}, // hexagon
                {'\uE043', "🚫"}, // no sign
                {'\uE044', "🔗"}, // link
                {'\uE048', "♢+"}, // crystal +
                {'\uE049', "ʛ"}, // gil icon
                {'\uE04a', "⚪"}, // circle
                {'\uE04b', "⬜"}, // square
                {'\uE04c', "❌"}, // cross
                {'\uE04d', "△"}, // triangle
                {'\uE04e', "➕"}, // plus

                {'\uE050', "🖰"}, //mouse icon -E050
                {'\uE051', "🖰L"}, //mouse left click -E051
                {'\uE052', "🖰R"}, //mouse right click -E052
                {'\uE053', "🖰LR"}, //mouse both click -E053
                {'\uE054', "🖱"}, //mouse scroll -E054
                {'\uE055', "🖰1"}, //mouse 1 -E055
                {'\uE056', "🖰2"}, //mouse 2 -E056
                {'\uE057', "🖰3"}, //mouse 3 -E057
                {'\uE058', "🖰4"}, //mouse 4 -E058
                {'\uE059', "🖰5"}, //mouse 5 -E059
                {'\uE05a', "…"}, //... stylized -E05A
                {'\uE05b', "⌧"}, // x marker
                {'\uE05c', "⧇"}, // circle marker
                {'\uE05d', "🌐"}, // server cluster icon
                {'\uE05e', "🎯"}, // target with bullseye hit
                {'\uE05f', "🗷"}, // x on a postit lookin thing

                {'\uE060', "⁰"}, //stylized 0 -E060
                {'\uE061', "¹"}, //stylized 1 -E061
                {'\uE062', "²"}, //stylized 2 -E062
                {'\uE063', "³"}, //stylized 3 -E063
                {'\uE064', "⁴"}, //stylized 4 -E064
                {'\uE065', "⁵"}, //stylized 5 - E065
                {'\uE066', "⁶"}, //stylized 6 - E066
                {'\uE067', "⁷"}, //stylized 7 - E067
                {'\uE068', "⁸"}, //stylized 8 - E068
                {'\uE069', "⁹"}, //stylized 9 - E069
                {'\uE06a', "Lᴠ"}, //Lv (small) -E06A
                {'\uE06b', "Sᴛ"}, //St -E06B
                {'\uE06c', "Nᴠ"}, //Nv -E06C
                {'\uE06d', "Aᴍ"}, //AM tile -E06D
                {'\uE06e', "Pᴍ"}, //PM tile -E06E
                {'\uE06f', "➨"}, //Arrow(right) -E06F

                {'\uE070', "❓"}, //? tile -E070
                //Block letters -> regional indicator range

                //number square range

                {'\uE0af', "➕"}, //+ tile -E0AF

                {'\uE0b0', "🇪\u200B"}, // another e in a square for some inexplicable reason
                {'\uE0b1', "❶"}, // numbers 1 - 9: now in hexagons!
                {'\uE0b2', "❷"}, // 
                {'\uE0b3', "❸"}, // 
                {'\uE0b4', "❹"}, // 
                {'\uE0b5', "❺"}, // 
                {'\uE0b6', "❻"}, // 
                {'\uE0b7', "❼"}, // 
                {'\uE0b8', "❽"}, // 
                {'\uE0b9', "❾"}, // 
                {'\uE0bb', "➲"}, //linked object arrow
                {'\uE0bc', "☯️"}, // level sync icon
                {'\uE0bd', "♋️"}, // level sync icon reversed
                {'\uE0be', "🔽"}, // sync down icon
                {'\uE0bf', "☒"}, // x in diamond

                {'\uE0c0', "🌟"}, // wide star
                {'\uE0c1', "Ⅰ"}, // wide roman numeral I
                {'\uE0c2', "Ⅱ"}, // wide roman numeral II
                {'\uE0c3', "Ⅲ"}, // wide roman numeral III
                {'\uE0c4', "Ⅳ"}, // wide roman numeral IV
                {'\uE0c5', "Ⅴ"}, // wide roman numeral V
                {'\uE0c6', "Ⅵ"}, // wide roman numeral VI

                {'\uE0D0', "Lᴛ"}, //LocalTimeEn   
                {'\uE0D1', "Sᴛ"}, //ServerTimeEn   
                {'\uE0D2', "Eᴛ"}, //EorzeaTimeEn   
                {'\uE0D3', "Oᴢ"}, //LocalTimeDe   
                {'\uE0D4', "Sᴢ"}, //ServerTimeDe   
                {'\uE0D5', "Eᴢ"}, //EorzeaTimeDe   
                {'\uE0D6', "Hʟ"}, //LocalTimeFr   
                {'\uE0D7', "Hs"}, //ServerTimeFr   
                {'\uE0D8', "Hᴇ"}, //EorzeaTimeFr   
                {'\uE0D9', "本"}, //LocalTimeJa   
                {'\uE0DA', "服"}, //ServerTimeJa   
                {'\uE0DB', "艾"} //EorzeaTimeJa 
            };

            // Transform the block letters into regional indicator letters. Will combine with flags if they are together in the right combo, so also adding a zero-width space
            for (var i = 0; i < 26; i++)
            {
                var xivchar = (char) (0xe071 + i);
                var unichar = char.ConvertFromUtf32('A' + 0x1f1a5 + i);
                var zerowidth = "\u200B";

                mappedChars.Add(xivchar, unichar + zerowidth);
            }

            // Number squares changed to enclosed number unicode. ⓪ is special, then ①-⑳ are one sequence then ㉑-㊿ are yet another, however xiv only goes to 31
            for (var i = 0; i <= 31; i++)
            {
                var xivchar = (char) (0xe08f + i);
                var unichar = "";

                if (i == 0) unichar = char.ConvertFromUtf32('⓪' + i);

                if (i > 0 && i <= 19) unichar = char.ConvertFromUtf32('①' + i - 1);

                if (i >= 21) unichar = char.ConvertFromUtf32('㉑' + i - 21);

                mappedChars.Add(xivchar, unichar);
            }

            return mappedChars;
        }
    }
}
