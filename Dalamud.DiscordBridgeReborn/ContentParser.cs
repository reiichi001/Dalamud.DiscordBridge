using System.Text.RegularExpressions;
using Dalamud.Utility;

namespace Dalamud.DiscordBridge
{
    /// <summary>
    /// Parses a content string so that messages received externally can be compared.
    /// </summary>
    public class ContentParser
    {
        /// <summary>
        /// Construct a <see cref="ContentParser"/>, parsing the given content string.
        /// </summary>
        /// <param name="content">The content string to parse.</param>
        public ContentParser(string content)
        {
            match = Parse.Match(content);
        }

        /// <summary>
        /// The user-defined prefix for the slug.
        /// </summary>
        public string Prefix => match.Groups[PrefixGroup].Value;
        
        /// <summary>
        /// The slug for the chat kind.
        /// </summary>
        public string Slug => match.Groups[SlugGroup].Value;
        
        /// <summary>
        /// The relevant text from the in-game chat line.
        /// </summary>
        public string Text => match.Groups[TextGroup].Value;

        /// <summary>
        /// Parse the text from a content string.
        /// </summary>
        /// <param name="content">The content string to parse.</param>
        /// <returns>The relevant text from the in-game chat line.</returns>
        public static string ParseText(string content)
        {
            var contentParser = new ContentParser(content);

            string result = contentParser.Text;
            
            return result.IsNullOrEmpty() ? content : result;
        }
        
        private const string PrefixGroup = "prefix";
        private const string SlugGroup = "slug";
        private const string TextGroup = "text";
        
        private static readonly Regex Parse = new(@$"(?'{PrefixGroup}'.*)\*\*\[(?'{SlugGroup}'.+)\]\*\* (?'{TextGroup}'.+)");

        private readonly Match match;
    }
}