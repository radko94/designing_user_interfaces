using SecureMesh.App.Services;
using SecureMesh.Core;
using SecureMesh.Core.Domain;
using ZXing.Net.Maui;

namespace SecureMesh.App.Pages;

public partial class AddContactPage : ContentPage
{
    private readonly ContactsService _contacts;
    private readonly IdentityService _identity;
    private bool _handled;

    public AddContactPage()
    {
        InitializeComponent();
        _contacts = ServiceHelper.Get<ContactsService>();
        _identity = ServiceHelper.Get<IdentityService>();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        try
        {
            var status = await Permissions.RequestAsync<Permissions.Camera>();
            if (status != PermissionStatus.Granted)
            {
                StatusLabel.Text = "Няма достъп до камерата.";
                return;
            }

            BarcodeView.IsDetecting = true;
        }
        catch
        {
            StatusLabel.Text = "Камерата не е налична.";
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        try { BarcodeView.IsDetecting = false; } catch { }
    }

    private async void OnBarcodesDetected(object? sender, BarcodeDetectionEventArgs e)
    {
        if (_handled)
            return;

        var payload = PeerQrPayload.TryDecode(e.Results?.FirstOrDefault()?.Value);
        if (payload is null)
            return;

        _handled = true;
        try { BarcodeView.IsDetecting = false; } catch { }

        var existing = _contacts.FindByEd25519Pub(payload.SigningPublicKey);
        if (existing is not null)
        {
            await DisplayAlertAsync("Вече съществува", $"Контактът „{existing.Nickname}“ вече е добавен.", "Добре");
            await Shell.Current.GoToAsync("..");
            return;
        }

        // Safety number: order-independent so both peers see the same value.
        var fingerprint = Fingerprint.Compute(_identity.Identity.SigningPublicKey, payload.SigningPublicKey);

        var confirmed = await DisplayAlertAsync(
            "Потвърдете контакта",
            $"Искате да добавите „{payload.Nickname}“?\n\nНомер за сигурност:\n{fingerprint}",
            "Добави",
            "Отказ");

        if (!confirmed)
        {
            await Shell.Current.GoToAsync("..");
            return;
        }

        _contacts.AddContact(payload.Nickname, payload.SigningPublicKey, payload.DhPublicKey);

        await DisplayAlertAsync(
            "Добавен",
            "Контактът е добавен. Сравнете номера за сигурност при среща, за да потвърдите самоличността.",
            "Добре");

        await Shell.Current.GoToAsync("..");
    }

    private async void OnCancelClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("..");
    }
}
