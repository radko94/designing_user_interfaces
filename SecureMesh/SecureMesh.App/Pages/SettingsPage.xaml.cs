using System.Text.Json;
using Microsoft.Maui.Storage;
using SecureMesh.App.Services;
using SecureMesh.Core.Domain;

namespace SecureMesh.App.Pages;

public partial class SettingsPage : ContentPage
{
    private readonly IdentityService _identity;
    private readonly ContactsService _contacts;

    public SettingsPage()
    {
        InitializeComponent();
        _identity = ServiceHelper.Get<IdentityService>();
        _contacts = ServiceHelper.Get<ContactsService>();
    }

    private async void OnExportClicked(object? sender, EventArgs e)
    {
        try
        {
            var identity = _identity.Identity;
            var contacts = _contacts.ListContacts();

            var export = new
            {
                ExportedAt = DateTime.UtcNow,
                Nickname = _identity.Nickname,
                SigningPublicKey = Convert.ToBase64String(identity.SigningPublicKey),
                DhPublicKey = Convert.ToBase64String(identity.DhPublicKey),
                ContactCount = contacts.Count,
                Contacts = contacts.Select(c => new
                {
                    c.Id,
                    c.Nickname,
                    c.Verified,
                    SigningPublicKey = Convert.ToBase64String(c.Ed25519Pub),
                }).ToArray(),
            };

            var path = Path.Combine(FileSystem.AppDataDirectory, "securemesh-export.json");
            File.WriteAllText(path, JsonSerializer.Serialize(export, new JsonSerializerOptions { WriteIndented = true }));

            await DisplayAlertAsync("Експортиране", $"Данните са записани в:\n{path}", "Добре");
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Грешка", $"Експортирането не успя: {ex.Message}", "Добре");
        }
    }

    private async void OnDeleteClicked(object? sender, EventArgs e)
    {
        var confirmed = await DisplayAlertAsync(
            "Изтриване на данни",
            "Това ще изтрие самоличността, контактите и съобщенията от това устройство. Действието е необратимо.",
            "Изтрий",
            "Отказ");

        if (!confirmed)
            return;

        try
        {
            DeleteIfExists(Path.Combine(FileSystem.AppDataDirectory, "securemesh.db3"));
            DeleteIfExists(Path.Combine(FileSystem.AppDataDirectory, "identity.json"));
            DeleteIfExists(Path.Combine(FileSystem.AppDataDirectory, "securemesh-export.json"));

            await DisplayAlertAsync("Готово", "Локалните данни са изтрити. Рестартирайте приложението.", "Добре");
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Грешка", $"Изтриването не успя: {ex.Message}", "Добре");
        }
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }
}
