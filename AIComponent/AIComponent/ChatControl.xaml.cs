using System.Windows;
using System.Windows.Controls;
using AIComponent.ViewModels;

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
        if (e.ExtentHeightChange <= 0 ||
            sender is not ScrollViewer scroller || !ReferenceEquals(e.OriginalSource, scroller))
            return;

        var oldBottom = e.ExtentHeight - e.ExtentHeightChange - e.ViewportHeight + e.ViewportHeightChange;
        if (e.VerticalOffset >= oldBottom - 1)
            scroller.ScrollToEnd();
    }
}
