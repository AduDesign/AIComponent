using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AIComponent;

public sealed record ModelConfiguration(string Name, string Endpoint, string Model, string ApiKey);
public sealed record ToolCall(string Id, string Name, string Arguments);
public sealed record ChatMessage(string Role, string? Content = null,
    IReadOnlyList<ToolCall>? ToolCalls = null, string? ToolCallId = null);
public sealed record ToolDefinition(string Name, string Description, JsonElement Parameters);
public interface IAiTool
{
    ToolDefinition Definition { get; }
    Task<string> ExecuteAsync(JsonElement arguments, CancellationToken cancellationToken);
}
public interface IChatProvider
{
    Task<ChatMessage> CompleteAsync(IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ToolDefinition> tools, CancellationToken cancellationToken);
}

/// <summary>Receives visible text fragments in order and returns a complete response, including tool calls.</summary>
public interface IStreamingChatProvider : IChatProvider
{
    Task<ChatMessage> CompleteStreamingAsync(IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ToolDefinition> tools, Action<string> onTextDelta, CancellationToken cancellationToken);
}

/// <summary>Endpoint is an API base URL, for example https://host/v1/.</summary>
public sealed class OpenAiCompatibleProvider(HttpClient client, ModelConfiguration configuration) : IStreamingChatProvider
{
    public Task<ChatMessage> CompleteAsync(IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ToolDefinition> tools, CancellationToken cancellationToken) =>
        CompleteCoreAsync(messages, tools, null, cancellationToken);

    public Task<ChatMessage> CompleteStreamingAsync(IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ToolDefinition> tools, Action<string> onTextDelta, CancellationToken cancellationToken) =>
        CompleteCoreAsync(messages, tools, onTextDelta, cancellationToken);

    private async Task<ChatMessage> CompleteCoreAsync(IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ToolDefinition> tools, Action<string>? onTextDelta, CancellationToken cancellationToken)
    {
        var payload = new JsonObject
        {
            ["model"] = configuration.Model,
            ["messages"] = new JsonArray(messages.Select(m =>
            {
                var node = new JsonObject { ["role"] = m.Role, ["content"] = m.Content };
                if (m.ToolCallId is not null) node["tool_call_id"] = m.ToolCallId;
                if (m.ToolCalls is { Count: > 0 })
                    node["tool_calls"] = new JsonArray(m.ToolCalls.Select(t => (JsonNode)new JsonObject
                    {
                        ["id"] = t.Id,
                        ["type"] = "function",
                        ["function"] = new JsonObject { ["name"] = t.Name, ["arguments"] = t.Arguments }
                    }).ToArray());
                return (JsonNode)node;
            }).ToArray())
        };
        if (tools.Count > 0)
            payload["tools"] = new JsonArray(tools.Select(t => (JsonNode)new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = t.Name,
                    ["description"] = t.Description,
                    ["parameters"] = JsonNode.Parse(t.Parameters.GetRawText())
                }
            }).ToArray());
        if (onTextDelta is not null) payload["stream"] = true;
        using var request = new HttpRequestMessage(HttpMethod.Post,
            new Uri(new Uri(configuration.Endpoint.TrimEnd('/') + "/"), "chat/completions"));
        if (!string.IsNullOrWhiteSpace(configuration.ApiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", configuration.ApiKey);
        request.Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");
        return await StreamingResponse.SendAsync(client, request,
            response => new HttpRequestException($"模型请求失败：HTTP {(int)response.StatusCode}。请检查地址、模型和密钥。"),
            ParseMessage, StreamingResponse.ReadOpenAiAsync, onTextDelta, cancellationToken);
    }

    private static ChatMessage ParseMessage(JsonElement root)
    {
        var message = root.GetProperty("choices")[0].GetProperty("message");
        var calls = new List<ToolCall>();
        if (message.TryGetProperty("tool_calls", out var toolCalls) && toolCalls.ValueKind == JsonValueKind.Array)
            foreach (var call in toolCalls.EnumerateArray())
            {
                var function = call.GetProperty("function");
                calls.Add(new(call.GetProperty("id").GetString()!, function.GetProperty("name").GetString()!,
                    function.GetProperty("arguments").GetString()!));
            }
        return new("assistant", message.TryGetProperty("content", out var content) &&
            content.ValueKind == JsonValueKind.String ? content.GetString() : null, calls);
    }
}

public sealed class AiAssistant(IChatProvider provider)
{
    private readonly List<ChatMessage> history = [new("system",
        "你是宿主应用中的 AI 助手。通过注册工具完成操作，不能虚构执行结果。")];
    private readonly Dictionary<string, IAiTool> tools = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim gate = new(1, 1);
    public IReadOnlyList<ChatMessage> History => history.AsReadOnly();
    public int MaxToolRounds { get; init; } = 8;
    public void RegisterTool(IAiTool tool) => tools.Add(tool.Definition.Name, tool);
    public Task<string> SendAsync(string text, Action<string>? progress = null,
        CancellationToken cancellationToken = default) => SendAsync(text, progress, cancellationToken, null);

    public async Task<string> SendAsync(string text, Action<string>? progress,
        CancellationToken cancellationToken, Action<string>? onTextDelta)
    {
        await gate.WaitAsync(cancellationToken);
        var start = history.Count;
        try
        {
            history.Add(new("user", text));
            var hasVisibleText = false;
            for (var round = 0; round <= MaxToolRounds; round++)
            {
                var definitions = tools.Values.Select(t => t.Definition).ToArray();
                var roundHasText = false;
                void PublishText(string fragment)
                {
                    if (string.IsNullOrEmpty(fragment)) return;
                    if (!roundHasText && hasVisibleText) onTextDelta?.Invoke("\n\n");
                    roundHasText = hasVisibleText = true;
                    onTextDelta?.Invoke(fragment);
                }
                var reply = onTextDelta is not null && provider is IStreamingChatProvider streaming
                    ? await streaming.CompleteStreamingAsync(history, definitions, PublishText, cancellationToken)
                    : await provider.CompleteAsync(history, definitions, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (onTextDelta is not null && !roundHasText && reply.Content is { Length: > 0 } content)
                    PublishText(content);
                if (reply.ToolCalls is not { Count: > 0 })
                {
                    history.Add(reply);
                    return reply.Content ?? "";
                }
                if (round == MaxToolRounds) throw new InvalidOperationException("工具调用已达到次数上限。");
                history.Add(reply);
                foreach (var call in reply.ToolCalls)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    progress?.Invoke($"正在执行：{call.Name}");
                    string result;
                    try
                    {
                        if (!tools.TryGetValue(call.Name, out var tool))
                            throw new InvalidOperationException("工具未注册。");
                        using var arguments = JsonDocument.Parse(call.Arguments);
                        result = await tool.ExecuteAsync(arguments.RootElement, cancellationToken);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception) { result = "{\"error\":\"工具执行失败，请检查参数或宿主状态\"}"; }
                    history.Add(new("tool", result, ToolCallId: call.Id));
                }
            }
            throw new InvalidOperationException();
        }
        catch
        {
            // Keep protocol history valid; completed host side effects cannot be rolled back.
            history.RemoveRange(start, history.Count - start);
            throw;
        }
        finally { gate.Release(); }
    }
}
