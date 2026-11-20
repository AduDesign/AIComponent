using AduSkin.Controls;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace AIComponent;

public partial class ChatControl : UserControl
{
    private CancellationTokenSource? cancellation;
    private AiAssistant? assistant;
    private ChatWorkspace workspace = new();
    private Guid? editingId;
    private bool loading;
    public string? ConfigurationPath { get; set; }
    public ChatWorkspace Workspace
    {
        get => workspace;
        set
        {
            if (cancellation is not null) throw new InvalidOperationException("请先停止当前对话。");
            workspace = value ?? throw new ArgumentNullException(nameof(value));
            AiSelector.ItemsSource = workspace.Profiles;
            ProfileSelector.ItemsSource = workspace.Profiles;
            AiSelector.SelectedIndex = workspace.Profiles.Count > 0 ? 0 : -1;
            if (workspace.Profiles.Count == 0) Assistant = null;
        }
    }
    public AiAssistant? Assistant
    {
        get => assistant;
        set
        {
            if (cancellation is not null) throw new InvalidOperationException("请先停止当前对话。");
            assistant = value;
            Messages.Items.Clear();
            if (value is not null)
                foreach (var message in value.History.Where(m => m.Role is "user" or "assistant"))
                    if (!string.IsNullOrEmpty(message.Content)) AddMessage(message.Content, message.Role == "user");
            Welcome.Visibility = Messages.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
    }
    public Func<AiAssistant>? AssistantFactory { get; set; }
    public ChatControl()
    {
        // AduSkin animations resolve through Application resources; initialize once before loading controls.
        if (Application.Current is not null && AduSkin.AduSkinTheme.GetAduSkinTheme() is null)
            Application.Current.Resources.MergedDictionaries.Add(
                new AduSkin.AduSkinTheme { ThemeType = AduSkin.ThemeType.Light });
        InitializeComponent();
        ProviderSelector.ItemsSource = ProviderPreset.All;
        Workspace = workspace;
        AddProfile(this, new RoutedEventArgs());
    }
    public void LoadConfiguration()
    {
        if (ConfigurationPath is null) return;
        var profiles = ProfileStorage.Load(ConfigurationPath);
        if (cancellation is not null) throw new InvalidOperationException("请先停止当前对话。");
        workspace.Profiles.Clear();
        foreach (var profile in profiles) workspace.Profiles.Add(profile);
        AiSelector.SelectedIndex = workspace.Profiles.Count > 0 ? 0 : -1;
    }
    private void Persist()
    {
        if (ConfigurationPath is not null) ProfileStorage.Save(ConfigurationPath, workspace.Profiles);
    }
    private void ToggleSettings(object sender, RoutedEventArgs e)
    {
        var open = SettingsPage.Visibility != Visibility.Visible;
        SettingsPage.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        ChatPage.Visibility = open ? Visibility.Collapsed : Visibility.Visible;
        SettingsButton.Background = open ? new SolidColorBrush(Color.FromRgb(255, 240, 238)) : Brushes.White;
        if (open && AiSelector.SelectedItem is AiProfile profile) ProfileSelector.SelectedItem = profile;
    }
    private void SelectAi(object sender, SelectionChangedEventArgs e)
    {
        if (loading || cancellation is not null) return;
        Assistant = AiSelector.SelectedItem is AiProfile profile ? workspace.GetAssistant(profile) : null;
        Status.Text = "";
    }
    private void EditProfile(object sender, SelectionChangedEventArgs e)
    {
        if (ProfileSelector.SelectedItem is not AiProfile profile) return;
        loading = true;
        editingId = profile.Id;
        ProviderSelector.SelectedItem = ProviderPreset.All.FirstOrDefault(p => p.Name == profile.Provider);
        ProfileName.Text = profile.Name;
        Endpoint.Text = profile.Endpoint;
        Model.Text = profile.Model;
        ApiKey.Password = profile.ApiKey;
        EnableTools.IsChecked = profile.EnableTools;
        loading = false;
        SettingsStatus.Text = "";
    }
    private void AddProfile(object sender, RoutedEventArgs e)
    {
        editingId = null;
        ProfileSelector.SelectedIndex = -1;
        ProviderSelector.SelectedIndex = 0;
        if (ProviderSelector.SelectedItem is ProviderPreset preset) FillPreset(preset);
        ApiKey.Password = "";
        EnableTools.IsChecked = true;
        SettingsStatus.Text = "填写配置后保存，即可在聊天页选择此 AI。";
    }
    private void FillPreset(ProviderPreset preset)
    {
        ProfileName.Text = preset.Name;
        Endpoint.Text = preset.Endpoint;
        Model.Text = preset.Model;
    }
    private void PresetChanged(object sender, SelectionChangedEventArgs e)
    {
        if (loading || ProviderSelector.SelectedItem is not ProviderPreset preset) return;
        FillPreset(preset);
        ApiKey.Password = "";
    }
    private void SaveProfile(object sender, RoutedEventArgs e)
    {
        try
        {
            if (cancellation is not null) throw new InvalidOperationException("请先停止当前对话，再保存配置。");
            if (ProviderSelector.SelectedItem is not ProviderPreset preset) throw new ArgumentException("请选择 API 类型。");
            if (string.IsNullOrWhiteSpace(ProfileName.Text) || string.IsNullOrWhiteSpace(Model.Text))
                throw new ArgumentException("显示名称和模型 ID 不能为空。");
            if (!Uri.TryCreate(Endpoint.Text.Trim(), UriKind.Absolute, out var uri) ||
                uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo))
                throw new ArgumentException("请输入有效的 HTTP 或 HTTPS API 基础地址。");
            if (preset.Name != "Ollama（本地）" && string.IsNullOrWhiteSpace(ApiKey.Password))
                throw new ArgumentException("请输入 API 密钥。");
            var old = workspace.Profiles.FirstOrDefault(p => p.Id == editingId);
            var profile = new AiProfile
            {
                Id = old?.Id ?? Guid.NewGuid(), Name = ProfileName.Text.Trim(), Provider = preset.Name,
                Endpoint = uri.AbsoluteUri, Model = Model.Text.Trim(), ApiKey = ApiKey.Password,
                EnableTools = EnableTools.IsChecked == true
            };
            var index = old is null ? -1 : workspace.Profiles.IndexOf(old);
            // Persist first so an IO failure does not discard the active configuration.
            var proposed = workspace.Profiles.Where(p => p.Id != profile.Id).Append(profile);
            if (ConfigurationPath is not null) ProfileStorage.Save(ConfigurationPath, proposed);
            loading = true;
            if (index >= 0) workspace.Profiles[index] = profile; else workspace.Profiles.Add(profile);
            workspace.Reset(profile.Id);
            AiSelector.SelectedItem = profile;
            ProfileSelector.SelectedItem = profile;
            loading = false;
            Assistant = workspace.GetAssistant(profile);
            editingId = profile.Id;
            SettingsPage.Visibility = Visibility.Collapsed;
            ChatPage.Visibility = Visibility.Visible;
            SettingsButton.Background = Brushes.White;
            Status.Text = $"已选择 {profile.Name}";
        }
        catch (Exception ex) { loading = false; SettingsStatus.Text = ex.Message; }
    }
    private void DeleteProfile(object sender, RoutedEventArgs e)
    {
        if (cancellation is not null) { SettingsStatus.Text = "请先停止当前对话。"; return; }
        if (ProfileSelector.SelectedItem is not AiProfile profile) return;
        try
        {
            if (ConfigurationPath is not null)
                ProfileStorage.Save(ConfigurationPath, workspace.Profiles.Where(p => p.Id != profile.Id));
            workspace.Profiles.Remove(profile);
            workspace.Reset(profile.Id);
            AiSelector.SelectedIndex = workspace.Profiles.Count > 0 ? 0 : -1;
            AddProfile(sender, e);
        }
        catch (Exception ex) { SettingsStatus.Text = ex.Message; }
    }
    private void AddMessage(string text, bool user)
    {
        Welcome.Visibility = Visibility.Collapsed;
        var bubble = new AduChatBubble
        {
            Text = text,
            ReceptionType = user ? ReceptionType.Sender : ReceptionType.Receiver,
            Style = (Style)FindResource("NoAvatarBubble"),
            MaxWidth = Math.Max(220, Math.Min(560, ActualWidth - 72))
        };
        Messages.Items.Add(bubble);
        Messages.ScrollIntoView(bubble);
    }
    private void InputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
        { e.Handled = true; SendClick(sender, e); }
    }
    private async void SendClick(object sender, RoutedEventArgs e)
    {
        if (cancellation is not null) { cancellation.Cancel(); return; }
        var current = Assistant;
        var text = Input.Text.Trim();
        if (text.Length == 0) return;
        if (current is null) { Status.Text = "请先点击右上角设置，添加并选择 AI。"; return; }
        AddMessage(text, true);
        Input.Clear();
        cancellation = new CancellationTokenSource();
        SendButton.Content = "■";
        AiSelector.IsEnabled = ClearButton.IsEnabled = SettingsButton.IsEnabled = false;
        Status.Text = "正在思考…";
        try
        {
            var reply = await current.SendAsync(text,
                progress => Dispatcher.Invoke(() => Status.Text = progress), cancellation.Token);
            AddMessage(reply, false);
            Status.Text = "";
        }
        catch (OperationCanceledException) { Status.Text = "已停止。已执行的业务操作可能已经生效。"; }
        catch (Exception ex) { Status.Text = ex.Message; }
        finally
        {
            cancellation.Dispose(); cancellation = null; SendButton.Content = "↑";
            AiSelector.IsEnabled = ClearButton.IsEnabled = SettingsButton.IsEnabled = true;
        }
    }
    private void NewChat(object sender, RoutedEventArgs e)
    {
        if (cancellation is not null) return;
        if (AiSelector.SelectedItem is AiProfile profile)
        {
            workspace.Reset(profile.Id);
            Assistant = workspace.GetAssistant(profile);
        }
        else if (AssistantFactory is not null) Assistant = AssistantFactory();
        Status.Text = "已清空当前对话";
    }
}