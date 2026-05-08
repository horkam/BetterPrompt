using BetterPrompt.Models;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace BetterPrompt.Services;

public partial class CodebaseIndexer
{
    private readonly AppSettings _settings;

    public CodebaseIndexer(AppSettings settings)
    {
        _settings = settings;
    }

    public async Task<CodebaseContext> IndexAsync(string rootPath, IProgress<string>? progress = null)
    {
        var context = new CodebaseContext { RootPath = rootPath };

        var claudeMd = Path.Combine(rootPath, "CLAUDE.md");
        if (File.Exists(claudeMd))
            context.ClaudeMdContent = await File.ReadAllTextAsync(claudeMd);

        var gitignorePatterns = LoadGitignorePatterns(rootPath);
        var allFiles = EnumerateFiles(rootPath, gitignorePatterns).ToList();
        context.TotalFiles = allFiles.Count;

        context.FileTree = BuildFileTree(rootPath, allFiles);

        var codeFiles = allFiles
            .Where(f => _settings.CodeExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .OrderBy(f => IndexPriority(Path.GetExtension(f).ToLowerInvariant()))
            .Take(_settings.MaxFilesToIndex)
            .ToList();

        context.IndexedFiles = codeFiles.Count;

        foreach (var file in codeFiles)
        {
            var relative = Path.GetRelativePath(rootPath, file);
            progress?.Report($"Indexing {relative}");
            var sigs = await ExtractSignaturesAsync(file);
            if (!string.IsNullOrWhiteSpace(sigs))
                context.FileSignatures.Add(new FileSignature { RelativePath = relative, Signatures = sigs });
        }

        context.ReferencedScripts = ScanReferencedScripts(rootPath, allFiles);
        context.ProjectType = DetectProjectType(rootPath, allFiles);

        return context;
    }

    private static ProjectType DetectProjectType(string rootPath, IEnumerable<string> allFiles)
    {
        var relPaths = allFiles
            .Select(f => Path.GetRelativePath(rootPath, f).Replace('\\', '/').ToLowerInvariant())
            .ToList();

        bool hasCsproj = relPaths.Any(f => f.EndsWith(".csproj"));
        if (hasCsproj)
        {
            bool hasControllers = relPaths.Any(f => f.Contains("/controllers/") || f.StartsWith("controllers/"));
            bool hasViews       = relPaths.Any(f => f.Contains("/views/")       || f.StartsWith("views/"));
            if (hasControllers && hasViews) return ProjectType.CSharpMvc;
            if (hasControllers)             return ProjectType.CSharpApi;
            return ProjectType.CSharpGeneric;
        }

        bool hasPackageJson = relPaths.Any(f => f == "package.json" || f.EndsWith("/package.json"));
        if (hasPackageJson)
        {
            var pkgPath = allFiles.FirstOrDefault(f =>
                Path.GetFileName(f).Equals("package.json", StringComparison.OrdinalIgnoreCase) &&
                Path.GetDirectoryName(f)?.Equals(rootPath, StringComparison.OrdinalIgnoreCase) == true);
            if (pkgPath != null)
            {
                try
                {
                    var content = File.ReadAllText(pkgPath).ToLowerInvariant();
                    if (content.Contains("\"@angular/core\"")) return ProjectType.Angular;
                    if (content.Contains("\"react\""))         return ProjectType.React;
                    if (content.Contains("\"vue\""))           return ProjectType.Vue;
                }
                catch { }
            }
            return ProjectType.Node;
        }

        if (relPaths.Any(f => Path.GetFileName(f) == "manage.py")) return ProjectType.Django;
        if (relPaths.Any(f => f.EndsWith(".py")))                   return ProjectType.Python;

        return ProjectType.Unknown;
    }

    private static HashSet<string> ScanReferencedScripts(string rootPath, IEnumerable<string> allFiles)
    {
        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var viewFiles = allFiles.Where(f =>
        {
            var ext = Path.GetExtension(f).ToLowerInvariant();
            return ext is ".cshtml" or ".html" or ".aspx";
        });

        var scriptSrcPattern = new System.Text.RegularExpressions.Regex(
            @"<script[^>]+src\s*=\s*[""']([^""']+)[""']",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var bundlePattern = new System.Text.RegularExpressions.Regex(
            @"(?:Scripts\.Render|bundles\.Add)\s*\(\s*[""']([^""']+)[""']",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        foreach (var viewFile in viewFiles)
        {
            try
            {
                var content = File.ReadAllText(viewFile);
                foreach (System.Text.RegularExpressions.Match m in scriptSrcPattern.Matches(content))
                {
                    var src = m.Groups[1].Value.TrimStart('~', '/').Replace('/', Path.DirectorySeparatorChar);
                    referenced.Add(src);
                    referenced.Add(Path.GetFileName(src));
                }
                foreach (System.Text.RegularExpressions.Match m in bundlePattern.Matches(content))
                    referenced.Add(m.Groups[1].Value.TrimStart('~', '/'));
            }
            catch { }
        }
        return referenced;
    }

    private static string BuildFileTree(string root, IEnumerable<string> files)
    {
        var sb = new StringBuilder();
        var dirs = new HashSet<string>();
        foreach (var file in files)
        {
            var rel = Path.GetRelativePath(root, file);
            var dir = Path.GetDirectoryName(rel);
            if (!string.IsNullOrEmpty(dir) && dirs.Add(dir))
                sb.AppendLine(dir.Replace('\\', '/') + "/");
            sb.AppendLine("  " + Path.GetFileName(file));
        }
        return sb.ToString();
    }

    private IEnumerable<string> EnumerateFiles(string root, HashSet<string> gitignorePatterns)
    {
        var excluded = _settings.ExcludedDirectories.ToHashSet(StringComparer.OrdinalIgnoreCase);

        return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(f =>
            {
                var parts = Path.GetRelativePath(root, f).Split(Path.DirectorySeparatorChar);
                if (parts.Any(p => excluded.Contains(p))) return false;
                var rel = Path.GetRelativePath(root, f).Replace('\\', '/');
                if (gitignorePatterns.Any(p => Regex.IsMatch(rel, p))) return false;
                // Skip minified and declaration files — always vendor, never useful for search
                var name = Path.GetFileName(f);
                if (name.EndsWith(".min.js", StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith(".min.css", StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith(".d.ts", StringComparison.OrdinalIgnoreCase))
                    return false;
                return true;
            });
    }

    private static HashSet<string> LoadGitignorePatterns(string root)
    {
        var patterns = new HashSet<string>();
        var gitignore = Path.Combine(root, ".gitignore");
        if (!File.Exists(gitignore)) return patterns;

        foreach (var line in File.ReadAllLines(gitignore))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#')) continue;
            // Convert simple glob to regex
            var regex = "^" + Regex.Escape(trimmed).Replace("\\*\\*", ".*").Replace("\\*", "[^/]*").Replace("\\?", ".") + "(/.*)?$";
            patterns.Add(regex);
        }
        return patterns;
    }

    private async Task<string> ExtractSignaturesAsync(string filePath)
    {
        string[] lines;
        try { lines = await File.ReadAllLinesAsync(filePath); }
        catch { return string.Empty; }

        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        var sigs = ext switch
        {
            ".cs" or ".vb" => ExtractCSharpSignatures(lines),
            ".ts" or ".tsx" or ".js" or ".jsx" => ExtractJsSignatures(lines),
            ".py" => ExtractPythonSignatures(lines),
            ".cshtml" or ".razor" => ExtractRazorSignatures(lines),
            ".sql" => ExtractSqlSignatures(lines),
            _ => ExtractGenericSignatures(lines)
        };

        return string.Join('\n', sigs.Take(_settings.MaxSignatureLinesPerFile));
    }

    private static IEnumerable<string> ExtractCSharpSignatures(string[] lines)
    {
        var patterns = new[]
        {
            CSharpNamespace(), CSharpClass(), CSharpInterface(),
            CSharpEnum(), CSharpMethod(), CSharpProperty()
        };
        return lines.Where(l => patterns.Any(p => p.IsMatch(l.Trim())));
    }

    private static IEnumerable<string> ExtractJsSignatures(string[] lines)
    {
        return lines.Where(l =>
        {
            var t = l.Trim();
            return t.StartsWith("export ") || t.StartsWith("function ") ||
                   t.StartsWith("class ") || t.StartsWith("interface ") ||
                   t.StartsWith("type ") || t.StartsWith("const ") && t.Contains("=>") ||
                   t.StartsWith("async function");
        });
    }

    private static IEnumerable<string> ExtractPythonSignatures(string[] lines)
    {
        return lines.Where(l =>
        {
            var t = l.TrimStart();
            return t.StartsWith("def ") || t.StartsWith("async def ") || t.StartsWith("class ");
        });
    }

    private static IEnumerable<string> ExtractRazorSignatures(string[] lines)
    {
        // Always include first 3 lines (usually @model / @using), then scan for directives
        var header = lines.Take(3);
        var directives = lines.Skip(3).Where(l =>
        {
            var t = l.TrimStart();
            return t.StartsWith("@model ") || t.StartsWith("@using ") ||
                   t.StartsWith("@inject ") || t.StartsWith("@page") ||
                   t.StartsWith("@section ");
        });
        return header.Concat(directives);
    }

    private static IEnumerable<string> ExtractSqlSignatures(string[] lines) =>
        lines.Where(l =>
        {
            var t = l.TrimStart();
            var upper = t.ToUpperInvariant();
            return upper.StartsWith("CREATE ") || upper.StartsWith("ALTER ") ||
                   upper.StartsWith("DROP ") || upper.StartsWith("SELECT ") ||
                   upper.StartsWith("FROM ") || upper.StartsWith("WHERE ") ||
                   upper.StartsWith("JOIN ") || t.StartsWith("--");
        });

    private static IEnumerable<string> ExtractGenericSignatures(string[] lines) =>
        lines.Take(30);

    // Lower number = indexed first when MaxFilesToIndex is hit
    private static int IndexPriority(string ext) => ext switch
    {
        ".sql"                              => 1,
        ".cs" or ".cshtml" or ".razor"
             or ".vb"                       => 2,
        ".ts" or ".tsx"                     => 3,
        ".py" or ".go" or ".java"
             or ".cpp" or ".h" or ".rs"
             or ".swift" or ".kt"           => 4,
        ".js" or ".jsx"                     => 5,
        _                                   => 6
    };

    [GeneratedRegex(@"^namespace\s+")]
    private static partial Regex CSharpNamespace();
    [GeneratedRegex(@"^(public|private|protected|internal|static|abstract|sealed).*\bclass\b")]
    private static partial Regex CSharpClass();
    [GeneratedRegex(@"^(public|private|protected|internal).*\binterface\b")]
    private static partial Regex CSharpInterface();
    [GeneratedRegex(@"^(public|private|protected|internal).*\benum\b")]
    private static partial Regex CSharpEnum();
    [GeneratedRegex(@"^(public|private|protected|internal|static|async|override|virtual).*\(.*\)")]
    private static partial Regex CSharpMethod();
    [GeneratedRegex(@"^(public|private|protected|internal).*\{.*get")]
    private static partial Regex CSharpProperty();
}
