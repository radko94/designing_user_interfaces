namespace SecureMesh.App;

public partial class AppShell : Shell
{
	public AppShell()
	{
		InitializeComponent();

		// Build the bottom tab bar in code: the .NET 10 XAML source generator
		// does not reliably emit bare <ShellContent> children as tabs, which
		// left an empty TabLayout. Constructing ShellContent directly works.
		var tabs = new TabBar();
		tabs.Items.Add(new ShellContent { Title = "Чатове", Route = "chats", ContentTemplate = new DataTemplate(typeof(Pages.ConversationListPage)) });
		tabs.Items.Add(new ShellContent { Title = "Контакти", Route = "contacts", ContentTemplate = new DataTemplate(typeof(Pages.ContactsPage)) });
		tabs.Items.Add(new ShellContent { Title = "Групи", Route = "groups", ContentTemplate = new DataTemplate(typeof(Pages.GroupListPage)) });
		Items.Add(tabs);

		Routing.RegisterRoute(nameof(Pages.AddContactPage), typeof(Pages.AddContactPage));
		Routing.RegisterRoute(nameof(Pages.ChatPage), typeof(Pages.ChatPage));
		Routing.RegisterRoute(nameof(Pages.SettingsPage), typeof(Pages.SettingsPage));
		Routing.RegisterRoute(nameof(Pages.IdentityPage), typeof(Pages.IdentityPage));
		Routing.RegisterRoute(nameof(Pages.NewGroupPage), typeof(Pages.NewGroupPage));
		Routing.RegisterRoute(nameof(Pages.GroupChatPage), typeof(Pages.GroupChatPage));
	}
}
