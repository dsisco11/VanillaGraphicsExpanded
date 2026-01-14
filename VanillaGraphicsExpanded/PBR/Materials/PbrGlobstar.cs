using System;
using System.Text;
using System.Text.RegularExpressions;

namespace VanillaGraphicsExpanded.PBR.Materials;

internal static class PbrGlobstar
{
    public static Regex CompileRegex(string globstarPattern)
    {
        if (string.IsNullOrWhiteSpace(globstarPattern))
        {
            throw new ArgumentException("Glob pattern must be non-empty", nameof(globstarPattern));
        }

        string regexPattern = ToRegexPattern(globstarPattern);

        // NOTE: Patterns are runtime values from JSON, so GeneratedRegex cannot be used.
        // NonBacktracking is available in modern .NET and prevents pathological backtracking.
        // Timeout prevents accidental DoS from malformed patterns.
        return new Regex(
            regexPattern,
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking,
            matchTimeout: TimeSpan.FromMilliseconds(50));
    }

    public static string ToRegexPattern(string globstarPattern)
    {
        // Standard globstar semantics:
        // - *  matches within segment (no '/')
        // - ?  matches one char within segment (no '/')
        // - ** matches across segments (may include '/'); when used as `**/` it matches zero or more complete segments

        var sb = new StringBuilder(globstarPattern.Length * 2);
        sb.Append('^');

        for (int i = 0; i < globstarPattern.Length; i++)
        {
            char c = globstarPattern[i];

            if (c == '*')
            {
                bool isGlobStar = i + 1 < globstarPattern.Length && globstarPattern[i + 1] == '*';
                if (isGlobStar)
                {
                    // Special-case `**/` so it can match zero-or-more complete path segments.
                    // This is required for patterns like `.../block/**/metal/...` to match `.../block/metal/...`.
                    bool followedBySlash = i + 2 < globstarPattern.Length && globstarPattern[i + 2] == '/';
                    if (followedBySlash)
                    {
                        // Consume both '*' and the following '/'
                        i += 2;
                        sb.Append("(?:[^/]+/)*");
                    }
                    else
                    {
                        // Consume the second '*'
                        i++;
                        sb.Append(".*");
                    }
                }
                else
                {
                    sb.Append("[^/]*");
                }

                continue;
            }

            if (c == '?')
            {
                sb.Append("[^/]");
                continue;
            }

            // Escape regex special characters
            if (c is '.' or '+' or '(' or ')' or '|' or '^' or '$' or '{' or '}' or '[' or ']' or '\\')
            {
                sb.Append('\\');
            }

            sb.Append(c);
        }

        sb.Append('$');
        return sb.ToString();
    }
}
