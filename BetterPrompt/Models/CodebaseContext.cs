namespace BetterPrompt.Models;

public class CodebaseContext
{
    public string RootPath { get; set; } = string.Empty;
    public string FileTree { get; set; } = string.Empty;
    public string ClaudeMdContent { get; set; } = string.Empty;
    public List<FileSignature> FileSignatures { get; set; } = [];
    public int TotalFiles { get; set; }
    public int IndexedFiles { get; set; }

    public string ToContextBlock()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"## Codebase: {RootPath}");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(ClaudeMdContent))
        {
            sb.AppendLine("### CLAUDE.md");
            sb.AppendLine(ClaudeMdContent);
            sb.AppendLine();
        }

        sb.AppendLine("### File Tree");
        sb.AppendLine(FileTree);
        sb.AppendLine();

        if (FileSignatures.Count > 0)
        {
            sb.AppendLine("### Key File Signatures");
            foreach (var sig in FileSignatures)
            {
                sb.AppendLine($"**{sig.RelativePath}**");
                sb.AppendLine(sig.Signatures);
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }
}

public class FileSignature
{
    public string RelativePath { get; set; } = string.Empty;
    public string Signatures { get; set; } = string.Empty;
}
