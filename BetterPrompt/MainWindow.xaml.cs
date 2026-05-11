using BetterPrompt.ViewModels;
using Microsoft.Web.WebView2.Core;
using System.Text;
using System.Text.Json;
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

    private bool _webViewReady;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        // ── Existing chat scroll ──────────────────────────────────────────────
        var current = SuggestedModels.FirstOrDefault(m => m.ModelId == vm.Settings.OllamaModel)
                      ?? SuggestedModels[0];
        ModelComboBox.SelectedItem = current;

        vm.NewProject.ChatHistory.CollectionChanged += (_, _) =>
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,
                () => ChatScrollViewer.ScrollToEnd());

        // ── WebView2 / ConPTY bridge ──────────────────────────────────────────
        try
        {
            await TerminalWebView.EnsureCoreWebView2Async();

            TerminalWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            TerminalWebView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            TerminalWebView.CoreWebView2.WebMessageReceived += OnTerminalWebMessage;
            TerminalWebView.CoreWebView2.NavigationCompleted += (_, args) =>
            {
                if (!args.IsSuccess)
                    LogWebView2Error($"NavigationCompleted failed: WebErrorStatus={args.WebErrorStatus}");
            };

            TerminalWebView.CoreWebView2.NavigateToString(TerminalPageHtml);
        }
        catch (Exception ex)
        {
            LogWebView2Error($"WebView2 init failed: {ex}");
            MessageBox.Show(
                $"Terminal WebView2 failed to initialize:\n\n{ex.Message}\n\nThe terminal panel will be unavailable.",
                "WebView2 Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Forward ConPTY output → xterm.js
        vm.TerminalOutputReceived += data =>
        {
            if (!_webViewReady) return;
            var b64 = Convert.ToBase64String(data);
            var json = $"{{\"type\":\"output\",\"data\":\"{b64}\"}}";
            Dispatcher.BeginInvoke(() => PostToTerminal(json));
        };

        // Clear xterm.js when terminal restarts
        vm.TerminalCleared += () =>
            Dispatcher.BeginInvoke(() =>
            {
                if (_webViewReady) PostToTerminal("{\"type\":\"clear\"}");
            });
    }

    private static void LogWebView2Error(string message)
    {
        try
        {
            var logPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "BetterPrompt_crash.log");
            System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss}] {message}\n\n");
        }
        catch { }
    }

    private void OnTerminalWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        using var doc = JsonDocument.Parse(e.WebMessageAsJson);
        var root = doc.RootElement;
        if (!root.TryGetProperty("type", out var typeProp)) return;

        switch (typeProp.GetString())
        {
            case "ready":
                _webViewReady = true;
                break;

            case "input":
                // xterm.js sends base64-encoded UTF-8 bytes for every keystroke
                if (root.TryGetProperty("data", out var dataProp))
                {
                    var raw = Convert.FromBase64String(dataProp.GetString()!);
                    vm.SendRawInput(raw);
                }
                break;

            case "resize":
                if (root.TryGetProperty("cols", out var cols) &&
                    root.TryGetProperty("rows", out var rows))
                    vm.ResizeTerminal(cols.GetInt32(), rows.GetInt32());
                break;
        }
    }

    private void PostToTerminal(string json)
        => TerminalWebView.CoreWebView2.PostWebMessageAsString(json);

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

    // ── xterm.js terminal HTML ────────────────────────────────────────────────

    private const string TerminalPageHtml = """
        <!DOCTYPE html>
        <html>
        <head>
        <meta charset="utf-8">
        <style>
          * { margin: 0; padding: 0; box-sizing: border-box; }
          html, body { width: 100%; height: 100%; background: #1e1e1e; overflow: hidden; }
          #terminal { width: 100%; height: 100%; padding: 4px; }
          .xterm-viewport::-webkit-scrollbar { width: 6px; }
          .xterm-viewport::-webkit-scrollbar-track { background: #1e1e1e; }
          .xterm-viewport::-webkit-scrollbar-thumb { background: #424242; border-radius: 3px; }
        </style>
        <link rel="stylesheet" href="https://cdn.jsdelivr.net/npm/xterm@5.3.0/css/xterm.css"/>
        </head>
        <body>
        <div id="terminal"></div>
        <script src="https://cdn.jsdelivr.net/npm/xterm@5.3.0/lib/xterm.js"></script>
        <script src="https://cdn.jsdelivr.net/npm/xterm-addon-fit@0.8.0/lib/xterm-addon-fit.js"></script>
        <script>
        const term = new Terminal({
          fontFamily: 'Cascadia Code, Consolas, "Courier New", monospace',
          fontSize: 13,
          lineHeight: 1.2,
          cursorBlink: true,
          scrollback: 5000,
          allowProposedApi: true,
          theme: {
            background:    '#1e1e1e', foreground:    '#cccccc', cursor:        '#aeafad',
            selectionBackground: 'rgba(255,255,255,0.25)',
            black:         '#1e1e1e', red:           '#f44747', green:         '#6a9955',
            yellow:        '#dcdcaa', blue:          '#569cd6', magenta:       '#c586c0',
            cyan:          '#4ec9b0', white:         '#d4d4d4',
            brightBlack:   '#808080', brightRed:     '#f44747', brightGreen:   '#6a9955',
            brightYellow:  '#dcdcaa', brightBlue:    '#569cd6', brightMagenta: '#c586c0',
            brightCyan:    '#4ec9b0', brightWhite:   '#ffffff',
          }
        });

        const fitAddon = new FitAddon.FitAddon();
        term.loadAddon(fitAddon);
        term.open(document.getElementById('terminal'));

        function sendResize() {
          fitAddon.fit();
          window.chrome.webview.postMessage({ type: 'resize', cols: term.cols, rows: term.rows });
        }

        // Notify C# when xterm.js is ready
        window.addEventListener('load', () => {
          setTimeout(() => {
            sendResize();
            window.chrome.webview.postMessage({ type: 'ready' });
          }, 150);
        });

        // Re-fit whenever the container is resized (splitter drag, window resize)
        new ResizeObserver(sendResize).observe(document.getElementById('terminal'));

        // Forward keystrokes to C# as base64-encoded UTF-8 bytes
        term.onData(data => {
          const bytes = new TextEncoder().encode(data);
          let bin = '';
          for (let i = 0; i < bytes.length; i++) bin += String.fromCharCode(bytes[i]);
          window.chrome.webview.postMessage({ type: 'input', data: btoa(bin) });
        });

        // Receive messages from C#
        window.chrome.webview.addEventListener('message', e => {
          const msg = JSON.parse(e.data);
          if (msg.type === 'output') {
            term.write(Uint8Array.from(atob(msg.data), c => c.charCodeAt(0)));
          } else if (msg.type === 'clear') {
            term.reset();
            sendResize();
          }
        });
        </script>
        </body>
        </html>
        """;
}
