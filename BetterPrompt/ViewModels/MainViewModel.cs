using BetterPrompt.Models;
using BetterPrompt.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;

namespace BetterPrompt.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly CodebaseIndexer _indexer;
    private readonly SettingsService _settingsService;
    private readonly LearningStore _learningStore;
    private readonly OllamaOptimizer _ollamaOptimizer;

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
    [ObservableProperty] private int _learningEntryCount;
    [ObservableProperty] private ObservableCollection<string> _changes = [];
    [ObservableProperty] private ObservableCollection<string> _fileTree = [];

    public MainViewModel()
    {
        _settingsService = new SettingsService();
        Settings = _settingsService.Load();
        _indexer = new CodebaseIndexer(Settings);
        _learningStore = new LearningStore();
        _ollamaOptimizer = new OllamaOptimizer(Settings);

        _ = CheckOllamaAsync();
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
}
