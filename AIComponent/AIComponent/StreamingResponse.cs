using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace AIComponent;

internal static class StreamingResponse
{
    public static async Task<ChatMessage> SendAsync(HttpClient client, HttpRequestMessage request,
        Func<HttpResponseMessage, Exception> requestError,
        Func<JsonElement, ChatMessage> parseMessage,
        Func<HttpContent, Action<string>, CancellationToken, Task<ChatMessage>> parseStream,
        Action<string>? onTextDelta, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(client.Timeout);
        var token = timeout.Token;
        try
        {
            if (onTextDelta is not null) request.Headers.Accept.ParseAdd("text/event-stream, application/json");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            if (!response.IsSuccessStatusCode) throw requestError(response);
            if (onTextDelta is not null && response.Content.Headers.ContentType?.MediaType == "text/event-stream")
                return await parseStream(response.Content, onTextDelta, token);

            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
            return parseMessage(body.RootElement);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("模型请求超时。");
        }
    }

    private static async IAsyncEnumerable<string> ReadEventsAsync(HttpContent content,
        [EnumeratorCancellation] CancellationToken token)
    {
        using var stream = await content.ReadAsStreamAsync(token);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var data = new StringBuilder();
        while (await reader.ReadLineAsync(token) is { } line)
        {
            if (line.Length == 0 && data.Length > 0)
            {
                yield return data.ToString();
                data.Clear();
            }
            else if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                if (data.Length > 0) data.Append('\n');
                var value = line[5..];
                data.Append(value.Length > 0 && value[0] == ' ' ? value[1..] : value);
            }
        }
    }

    public static async Task<ChatMessage> ReadOpenAiAsync(HttpContent content,
        Action<string> onTextDelta, CancellationToken token)
    {
        var text = new StringBuilder();
        var calls = new SortedDictionary<int, ToolBuffer>();
        string? finishReason = null;
        var done = false;
        await foreach (var data in ReadEventsAsync(content, token))
        {
            if (data == "[DONE]") { done = true; break; }
            using var json = JsonDocument.Parse(data);
            var root = json.RootElement;
            if (root.TryGetProperty("error", out _))
                throw new HttpRequestException("模型返回了流式响应错误。");
            if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0) continue;
            var choice = choices[0];
            finishReason = StringProperty(choice, "finish_reason") ?? finishReason;
            if (!choice.TryGetProperty("delta", out var delta) || delta.ValueKind != JsonValueKind.Object) continue;
            AppendText(StringProperty(delta, "content"), text, onTextDelta);
            if (!delta.TryGetProperty("tool_calls", out var deltas) || deltas.ValueKind != JsonValueKind.Array) continue;
            foreach (var call in deltas.EnumerateArray())
            {
                var index = call.GetProperty("index").GetInt32();
                if (!calls.TryGetValue(index, out var buffer)) calls[index] = buffer = new();
                buffer.Id.Append(StringProperty(call, "id"));
                if (call.TryGetProperty("function", out var function) && function.ValueKind == JsonValueKind.Object)
                {
                    buffer.Name.Append(StringProperty(function, "name"));
                    buffer.Arguments.Append(StringProperty(function, "arguments"));
                }
            }
        }
        if (!done && finishReason is null)
            throw new IOException("模型响应中断，未收到完成标记。");
        if (calls.Count > 0 && finishReason is "length" or "content_filter")
            throw new IOException("模型未完成工具调用，未执行该批操作。");
        return new("assistant", text.ToString(), calls.Values.Select(call => call.Build()).ToArray());
    }

    public static async Task<ChatMessage> ReadClaudeAsync(HttpContent content,
        Action<string> onTextDelta, CancellationToken token)
    {
        var text = new StringBuilder();
        var calls = new SortedDictionary<int, ToolBuffer>();
        string? stopReason = null;
        await foreach (var data in ReadEventsAsync(content, token))
        {
            using var json = JsonDocument.Parse(data);
            var root = json.RootElement;
            switch (StringProperty(root, "type"))
            {
                case "content_block_start":
                    var block = root.GetProperty("content_block");
                    if (StringProperty(block, "type") == "tool_use")
                    {
                        var tool = new ToolBuffer { InitialArguments = block.GetProperty("input").GetRawText() };
                        tool.Id.Append(StringProperty(block, "id"));
                        tool.Name.Append(StringProperty(block, "name"));
                        calls.Add(root.GetProperty("index").GetInt32(), tool);
                    }
                    else if (StringProperty(block, "type") == "text")
                        AppendText(StringProperty(block, "text"), text, onTextDelta);
                    break;
                case "content_block_delta":
                    var delta = root.GetProperty("delta");
                    if (StringProperty(delta, "type") == "text_delta")
                        AppendText(StringProperty(delta, "text"), text, onTextDelta);
                    else if (StringProperty(delta, "type") == "input_json_delta" &&
                        calls.TryGetValue(root.GetProperty("index").GetInt32(), out var buffer))
                        buffer.Arguments.Append(StringProperty(delta, "partial_json"));
                    break;
                case "message_delta":
                    stopReason = StringProperty(root.GetProperty("delta"), "stop_reason");
                    break;
                case "message_stop":
                    if (calls.Count > 0 && stopReason != "tool_use")
                        throw new IOException("Claude 未完成工具调用，未执行该批操作。");
                    return new("assistant", text.ToString(), calls.Values.Select(call => call.Build()).ToArray());
                case "error":
                    throw new HttpRequestException("Claude 返回了流式响应错误。");
            }
        }
        throw new IOException("Claude 响应中断，未收到完成标记。");
    }

    private static string? StringProperty(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static void AppendText(string? fragment, StringBuilder text, Action<string> onTextDelta)
    {
        if (string.IsNullOrEmpty(fragment)) return;
        text.Append(fragment);
        onTextDelta(fragment);
    }

    private sealed class ToolBuffer
    {
        public StringBuilder Id { get; } = new();
        public StringBuilder Name { get; } = new();
        public StringBuilder Arguments { get; } = new();
        public string? InitialArguments { get; init; }

        public ToolCall Build()
        {
            if (Id.Length == 0 || Name.Length == 0)
                throw new JsonException("模型返回了不完整的工具调用。");
            var arguments = Arguments.Length > 0 ? Arguments.ToString() : InitialArguments;
            using var document = JsonDocument.Parse(arguments ?? "");
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new JsonException("工具参数必须是 JSON 对象。");
            return new(Id.ToString(), Name.ToString(), arguments!);
        }
    }
}
