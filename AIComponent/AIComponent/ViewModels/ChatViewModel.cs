using System.Collections.ObjectModel;

using AduSkin.Controls;
using AduSkin.Utility.Commands;
using AduSkin.Utility.Mvvm;

namespace AIComponent.ViewModels;

public sealed class ChatMessageViewModel(string text, ReceptionType receptionType) : NotifyPropertyBase
{
    private string content = text;
    public string Text { get => content; internal set => SetProperty(ref content, value); }
    public ReceptionType ReceptionType { get; } = receptionType;
}

public sealed class ChatViewModel : NotifyPropertyBase
{
	private ChatWorkspace workspace = new();
	private AiAssistant? assistant;
	private AiProfile? selectedAi, editingProfile;
	private ProviderPreset? selectedProvider;
	private Guid? editingId;
	private bool loading, isBusy, isSettingsOpen;
	private string inputText = "", status = "", settingsStatus = "";
	private string profileName = "", endpoint = "", model = "", apiKey = "";
	private bool enableTools = true;
	private CancellationTokenSource? cancellation;
	public ChatViewModel()
	{
		SendCommand = new RelayCommand(SendAsync, () => !IsBusy && !string.IsNullOrWhiteSpace(InputText));
		StopCommand = new RelayCommand(() => cancellation?.Cancel(), () => IsBusy);
		// The async RelayCommand blocks reentry; keep a separate dispatcher command so Stop stays usable.
		SendOrStopCommand = new RelayCommand(() =>
		{
			var command = IsBusy ? StopCommand : SendCommand;
			if (command.CanExecute(null!)) command.Execute(null!);
		}, () => IsBusy || SendCommand.CanExecute(null!));
		SendCommand.CanExecuteChanged += (_, _) => SendOrStopCommand.RaiseCanExecuteChanged();
		ToggleSettingsCommand = new RelayCommand(() =>
		{
			IsSettingsOpen = !IsSettingsOpen;
			if (IsSettingsOpen && SelectedAi is not null) EditingProfile = SelectedAi;
		}, () => !IsBusy);
		NewChatCommand = new RelayCommand(NewChat, () => !IsBusy);
		AddProfileCommand = new RelayCommand(AddProfile, () => !IsBusy);
		SaveProfileCommand = new RelayCommand(SaveProfile, () => !IsBusy);
		DeleteProfileCommand = new RelayCommand(DeleteProfile, () => !IsBusy && EditingProfile is not null);
		Messages.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsEmpty));
		AddProfile();
	}
	public RelayCommand SendCommand { get; }
	public RelayCommand StopCommand { get; }
	public RelayCommand SendOrStopCommand { get; }
	public RelayCommand ToggleSettingsCommand { get; }
	public RelayCommand NewChatCommand { get; }
	public RelayCommand AddProfileCommand { get; }
	public RelayCommand SaveProfileCommand { get; }
	public RelayCommand DeleteProfileCommand { get; }
	public ObservableCollection<ChatMessageViewModel> Messages { get; } = [];
	public IReadOnlyList<ProviderPreset> Providers => ProviderPreset.All;
	public ObservableCollection<AiProfile> Profiles => workspace.Profiles;
	public string? ConfigurationPath { get; set; }
	public Func<AiAssistant>? AssistantFactory { get; set; }
	public bool IsEmpty => Messages.Count == 0;
	public bool IsIdle => !IsBusy;
	public string SendToolTip => IsBusy ? "停止生成" : "发送消息";
	public ChatWorkspace Workspace
	{
		get => workspace;
		set
		{
			EnsureIdle();
			workspace = value ?? throw new ArgumentNullException(nameof(value));
			OnPropertyChanged(nameof(Workspace));
			OnPropertyChanged(nameof(Profiles));
			selectedAi = null;
			SelectedAi = workspace.Profiles.FirstOrDefault();
			if (selectedAi is null) Assistant = null;
			OnPropertyChanged(nameof(SelectedAi));
		}
	}
	public AiAssistant? Assistant
	{
		get => assistant;
		set
		{
			EnsureIdle();
			assistant = value;
			OnPropertyChanged(nameof(Assistant));
			Messages.Clear();
			if (value is not null)
				foreach (var message in value.History.Where(m => m.Role is "user" or "assistant"))
					if (!string.IsNullOrEmpty(message.Content))
						Messages.Add(new(message.Content, message.Role == "user" ? ReceptionType.Sender : ReceptionType.Receiver));
		}
	}
	public AiProfile? SelectedAi
	{
		get => selectedAi;
		set
		{
			if (IsBusy) return;
			if (!SetProperty(ref selectedAi, value) || loading) return;
			Assistant = value is null ? null : workspace.GetAssistant(value);
			Status = "";
		}
	}
	public AiProfile? EditingProfile
	{
		get => editingProfile;
		set
		{
			if (!SetProperty(ref editingProfile, value)) return;
			RefreshCommands();
			if (value is null) return;
			loading = true;
			editingId = value.Id;
			SelectedProvider = Providers.FirstOrDefault(p => p.Name == value.Provider);
			ProfileName = value.Name; Endpoint = value.Endpoint; Model = value.Model;
			ApiKey = value.ApiKey; EnableTools = value.EnableTools;
			loading = false;
			SettingsStatus = "";
		}
	}
	public ProviderPreset? SelectedProvider
	{
		get => selectedProvider;
		set
		{
			if (!SetProperty(ref selectedProvider, value) || loading || value is null) return;
			FillPreset(value);
			ApiKey = "";
		}
	}
	public string InputText { get => inputText; set { if (SetProperty(ref inputText, value)) RefreshCommands(); } }
	public string Status { get => status; private set => SetProperty(ref status, value); }
	public string SettingsStatus { get => settingsStatus; private set => SetProperty(ref settingsStatus, value); }
	public string ProfileName { get => profileName; set => SetProperty(ref profileName, value); }
	public string Endpoint { get => endpoint; set => SetProperty(ref endpoint, value); }
	public string Model { get => model; set => SetProperty(ref model, value); }
	public string ApiKey { get => apiKey; set => SetProperty(ref apiKey, value); }
	public bool EnableTools { get => enableTools; set => SetProperty(ref enableTools, value); }
	public bool IsSettingsOpen
	{
		get => isSettingsOpen;
		private set { SetProperty(ref isSettingsOpen, value); }
	}
	public bool IsBusy
	{
		get => isBusy;
		private set
		{
			if (!SetProperty(ref isBusy, value)) return;
			OnPropertyChanged(nameof(IsIdle)); OnPropertyChanged(nameof(SendToolTip));
			RefreshCommands();
		}
	}
	private void RefreshCommands()
	{
		SendCommand?.RaiseCanExecuteChanged(); StopCommand?.RaiseCanExecuteChanged();
		SendOrStopCommand?.RaiseCanExecuteChanged(); ToggleSettingsCommand?.RaiseCanExecuteChanged();
		NewChatCommand?.RaiseCanExecuteChanged(); AddProfileCommand?.RaiseCanExecuteChanged();
		SaveProfileCommand?.RaiseCanExecuteChanged(); DeleteProfileCommand?.RaiseCanExecuteChanged();
	}
	private void EnsureIdle()
	{
		if (IsBusy) throw new InvalidOperationException("请先停止当前对话。");
	}
	public void LoadConfiguration()
	{
		EnsureIdle();
		if (ConfigurationPath is null) return;
		var profiles = ProfileStorage.Load(ConfigurationPath);
		SelectedAi = null; EditingProfile = null;
		foreach (var profile in Profiles) workspace.Reset(profile.Id);
		Profiles.Clear();
		foreach (var profile in profiles) Profiles.Add(profile);
		SelectedAi = Profiles.FirstOrDefault();
	}
	private void FillPreset(ProviderPreset preset)
	{
		ProfileName = preset.Name; Endpoint = preset.Endpoint; Model = preset.Model;
	}
	private void AddProfile()
	{
		editingId = null; EditingProfile = null;
		SelectedProvider = Providers[0];
		FillPreset(SelectedProvider);
		ApiKey = ""; EnableTools = true;
		SettingsStatus = "填写配置后保存，即可在聊天页选择此 AI。";
	}
	private void SaveProfile()
	{
		try
		{
			EnsureIdle();
			var preset = SelectedProvider ?? throw new ArgumentException("请选择 API 类型。");
			if (string.IsNullOrWhiteSpace(ProfileName) || string.IsNullOrWhiteSpace(Model))
				throw new ArgumentException("显示名称和模型 ID 不能为空。");
			if (!Uri.TryCreate(Endpoint.Trim(), UriKind.Absolute, out var uri) ||
				uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo))
				throw new ArgumentException("请输入有效的 HTTP 或 HTTPS API 基础地址。");
			if (preset.Name != "Ollama（本地）" && string.IsNullOrWhiteSpace(ApiKey))
				throw new ArgumentException("请输入 API 密钥。");
			var old = Profiles.FirstOrDefault(p => p.Id == editingId);
			var profile = new AiProfile
			{
				Id = old?.Id ?? Guid.NewGuid(),
				Name = ProfileName.Trim(),
				Provider = preset.Name,
				Endpoint = uri.AbsoluteUri,
				Model = Model.Trim(),
				ApiKey = ApiKey,
				EnableTools = EnableTools
			};
			if (ConfigurationPath is not null)
				ProfileStorage.Save(ConfigurationPath, Profiles.Where(p => p.Id != profile.Id).Append(profile));
			var index = old is null ? -1 : Profiles.IndexOf(old);
			loading = true;
			if (index >= 0) Profiles[index] = profile; else Profiles.Add(profile);
			workspace.Reset(profile.Id);
			SelectedAi = profile;
			loading = false;
			Assistant = workspace.GetAssistant(profile);
			EditingProfile = profile;
			editingId = profile.Id;
			IsSettingsOpen = false;
			Status = $"已选择 {profile.Name}";
		}
		catch (Exception ex) { loading = false; SettingsStatus = ex.Message; }
	}
	private void DeleteProfile()
	{
		if (EditingProfile is not { } profile) return;
		try
		{
			EnsureIdle();
			if (ConfigurationPath is not null)
				ProfileStorage.Save(ConfigurationPath, Profiles.Where(p => p.Id != profile.Id));
			Profiles.Remove(profile); workspace.Reset(profile.Id);
			if (SelectedAi?.Id == profile.Id) SelectedAi = Profiles.FirstOrDefault();
			AddProfile();
		}
		catch (Exception ex) { SettingsStatus = ex.Message; }
	}
	public async Task SendAsync()
	{
		if (IsBusy || string.IsNullOrWhiteSpace(InputText)) return;
		var current = Assistant;
		if (current is null) { Status = "请先点击右上角设置，添加并选择 AI。"; return; }
		var text = InputText.Trim();
		Messages.Add(new(text, ReceptionType.Sender));
		InputText = "";
		using var source = new CancellationTokenSource();
		cancellation = source;
		IsBusy = true; Status = "正在思考…";
		try
		{
			// Progress marshals back to the caller's UI context, not a view reference.
			var progress = new Progress<string>(message => { if (ReferenceEquals(cancellation, source)) Status = message; });
			var uiContext = SynchronizationContext.Current;
            ChatMessageViewModel? reply = null;
            void AppendFragment(string fragment)
            {
                if (!ReferenceEquals(cancellation, source) || source.IsCancellationRequested) return;
                if (reply is null)
                {
                    reply = new(fragment, ReceptionType.Receiver);
                    Messages.Add(reply);
                }
                else
                {
                    reply.Text += fragment;
                }
            }

            await current.SendAsync(text, message => ((IProgress<string>)progress).Report(message), source.Token,
                fragment =>
                {
                    if (uiContext is null || ReferenceEquals(uiContext, SynchronizationContext.Current))
                        AppendFragment(fragment);
                    else
                        uiContext.Send(_ => AppendFragment(fragment), null);
                });
            Status = "";
		}
		catch (OperationCanceledException) { Status = "已停止。已执行的业务操作可能已经生效。"; }
		catch (Exception ex) { Status = ex.Message; }
		finally { cancellation = null; IsBusy = false; }
	}
	private void NewChat()
	{
		EnsureIdle();
		if (SelectedAi is { } profile)
		{
			workspace.Reset(profile.Id);
			Assistant = workspace.GetAssistant(profile);
		}
		else if (AssistantFactory is not null) Assistant = AssistantFactory();
		Status = "已清空当前对话";
	}
}