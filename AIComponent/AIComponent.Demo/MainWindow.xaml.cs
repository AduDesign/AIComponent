using System.Net.Http;
using System.Text.Json;
using System.Windows;

namespace AIComponent;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        var workspace = new ChatWorkspace();
        workspace.RegisterTool(new MusicTool(title =>
            PlayerStatus.Text = $"宿主模拟播放器：正在播放《{title}》"));
        workspace.ProviderFactory = profile => profile.Provider == "离线演示"
            ? new DemoProvider()
            : profile.Provider == "Anthropic Claude"
                ? new ClaudeProvider(Client, new(profile.Name, profile.Endpoint, profile.Model, profile.ApiKey))
                : new OpenAiCompatibleProvider(Client, new(profile.Name, profile.Endpoint, profile.Model, profile.ApiKey));
        Chat.Workspace = workspace;
        Chat.ConfigurationPath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AIComponent.Demo", "profiles.dat");
        try { Chat.LoadConfiguration(); }
        catch (Exception) { MessageBox.Show("无法加载已保存的 AI 配置。请检查文件访问权限或重新配置。"); }
        if (workspace.Profiles.Count == 0)
        {
            workspace.Profiles.Add(new AiProfile
            {
                Name = "离线演示 · 模拟播放器",
                Provider = "离线演示",
                Model = "demo"
            });
            Chat.Workspace = workspace;
        }
    }
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(120) };
}
internal sealed class MusicTool(Action<string> play) : IAiTool
{
    public ToolDefinition Definition { get; } = new("music_play",
        "在模拟播放器中播放指定歌曲，不会播放真实音频。",
        JsonDocument.Parse("""{"type":"object","properties":{"title":{"type":"string","minLength":1}},"required":["title"],"additionalProperties":false}""").RootElement.Clone());
    public Task<string> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var title = arguments.GetProperty("title").GetString();
        if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("歌曲名不能为空。");
        play(title);
        return Task.FromResult(JsonSerializer.Serialize(new { simulated = true, title, status = "playing" }));
    }
}
internal sealed class DemoProvider : IChatProvider
{
    public Task<ChatMessage> CompleteAsync(IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ToolDefinition> tools, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (messages[^1].Role == "tool")
            return Task.FromResult(new ChatMessage("assistant", "模拟播放器已切换为播放状态。这是工具调用演示，没有播放真实音频。"));
        var input = messages[^1].Content ?? "";
        return Task.FromResult(input.Contains("播放")
            ? new ChatMessage("assistant", ToolCalls: [new(Guid.NewGuid().ToString("N"),
                "music_play", """{"title":"晴天（周杰伦）"}""")])
            : new ChatMessage("assistant", "你好！当前是离线演示模式。输入“帮我播放一首周杰伦的歌曲”可体验工具调用；切换到兼容接口可连接真实模型。"));
    }
}

