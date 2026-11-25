using System.Windows;
using System.Windows.Controls;
using AIComponent.ViewModels;
using Microsoft.Win32;

namespace AIComponent;

public partial class ChatControl : UserControl
{
    public ChatViewModel ViewModel { get; } = new();
    public ChatWorkspace Workspace { get => ViewModel.Workspace; set => ViewModel.Workspace = value; }
    public AiAssistant? Assistant { get => ViewModel.Assistant; set => ViewModel.Assistant = value; }
    public string? ConfigurationPath { get => ViewModel.ConfigurationPath; set => ViewModel.ConfigurationPath = value; }
    public Func<AiAssistant>? AssistantFactory { get => ViewModel.AssistantFactory; set => ViewModel.AssistantFactory = value; }
    public void LoadConfiguration() => ViewModel.LoadConfiguration();
    public ChatControl()
    {
        if (Application.Current is not null && AduSkin.AduSkinTheme.GetAduSkinTheme() is null)
            Application.Current.Resources.MergedDictionaries.Add(
                new AduSkin.AduSkinTheme { ThemeType = AduSkin.ThemeType.Light });
        InitializeComponent();
    }
    private void FollowStreamingReply(object sender, ScrollChangedEventArgs e)
    {
        if (!ViewModel.IsBusy || e.ExtentHeightChange <= 0 ||
            e.OriginalSource is not ScrollViewer scroller || scroller.TemplatedParent != sender)
            return;

        var oldBottom = e.ExtentHeight - e.ExtentHeightChange - e.ViewportHeight + e.ViewportHeightChange;
        if (e.VerticalOffset >= oldBottom - 1)
            scroller.ScrollToEnd();
    }

    private async void SelectTextFiles(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.IsIdle) return;
        var dialog = new OpenFileDialog
        {
            Title = "选择文本或 Markdown 文件",
            Filter = "文本和 Markdown|*.txt;*.text;*.md;*.markdown",
            Multiselect = true,
            CheckFileExists = true
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
            await ViewModel.AddAttachmentsAsync(dialog.FileNames);
    }

    private async void SelectDirectory(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.IsIdle) return;
        var dialog = new OpenFolderDialog { Title = "选择要阅读的目录（包含子目录中的文本文件）" };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
            await ViewModel.AddDirectoryAsync(dialog.FolderName);
    }

    private void RemoveTextAttachment(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsIdle && sender is FrameworkElement { DataContext: TextAttachment attachment })
            ViewModel.RemoveAttachment(attachment);
    }
}
