using System.Text.Json;
using Microsoft.Maui.ApplicationModel.DataTransfer;
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

            // Public keys only — no private key material.
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

            var json = JsonSerializer.Serialize(export, new JsonSerializerOptions { WriteIndented = true });

            // Hand the JSON to the OS share sheet so the user can save/send it;
            // no internal path is written or shown.
            await Share.Default.RequestAsync(new ShareTextRequest
            {
                Title = "SecureMesh експорт",
                Text = json,
            });
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
            // Encrypted DB + any WAL/SHM sidecars.
            DeleteIfExists(Path.Combine(FileSystem.AppDataDirectory, "securemesh.db3"));
            DeleteIfExists(Path.Combine(FileSystem.AppDataDirectory, "securemesh.db3-wal"));
            DeleteIfExists(Path.Combine(FileSystem.AppDataDirectory, "securemesh.db3-shm"));

            // Legacy plaintext files from older versions.
            DeleteIfExists(Path.Combine(FileSystem.AppDataDirectory, "identity.json"));
            DeleteIfExists(Path.Combine(FileSystem.AppDataDirectory, "securemesh-export.json"));

            // Clear the Keystore-backed identity private keys + DB password, and
            // the public-key/nickname preferences. The in-memory singletons keep
            // running, so a restart is still required.
            KeyVault.WipeSecrets();
            KeyVault.WipePreferences();

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
