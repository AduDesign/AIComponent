using System.Collections.ObjectModel;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AIComponent;

public sealed record ProviderPreset(string Name, string Endpoint, string Model, bool NativeClaude = false)
{
    public static IReadOnlyList<ProviderPreset> All { get; } =
    [
        new("Google Gemini", "https://generativelanguage.googleapis.com/v1beta/openai/", "gemini-2.5-flash"),
        new("OpenAI GPT", "https://api.openai.com/v1/", ""),
        new("DeepSeek", "https://api.deepseek.com/", "deepseek-flash"),
        new("Ollama（本地）", "http://localhost:11434/v1/", ""),
        new("Anthropic Claude", "https://api.anthropic.com/v1/", "", true),
        new("硅基流动", "https://api.siliconflow.cn/v1/", ""),
        new("自定义 OpenAI 兼容服务", "", "")
    ];
}
public sealed class AiProfile
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Provider { get; set; } = "";
    public string Endpoint { get; set; } = "";
    public string Model { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public bool EnableTools { get; set; } = true;
    public override string ToString() => Name;
}
public sealed class ChatWorkspace
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(120) };
    private readonly Dictionary<Guid, AiAssistant> sessions = [];
    private readonly List<IAiTool> tools = [];
    public ObservableCollection<AiProfile> Profiles { get; } = [];
    public Func<AiProfile, IChatProvider>? ProviderFactory { get; set; }
    public void RegisterTool(IAiTool tool)
    {
        if (tools.Any(t => t.Definition.Name == tool.Definition.Name))
            throw new ArgumentException("工具名称重复。");
        tools.Add(tool);
        sessions.Clear();
    }
    public void Reset(Guid id) => sessions.Remove(id);
    public AiAssistant GetAssistant(AiProfile profile)
    {
        if (sessions.TryGetValue(profile.Id, out var existing)) return existing;
        var configuration = new ModelConfiguration(profile.Name, profile.Endpoint, profile.Model, profile.ApiKey);
        var provider = ProviderFactory?.Invoke(profile) ??
            (profile.Provider == "Anthropic Claude"
                ? (IChatProvider)new ClaudeProvider(Client, configuration)
                : new OpenAiCompatibleProvider(Client, configuration));
        var assistant = new AiAssistant(provider);
        if (profile.EnableTools)
            foreach (var tool in tools) assistant.RegisterTool(tool);
        sessions.Add(profile.Id, assistant);
        return assistant;
    }
}
public sealed class ClaudeProvider(HttpClient client, ModelConfiguration configuration) : IChatProvider
{
    public async Task<ChatMessage> CompleteAsync(IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ToolDefinition> tools, CancellationToken cancellationToken)
    {
        var translated = new JsonArray();
        foreach (var message in messages.Where(m => m.Role != "system"))
        {
            var role = message.Role == "tool" ? "user" : message.Role;
            var parts = new JsonArray();
            if (message.Role == "tool")
                parts.Add(new JsonObject { ["type"] = "tool_result", ["tool_use_id"] = message.ToolCallId,
                    ["content"] = message.Content });
            else
            {
                if (!string.IsNullOrEmpty(message.Content))
                    parts.Add(new JsonObject { ["type"] = "text", ["text"] = message.Content });
                if (message.ToolCalls is not null)
                    foreach (var call in message.ToolCalls)
                        parts.Add(new JsonObject { ["type"] = "tool_use", ["id"] = call.Id,
                            ["name"] = call.Name, ["input"] = JsonNode.Parse(call.Arguments) });
            }
            if (translated.LastOrDefault() is JsonObject previous && previous["role"]?.GetValue<string>() == role)
                foreach (var part in parts) ((JsonArray)previous["content"]!).Add(part!.DeepClone());
            else translated.Add(new JsonObject { ["role"] = role, ["content"] = parts });
        }
        var payload = new JsonObject
        {
            ["model"] = configuration.Model, ["max_tokens"] = 4096,
            ["system"] = string.Join("\n", messages.Where(m => m.Role == "system").Select(m => m.Content)),
            ["messages"] = translated
        };
        if (tools.Count > 0)
            payload["tools"] = new JsonArray(tools.Select(t => (JsonNode)new JsonObject
            {
                ["name"] = t.Name, ["description"] = t.Description,
                ["input_schema"] = JsonNode.Parse(t.Parameters.GetRawText())
            }).ToArray());
        using var request = new HttpRequestMessage(HttpMethod.Post,
            configuration.Endpoint.TrimEnd('/') + "/messages");
        request.Headers.Add("x-api-key", configuration.ApiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");
        request.Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Claude 请求失败：HTTP {(int)response.StatusCode}。请检查配置。");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var text = new StringBuilder();
        var calls = new List<ToolCall>();
        foreach (var part in document.RootElement.GetProperty("content").EnumerateArray())
        {
            switch (part.GetProperty("type").GetString())
            {
                case "text": text.Append(part.GetProperty("text").GetString()); break;
                case "tool_use": calls.Add(new(part.GetProperty("id").GetString()!,
                    part.GetProperty("name").GetString()!, part.GetProperty("input").GetRawText())); break;
            }
        }
        return new("assistant", text.ToString(), calls);
    }
}
