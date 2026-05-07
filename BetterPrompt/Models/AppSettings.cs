namespace BetterPrompt.Models;

public class AppSettings
{
    public string OllamaUrl { get; set; } = "http://localhost:11434";
    public string OllamaModel { get; set; } = "llama3.2:3b";
    public bool UseOllama { get; set; } = true;
    public double SimilarityThreshold { get; set; } = 0.65;
    public int MaxFilesToIndex { get; set; } = 150;
    public int MaxSignatureLinesPerFile { get; set; } = 40;
    public List<string> ExcludedDirectories { get; set; } =
    [
        ".git", ".vs", "bin", "obj", "node_modules", ".next",
        "dist", "out", "build", "packages", ".nuget"
    ];
    public List<string> CodeExtensions { get; set; } =
    [
        ".cs", ".ts", ".tsx", ".js", ".jsx", ".py", ".go",
        ".java", ".cpp", ".h", ".rs", ".swift", ".kt"
    ];
}
