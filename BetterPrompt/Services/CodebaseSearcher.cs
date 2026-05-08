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
    public List<CodebaseSearchResult> Search(IEnumerable<string> keywords, CodebaseContext context, int maxResults = 6, IEnumerable<string>? baseKeywords = null)
    {
        var terms = keywords
            .Select(k => k.ToLowerInvariant())
            .Where(k => k.Length > 2)
            .ToList();

        if (terms.Count == 0) return [];

        // Base keywords (original prompt terms before expansion) get 2× score weight
        var baseTerms = baseKeywords != null
            ? baseKeywords.Select(k => k.ToLowerInvariant()).Where(k => k.Length > 2).ToHashSet()
            : terms.ToHashSet();

        var results = new List<CodebaseSearchResult>();

        // Search file names and directory names in the tree
        foreach (var file in context.FileSignatures)
        {
            // Keep original case so SplitIdentifier can split PascalCase correctly (e.g. CorePOS → ["Core","POS"])
            var fileName = Path.GetFileNameWithoutExtension(file.RelativePath);
            var relativePath = file.RelativePath.Replace('\\', '/');
            var dirParts  = relativePath.Split('/').SkipLast(1);

            var fileScore = ScoreAgainstTerms(fileName, terms, baseTerms);
            if (fileScore > 0)
            {
                results.Add(new CodebaseSearchResult
                {
                    FilePath  = relativePath,
                    Symbol    = Path.GetFileName(file.RelativePath),
                    MatchKind = "file",
                    Score     = fileScore * ExtensionMultiplier(file.RelativePath, context.ReferencedScripts)
                                         * GetDirectoryMultiplier(relativePath, context.ProjectType)
                });
            }

            // Search directory segments too
            foreach (var dir in dirParts)
            {
                var dirScore = ScoreAgainstTerms(dir.ToLowerInvariant(), terms, baseTerms);
                if (dirScore > 0.15)
                {
                    results.Add(new CodebaseSearchResult
                    {
                        FilePath  = relativePath,
                        Symbol    = dir,
                        MatchKind = "directory",
                        Score     = dirScore * 0.7
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
                    var score = ScoreAgainstTerms(name, terms, baseTerms);
                    if (score > 0)
                        results.Add(new CodebaseSearchResult
                        {
                            FilePath  = relativePath,
                            Symbol    = name,
                            MatchKind = "class",
                            Score     = score * 1.1 * ExtensionMultiplier(file.RelativePath, context.ReferencedScripts)
                                                    * GetDirectoryMultiplier(relativePath, context.ProjectType)
                        });
                }

                var methodMatch = Regex.Match(line, @"\b(?:public|private|protected|internal|async)\s+\S+\s+(\w+)\s*\(");
                if (methodMatch.Success)
                {
                    var name = methodMatch.Groups[1].Value;
                    var score = ScoreAgainstTerms(name, terms, baseTerms);
                    if (score > 0)
                        results.Add(new CodebaseSearchResult
                        {
                            FilePath  = relativePath,
                            Symbol    = name,
                            MatchKind = "method",
                            Score     = score * ExtensionMultiplier(file.RelativePath, context.ReferencedScripts)
                                              * GetDirectoryMultiplier(relativePath, context.ProjectType)
                        });
                }
            }
        }

        // Also scan the raw file tree text for files not in signatures (e.g. non-code files)
        // Only directory lines (no leading spaces) — file entries are already covered by signatures
        foreach (var treeLine in context.FileTree.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var isDirectory = treeLine.StartsWith("  ") == false;
            var trimmed = treeLine.Trim().TrimEnd('/');
            var lower   = trimmed.ToLowerInvariant();
            var score   = ScoreAgainstTerms(lower, terms, baseTerms);
            if (score > 0 && !results.Any(r => Path.GetFileName(r.FilePath).Equals(trimmed, StringComparison.OrdinalIgnoreCase) ||
                                                   r.FilePath.Equals(trimmed, StringComparison.OrdinalIgnoreCase)))
            {
                var multiplier = isDirectory ? 0.8 : 0.8 * ExtensionMultiplier(trimmed, context.ReferencedScripts)
                                                         * GetDirectoryMultiplier(trimmed, context.ProjectType);
                results.Add(new CodebaseSearchResult
                {
                    FilePath  = trimmed,
                    Symbol    = trimmed,
                    MatchKind = isDirectory ? "directory" : "file",
                    Score     = score * multiplier
                });
            }
        }

        return results
            .GroupBy(r => $"{r.FilePath}|{r.Symbol}")   // deduplicate
            .Select(g => g.OrderByDescending(x => x.Score).First())
            .Where(r => r.Score >= 0.22)                // suppress near-zero matches
            .OrderByDescending(r => r.Score)
            .Take(maxResults)
            .ToList();
    }

    private static double ExtensionMultiplier(string filePath, HashSet<string>? referencedScripts = null)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        if (ext is ".js" or ".jsx")
        {
            // App JS referenced in views scores like .ts; unreferenced vendor JS is penalized
            if (referencedScripts != null &&
                (referencedScripts.Contains(filePath) || referencedScripts.Contains(Path.GetFileName(filePath))))
                return 1.2;
            return 0.7;
        }
        return ext switch
        {
            ".sql"                          => 2.0,
            ".cs" or ".cshtml" or ".razor"
                 or ".vb"                   => 1.5,
            ".ts" or ".tsx"                 => 1.2,
            _                               => 1.0
        };
    }

    private static double GetDirectoryMultiplier(string filePath, ProjectType projectType)
    {
        if (projectType == ProjectType.Unknown) return 1.0;

        // Extract directory segments (exclude the filename itself)
        var parts = filePath.ToLowerInvariant().Replace('\\', '/').Split('/');
        var dirs  = parts.Length > 1 ? parts[..^1] : [];

        var (tier1, tier2, tier3) = projectType switch
        {
            ProjectType.CSharpMvc => (
                new[] { "controllers", "models", "views", "sql", "data", "services", "repositories", "entities" },
                new[] { "helpers", "filters", "extensions", "viewmodels", "dtos", "utilities", "infrastructure" },
                new[] { "scripts", "content", "wwwroot", "assets", "fonts" }
            ),
            ProjectType.CSharpApi => (
                new[] { "controllers", "models", "services", "data", "repositories", "sql", "entities" },
                new[] { "helpers", "filters", "extensions", "dtos", "validators", "middleware" },
                new[] { "wwwroot", "scripts", "assets" }
            ),
            ProjectType.CSharpGeneric => (
                new[] { "models", "services", "data", "sql", "core", "domain" },
                new[] { "helpers", "extensions", "utilities", "infrastructure" },
                new[] { "scripts", "assets", "resources" }
            ),
            ProjectType.React => (
                new[] { "src", "components", "pages", "hooks", "context", "store", "features" },
                new[] { "utils", "services", "api", "helpers", "lib" },
                new[] { "public", "assets", "styles", "static" }
            ),
            ProjectType.Angular => (
                new[] { "src", "app", "components", "services", "modules", "pages" },
                new[] { "shared", "helpers", "guards", "interceptors", "models" },
                new[] { "assets", "environments" }
            ),
            ProjectType.Vue => (
                new[] { "src", "components", "views", "store", "pages", "composables" },
                new[] { "utils", "services", "api", "helpers", "plugins" },
                new[] { "public", "assets", "static" }
            ),
            ProjectType.Django => (
                new[] { "models", "views", "serializers", "api", "urls" },
                new[] { "utils", "helpers", "services", "forms", "admin" },
                new[] { "static", "templates", "migrations" }
            ),
            ProjectType.Python => (
                new[] { "models", "services", "core", "api", "domain" },
                new[] { "utils", "helpers", "exceptions" },
                new[] { "static", "templates", "tests" }
            ),
            _ => (Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>())
        };

        foreach (var dir in dirs)
        {
            if (Array.IndexOf(tier1, dir) >= 0) return 2.0;
            if (Array.IndexOf(tier2, dir) >= 0) return 1.3;
            if (Array.IndexOf(tier3, dir) >= 0) return 0.7;
        }

        return 1.0;
    }

    private static double ScoreAgainstTerms(string candidate, List<string> terms, HashSet<string>? baseTerms = null)
    {
        if (string.IsNullOrWhiteSpace(candidate)) return 0;

        // Split camelCase / PascalCase into parts for better matching
        var parts = SplitIdentifier(candidate);
        double total = 0;

        foreach (var term in terms)
        {
            // Original prompt terms score 2× vs Ollama-expanded synonyms
            double weight = baseTerms != null && baseTerms.Contains(term) ? 2.0 : 1.0;

            // Exact segment match scores highest
            if (parts.Any(p => p == term))
                total += 1.0 * weight;
            // Partial / contains match scores lower
            else if (parts.Any(p => p.Contains(term) || term.Contains(p)))
                total += 0.5 * weight;
            // Full string contains
            else if (candidate.Contains(term, StringComparison.OrdinalIgnoreCase))
                total += 0.3 * weight;
        }

        // Normalize by sqrt(termCount) so a single strong match isn't buried by query length
        return terms.Count > 0 ? total / Math.Sqrt(terms.Count) : 0;
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
