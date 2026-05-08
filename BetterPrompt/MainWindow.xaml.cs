using BetterPrompt.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace BetterPrompt;

public partial class MainWindow : Window
{
    public List<OllamaModelOption> SuggestedModels => MainViewModel.SuggestedModels;
    public List<string> ClaudeModels => MainViewModel.ClaudeModels;
    public List<string> OpenAiModels => MainViewModel.OpenAiModels;
    public List<string> GeminiModels => MainViewModel.GeminiModels;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        var current = SuggestedModels.FirstOrDefault(m => m.ModelId == vm.Settings.OllamaModel)
                      ?? SuggestedModels[0];
        ModelComboBox.SelectedItem = current;

        vm.NewProject.ChatHistory.CollectionChanged += (_, _) =>
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,
                () => ChatScrollViewer.ScrollToEnd());
    }

    private void ChatInputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
        {
            if (DataContext is MainViewModel vm)
                vm.NewProject.SendChatMessageCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void ModelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is MainViewModel vm && ModelComboBox.SelectedItem is OllamaModelOption opt)
            vm.OnModelSelected(opt.ModelId);
    }
}