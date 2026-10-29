using Microsoft.Extensions.Logging;
using SecureMesh.App.Pages;
using SecureMesh.App.Services;
using SecureMesh.App.Transport;
using SecureMesh.Core;
using SecureMesh.Core.Domain;
using SecureMesh.Core.Domain.Persistence;
using Shiny;
using ZXing.Net.Maui.Controls;

namespace SecureMesh.App;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.UseShiny()
			.UseBarcodeReader()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

#if DEBUG
		builder.Logging.AddDebug();
#endif

		// Identity (seed/persist on startup via App / IdentityService).
		builder.Services.AddSingleton<IdentityService>();
		builder.Services.AddSingleton<IdentityKeyPair>(sp => sp.GetRequiredService<IdentityService>().Identity);

		// Persistence.
		builder.Services.AddSingleton<Database>(_ =>
			new Database(Path.Combine(Microsoft.Maui.Storage.FileSystem.AppDataDirectory, "securemesh.db3")));
		builder.Services.AddSingleton<ContactRepository>(sp => new ContactRepository(sp.GetRequiredService<Database>().Connection));
		builder.Services.AddSingleton<ConversationRepository>(sp => new ConversationRepository(sp.GetRequiredService<Database>().Connection));
		builder.Services.AddSingleton<MessageRepository>(sp => new MessageRepository(sp.GetRequiredService<Database>().Connection));
		builder.Services.AddSingleton<SyncStateRepository>(sp => new SyncStateRepository(sp.GetRequiredService<Database>().Connection));

		// Domain services. Explicit factories avoid DI constructor-selection
		// ambiguity (both classes expose convenience + repository constructors).
		builder.Services.AddSingleton<ContactsService>(sp =>
			new ContactsService(sp.GetRequiredService<Database>()));
		builder.Services.AddSingleton<SyncService>(sp =>
			new SyncService(sp.GetRequiredService<Database>()));

		// Shiny BLE (central + peripheral/hosting roles) — required by BleTransport.
		builder.Services.AddBluetoothLE();
		builder.Services.AddBluetoothLeHosting();

		// Transport + chat. ChatService.StartAsync() wires HandleIncoming to
		// ITransport.MessageReceived and starts the BLE transport.
		builder.Services.AddSingleton<ITransport, BleTransport>();
		builder.Services.AddSingleton<ChatService>(sp => new ChatService(
			sp.GetRequiredService<IdentityKeyPair>(),
			sp.GetRequiredService<ITransport>(),
			sp.GetRequiredService<Database>()));

		// Pages (transient; resolved on navigation).
		builder.Services.AddTransient<IdentityPage>();
		builder.Services.AddTransient<ContactsPage>();
		builder.Services.AddTransient<AddContactPage>();
		builder.Services.AddTransient<ChatPage>();
		builder.Services.AddTransient<SettingsPage>();

		return builder.Build();
	}
}
