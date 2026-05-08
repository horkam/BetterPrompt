using BetterPrompt.Models;
using BetterPrompt.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace BetterPrompt.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly CodebaseIndexer _indexer;
    private readonly SettingsService _settingsService;
    private readonly LearningStore _learningStore;
    private readonly OllamaOptimizer _ollamaOptimizer;
    private readonly UpdateService _updateService;

    private PromptOptimizerService? _optimizer;
    private CodebaseContext? _context;

    [ObservableProperty] private AppSettings _settings = null!;
    [ObservableProperty] private string _codebasePath = string.Empty;
    [ObservableProperty] private string _rawPrompt = string.Empty;
    [ObservableProperty] private string _optimizedPrompt = string.Empty;
    [ObservableProperty] private string _explanation = string.Empty;
    [ObservableProperty] private string _sourceLabel = string.Empty;
    [ObservableProperty] private string _statusMessage = "No codebase loaded.";
    [ObservableProperty] private string _ollamaStatus = "Ollama: checking...";
    [ObservableProperty] private bool _ollamaAvailable;
    [ObservableProperty] private bool _isIndexing;
    [ObservableProperty] private bool _isOptimizing;
    [ObservableProperty] private bool _hasOptimizedPrompt;
    [ObservableProperty] private bool _hasCodebase;
    [ObservableProperty] private bool _fromCache;

    public bool CanOptimize => HasCodebase && !IsOptimizing;
    [ObservableProperty] private int _learningEntryCount;
    [ObservableProperty] private string _ollamaPullCommand = string.Empty;
    [ObservableProperty] private int _statRulesApplied;
    [ObservableProperty] private bool _statOllamaUsed;
    [ObservableProperty] private bool _statCacheHit;
    [ObservableProperty] private double _statCacheScore;
    [ObservableProperty] private int _statOriginalLength;
    [ObservableProperty] private int _statOptimizedLength;
    [ObservableProperty] private int _statCharsRemoved;
    [ObservableProperty] private ObservableCollection<string> _changes = [];
    [ObservableProperty] private ObservableCollection<string> _fileTree = [];
    [ObservableProperty] private AppTheme _currentTheme;
    [ObservableProperty] private bool _updateAvailable;
    [ObservableProperty] private bool _updateChecked;
    [ObservableProperty] private string _latestVersion = string.Empty;
    [ObservableProperty] private string _updateStatusMessage = string.Empty;

    public string CurrentVersion => _updateService.CurrentVersion;
    public NewProjectViewModel NewProject { get; private set; } = null!;

    public bool IsThemeDark   { get => CurrentTheme == AppTheme.Dark;   set { if (value) CurrentTheme = AppTheme.Dark; } }
    public bool IsThemeLight  { get => CurrentTheme == AppTheme.Light;  set { if (value) CurrentTheme = AppTheme.Light; } }
    public bool IsThemeSystem { get => CurrentTheme == AppTheme.System; set { if (value) CurrentTheme = AppTheme.System; } }

    public static readonly List<OllamaModelOption> SuggestedModels =
    [
        new("llama3.2:3b",        "Llama 3.2 3B",         "Fast, well-rounded — recommended default"),
        new("llama3.2:1b",        "Llama 3.2 1B",         "Lightest option, fastest response"),
        new("llama3.1:8b",        "Llama 3.1 8B",         "More capable, needs ~6 GB RAM"),
        new("qwen2.5-coder:3b",   "Qwen 2.5 Coder 3B",   "Code-focused, great for prompt rewriting"),
        new("qwen2.5-coder:7b",   "Qwen 2.5 Coder 7B",   "Best code quality, needs ~6 GB RAM"),
        new("phi3:mini",          "Phi-3 Mini",            "Microsoft's compact model, fast"),
        new("phi3.5:mini",        "Phi-3.5 Mini",          "Newer Phi, slightly better quality"),
        new("mistral:7b",         "Mistral 7B",            "Strong general-purpose model"),
        new("codellama:7b",       "Code Llama 7B",         "Meta's code-specialized model"),
        new("deepseek-coder:6.7b","DeepSeek Coder 6.7B",  "Excellent for code tasks"),
    ];

    public MainViewModel()
    {
        _settingsService = new SettingsService();
        Settings = _settingsService.Load();
        _indexer = new CodebaseIndexer(Settings);
        _learningStore = new LearningStore();
        _ollamaOptimizer = new OllamaOptimizer(Settings);
        _updateService = new UpdateService();
        NewProject = new NewProjectViewModel(Settings, _ollamaOptimizer);
        UpdatePullCommand(Settings.OllamaModel);
        // Set backing field directly — theme was already applied by App.OnStartup
        _currentTheme = Settings.Theme;

        _ = CheckOllamaAsync();
        _ = CheckForUpdatesAsync();
        _ = AutoIndexLastAsync();
    }

    private async Task AutoIndexLastAsync()
    {
        var last = Settings.LastCodebasePath;
        if (string.IsNullOrWhiteSpace(last) || !Directory.Exists(last)) return;
        CodebasePath = last;
        await IndexCodebaseAsync();
    }

    partial void OnHasCodebaseChanged(bool value) => OnPropertyChanged(nameof(CanOptimize));
    partial void OnIsOptimizingChanged(bool value) => OnPropertyChanged(nameof(CanOptimize));

    partial void OnCurrentThemeChanged(AppTheme value)
    {
        ThemeService.Apply(value);
        Settings.Theme = value;
        _settingsService.Save(Settings);
        OnPropertyChanged(nameof(IsThemeDark));
        OnPropertyChanged(nameof(IsThemeLight));
        OnPropertyChanged(nameof(IsThemeSystem));
    }

    partial void OnCodebasePathChanged(string value)
    {
        if (Directory.Exists(value))
        {
            _learningStore.Load(value);
            LearningEntryCount = _learningStore.EntryCount;
        }
    }

    [RelayCommand]
    private async Task IndexCodebaseAsync()
    {
        if (!Directory.Exists(CodebasePath))
        {
            StatusMessage = "Path does not exist.";
            return;
        }

        IsIndexing = true;
        HasCodebase = false;
        FileTree.Clear();
        StatusMessage = "Indexing...";

        _learningStore.Load(CodebasePath);
        LearningEntryCount = _learningStore.EntryCount;

        _optimizer = new PromptOptimizerService(Settings, _learningStore);

        var progress = new Progress<string>(msg => StatusMessage = msg);

        try
        {
            _context = await _indexer.IndexAsync(CodebasePath, progress);
            foreach (var line in _context.FileTree.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                FileTree.Add(line);

            StatusMessage = $"Indexed {_context.IndexedFiles} code files ({_context.TotalFiles} total). {LearningEntryCount} learning entries loaded.";
            HasCodebase = true;
            Settings.LastCodebasePath = CodebasePath;
            _settingsService.Save(Settings);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Indexing failed: {ex.Message}";
        }
        finally
        {
            IsIndexing = false;
        }
    }

    [RelayCommand]
    private async Task OptimizePromptAsync()
    {
        if (string.IsNullOrWhiteSpace(RawPrompt))
        {
            StatusMessage = "Enter a prompt to optimize.";
            return;
        }

        if (_optimizer is null)
        {
            // Allow optimization without a codebase — create a store-less optimizer
            _learningStore.Load(Path.GetTempPath());
            _optimizer = new PromptOptimizerService(Settings, _learningStore);
        }

        IsOptimizing = true;
        HasOptimizedPrompt = false;
        FromCache = false;
        Changes.Clear();
        StatusMessage = "Optimizing...";

        var progress = new Progress<string>(msg => StatusMessage = msg);

        try
        {
            var result = await _optimizer.OptimizeAsync(RawPrompt, _context, progress);

            if (result.Success)
            {
                OptimizedPrompt = result.OptimizedPrompt;
                Explanation = result.Explanation;
                SourceLabel = result.SourceLabel;
                FromCache = result.Source == OptimizationSource.Cache;

                foreach (var c in result.Changes)
                    Changes.Add(c);

                // Stats
                StatCacheHit    = result.Source == OptimizationSource.Cache;
                StatCacheScore  = result.CacheMatchScore ?? 0;
                StatOllamaUsed  = result.Source is OptimizationSource.Ollama or OptimizationSource.RulesAndOllama;
                StatRulesApplied = result.Changes.Count(c => !c.StartsWith("Ollama"));
                StatOriginalLength  = RawPrompt.Length;
                StatOptimizedLength = result.OptimizedPrompt.Length;
                StatCharsRemoved    = Math.Max(0, RawPrompt.Length - result.OptimizedPrompt.Length);

                LearningEntryCount = _learningStore.EntryCount;
                HasOptimizedPrompt = true;
                StatusMessage = $"Done. Source: {result.SourceLabel}. {LearningEntryCount} entries in learning store.";
            }
            else
            {
                StatusMessage = result.ErrorMessage ?? "Optimization failed.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsOptimizing = false;
        }
    }

    [RelayCommand]
    private void CopyToClipboard()
    {
        if (!string.IsNullOrWhiteSpace(OptimizedPrompt))
        {
            Clipboard.SetText(OptimizedPrompt);
            StatusMessage = "Copied to clipboard.";
        }
    }

    [RelayCommand]
    private void CopyRules()
    {
        if (Changes.Count == 0) return;
        var text = $"Source: {SourceLabel}\n\nRules applied ({StatRulesApplied}):\n" +
                   string.Join("\n", Changes.Select(c => $"- {c}"));
        Clipboard.SetText(text);
        StatusMessage = $"Copied {Changes.Count} rules to clipboard.";
    }

    [RelayCommand]
    private void SaveSettings()
    {
        _settingsService.Save(Settings);
        _ = CheckOllamaAsync();
        StatusMessage = "Settings saved.";
    }

    [RelayCommand]
    private void BrowseCodebase()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select Codebase Folder"
        };
        if (dialog.ShowDialog() == true)
            CodebasePath = dialog.FolderName;
    }

    [RelayCommand]
    private async Task CheckOllamaAsync()
    {
        OllamaStatus = "Ollama: checking...";
        OllamaAvailable = await _ollamaOptimizer.IsAvailableAsync();
        OllamaStatus = OllamaAvailable
            ? $"Ollama: connected ({Settings.OllamaModel})"
            : "Ollama: not running";
    }

    [RelayCommand]
    private void CopyPullCommand()
    {
        if (!string.IsNullOrWhiteSpace(OllamaPullCommand))
        {
            Clipboard.SetText(OllamaPullCommand);
            StatusMessage = $"Copied: {OllamaPullCommand}";
        }
    }

    // Called when Settings.OllamaModel changes via the ComboBox binding
    public void OnModelSelected(string model)
    {
        Settings.OllamaModel = model;
        UpdatePullCommand(model);
    }

    private void UpdatePullCommand(string model)
        => OllamaPullCommand = $"ollama pull {model}";

    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        UpdateStatusMessage = "Checking for updates...";
        UpdateChecked = false;
        UpdateAvailable = false;

        var result = await _updateService.CheckAsync();
        UpdateChecked = true;

        if (!result.CheckSucceeded)
        {
            UpdateStatusMessage = "Could not reach GitHub — check your internet connection.";
            return;
        }

        if (result.UpdateAvailable && result.LatestVersion is not null)
        {
            LatestVersion = result.LatestVersion;
            UpdateAvailable = true;
            UpdateStatusMessage = $"Update available: v{result.LatestVersion}";
        }
        else
        {
            UpdateStatusMessage = $"You're up to date (v{CurrentVersion}).";
        }
    }

    [RelayCommand]
    private void OpenReleases()
        => Process.Start(new ProcessStartInfo(UpdateService.ReleasesUrl) { UseShellExecute = true });

    [RelayCommand]
    private void OpenGitHub()
        => Process.Start(new ProcessStartInfo(UpdateService.GitHubUrl) { UseShellExecute = true });
}

public record OllamaModelOption(string ModelId, string DisplayName, string Description);
