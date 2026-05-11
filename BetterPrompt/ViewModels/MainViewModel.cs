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
    private readonly ConPtyService _conPtyService = new();

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
    [ObservableProperty] private string _claudeStatus = "Claude: not tested";
    [ObservableProperty] private bool _claudeAvailable;
    [ObservableProperty] private string _openAiStatus = "OpenAI: not tested";
    [ObservableProperty] private bool _openAiAvailable;
    [ObservableProperty] private string _geminiStatus = "Gemini: not tested";
    [ObservableProperty] private bool _geminiAvailable;
    [ObservableProperty] private bool _isIndexing;
    [ObservableProperty] private bool _isOptimizing;
    [ObservableProperty] private bool _hasOptimizedPrompt;
    [ObservableProperty] private bool _hasCodebase;
    [ObservableProperty] private bool _fromCache;
    [ObservableProperty] private string _currentCachedEntryId = string.Empty;
    [ObservableProperty] private ObservableCollection<LearningEntry> _cacheEntries = [];

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
    [ObservableProperty] private bool _isTerminalRunning;

    // View subscribes to these to bridge ConPTY output into WebView2 / xterm.js
    public event Action<byte[]>? TerminalOutputReceived;
    public event Action? TerminalCleared;
    [ObservableProperty] private bool _updateAvailable;
    [ObservableProperty] private bool _updateChecked;
    [ObservableProperty] private string _latestVersion = string.Empty;
    [ObservableProperty] private string _updateStatusMessage = string.Empty;

    public string CurrentVersion => _updateService.CurrentVersion;
    public NewProjectViewModel NewProject { get; private set; } = null!;

    public bool IsThemeDark   { get => CurrentTheme == AppTheme.Dark;   set { if (value) CurrentTheme = AppTheme.Dark; } }
    public bool IsThemeLight  { get => CurrentTheme == AppTheme.Light;  set { if (value) CurrentTheme = AppTheme.Light; } }
    public bool IsThemeSystem { get => CurrentTheme == AppTheme.System; set { if (value) CurrentTheme = AppTheme.System; } }

    public static readonly List<string> ClaudeModels =
    [
        "claude-sonnet-4-6",
        "claude-opus-4-7",
        "claude-haiku-4-5",
    ];

    public static readonly List<string> OpenAiModels =
    [
        "gpt-4o",
        "gpt-4o-mini",
    ];

    public static readonly List<string> GeminiModels =
    [
        "gemini-2.0-flash",
        "gemini-1.5-pro",
    ];

    public bool ProviderIsOllama
    {
        get => Settings.ChatProvider == AiProvider.Ollama;
        set { if (value) { Settings.ChatProvider = AiProvider.Ollama; NotifyProviderChanged(); } }
    }
    public bool ProviderIsClaude
    {
        get => Settings.ChatProvider == AiProvider.Claude;
        set { if (value) { Settings.ChatProvider = AiProvider.Claude; NotifyProviderChanged(); } }
    }
    public bool ProviderIsOpenAI
    {
        get => Settings.ChatProvider == AiProvider.OpenAI;
        set { if (value) { Settings.ChatProvider = AiProvider.OpenAI; NotifyProviderChanged(); } }
    }
    public bool ProviderIsGemini
    {
        get => Settings.ChatProvider == AiProvider.Gemini;
        set { if (value) { Settings.ChatProvider = AiProvider.Gemini; NotifyProviderChanged(); } }
    }

    public string ActiveProviderLabel => Settings.ChatProvider switch
    {
        AiProvider.Claude  => "Claude",
        AiProvider.OpenAI  => "OpenAI",
        AiProvider.Gemini  => "Gemini",
        _                  => "Ollama"
    };

    public bool ActiveProviderAvailable => Settings.ChatProvider switch
    {
        AiProvider.Claude  => ClaudeAvailable,
        AiProvider.OpenAI  => OpenAiAvailable,
        AiProvider.Gemini  => GeminiAvailable,
        _                  => OllamaAvailable
    };

    private void NotifyProviderChanged()
    {
        OnPropertyChanged(nameof(ProviderIsOllama));
        OnPropertyChanged(nameof(ProviderIsClaude));
        OnPropertyChanged(nameof(ProviderIsOpenAI));
        OnPropertyChanged(nameof(ProviderIsGemini));
        OnPropertyChanged(nameof(ActiveProviderLabel));
        OnPropertyChanged(nameof(ActiveProviderAvailable));
    }

    partial void OnOllamaAvailableChanged(bool value) => OnPropertyChanged(nameof(ActiveProviderAvailable));
    partial void OnClaudeAvailableChanged(bool value) => OnPropertyChanged(nameof(ActiveProviderAvailable));
    partial void OnOpenAiAvailableChanged(bool value) => OnPropertyChanged(nameof(ActiveProviderAvailable));
    partial void OnGeminiAvailableChanged(bool value) => OnPropertyChanged(nameof(ActiveProviderAvailable));

    private IAiChatService GetCurrentChatService() => Settings.ChatProvider switch
    {
        AiProvider.Claude => new ClaudeChatService(Settings),
        AiProvider.OpenAI => new OpenAiChatService(Settings),
        AiProvider.Gemini => new GeminiChatService(Settings),
        _ => _ollamaOptimizer
    };

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
        NewProject = new NewProjectViewModel(Settings, GetCurrentChatService);
        UpdatePullCommand(Settings.OllamaModel);
        // Set backing field directly — theme was already applied by App.OnStartup
        _currentTheme = Settings.Theme;

        _conPtyService.OutputReceived += data => TerminalOutputReceived?.Invoke(data);
        _conPtyService.ProcessExited  += () =>
            Application.Current.Dispatcher.Invoke(() => IsTerminalRunning = false);

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
            RefreshCacheEntries();
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

        if (_learningStore.EntryCount > 0)
        {
            var answer = MessageBox.Show(
                $"This codebase has {_learningStore.EntryCount} cached learning entries.\n\nClearing the cache is recommended after re-indexing so stale results don't appear.\n\nClear the cache now?",
                "Learning Cache", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer == MessageBoxResult.Yes)
            {
                _learningStore.Clear();
                LearningEntryCount = 0;
            }
        }

        _optimizer = new PromptOptimizerService(Settings, _learningStore);

        var progress = new Progress<string>(msg => StatusMessage = msg);

        try
        {
            _context = await _indexer.IndexAsync(CodebasePath, progress);
            foreach (var line in _context.FileTree.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                FileTree.Add(line);

            StatusMessage = $"Indexed {_context.IndexedFiles} code files ({_context.TotalFiles} total). {LearningEntryCount} learning entries loaded.";
            RefreshCacheEntries();
            HasCodebase = true;
            Settings.LastCodebasePath = CodebasePath;
            _settingsService.Save(Settings);
            StartTerminalInDirectory(CodebasePath);
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
                CurrentCachedEntryId = result.CachedEntryId ?? string.Empty;

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
                RefreshCacheEntries();
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
    private void UseOptimizedPromptInChat()
    {
        if (string.IsNullOrWhiteSpace(OptimizedPrompt)) return;
        if (!_conPtyService.IsRunning)
        {
            StatusMessage = "Terminal is not running. Index a codebase first.";
            return;
        }
        _conPtyService.Write(OptimizedPrompt);
    }

    [RelayCommand]
    private void SaveSettings()
    {
        _settingsService.Save(Settings);
        _ = CheckOllamaAsync();
        NotifyProviderChanged();
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
    private async Task TestClaudeAsync()
    {
        ClaudeStatus = "Claude: checking...";
        var (success, message) = await new ClaudeChatService(Settings).TestConnectionAsync();
        ClaudeAvailable = success;
        ClaudeStatus = message;
    }

    [RelayCommand]
    private async Task TestOpenAiAsync()
    {
        OpenAiStatus = "OpenAI: checking...";
        var (success, message) = await new OpenAiChatService(Settings).TestConnectionAsync();
        OpenAiAvailable = success;
        OpenAiStatus = message;
    }

    [RelayCommand]
    private async Task TestGeminiAsync()
    {
        GeminiStatus = "Gemini: checking...";
        var (success, message) = await new GeminiChatService(Settings).TestConnectionAsync();
        GeminiAvailable = success;
        GeminiStatus = message;
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

    [RelayCommand]
    private void RemoveCachedEntry()
    {
        if (string.IsNullOrEmpty(CurrentCachedEntryId)) return;
        _learningStore.Delete(CurrentCachedEntryId);
        CurrentCachedEntryId = string.Empty;
        FromCache = false;
        LearningEntryCount = _learningStore.EntryCount;
        RefreshCacheEntries();
        StatusMessage = "Cache entry removed.";
    }

    [RelayCommand]
    private void DeleteCacheEntry(string id)
    {
        _learningStore.Delete(id);
        LearningEntryCount = _learningStore.EntryCount;
        RefreshCacheEntries();
        if (CurrentCachedEntryId == id)
            CurrentCachedEntryId = string.Empty;
    }

    [RelayCommand]
    private void ClearAllCache()
    {
        if (_learningStore.EntryCount == 0) return;
        var answer = MessageBox.Show(
            $"Delete all {_learningStore.EntryCount} learning entries for this codebase?\n\nThis cannot be undone.",
            "Clear Cache", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;
        _learningStore.Clear();
        LearningEntryCount = 0;
        CurrentCachedEntryId = string.Empty;
        RefreshCacheEntries();
        StatusMessage = "Learning cache cleared.";
    }

    private void StartTerminalInDirectory(string directory)
    {
        try
        {
            TerminalCleared?.Invoke();
            _conPtyService.Start(directory);
            IsTerminalRunning = true;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Terminal failed to start: {ex.Message}";
            IsTerminalRunning = false;
        }
    }

    // Called from View when xterm.js sends keystrokes (raw UTF-8 bytes, base64-decoded)
    public void SendRawInput(byte[] data) => _conPtyService.Write(data);

    // Called from View when xterm.js reports a resize
    public void ResizeTerminal(int cols, int rows) => _conPtyService.Resize(cols, rows);

    [RelayCommand]
    private void RestartTerminal()
    {
        if (string.IsNullOrWhiteSpace(CodebasePath) || !Directory.Exists(CodebasePath))
        {
            StatusMessage = "No codebase directory to start terminal in.";
            return;
        }
        StartTerminalInDirectory(CodebasePath);
    }

    [RelayCommand]
    private void SendPromptToTerminal()
    {
        if (string.IsNullOrWhiteSpace(OptimizedPrompt)) return;
        if (!_conPtyService.IsRunning)
        {
            StatusMessage = "Terminal is not running. Index a codebase first.";
            return;
        }

        // Write to a temp file to avoid shell escaping, then type the claude command + Enter.
        // With ConPTY the terminal is a real TTY, so claude runs interactively.
        var tempFile = Path.Combine(Path.GetTempPath(), "betterprompt_prompt.txt");
        File.WriteAllText(tempFile, OptimizedPrompt, System.Text.Encoding.UTF8);
        var escaped = tempFile.Replace("'", "''");
        _conPtyService.Write($"claude (Get-Content -Raw '{escaped}')\r");
    }

    [RelayCommand]
    private void OpenExternalTerminal()
    {
        var dir = CodebasePath;
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
            dir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var wtPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            @"Microsoft\WindowsApps\wt.exe");

        if (File.Exists(wtPath))
            Process.Start(new ProcessStartInfo { FileName = wtPath, Arguments = $"-d \"{dir}\"", UseShellExecute = true });
        else
            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoExit -Command \"Set-Location '{dir.Replace("'", "''")}' \"",
                UseShellExecute = true,
            });
    }

    private void RefreshCacheEntries()
    {
        CacheEntries.Clear();
        foreach (var entry in _learningStore.Entries.OrderByDescending(e => e.CreatedAt))
            CacheEntries.Add(entry);
    }
}

public record OllamaModelOption(string ModelId, string DisplayName, string Description);
