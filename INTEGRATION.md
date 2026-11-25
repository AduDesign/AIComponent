# AIComponent 接入说明

.NET 8 WPF 类库，ChatControl 是包含聊天、AI 选择和模型配置的完整组件。
示例项目只负责嵌入组件和注册业务工具。

## 运行示例
dotnet run --project AIComponent/AIComponent.Demo/AIComponent.Demo.csproj

首次打开默认离线演示。输入“帮我播放一首周杰伦的歌曲”可体验模拟工具调用，不播放真实音频。
点击右上角设置，原内容区域显示设置覆盖层；再次点击同一按钮返回聊天，未保存草稿不会生效。
支持添加多份 AI 配置、修改和删除，保存后自动选中并返回聊天。
聊天页通过当前 AI 下拉框选择已保存的配置，各 AI 的进程内会话独立。
修改配置会重置该配置会话。清空按钮只清空当前 AI 会话。

## 服务
内置 Google Gemini、OpenAI GPT、DeepSeek、Ollama 本地、Anthropic Claude、
硅基流动与自定义 OpenAI 兼容服务。
Claude 使用原生 /messages 协议；其余使用服务提供的 /chat/completions 兼容协议。
地址填写 API 基础地址，不要填写完整的 chat/completions 或 messages 路径。
模型 ID 按账户实际可用模型填写，预填值仅为可编辑示例，不保证可用性或价格。
Ollama 可不填密钥，实际工具支持取决于所选模型。
硅基流动不标注免费，计费以平台为准。

## 宿主调用
引用 AIComponent.csproj，在 XAML 引入：
xmlns:ai="clr-namespace:AIComponent;assembly=AIComponent"
嵌入 <ai:ChatControl x:Name="Chat" />。

代码：
var workspace = new ChatWorkspace();
workspace.RegisterTool(new YourBusinessTool());
Chat.Workspace = workspace;
Chat.ConfigurationPath = yourApplicationSpecificFilePath;
Chat.LoadConfiguration();

不需要宿主自行实现模型配置界面。
ConfigurationPath 未设置时仅在内存管理配置；设置后通过 Windows 当前用户 DPAPI 加密保存整个配置文件，
包含 API 密钥，不能跨 Windows 用户解密。文件路径由宿主决定，避免不同应用共用配置。
示例使用 %LOCALAPPDATA%/AIComponent.Demo/profiles.dat。
历史聊天不写入磁盘。载入失败会抛异常，宿主应显示提示。
可通过 workspace.ProviderFactory 注入自定义模型实现；默认无需设置。
Workspace、控件配置与工具注册应在 UI 线程且无对话请求运行时操作。

## 工具
IAiTool 提供 Definition（名称、描述、参数 JSON Schema）和 ExecuteAsync。
宿主必须校验参数、权限、资源状态，并实现业务需要的确认流程。
描述用 JSON Schema 不代替执行时校验。每轮最多八批工具调用，支持取消。
失败/取消会移除本轮协议历史，已经执行的业务副作用不会回滚，工具应考虑幂等性。
聊天控件使用流式回复。OpenAI 兼容接口和 Claude 原生接口均支持 SSE；普通 JSON 及仅实现 IChatProvider 的适配器整段返回。宿主模型包装器可实现 IStreamingChatProvider 传递正文增量，工具参数会在响应完成后执行。当前尚未支持 Markdown 渲染、非文本附件或上下文自动裁剪。

## 本地文件与目录
输入区支持多选 TXT / Markdown 文件，或递归选择目录中的文本与代码文件。附件可在发送前移除，也可只发送附件以请求概述。
文件选择时读取为快照；发送时以用户消息正文提供给模型。自定义 IChatProvider / IStreamingChatProvider 也能直接从 Content 取得文件正文，不需要上传文件 API。
ChatMessage.Attachments 保留会话中的快照，GetDisplayContent() 用于显示文件名，GetModelContent() 用于构造包含正文的模型文本。宿主可调用 Chat.ViewModel.AddAttachmentsAsync(paths)、AddDirectoryAsync(path)、RemoveAttachment(attachment)，Attachments 为只读可观察集合。
支持 UTF-8、带 BOM 的 UTF-16 / UTF-32；不支持的编码提示另存为 UTF-8。目录递归跳过重解析点（链接），包含隐藏文本文件，二进制和失败文件显示读取提示。
每条消息最多 128 个附件、单文件 256 KiB、总计 2 MiB，每次最多扫描 2000 个候选文件；达到限制提示未完整读取。模型实际上下文容量由服务决定，组件不自动裁剪。
文件正文不写入配置文件，也不会自动跟踪原文件变化。清空对话会清空待发送附件。

## 验证
解决方案编译零警告、零错误。
已通过实际 WPF 控件自动检查：设置开关、保存配置并选择 AI、返回聊天、
加密配置往返及密钥不以明文写出。
Claude 协议经过模拟 HTTP 检查，设置页已渲染检查布局。
未使用真实密钥请求任何模型厂商，不宣称线上服务均已实测。

## 协议参考
Gemini：https://ai.google.dev/gemini-api/docs/openai
DeepSeek：https://api-docs.deepseek.com/
Claude：https://platform.claude.com/docs/en/api/messages/create
Ollama：https://docs.ollama.com/openai
硅基流动：https://docs.siliconflow.cn/docs/api/chat-completions-post

## AduSkin 界面依赖
聊天和设置页均使用 AduSkin 实际控件，参考 AduSkin.Demo/Views/Demo/UCChat.xaml：
AduListBox + AduChatBubble、AduTextBox、底部 AduComboBox，设置页使用 AduPasswordBox、
AduCheckBox 和 AduButton。组件内为 AduChatBubble 提供无头像模板，不改动 AduSkin 源码。

当前通过相对 ProjectReference 引用 ../../../AduSkin2/src/AduSkin/AduSkin.csproj，
需要将 AduSkin2 与 AIComponent 放在同级目录。现有 net8.0-windows 项目会选择 AduSkin 的
net6.0-windows 兼容目标。分发应用时需一起部署 AduSkin.dll（项目引用会自动复制）。
不依赖 AduSkin.Demo，也不要求宿主自行写配置页面。

AduSkin 控件动画依赖 Application 级资源。组件创建时，如果宿主未加载 AduSkinTheme，
会初始化一次 Light 主题；已经加载时沿用已有主题。此初始化会加载应用级控件资源，
宿主若需管理主题，应在创建组件之前加载自己的 AduSkinTheme。
无头像气泡模板保留在组件资源中。

迁移后已验证：控件初始化、设置切换、配置保存与加密读写、AduSkin 气泡类型和零头像控件。
聊天和设置页面均已渲染检查。

## MVVM 结构
ChatControl.ViewModel 为 AIComponent.ViewModels.ChatViewModel，继承 AduSkin.Utility.Mvvm.NotifyPropertyBase。
所有操作使用 AduSkin.Utility.Commands.RelayCommand：
SendCommand、StopCommand、SendOrStopCommand、ToggleSettingsCommand、NewChatCommand、
AddProfileCommand、SaveProfileCommand、DeleteProfileCommand。

ViewModel 管理输入、消息集合、忙碌状态、AI 选择、配置表单、校验及持久化。
XAML 通过 ItemsSource 和 DataTemplate 创建气泡，聊天与配置操作通过 Command 绑定；
AduPasswordBox.Password 与 ApiKey 双向绑定。
Enter 由 ChatViewBehavior.EnterCommand 转发 SendCommand，Shift+Enter 保留换行。
消息滚动使用 AduSkin ListBoxExtend.ScrollPositionType。
视图代码保留主题初始化、宿主 API 转接、滚动跟随和文件/目录选择对话框；文件读取与附件状态由 ViewModel 管理。

组件根 Grid 绑定自身 ViewModel，不覆盖宿主 DataContext。
原 Workspace、Assistant、AssistantFactory、ConfigurationPath、LoadConfiguration 接口保持兼容；
也可通过 Chat.ViewModel 直接访问绑定状态和命令。
配置和命令应在 UI 线程访问。SendAsync 可等待完成，SendCommand 用于 UI 异步调用；
SendOrStopCommand 在发送期间转发 StopCommand，避免异步 RelayCommand 的重入保护禁用停止操作。

## Agnes AI
设置页的 API 类型选择 Agnes AI，会自动填写：
- API 基础地址：https://apihub.agnes-ai.com/v1/
- 模型 ID：agnes-2.5-flash

填写 Agnes AI 控制台申请的密钥，点击“保存并使用此 AI”后即可在聊天页选择。
地址和模型 ID 均可修改。复用 OpenAiCompatibleProvider，无需单独的宿主适配代码。
工具调用沿用组件现有工具注册机制，实际调用以服务响应为准。
官方文档：https://agnes-ai.com/en/docs/overview
未使用真实 API 密钥进行线上请求验证。
