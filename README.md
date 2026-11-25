# AIComponent

基于 .NET 8、WPF、AduSkin 和 MVVM 的 AI 助手组件。

将聊天界面、模型配置、AI 选择和工具调用封装在一个类库中。宿主只需嵌入控件并注册业务工具，即可通过自然语言操作播放器、编辑器等桌面应用。

## 功能

- 无头像聊天气泡、多行输入、文本复制、发送与停止、清空当前对话。
- 添加本地 TXT / Markdown 文件或整个目录，递归读取文本和代码文件，发送前可逐个移除附件。
- 内置 AI 配置页面，点击右上角设置按钮，在当前组件内切换聊天与设置覆盖层。
- 添加、修改、删除多份 AI 配置，在输入区选择 AI。
- 每份配置维护独立的进程内会话上下文。
- 模型选择宿主工具，执行结果回传模型后继续回答。
- 可选通过 Windows 当前用户 DPAPI 加密保存配置与 API 密钥。
- ViewModel 继承 AduSkin 的 NotifyPropertyBase，操作使用 RelayCommand。

支持流式响应的模型会逐步显示正文，并保留换行。返回普通 JSON 的服务和旧版自定义适配器仍整段显示。尚未实现 Markdown 渲染、非文本附件、聊天记录持久化、自动上下文裁剪及工作流设计器。
支持流式响应的模型会逐步显示正文。AI 回复使用 AduSkin 的 AduMarkDownViewer 实时渲染标题、粗体、列表、代码块和表格，流式更新按约 80ms 合并；用户消息保持普通文本。返回普通 JSON 的服务和旧版自定义适配器仍整段显示。尚未实现附件、聊天记录持久化、自动上下文裁剪及工作流设计器。

## 模型服务

| 服务类型 | 接入方式 |
| --- | --- |
| Agnes AI | OpenAI 兼容接口 |
| Google Gemini | OpenAI 兼容接口 |
| OpenAI GPT | OpenAI 兼容接口 |
| DeepSeek | OpenAI 兼容接口 |
| Ollama（本地） | OpenAI 兼容接口 |
| Anthropic Claude | 原生 Messages API |
| 硅基流动 | OpenAI 兼容接口 |
| 自定义服务 | OpenAI 兼容接口，或宿主注入 IChatProvider |

API 类型用于选择服务预设，模型 ID 可自行填写。模型可用性、工具能力和计费取决于服务及账户；预填模型名称仅为可编辑示例。已实现协议适配，尚未逐一使用真实密钥验证线上服务。

## 项目结构

~~~text
AIComponent/
├─ AIComponent.slnx
├─ AIComponent/                 WPF 组件类库
│  ├─ ChatControl.xaml          聊天与设置视图
│  ├─ ChatControl.xaml.cs       主题初始化、宿主 API 转接
│  ├─ ViewModels/
│  │  └─ ChatViewModel.cs       界面状态、配置逻辑和命令
│  ├─ Behaviors/
│  │  └─ ChatViewBehavior.cs    Enter 键命令行为
│  ├─ Assistant.cs             消息协议、模型接口、工具执行循环
│  ├─ ChatWorkspace.cs         配置会话、服务预设、Claude 适配
│  └─ ProfileStorage.cs        加密配置存储
└─ AIComponent.Demo/            独立示例程序
~~~

组件提供通用能力，具体业务工具由宿主实现。已有业务工作流也可以作为工具注册。

## 环境准备

- Windows。
- 支持 net8.0-windows 和 WPF 的 .NET SDK。
- 本地 AduSkin2 源码。
- 打开 .slnx 解决方案需要支持该格式的 IDE / SDK；也可直接构建项目文件。

当前通过 ProjectReference 引用本地 AduSkin，目录应保持：

~~~text
OpenSources/
├─ AIComponent/
│  └─ AIComponent/
│     ├─ AIComponent.slnx
│     ├─ AIComponent/
│     └─ AIComponent.Demo/
└─ AduSkin2/
   └─ src/
      └─ AduSkin/
         └─ AduSkin.csproj
~~~

引用位于 AIComponent/AIComponent/AIComponent.csproj，相对路径为：

~~~text
../../../AduSkin2/src/AduSkin/AduSkin.csproj
~~~

当前 AduSkin 项目未提供 net8.0-windows 目标，组件使用其兼容的 net6.0-windows 目标。改变目录结构后，需要同步修改引用路径。

## 快速运行

在仓库根目录执行：

~~~powershell
dotnet build ./AIComponent/AIComponent.Demo/AIComponent.Demo.csproj
dotnet run --project ./AIComponent/AIComponent.Demo/AIComponent.Demo.csproj
~~~

首次运行且没有已保存配置时，示例提供离线模拟播放器。输入：

> 帮我播放一首周杰伦的歌曲

即可体验工具调用与宿主状态更新。离线模式使用固定演示结果，不进行真实模型推理，也不播放真实音频。

连接真实模型：

1. 点击右上角设置按钮，选择“添加 AI”。
2. 选择 API 类型，填写显示名称、API 基础地址、模型 ID 和密钥。
3. 点击“保存并使用此 AI”，自动返回聊天页。
4. 在输入区的 AI 下拉框中选择配置，开始对话。

地址填写 API 基础地址，组件会追加 chat/completions；Claude 会追加 messages。不要填写完整请求路径。Ollama 本地服务可不填密钥。

Enter 发送，Shift+Enter 换行；生成期间发送按钮切换为停止。

点击输入框上方的“添加文件”选择 TXT / Markdown，或点击“添加目录”递归读取其中的文本文件（包括代码和无扩展名文本）。目录文件显示相对路径，附件右侧 × 可移除。可以只附文件直接发送，默认请求模型阅读并概述。

文件在选择时读取为内存快照，发送后将正文交给当前模型服务，并保留在该 AI 的当前会话中；不会上传整个本地目录或把本机绝对路径发给模型。支持 UTF-8 及带 BOM 的 UTF-16 / UTF-32，其他编码请先另存为 UTF-8。目录链接和文件链接跳过，隐藏文本文件也会读取；二进制、读取失败或超过限制的文件会在状态提示中列出，提示过长时可悬停查看。

每条消息最多 128 个文件，单文件最多 256 KiB，合计最多 2 MiB；单次最多扫描 2000 个候选文件。达到限制会明确提示未完整读取，不会静默截断正文。模型的上下文限制仍可能小于这些文件上限，可移除附件后分批阅读。清空当前对话会同时清空待发送附件。

## 嵌入 WPF 应用

### 引用与布局

在宿主 WPF 项目中引用 AIComponent.csproj，然后嵌入控件：

~~~xml
<Window
    x:Class="YourApp.MainWindow"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:ai="clr-namespace:AIComponent;assembly=AIComponent">
    <Grid>
        <ai:ChatControl x:Name="Chat" />
    </Grid>
</Window>
~~~

宿主无需另写配置窗口。分发应用时需包含 AduSkin.dll，项目引用会自动复制相应依赖。

### 初始化工作区

在 InitializeComponent() 之后配置：

~~~csharp
using AIComponent;

var workspace = new ChatWorkspace();
Chat.Workspace = workspace;

Chat.ConfigurationPath = System.IO.Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "YourApp",
    "ai-profiles.dat");

try
{
    Chat.LoadConfiguration();
}
catch (Exception)
{
    // 根据宿主需求提示加载失败，或让用户重新配置。
}
~~~

ConfigurationPath 不设置时，配置仅在内存中管理。设置后，保存和删除配置会同步更新加密文件。不同应用应使用独立路径。

## 注册业务工具

IAiTool 描述工具名称、用途、参数结构和执行逻辑。下面是一个读取当前时间的完整示例：

~~~csharp
using System.Text.Json;
using AIComponent;

public sealed class GetCurrentTimeTool : IAiTool
{
    public ToolDefinition Definition { get; } = new(
        "get_current_time",
        "读取宿主计算机当前的本地时间。",
        JsonDocument.Parse(
            """{"type":"object","properties":{},"additionalProperties":false}"""
        ).RootElement.Clone());

    public Task<string> ExecuteAsync(
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(JsonSerializer.Serialize(new
        {
            localTime = DateTimeOffset.Now.ToString("O")
        }));
    }
}
~~~

创建工作区时注册：

~~~csharp
var workspace = new ChatWorkspace();
workspace.RegisterTool(new GetCurrentTimeTool());
Chat.Workspace = workspace;
~~~

用户可以询问“现在几点？”，支持工具调用的模型会根据需要选择工具。是否实际调用取决于模型行为。

~~~text
用户输入 → 模型请求 → 工具调用 → 宿主执行 → 结果回传模型 → AI 回答
~~~

参数 JSON Schema 用于描述接口，当前组件不会自动进行完整的 Schema 校验。工具应自行校验参数、权限和业务状态；编辑、删除等操作需要的确认流程由宿主实现。

默认每轮最多执行八批工具调用。取消或失败会移除该轮协议历史，但已经执行的业务操作不会回滚。有副作用的工具应考虑幂等性。

## MVVM 与扩展

ChatControl.ViewModel 的类型为 AIComponent.ViewModels.ChatViewModel：

- 基类：AduSkin.Utility.Mvvm.NotifyPropertyBase。
- 命令：AduSkin.Utility.Commands.RelayCommand。
- 消息集合通过 ItemsSource 和 DataTemplate 显示。
- 配置表单与 AduPasswordBox.Password 通过绑定同步。
- 组件内部绑定自身 ViewModel，不覆盖宿主 DataContext。

| 命令 | 用途 |
| --- | --- |
| SendCommand | 异步发送 |
| StopCommand | 取消当前请求 |
| SendOrStopCommand | 根据状态发送或停止 |
| ToggleSettingsCommand | 切换设置与聊天 |
| NewChatCommand | 清空当前会话 |
| AddProfileCommand | 新建配置草稿 |
| SaveProfileCommand | 保存并使用配置 |
| DeleteProfileCommand | 删除选中的配置 |

宿主可通过 Chat.ViewModel 访问状态、命令和可等待的 SendAsync()。原 Workspace、Assistant、AssistantFactory、ConfigurationPath、LoadConfiguration() 接口保留。

自定义模型服务可实现 IChatProvider，并通过 ChatWorkspace.ProviderFactory 注入。设置工厂后，应自行处理需要的全部配置类型，具体方式可参考示例程序。

工作区、ViewModel 和命令应在 UI 线程操作；切换工作区、注册工具或加载配置应在没有对话请求执行时进行。

## 主题与存储

界面使用 AduSkin 实际控件，聊天布局参考 AduSkin.Demo 的 UCChat.xaml，并为 AduChatBubble 提供无头像模板。

AduSkin 动画依赖 Application 级资源。宿主已有 AduSkinTheme 时沿用，否则组件创建时初始化一次 Light 主题。该初始化会加载应用级控件资源；需要统一管理主题的宿主应在创建组件前加载自己的 AduSkinTheme。

配置通过 Windows 当前用户 DPAPI 加密，包含 API 密钥，不能直接跨 Windows 用户解密。聊天记录仅保存在进程内：

- 切换 AI 保留其当前会话。
- 修改某个配置会重置该配置会话。
- 清空按钮只清空当前 AI 会话。
- 退出程序后不保留聊天记录。

示例默认配置位置为 %LOCALAPPDATA%/AIComponent.Demo/profiles.dat。

## 验证范围

附件回归检查可运行 `dotnet run --project AIComponent/AIComponent.Tests/AIComponent.Tests.csproj`。使用本地模拟模型，覆盖目录递归、相对路径、编码、二进制跳过、去重与移除、数量与大小限制、快照与后续上下文、OpenAI/Claude 请求正文和 WPF 控件初始化。

已完成解决方案编译和实际 WPF 绑定回归检查，覆盖配置增删改、密钥双向绑定、设置切换、消息绑定、发送与停止、清空及配置加密读写。模型协议使用模拟接口进行过检查，真实服务仍需使用有效配置验证。

更多接入细节见 [INTEGRATION.md](INTEGRATION.md)。

## 许可证

本项目使用 [MIT License](LICENSE)。使用和分发 AduSkin 依赖时，请同时遵守其许可证。
