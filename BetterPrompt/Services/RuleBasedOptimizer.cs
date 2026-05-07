using BetterPrompt.Models;
using System.Text;
using System.Text.RegularExpressions;

namespace BetterPrompt.Services;

public partial class RuleBasedOptimizer
{
    private static readonly (Regex Pattern, string Replacement)[] MetaInstructionRules =
    [
        (MetaLookAt(),    ""),
        (MetaGoAhead(),   ""),
        (MetaCanYou(),    ""),
        (MetaPlease(),    ""),
        (MetaINeedYou(),  ""),
        (MetaInTheCode(), ""),
        (MetaInTheProject(), ""),
        (MetaInTheCodebase(), ""),
        (MetaSearchFor(), ""),
    ];

    public (string prompt, List<string> rulesFired) Optimize(string rawPrompt, CodebaseContext? context, List<string>? expandedKeywords = null)
    {
        var prompt = rawPrompt.Trim();
        var rulesFired = new List<string>();

        // Pass 1: strip meta-instructions
        foreach (var (pattern, replacement) in MetaInstructionRules)
        {
            var replaced = pattern.Replace(prompt, replacement);
            if (replaced != prompt)
            {
                rulesFired.Add($"Removed: '{pattern}'");
                prompt = replaced.Trim();
            }
        }

        // Pass 2: normalize action verb at start
        prompt = EnsureActionVerb(prompt, rulesFired);

        // Pass 3: collapse horizontal whitespace only (preserve newlines)
        prompt = CollapseSpaces().Replace(prompt, " ").Trim();

        // Pass 4: inject codebase references — runs AFTER whitespace cleanup so injected
        // newlines are not destroyed
        if (context is not null)
            prompt = InjectCodebaseReferences(prompt, context, rulesFired, expandedKeywords);

        return (prompt, rulesFired);
    }

    private static string EnsureActionVerb(string prompt, List<string> rulesFired)
    {
        var lower = prompt.ToLowerInvariant();
        var vague = new[] { "i want to", "i would like to", "i'd like to", "we need to", "we should", "we want to" };

        foreach (var v in vague)
        {
            if (lower.StartsWith(v))
            {
                // Capitalize whatever follows and remove the vague opener
                var rest = prompt[v.Length..].TrimStart();
                if (rest.Length > 0)
                    rest = char.ToUpperInvariant(rest[0]) + rest[1..];
                rulesFired.Add($"Removed vague opener: '{v}'");
                return rest;
            }
        }

        return prompt;
    }

    private static readonly CodebaseSearcher Searcher = new();

    private static string InjectCodebaseReferences(string prompt, CodebaseContext context, List<string> rulesFired, List<string>? expandedKeywords)
    {
        if (context.FileSignatures.Count == 0 && string.IsNullOrWhiteSpace(context.FileTree))
            return prompt;

        var keywords = expandedKeywords is { Count: > 0 }
            ? expandedKeywords
            : SimilarityMatcher.Tokenize(prompt);
        if (keywords.Count == 0) return prompt;

        var matches = Searcher.Search(keywords, context, maxResults: 6);
        if (matches.Count == 0) return prompt;

        // Deduplicate by file path — keep the highest-scored match per file
        var byFile = matches
            .GroupBy(m => m.FilePath)
            .Select(g => g.OrderByDescending(m => m.Score).First())
            .OrderByDescending(m => m.Score)
            .ToList();

        var sb = new StringBuilder(prompt.TrimEnd());
        sb.AppendLine();
        sb.AppendLine();
        sb.AppendLine("Relevant locations found in codebase:");
        foreach (var match in byFile)
        {
            sb.AppendLine($"  - {match.Description}");
            rulesFired.Add($"Found {match.MatchKind}: {match.Symbol} in {match.FilePath}");
        }

        return sb.ToString().TrimEnd();
    }

    // Meta-instruction patterns
    [GeneratedRegex(@"\blook at the code and\b\s*", RegexOptions.IgnoreCase)]
    private static partial Regex MetaLookAt();

    [GeneratedRegex(@"\bgo ahead and\b\s*", RegexOptions.IgnoreCase)]
    private static partial Regex MetaGoAhead();

    [GeneratedRegex(@"^can you\s+", RegexOptions.IgnoreCase)]
    private static partial Regex MetaCanYou();

    [GeneratedRegex(@"^please\s+", RegexOptions.IgnoreCase)]
    private static partial Regex MetaPlease();

    [GeneratedRegex(@"^i need you to\s+", RegexOptions.IgnoreCase)]
    private static partial Regex MetaINeedYou();

    [GeneratedRegex(@"\bin the code\b", RegexOptions.IgnoreCase)]
    private static partial Regex MetaInTheCode();

    [GeneratedRegex(@"\bin the project\b", RegexOptions.IgnoreCase)]
    private static partial Regex MetaInTheProject();

    [GeneratedRegex(@"\bin the codebase\b", RegexOptions.IgnoreCase)]
    private static partial Regex MetaInTheCodebase();

    [GeneratedRegex(@"\bsearch for\b\s*", RegexOptions.IgnoreCase)]
    private static partial Regex MetaSearchFor();

    [GeneratedRegex(@"[ \t]{2,}")]
    private static partial Regex CollapseSpaces();

}
