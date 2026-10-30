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

        // Step 1 — add confirmation. The contact is only persisted after the
        // user explicitly accepts; it is created unverified (TOFU).
        var confirmed = await DisplayAlertAsync(
            "Добавяне на контакт",
            $"Искате да добавите „{payload.Nickname}“?\n\nНомер за сигурност:\n{fingerprint}",
            "Добави",
            "Отказ");

        if (!confirmed)
        {
            await Shell.Current.GoToAsync("..");
            return;
        }

        var contact = _contacts.AddContact(
            payload.Nickname, payload.SigningPublicKey, payload.DhPublicKey, payload.EphemeralPublicKey);

        // Step 2 — out-of-band safety-number comparison. Only a matching
        // comparison marks the contact "Проверен"; "Не" keeps it unverified.
        var matches = await DisplayAlertAsync(
            "Сравнихте ли номера за сигурност?",
            $"Сравнете този номер с показания от {payload.Nickname}:\n\n{fingerprint}",
            "Да, съвпадат",
            "Не");

        if (matches)
            _contacts.VerifyContact(contact.Id);

        await DisplayAlertAsync(
            "Добавен",
            matches
                ? $"Контактът „{payload.Nickname}“ е добавен и потвърден."
                : $"Контактът „{payload.Nickname}“ е добавен като непроверен. Сравнете номера за сигурност при среща, за да потвърдите самоличността.",
            "Добре");

        await Shell.Current.GoToAsync("..");
    }

    private async void OnCancelClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("..");
    }
}
