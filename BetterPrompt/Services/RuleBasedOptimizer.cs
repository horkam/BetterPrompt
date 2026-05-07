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

    public (string prompt, List<string> rulesFired) Optimize(string rawPrompt, CodebaseContext? context)
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

        // Pass 3: inject codebase references if we have context
        if (context is not null)
            prompt = InjectCodebaseReferences(prompt, context, rulesFired);

        // Pass 4: clean up extra whitespace
        prompt = CollapseWhitespace().Replace(prompt, " ").Trim();

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

    private static string InjectCodebaseReferences(string prompt, CodebaseContext context, List<string> rulesFired)
    {
        if (context.FileSignatures.Count == 0) return prompt;

        var injections = new List<string>();

        // Build a lookup: class/method name → file path
        var symbolMap = BuildSymbolMap(context);

        foreach (var (symbol, filePath) in symbolMap)
        {
            // Only match whole words, case-insensitive
            var wordPattern = new Regex($@"\b{Regex.Escape(symbol)}\b", RegexOptions.IgnoreCase);
            if (wordPattern.IsMatch(prompt))
            {
                var shortPath = filePath.Replace('\\', '/');
                if (!prompt.Contains(shortPath))
                {
                    injections.Add($"`{symbol}` ({shortPath})");
                    rulesFired.Add($"Injected path for: {symbol} → {shortPath}");
                }
            }
        }

        if (injections.Count == 0) return prompt;

        var sb = new StringBuilder(prompt);
        sb.AppendLine();
        sb.AppendLine();
        sb.Append("Relevant files: ");
        sb.Append(string.Join(", ", injections.Distinct().Take(5)));
        return sb.ToString();
    }

    private static Dictionary<string, string> BuildSymbolMap(CodebaseContext context)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var sig in context.FileSignatures)
        {
            foreach (var line in sig.Signatures.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                // Extract class names
                var classMatch = ClassNamePattern().Match(line);
                if (classMatch.Success)
                    map.TryAdd(classMatch.Groups[1].Value, sig.RelativePath);

                // Extract method names
                var methodMatch = MethodNamePattern().Match(line);
                if (methodMatch.Success)
                    map.TryAdd(methodMatch.Groups[1].Value, sig.RelativePath);
            }
        }

        return map;
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

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex CollapseWhitespace();

    [GeneratedRegex(@"\bclass\s+(\w+)")]
    private static partial Regex ClassNamePattern();

    [GeneratedRegex(@"\b(?:public|private|protected|internal|async)\s+\S+\s+(\w+)\s*\(")]
    private static partial Regex MethodNamePattern();
}
