namespace SecureMesh.App;

public partial class AppShell : Shell
{
	public AppShell()
	{
		InitializeComponent();

		Routing.RegisterRoute(nameof(Pages.AddContactPage), typeof(Pages.AddContactPage));
		Routing.RegisterRoute(nameof(Pages.ChatPage), typeof(Pages.ChatPage));
		Routing.RegisterRoute(nameof(Pages.SettingsPage), typeof(Pages.SettingsPage));
		Routing.RegisterRoute(nameof(Pages.IdentityPage), typeof(Pages.IdentityPage));
		Routing.RegisterRoute(nameof(Pages.NewGroupPage), typeof(Pages.NewGroupPage));
		Routing.RegisterRoute(nameof(Pages.GroupChatPage), typeof(Pages.GroupChatPage));
	}
}
