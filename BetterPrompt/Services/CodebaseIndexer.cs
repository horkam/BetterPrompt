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

        return context;
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
            ".cs" => ExtractCSharpSignatures(lines),
            ".ts" or ".tsx" or ".js" or ".jsx" => ExtractJsSignatures(lines),
            ".py" => ExtractPythonSignatures(lines),
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

    private static IEnumerable<string> ExtractGenericSignatures(string[] lines) =>
        lines.Take(30);

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
