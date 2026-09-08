using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;

namespace Oucx.Reader;

public partial class MainWindow : Window
{
    private const int MaximumRecentFiles = 10;
    private readonly string _settingsPath;
    private string? _currentFile;
    private int _currentPage = 1;
    private bool _isFullScreen;
    private WindowState _stateBeforeFullScreen;
    private WindowStyle _styleBeforeFullScreen;
    private GridLength _sidebarWidthBeforeFullScreen;

    private const byte VkControl = 0x11;
    private const byte VkF = 0x46;
    private const uint KeyEventKeyUp = 0x0002;

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);

    public ObservableCollection<RecentFile> RecentFiles { get; } = [];

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _settingsPath = Path.Combine(appData, "OucxReader", "recent.json");
        LoadRecentFiles();

        Loaded += async (_, _) =>
        {
            try
            {
                await PdfWebView.EnsureCoreWebView2Async();
                PdfWebView.CoreWebView2.Settings.AreDevToolsEnabled = false;
                PdfWebView.CoreWebView2.Settings.IsStatusBarEnabled = false;
                PdfWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            }
            catch (Exception ex)
            {
                ShowError("The PDF engine could not start. Install the Microsoft Edge WebView2 Runtime and try again.", ex);
            }

            await CheckForUpdatesAsync();
        };
    }

    private async Task CheckForUpdatesAsync()
    {
        try
        {
            var updater = new UpdateService();
            var update = await updater.CheckAsync();
            if (update is null)
                return;

            var result = MessageBox.Show(this,
                $"Oucx Reader {update.Version} is available. Download and install it now?",
                "Update available", MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (result != MessageBoxResult.Yes)
                return;

            StatusText.Text = $"Downloading Oucx Reader {update.Version}…";
            await updater.DownloadAndInstallAsync(update);
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            // Update checks should never prevent the reader from starting.
            StatusText.Text = $"Could not check for updates: {ex.Message}";
        }
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open a PDF",
            Filter = "PDF documents (*.pdf)|*.pdf",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) == true)
            _ = OpenDocumentAsync(dialog.FileName);
    }

    private async Task OpenDocumentAsync(string path)
    {
        if (!File.Exists(path) || !string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(this, "Choose an existing PDF document.", "Unsupported file",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            LoadingPanel.Visibility = Visibility.Visible;
            WelcomePanel.Visibility = Visibility.Collapsed;
            await PdfWebView.EnsureCoreWebView2Async();

            _currentFile = Path.GetFullPath(path);
            _currentPage = 1;
            PageNumberBox.Text = "1";
            DocumentTitle.Text = Path.GetFileNameWithoutExtension(path);
            Title = $"{Path.GetFileName(path)} — Oucx Reader";
            StatusText.Text = _currentFile;
            PdfWebView.Source = new Uri(_currentFile);
            AddRecentFile(_currentFile);
        }
        catch (Exception ex)
        {
            LoadingPanel.Visibility = Visibility.Collapsed;
            ShowError("This document could not be opened.", ex);
        }
    }

    private void NavigateToPage(int page)
    {
        if (_currentFile is null)
            return;

        _currentPage = Math.Max(1, page);
        PageNumberBox.Text = _currentPage.ToString();
        var fileUri = new Uri(_currentFile).AbsoluteUri;
        PdfWebView.CoreWebView2?.Navigate($"{fileUri}#page={_currentPage}");
    }

    private void PreviousPage_Click(object sender, RoutedEventArgs e) => NavigateToPage(_currentPage - 1);
    private void NextPage_Click(object sender, RoutedEventArgs e) => NavigateToPage(_currentPage + 1);

    private void PageNumberBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        if (int.TryParse(PageNumberBox.Text, out var page) && page > 0)
            NavigateToPage(page);
        else
            PageNumberBox.Text = _currentPage.ToString();

        Keyboard.ClearFocus();
        e.Handled = true;
    }

    private void ZoomIn_Click(object sender, RoutedEventArgs e) => SetZoom(PdfWebView.ZoomFactor + 0.1);
    private void ZoomOut_Click(object sender, RoutedEventArgs e) => SetZoom(PdfWebView.ZoomFactor - 0.1);

    private void SetZoom(double factor)
    {
        PdfWebView.ZoomFactor = Math.Clamp(factor, 0.5, 3.0);
        ZoomText.Text = $"{PdfWebView.ZoomFactor:P0}";
    }

    private void Find_Click(object sender, RoutedEventArgs e) => SendFindShortcut();

    private void Print_Click(object sender, RoutedEventArgs e)
    {
        if (_currentFile is not null && PdfWebView.CoreWebView2 is not null)
            PdfWebView.CoreWebView2.ShowPrintUI(Microsoft.Web.WebView2.Core.CoreWebView2PrintDialogKind.Browser);
    }

    private void SendFindShortcut()
    {
        if (_currentFile is null)
            return;

        PdfWebView.Focus();
        keybd_event(VkControl, 0, 0, UIntPtr.Zero);
        keybd_event(VkF, 0, 0, UIntPtr.Zero);
        keybd_event(VkF, 0, KeyEventKeyUp, UIntPtr.Zero);
        keybd_event(VkControl, 0, KeyEventKeyUp, UIntPtr.Zero);
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.O && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            Open_Click(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.Add || (e.Key == Key.OemPlus && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)))
        {
            SetZoom(PdfWebView.ZoomFactor + 0.1);
            e.Handled = true;
        }
        else if (e.Key == Key.Subtract || (e.Key == Key.OemMinus && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)))
        {
            SetZoom(PdfWebView.ZoomFactor - 0.1);
            e.Handled = true;
        }
        else if (e.Key == Key.PageUp)
        {
            NavigateToPage(_currentPage - 1);
            e.Handled = true;
        }
        else if (e.Key == Key.PageDown)
        {
            NavigateToPage(_currentPage + 1);
            e.Handled = true;
        }
        else if (e.Key == Key.F11)
        {
            ToggleFullScreen();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && _isFullScreen)
        {
            ToggleFullScreen();
            e.Handled = true;
        }
    }

    private void FullScreen_Click(object sender, RoutedEventArgs e) => ToggleFullScreen();

    private void ToggleFullScreen()
    {
        if (!_isFullScreen)
        {
            _stateBeforeFullScreen = WindowState;
            _styleBeforeFullScreen = WindowStyle;
            _sidebarWidthBeforeFullScreen = SidebarColumn.Width;
            WindowStyle = WindowStyle.None;
            WindowState = WindowState.Maximized;
            TitleRow.Height = new GridLength(0);
            ToolbarRow.Height = new GridLength(0);
            StatusRow.Height = new GridLength(0);
            SidebarColumn.Width = new GridLength(0);
            _isFullScreen = true;
        }
        else
        {
            WindowStyle = _styleBeforeFullScreen;
            WindowState = _stateBeforeFullScreen;
            TitleRow.Height = new GridLength(46);
            ToolbarRow.Height = new GridLength(52);
            StatusRow.Height = new GridLength(28);
            SidebarColumn.Width = _sidebarWidthBeforeFullScreen;
            _isFullScreen = false;
        }
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = HasPdf(e.Data) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
        {
            var pdf = files.FirstOrDefault(file => string.Equals(Path.GetExtension(file), ".pdf", StringComparison.OrdinalIgnoreCase));
            if (pdf is not null)
                _ = OpenDocumentAsync(pdf);
        }
    }

    private static bool HasPdf(IDataObject data) =>
        data.GetData(DataFormats.FileDrop) is string[] files &&
        files.Any(file => string.Equals(Path.GetExtension(file), ".pdf", StringComparison.OrdinalIgnoreCase));

    private void PdfWebView_NavigationStarting(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2NavigationStartingEventArgs e)
    {
        if (_currentFile is not null)
            LoadingPanel.Visibility = Visibility.Visible;
    }

    private void PdfWebView_NavigationCompleted(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs e)
    {
        LoadingPanel.Visibility = Visibility.Collapsed;
        if (!e.IsSuccess)
            StatusText.Text = "The PDF engine could not display this document";
    }

    private void AddRecentFile(string path)
    {
        var existing = RecentFiles.FirstOrDefault(item => string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
            RecentFiles.Remove(existing);

        RecentFiles.Insert(0, RecentFile.FromPath(path));
        while (RecentFiles.Count > MaximumRecentFiles)
            RecentFiles.RemoveAt(RecentFiles.Count - 1);

        SaveRecentFiles();
    }

    private void LoadRecentFiles()
    {
        try
        {
            if (!File.Exists(_settingsPath))
                return;

            var paths = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(_settingsPath)) ?? [];
            foreach (var path in paths.Where(File.Exists).Take(MaximumRecentFiles))
                RecentFiles.Add(RecentFile.FromPath(path));
        }
        catch
        {
            // A corrupt recent-files list should never stop the reader from launching.
        }
    }

    private void SaveRecentFiles()
    {
        try
        {
            var directory = Path.GetDirectoryName(_settingsPath)!;
            Directory.CreateDirectory(directory);
            File.WriteAllText(_settingsPath, JsonSerializer.Serialize(RecentFiles.Select(item => item.Path)));
        }
        catch
        {
            StatusText.Text = "Document opened; recent-file history could not be saved";
        }
    }

    private void RecentFilesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RecentFilesList.SelectedItem is not RecentFile recent)
            return;

        RecentFilesList.SelectedItem = null;
        _ = OpenDocumentAsync(recent.Path);
    }

    private void RecentToggle_Click(object sender, RoutedEventArgs e)
    {
        SidebarColumn.Width = SidebarColumn.Width.Value == 0 ? new GridLength(250) : new GridLength(0);
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
            Maximize_Click(sender, e);
        else
            DragMove();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        MaximizeButton.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void ShowError(string message, Exception exception)
    {
        StatusText.Text = exception.Message;
        MessageBox.Show(this, $"{message}\n\n{exception.Message}", "Oucx Reader",
            MessageBoxButton.OK, MessageBoxImage.Error);
    }
}

public sealed record RecentFile(string Path, string Name, string Folder)
{
    public static RecentFile FromPath(string path) => new(
        path,
        System.IO.Path.GetFileNameWithoutExtension(path),
        System.IO.Path.GetDirectoryName(path) ?? string.Empty);
}
