using BetterPrompt.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace BetterPrompt;

public partial class MainWindow : Window
{
    public List<OllamaModelOption> SuggestedModels => MainViewModel.SuggestedModels;

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
    }

    private void ModelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is MainViewModel vm && ModelComboBox.SelectedItem is OllamaModelOption opt)
            vm.OnModelSelected(opt.ModelId);
    }
}