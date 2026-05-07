using BetterPrompt.Models;
using System.IO;
using System.Text.RegularExpressions;

namespace BetterPrompt.Services;

public class CodebaseSearchResult
{
    public string FilePath { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public string MatchKind { get; set; } = string.Empty; // "file", "class", "method", "directory"
    public double Score { get; set; }

    public string Description => MatchKind switch
    {
        "file"      => $"`{Symbol}` in {FilePath}",
        "class"     => $"class `{Symbol}` in {FilePath}",
        "method"    => $"`{Symbol}()` in {FilePath}",
        "directory" => $"{FilePath}/",
        _           => FilePath
    };
}

public class CodebaseSearcher
{
    public List<CodebaseSearchResult> Search(IEnumerable<string> keywords, CodebaseContext context, int maxResults = 6)
    {
        var terms = keywords
            .Select(k => k.ToLowerInvariant())
            .Where(k => k.Length > 2)
            .ToList();

        if (terms.Count == 0) return [];

        var results = new List<CodebaseSearchResult>();

        // Search file names and directory names in the tree
        foreach (var file in context.FileSignatures)
        {
            var fileName = Path.GetFileNameWithoutExtension(file.RelativePath).ToLowerInvariant();
            var dirParts  = file.RelativePath.Replace('\\', '/').Split('/').SkipLast(1);

            var fileScore = ScoreAgainstTerms(fileName, terms);
            if (fileScore > 0)
            {
                results.Add(new CodebaseSearchResult
                {
                    FilePath  = file.RelativePath.Replace('\\', '/'),
                    Symbol    = Path.GetFileName(file.RelativePath),
                    MatchKind = "file",
                    Score     = fileScore
                });
            }

            // Search directory segments too
            foreach (var dir in dirParts)
            {
                var dirScore = ScoreAgainstTerms(dir.ToLowerInvariant(), terms);
                if (dirScore > 0.4)
                {
                    results.Add(new CodebaseSearchResult
                    {
                        FilePath  = file.RelativePath.Replace('\\', '/'),
                        Symbol    = dir,
                        MatchKind = "directory",
                        Score     = dirScore * 0.7 // slightly lower than direct file match
                    });
                }
            }

            // Search class and method names in signatures
            foreach (var line in file.Signatures.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var classMatch = Regex.Match(line, @"\bclass\s+(\w+)");
                if (classMatch.Success)
                {
                    var name = classMatch.Groups[1].Value;
                    var score = ScoreAgainstTerms(name.ToLowerInvariant(), terms);
                    if (score > 0)
                        results.Add(new CodebaseSearchResult
                        {
                            FilePath  = file.RelativePath.Replace('\\', '/'),
                            Symbol    = name,
                            MatchKind = "class",
                            Score     = score * 1.1 // class names are strong matches
                        });
                }

                var methodMatch = Regex.Match(line, @"\b(?:public|private|protected|internal|async)\s+\S+\s+(\w+)\s*\(");
                if (methodMatch.Success)
                {
                    var name = methodMatch.Groups[1].Value;
                    var score = ScoreAgainstTerms(name.ToLowerInvariant(), terms);
                    if (score > 0)
                        results.Add(new CodebaseSearchResult
                        {
                            FilePath  = file.RelativePath.Replace('\\', '/'),
                            Symbol    = name,
                            MatchKind = "method",
                            Score     = score
                        });
                }
            }
        }

        // Also scan the raw file tree text for files not in signatures (e.g. non-code files)
        foreach (var treeLine in context.FileTree.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = treeLine.Trim().TrimEnd('/');
            var lower   = trimmed.ToLowerInvariant();
            var score   = ScoreAgainstTerms(lower, terms);
            if (score > 0 && !results.Any(r => r.FilePath.EndsWith(trimmed, StringComparison.OrdinalIgnoreCase)))
            {
                results.Add(new CodebaseSearchResult
                {
                    FilePath  = trimmed,
                    Symbol    = trimmed,
                    MatchKind = "file",
                    Score     = score * 0.8
                });
            }
        }

        return results
            .GroupBy(r => $"{r.FilePath}|{r.Symbol}")   // deduplicate
            .Select(g => g.OrderByDescending(x => x.Score).First())
            .OrderByDescending(r => r.Score)
            .Take(maxResults)
            .ToList();
    }

    private static double ScoreAgainstTerms(string candidate, List<string> terms)
    {
        if (string.IsNullOrWhiteSpace(candidate)) return 0;

        // Split camelCase / PascalCase into parts for better matching
        var parts = SplitIdentifier(candidate);
        double total = 0;

        foreach (var term in terms)
        {
            // Exact segment match scores highest
            if (parts.Any(p => p == term))
                total += 1.0;
            // Partial / contains match scores lower
            else if (parts.Any(p => p.Contains(term) || term.Contains(p)))
                total += 0.5;
            // Full string contains
            else if (candidate.Contains(term))
                total += 0.3;
        }

        // Normalize by term count so multi-word queries don't artificially outscore
        return terms.Count > 0 ? total / terms.Count : 0;
    }

    private static List<string> SplitIdentifier(string name)
    {
        // Split on PascalCase, camelCase, underscores, hyphens, dots
        var parts = Regex.Split(name, @"(?<=[a-z])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])|[_\-\.\s]+")
            .Select(p => p.ToLowerInvariant())
            .Where(p => p.Length > 0)
            .ToList();

        // Also include the full lowercased name for simple contains checks
        parts.Add(name.ToLowerInvariant());
        return parts;
    }
}
