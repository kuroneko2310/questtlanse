using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace QuestTranslationAddon
{
    /// <summary>
    /// Builds a translation payload while shielding text that must survive byte-for-byte.
    /// Temporary markers are never accepted as final output: restoration is all-or-nothing.
    /// </summary>
    internal sealed class ProtectedTextPayload
    {
        private const string TokenStem = "QTSHIELD_";

        private static readonly Regex TranslatableEnglishRegex =
            new Regex(@"[A-Za-z]{2,}", RegexOptions.Compiled);

        private static readonly Regex LeakedBasePlaceholderRegex =
            new Regex(@"__PH\s*\d+\s*__", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Every Arabic-numeral run is protected, including digits touching Japanese text or IDs.
        private static readonly Regex NumberLikeRegex =
            new Regex(@"[-+]?\d+(?:[.,:/-]\d+)*(?:[%‰]|[A-Za-z]{1,4})?", RegexOptions.Compiled);

        private static readonly Regex TagRegex =
            new Regex(@"<(/?)([A-Za-z][A-Za-z0-9]*)(?:=[^>\s]+|\s[^>]*)?(/?)>", RegexOptions.Compiled);

        private static readonly Regex[] FixedProtectionPatterns =
        {
            new Regex(@"\(\*[^)]*\)", RegexOptions.Compiled),
            new Regex(@"<[^>\r\n]+>", RegexOptions.Compiled),
            new Regex(@"\{[^{}\r\n]+\}", RegexOptions.Compiled),
            new Regex(@"\[[^\[\]\r\n]+\]", RegexOptions.Compiled),
            new Regex(@"\\[nrt]", RegexOptions.Compiled),
            new Regex(@"https?://[^\s<>{}\[\]]+|www\.[^\s<>{}\[\]]+", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            new Regex(@"\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b", RegexOptions.Compiled | RegexOptions.IgnoreCase),
            new Regex(@"(?:[A-Za-z]:\\|/)[^\s<>{}\[\]]+", RegexOptions.Compiled),
            new Regex(@"\b[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\b", RegexOptions.Compiled),
            new Regex(@"\b0x[0-9A-Fa-f]+\b", RegexOptions.Compiled),
            new Regex(@"\bQuest\d+(?:\.[A-Za-z][A-Za-z0-9_]*)+\b", RegexOptions.Compiled),
            new Regex(@"\b(?=[A-Za-z0-9-]*\d)[A-Za-z][A-Za-z0-9]*(?:-[A-Za-z0-9]+)+\b", RegexOptions.Compiled),
            new Regex(@"\b[A-Za-z][A-Za-z0-9]*(?:[_./:\\][A-Za-z0-9]+)+\b", RegexOptions.Compiled),
            new Regex(@"(?<![A-Za-z0-9])(?:[a-z]+[A-Z][A-Za-z0-9]*|[A-Z][a-z0-9]+(?:[A-Z][a-z0-9]+)+)(?![A-Za-z0-9])", RegexOptions.Compiled),
            new Regex(@"\b(?:[A-Z]{2,}[A-Z0-9]*|[A-Za-z]+\d+[A-Za-z0-9]*)\b", RegexOptions.Compiled),
            NumberLikeRegex
        };

        private readonly List<ProtectedPiece> pieces;

        private ProtectedTextPayload(string originalText, string protectedText, List<ProtectedPiece> pieces)
        {
            OriginalText = originalText;
            ProtectedText = protectedText;
            this.pieces = pieces;
        }

        internal string OriginalText { get; }

        internal string ProtectedText { get; }

        internal IReadOnlyList<string> Tokens
        {
            get { return pieces.Select(piece => piece.Token).ToList(); }
        }

        internal static bool TryCreate(string originalText, string targetLanguageName, out ProtectedTextPayload payload)
        {
            payload = null;

            if (string.IsNullOrWhiteSpace(originalText))
            {
                return false;
            }

            // Do not process text that already resembles either our markers or a leaked marker
            // from the parent mod. Leaving the source untouched is safer than compounding damage.
            if (originalText.IndexOf(TokenStem, StringComparison.OrdinalIgnoreCase) >= 0 ||
                LeakedBasePlaceholderRegex.IsMatch(originalText))
            {
                return false;
            }

            var protectedCharacters = new bool[originalText.Length];

            foreach (var pattern in FixedProtectionPatterns)
            {
                MarkMatches(originalText, pattern, protectedCharacters);
            }

            var targetScriptPattern = CreateTargetScriptPattern(targetLanguageName);
            if (targetScriptPattern != null)
            {
                MarkMatches(originalText, targetScriptPattern, protectedCharacters);
            }

            var translatableCandidate = new StringBuilder(originalText.Length);
            for (var index = 0; index < originalText.Length; index++)
            {
                translatableCandidate.Append(protectedCharacters[index] ? ' ' : originalText[index]);
            }

            if (!TranslatableEnglishRegex.IsMatch(translatableCandidate.ToString()))
            {
                return false;
            }

            var output = new StringBuilder(originalText.Length + 32);
            var protectedPieces = new List<ProtectedPiece>();
            var position = 0;

            while (position < originalText.Length)
            {
                if (!protectedCharacters[position])
                {
                    output.Append(originalText[position]);
                    position++;
                    continue;
                }

                var start = position;
                while (position < originalText.Length && protectedCharacters[position])
                {
                    position++;
                }

                var originalPiece = originalText.Substring(start, position - start);
                var token = CreateToken(protectedPieces.Count);
                protectedPieces.Add(new ProtectedPiece(token, originalPiece));
                output.Append(token);
            }

            payload = new ProtectedTextPayload(originalText, output.ToString(), protectedPieces);
            return true;
        }

        internal bool TryRestore(string translatedText, out string restoredText)
        {
            restoredText = OriginalText;

            if (string.IsNullOrWhiteSpace(translatedText))
            {
                return false;
            }

            var result = translatedText;

            foreach (var piece in pieces)
            {
                // Missing or duplicated markers mean the engine changed the protected structure.
                // Never expose a partly restored result to the player.
                if (CountOccurrences(result, piece.Token) != 1)
                {
                    return false;
                }

                result = result.Replace(piece.Token, piece.Original);
            }

            if (result.IndexOf(TokenStem, StringComparison.OrdinalIgnoreCase) >= 0 ||
                LeakedBasePlaceholderRegex.IsMatch(result))
            {
                return false;
            }

            if (!HaveSameNumberTokens(OriginalText, result))
            {
                return false;
            }

            if (HasBalancedTags(OriginalText) && !HasBalancedTags(result))
            {
                return false;
            }

            restoredText = result;
            return true;
        }

        private static void MarkMatches(string text, Regex regex, bool[] protectedCharacters)
        {
            foreach (Match match in regex.Matches(text))
            {
                var end = Math.Min(match.Index + match.Length, protectedCharacters.Length);
                for (var index = match.Index; index < end; index++)
                {
                    protectedCharacters[index] = true;
                }
            }
        }

        private static Regex CreateTargetScriptPattern(string targetLanguageName)
        {
            if (string.IsNullOrEmpty(targetLanguageName))
            {
                return null;
            }

            var language = targetLanguageName.ToLowerInvariant();

            if (language.Contains("japanese") || language.Contains("日本語"))
            {
                return new Regex(@"[\u3040-\u30FF\u31F0-\u31FF\u3400-\u9FFF\uF900-\uFAFF]+", RegexOptions.Compiled);
            }

            if (language.Contains("chinese") || language.Contains("简体") || language.Contains("繁體"))
            {
                return new Regex(@"[\u3400-\u9FFF\uF900-\uFAFF]+", RegexOptions.Compiled);
            }

            if (language.Contains("korean") || language.Contains("한국어"))
            {
                return new Regex(@"[\u1100-\u11FF\u3130-\u318F\uAC00-\uD7A3]+", RegexOptions.Compiled);
            }

            if (language.Contains("russian") || language.Contains("ukrainian"))
            {
                return new Regex(@"[\u0400-\u04FF]+", RegexOptions.Compiled);
            }

            if (language.Contains("arabic"))
            {
                return new Regex(@"[\u0600-\u06FF\u0750-\u077F]+", RegexOptions.Compiled);
            }

            if (language.Contains("thai"))
            {
                return new Regex(@"[\u0E00-\u0E7F]+", RegexOptions.Compiled);
            }

            if (language.Contains("hebrew"))
            {
                return new Regex(@"[\u0590-\u05FF]+", RegexOptions.Compiled);
            }

            if (language.Contains("greek"))
            {
                return new Regex(@"[\u0370-\u03FF\u1F00-\u1FFF]+", RegexOptions.Compiled);
            }

            return null;
        }

        private static string CreateToken(int index)
        {
            // Alphabetic labels avoid creating a second, unrelated numeric sequence in the text.
            var label = new StringBuilder();
            var value = index;

            do
            {
                label.Insert(0, (char)('A' + value % 26));
                value = value / 26 - 1;
            }
            while (value >= 0);

            return "{" + TokenStem + label + "}";
        }

        private static int CountOccurrences(string text, string value)
        {
            var count = 0;
            var position = 0;

            while ((position = text.IndexOf(value, position, StringComparison.Ordinal)) >= 0)
            {
                count++;
                position += value.Length;
            }

            return count;
        }

        private static bool HaveSameNumberTokens(string original, string translated)
        {
            var originalNumbers = NumberLikeRegex.Matches(original)
                .Cast<Match>()
                .Select(match => match.Value)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();

            var translatedNumbers = NumberLikeRegex.Matches(translated)
                .Cast<Match>()
                .Select(match => match.Value)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();

            return originalNumbers.SequenceEqual(translatedNumbers, StringComparer.Ordinal);
        }

        private static bool HasBalancedTags(string text)
        {
            var stack = new Stack<string>();

            foreach (Match match in TagRegex.Matches(text))
            {
                var isClosing = match.Groups[1].Value == "/";
                var isSelfClosing = match.Groups[3].Value == "/";
                var name = match.Groups[2].Value;

                if (isSelfClosing || string.Equals(name, "br", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!isClosing)
                {
                    stack.Push(name);
                    continue;
                }

                if (stack.Count == 0 || !string.Equals(stack.Pop(), name, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return stack.Count == 0;
        }

        private sealed class ProtectedPiece
        {
            internal ProtectedPiece(string token, string original)
            {
                Token = token;
                Original = original;
            }

            internal string Token { get; }

            internal string Original { get; }
        }
    }
}
