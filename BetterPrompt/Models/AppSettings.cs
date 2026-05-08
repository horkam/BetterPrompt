namespace BetterPrompt.Models;

public class AppSettings
{
    public AppTheme Theme { get; set; } = AppTheme.Dark;
    public string OllamaUrl { get; set; } = "http://localhost:11434";
    public string OllamaModel { get; set; } = "llama3.2:3b";
    public bool UseOllama { get; set; } = true;
    public AiProvider ChatProvider { get; set; } = AiProvider.Ollama;
    public string AnthropicApiKey { get; set; } = string.Empty;
    public string OpenAiApiKey { get; set; } = string.Empty;
    public string GeminiApiKey { get; set; } = string.Empty;
    public string ClaudeModel { get; set; } = "claude-sonnet-4-6";
    public string OpenAiModel { get; set; } = "gpt-4o";
    public string GeminiModel { get; set; } = "gemini-2.0-flash";
    public double SimilarityThreshold { get; set; } = 0.65;
    public int MaxFilesToIndex { get; set; } = 150;
    public int MaxSignatureLinesPerFile { get; set; } = 40;
    public List<string> ExcludedDirectories { get; set; } =
    [
        ".git", ".vs", "bin", "obj", "node_modules", ".next",
        "dist", "out", "build", "packages", ".nuget",
        "lib", "vendor", "fonts", "webfonts", "aspnet_client",
        "Ace", "ace", "fontawesome", "locales", "esm", "umd",
        "Migrations"
    ];
    public List<string> CodeExtensions { get; set; } =
    [
        ".cs", ".ts", ".tsx", ".js", ".jsx", ".py", ".go",
        ".java", ".cpp", ".h", ".rs", ".swift", ".kt",
        ".cshtml", ".razor", ".sql", ".vb"
    ];
    public string LastCodebasePath { get; set; } = string.Empty;
}
