using BetterPrompt.Models;
using BetterPrompt.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;

namespace BetterPrompt.ViewModels;

public partial class NewProjectViewModel : ObservableObject
{
    private readonly OllamaOptimizer _ollama;

    // Form fields
    [ObservableProperty] private string _projectName = string.Empty;
    [ObservableProperty] private string _description = string.Empty;
    [ObservableProperty] private string _goal = string.Empty;
    [ObservableProperty] private string _targetAudience = string.Empty;
    [ObservableProperty] private string _platform = "Windows Desktop";
    [ObservableProperty] private string _language = string.Empty;
    [ObservableProperty] private string _database = string.Empty;
    [ObservableProperty] private string _coreFeatures = string.Empty;
    [ObservableProperty] private string _outOfScope = string.Empty;
    [ObservableProperty] private string _nonFunctionalRequirements = string.Empty;
    [ObservableProperty] private string _timeline = string.Empty;
    [ObservableProperty] private string _similarApps = string.Empty;
    [ObservableProperty] private string _projectPath = string.Empty;

    // Generate status
    [ObservableProperty] private string _generateStatus = string.Empty;
    [ObservableProperty] private bool _generateSuccess;
    [ObservableProperty] private bool _hasGenerateStatus;

    // Chat
    [ObservableProperty] private string _chatInput = string.Empty;
    [ObservableProperty] private bool _isChatting;
    public ObservableCollection<ProjectChatMessage> ChatHistory { get; } = [];

    public static readonly List<string> PlatformOptions =
    [
        "Windows Desktop",
        "Web App",
        "Mobile App",
        "Cross-platform Desktop",
        "CLI Tool",
        "REST API / Backend",
        "Library / Package",
    ];

    private const string ScopingSystemPrompt =
        "You are a helpful assistant guiding a developer through scoping a new software project. " +
        "Help them think through goals, target audience, features, technical choices, and constraints. " +
        "Keep responses concise and practical — 2–4 sentences unless the user asks for more detail. " +
        "Ask a clarifying question when it would help them think more clearly. " +
        "Do not generate code.";

    public NewProjectViewModel(AppSettings settings, OllamaOptimizer ollama)
    {
        _ollama = ollama;
    }

    // ── Browse / load / generate ──────────────────────────────────────────────

    [RelayCommand]
    private void BrowseProjectPath()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Select Project Folder" };
        if (dialog.ShowDialog() == true)
            ProjectPath = dialog.FolderName;
    }

    [RelayCommand]
    private void LoadProject()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Select Existing Project Folder" };
        if (dialog.ShowDialog() != true) return;

        var scopePath = Path.Combine(dialog.FolderName, "project-scope.json");
        if (!File.Exists(scopePath))
        {
            MessageBox.Show("No project-scope.json found in that folder.", "BetterPrompt",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var json = File.ReadAllText(scopePath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            ProjectPath   = dialog.FolderName;
            ProjectName   = GetString(root, "projectName");
            Description   = GetString(root, "description");
            Goal          = GetString(root, "goal");
            TargetAudience = GetString(root, "targetAudience");
            NonFunctionalRequirements = GetString(root, "nonFunctionalRequirements");
            Timeline      = GetString(root, "timeline");
            SimilarApps   = GetString(root, "similarApps");

            if (root.TryGetProperty("technical", out var tech))
            {
                Platform = GetString(tech, "platform", Platform);
                Language = GetString(tech, "language");
                Database = GetString(tech, "database");
            }

            CoreFeatures = GetBulletList(root, "coreFeatures");
            OutOfScope   = GetBulletList(root, "outOfScope");

            HasGenerateStatus = false;
            GenerateStatus = string.Empty;
            GenerateSuccess = false;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not load project-scope.json: {ex.Message}", "BetterPrompt",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void GenerateProject()
    {
        HasGenerateStatus = true;

        if (string.IsNullOrWhiteSpace(ProjectName))
        {
            GenerateStatus = "Project name is required.";
            GenerateSuccess = false;
            return;
        }
        if (string.IsNullOrWhiteSpace(ProjectPath))
        {
            GenerateStatus = "Project directory is required.";
            GenerateSuccess = false;
            return;
        }

        try
        {
            Directory.CreateDirectory(ProjectPath);
            File.WriteAllText(Path.Combine(ProjectPath, "CLAUDE.md"), BuildClaudeMd());
            File.WriteAllText(Path.Combine(ProjectPath, "project-scope.json"), BuildScopeJson());
            GenerateSuccess = true;
            GenerateStatus = $"Created CLAUDE.md and project-scope.json in {ProjectPath}";
        }
        catch (Exception ex)
        {
            GenerateSuccess = false;
            GenerateStatus = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private void OpenProjectFolder()
    {
        if (Directory.Exists(ProjectPath))
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(ProjectPath) { UseShellExecute = true });
    }

    // ── Chat ──────────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task SendChatMessageAsync()
    {
        var input = ChatInput.Trim();
        if (string.IsNullOrWhiteSpace(input) || IsChatting) return;

        ChatHistory.Add(new ProjectChatMessage("user", input));
        ChatInput = string.Empty;
        IsChatting = true;

        try
        {
            var history = ChatHistory.Where(m => m.Role != "error")
                                     .Select(m => (m.Role, m.Content));
            var (result, error) = await _ollama.ChatAsync(ScopingSystemPrompt, history);

            ChatHistory.Add(error is not null
                ? new ProjectChatMessage("error", error)
                : new ProjectChatMessage("assistant", result!));
        }
        catch (Exception ex)
        {
            ChatHistory.Add(new ProjectChatMessage("error", $"Unexpected error: {ex.Message}"));
        }
        finally
        {
            IsChatting = false;
        }
    }

    [RelayCommand]
    private void CopyChatMessage(string content)
    {
        if (!string.IsNullOrWhiteSpace(content))
            Clipboard.SetText(content);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private string BuildClaudeMd()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {ProjectName}");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(Description))
        {
            sb.AppendLine($"> {Description}");
            sb.AppendLine();
        }

        AppendSection(sb, "Goal", Goal);
        AppendSection(sb, "Target Audience", TargetAudience);

        sb.AppendLine("## Technical Stack");
        sb.AppendLine($"- **Platform:** {Platform}");
        if (!string.IsNullOrWhiteSpace(Language))
            sb.AppendLine($"- **Language / Framework:** {Language}");
        if (!string.IsNullOrWhiteSpace(Database))
            sb.AppendLine($"- **Database / Storage:** {Database}");
        sb.AppendLine();

        AppendBulletSection(sb, "Core Features", CoreFeatures);
        AppendBulletSection(sb, "Out of Scope", OutOfScope);
        AppendSection(sb, "Non-Functional Requirements", NonFunctionalRequirements);
        AppendSection(sb, "Timeline", Timeline);
        AppendSection(sb, "Reference / Inspiration", SimilarApps);

        sb.AppendLine("---");
        sb.AppendLine($"*Generated by BetterPrompt on {DateTime.Now:yyyy-MM-dd}*");
        return sb.ToString();
    }

    private string BuildScopeJson()
    {
        static string[] ToList(string raw) =>
            raw.Split('\n', StringSplitOptions.RemoveEmptyEntries)
               .Select(l => l.Trim().TrimStart('-').Trim())
               .Where(l => !string.IsNullOrWhiteSpace(l))
               .ToArray();

        var scope = new
        {
            projectName = ProjectName,
            description = Description,
            goal = Goal,
            targetAudience = TargetAudience,
            technical = new { platform = Platform, language = Language, database = Database },
            coreFeatures = ToList(CoreFeatures),
            outOfScope = ToList(OutOfScope),
            nonFunctionalRequirements = NonFunctionalRequirements,
            timeline = Timeline,
            similarApps = SimilarApps,
            generatedAt = DateTime.Now.ToString("yyyy-MM-dd"),
        };

        return JsonSerializer.Serialize(scope, new JsonSerializerOptions { WriteIndented = true });
    }

    private static void AppendSection(StringBuilder sb, string heading, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        sb.AppendLine($"## {heading}");
        sb.AppendLine(value.Trim());
        sb.AppendLine();
    }

    private static void AppendBulletSection(StringBuilder sb, string heading, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        sb.AppendLine($"## {heading}");
        foreach (var line in value.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var item = line.Trim().TrimStart('-').Trim();
            if (!string.IsNullOrWhiteSpace(item))
                sb.AppendLine($"- {item}");
        }
        sb.AppendLine();
    }

    private static string GetString(JsonElement el, string key, string fallback = "")
        => el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? fallback
            : fallback;

    private static string GetBulletList(JsonElement el, string key)
    {
        if (!el.TryGetProperty(key, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return string.Empty;
        return string.Join('\n', arr.EnumerateArray()
            .Where(e => e.ValueKind == JsonValueKind.String)
            .Select(e => e.GetString() ?? string.Empty)
            .Where(s => !string.IsNullOrWhiteSpace(s)));
    }
}

public record ProjectChatMessage(string Role, string Content);
