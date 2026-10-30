using SecureMesh.App.Services;
using SecureMesh.Core.Domain;

namespace SecureMesh.App;

public partial class App : Application
{
	private readonly IdentityService _identity;
	private readonly ChatService _chat;
	private readonly GroupChatService _groupChat;

	public App(IdentityService identity, ChatService chat, GroupChatService groupChat)
	{
		InitializeComponent();

		_identity = identity;
		_chat = chat;
		_groupChat = groupChat;

		// Seed (load or generate + persist) the long-term identity on startup.
		_identity.EnsureSeeded();
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		// Start the BLE transport (and ChatService's incoming handler) in the
		// background; failures (e.g. Bluetooth disabled) degrade gracefully.
		_ = Task.Run(async () =>
		{
			try { await _chat.StartAsync(); } catch { }
			try { await _groupChat.StartAsync(); } catch { }
		});

		var shell = new AppShell();
		if (!_identity.IsInitialized)
		{
			shell.Loaded += async (_, _) =>
			{
				if (!_identity.IsInitialized)
					await shell.GoToAsync(nameof(Pages.IdentityPage));
			};
		}
		return new Window(shell);
	}
}
