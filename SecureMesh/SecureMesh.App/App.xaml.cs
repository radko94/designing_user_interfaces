using SecureMesh.App.Services;
using SecureMesh.Core.Domain;

namespace SecureMesh.App;

public partial class App : Application
{
	private readonly IdentityService _identity;
	private readonly ChatService _chat;

	public App(IdentityService identity, ChatService chat)
	{
		InitializeComponent();

		_identity = identity;
		_chat = chat;

		// Seed (load or generate + persist) the long-term identity on startup.
		_identity.EnsureSeeded();
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		// Start the BLE transport (and ChatService's incoming handler) in the
		// background; failures (e.g. Bluetooth disabled) degrade gracefully.
		_ = Task.Run(async () =>
		{
			try
			{
				await _chat.StartAsync();
			}
			catch
			{
				// Bluetooth unavailable — the app still works for identity/QR/contacts.
			}
		});

		return new Window(new AppShell());
	}
}
