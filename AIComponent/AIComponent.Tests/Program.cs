using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using AIComponent;
using AIComponent.ViewModels;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        var application = new Application();
        var exitCode = 0;
        application.Dispatcher.InvokeAsync(async () =>
        {
            try { await CheckAsync(); Console.WriteLine("All attachment checks passed."); }
            catch (Exception ex) { Console.Error.WriteLine(ex); exitCode = 1; }
            finally { application.Shutdown(); }
        });
        application.Run();
        return exitCode;
    }

    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private static async Task CheckAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "AIComponent-attachments-" + Guid.NewGuid());
        Directory.CreateDirectory(Path.Combine(root, "nested"));
        try
        {
            var note = Path.Combine(root, "note.md");
            await File.WriteAllTextAsync(note, "# 中文标题\n文件正文", new UTF8Encoding(false));
            await File.WriteAllTextAsync(Path.Combine(root, "nested", "note.md"), "嵌套文件", Encoding.Unicode);
            await File.WriteAllTextAsync(Path.Combine(root, "sample.cs"), "class Example {}", Encoding.UTF8);
            await File.WriteAllTextAsync(Path.Combine(root, "LICENSE"), "License text");
            await File.WriteAllBytesAsync(Path.Combine(root, "image.bin"), [0, 1, 2, 3]);
            await File.WriteAllBytesAsync(Path.Combine(root, "invalid.txt"), [0xFF, 0xAA]);
            var vm = new ChatViewModel();
            await vm.AddDirectoryAsync(root);
            Check(vm.Attachments.Count == 4, "Directory must include nested files, code and extensionless text, and skip binary/invalid encodings.");
            Check(vm.Status.Contains("2 项"), "Skipped files must be reported.");
            Check(vm.Attachments.Single(a => a.DisplayName == Path.Combine("nested", "note.md")).Content == "嵌套文件", "UTF-16 and relative paths.");
            Check(vm.Attachments.Single(a => a.Name == "sample.cs").Content == "class Example {}", "UTF-8 BOM should be removed.");
            await vm.AddAttachmentsAsync([note]);
            Check(vm.Attachments.Count == 4, "Duplicate selections must not add files twice.");
            var removed = vm.Attachments.Single(a => a.Name == "sample.cs");
            vm.RemoveAttachment(removed);
            Check(vm.Attachments.Count == 3, "Remove attachment.");

            var legacy = new CaptureProvider();
            vm.Assistant = new AiAssistant(legacy);
            Check(vm.SendCommand.CanExecute(null!), "Attachment-only message must be sendable.");
            await File.WriteAllTextAsync(note, "Changed on disk");
            await vm.SendAsync();
            Check(legacy.LastContent!.Contains("文件正文") && !legacy.LastContent.Contains("Changed on disk"), "Legacy providers must receive the captured snapshot in Content.");
            Check(!vm.HasAttachments && vm.Assistant.History[1].Attachments?.Count == 3, "Sending clears draft attachments and preserves history metadata.");
            Check(!vm.Messages[0].Text.Contains("文件正文") && vm.Messages[0].Text.Contains("note.md"), "Bubble must show filenames without dumping source contents.");
            vm.InputText = "继续";
            await vm.SendAsync();
            Check(legacy.Messages!.Any(m => m.Content?.Contains("文件正文") == true), "Follow-up must retain file context.");
            var previous = vm.Assistant;
            vm.Assistant = previous;
            Check(vm.Messages[0].Text.Contains("note.md") && !vm.Messages[0].Text.Contains("文件正文"), "Restoring a session must keep compact attachment display.");

            var attachment = await TextAttachment.ReadAsync(note);
            foreach (var claude in new[] { false, true })
            {
                var handler = new CaptureHandler(claude);
                using var client = new HttpClient(handler);
                var config = new ModelConfiguration("test", "https://example.invalid/v1/", "test", "test");
                IChatProvider provider = claude ? new ClaudeProvider(client, config) : new OpenAiCompatibleProvider(client, config);
                await new AiAssistant(provider).SendAsync("阅读附件", null, default, _ => { }, [attachment]);
                using var payload = JsonDocument.Parse(handler.Body!);
                var message = payload.RootElement.GetProperty("messages").EnumerateArray().Last();
                var body = claude ? message.GetProperty("content")[0].GetProperty("text").GetString()! : message.GetProperty("content").GetString()!;
                Check(body.Contains("Changed on disk") && body.Contains("note.md") && !body.Contains(root), "Both HTTP protocols must send content and names without absolute paths.");
            }

            var oversized = Path.Combine(root, "large.txt");
            await File.WriteAllTextAsync(oversized, new string('a', TextAttachment.MaxFileBytes + 1));
            await vm.AddAttachmentsAsync([oversized]);
            Check(vm.Attachments.Count == 0 && vm.Status.Contains("256 KiB"), "Reject oversized files without truncation.");
            await vm.AddAttachmentsAsync([Path.Combine(root, "missing.txt")]);
            Check(vm.IsIdle && vm.Status.Contains("missing.txt"), "Read failure must restore idle state.");

            var many = Path.Combine(root, "many");
            Directory.CreateDirectory(many);
            for (var i = 0; i < 129; i++) await File.WriteAllTextAsync(Path.Combine(many, i + ".txt"), "text");
            await vm.AddDirectoryAsync(many);
            Check(vm.Attachments.Count == 128 && vm.Status.Contains("其余文件未读取"), "File count limit must be explicit.");
            vm.NewChatCommand.Execute(null!);
            Check(!vm.HasAttachments, "New chat must clear pending attachments.");
            var total = Path.Combine(root, "total");
            Directory.CreateDirectory(total);
            for (var i = 0; i < 9; i++) await File.WriteAllTextAsync(Path.Combine(total, i + ".txt"), new string('a', TextAttachment.MaxFileBytes));
            await vm.AddDirectoryAsync(total);
            Check(vm.Attachments.Count == 8 && vm.Status.Contains("2 MiB"), "Total byte limit must be enforced and reported.");

            var control = new ChatControl();
            Check(control.FindName("AttachFilesButton") is Button && control.FindName("AttachDirectoryButton") is Button, "WPF attachment entry points must initialize.");
            await control.ViewModel.AddAttachmentsAsync([note]);
            control.Measure(new Size(520, 700));
            control.Arrange(new Rect(0, 0, 520, 700));
            control.UpdateLayout();
            Check(control.ViewModel.HasAttachments && control.ActualHeight == 700, "WPF layout with attachments must initialize.");
        }
        finally
        {
            // root is a newly created, uniquely named test directory under the OS temp directory.
            if (Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
                Directory.Delete(root, true);
        }
    }

    private sealed class CaptureProvider : IChatProvider
    {
        public string? LastContent;
        public IReadOnlyList<ChatMessage>? Messages;
        public Task<ChatMessage> CompleteAsync(IReadOnlyList<ChatMessage> messages, IReadOnlyList<ToolDefinition> tools, CancellationToken cancellationToken)
        {
            Messages = messages;
            LastContent = messages.Last().Content;
            return Task.FromResult(new ChatMessage("assistant", "收到"));
        }
    }

    private sealed class CaptureHandler(bool claude) : HttpMessageHandler
    {
        public string? Body;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new(HttpStatusCode.OK)
            {
                Content = new StringContent(claude ? """{"content":[{"type":"text","text":"ok"}]}""" :
                    """{"choices":[{"message":{"role":"assistant","content":"ok"}}]}""", Encoding.UTF8, "application/json")
            };
        }
    }
}
