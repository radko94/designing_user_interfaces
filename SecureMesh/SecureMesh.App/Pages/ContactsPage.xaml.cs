using SecureMesh.App.Services;
using SecureMesh.App.Transport;
using SecureMesh.Core;
using SecureMesh.Core.Domain;

namespace SecureMesh.App.Pages;

public partial class ContactsPage : ContentPage
{
    private readonly ContactsService _contacts;
    private readonly IdentityService _identity;
    private readonly ITransport _transport;
    private bool _connected;

    public ContactsPage()
    {
        InitializeComponent();
        _contacts = ServiceHelper.Get<ContactsService>();
        _identity = ServiceHelper.Get<IdentityService>();
        _transport = ServiceHelper.Get<ITransport>();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        _transport.PeerConnected += OnPeerConnected;
        _transport.PeerDisconnected += OnPeerDisconnected;
        UpdateConnectionLabel();
        Refresh();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _transport.PeerConnected -= OnPeerConnected;
        _transport.PeerDisconnected -= OnPeerDisconnected;
    }

    private void OnPeerConnected(string peerId)
    {
        _connected = true;
        UpdateConnectionLabel();
    }

    private void OnPeerDisconnected(string peerId)
    {
        _connected = false;
        UpdateConnectionLabel();
    }

    private void UpdateConnectionLabel()
    {
        if (_connected)
        {
            ConnectionLabel.Text = "Свързан";
            ConnectionLabel.TextColor = Colors.Green;
        }
        else
        {
            ConnectionLabel.Text = "Изключено";
            ConnectionLabel.TextColor = Color.FromArgb("#B45309");
        }
    }

    private void Refresh()
    {
        var items = _contacts.ListContacts()
            .OrderBy(c => c.Nickname, StringComparer.CurrentCulture)
            .Select(c => new ContactItem(
                c.Id,
                c.Nickname,
                c.Verified ? "✓ Проверен" : "Непроверен",
                c.Verified ? Colors.Green : Colors.Orange))
            .ToList();

        ContactsView.ItemsSource = items;
    }

    private async void OnContactSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not ContactItem item)
            return;

        ContactsView.SelectedItem = null;
        await Shell.Current.GoToAsync($"{nameof(ChatPage)}?contactId={item.Id}");
    }

    private async void OnVerifyContact(object? sender, EventArgs e)
    {
        if ((sender as SwipeItem)?.CommandParameter is not ContactItem item)
            return;

        var contact = _contacts.GetById(item.Id);
        if (contact is null)
            return;

        var fingerprint = Fingerprint.Compute(_identity.Identity.SigningPublicKey, contact.Ed25519Pub);

        if (contact.Verified)
        {
            await DisplayAlertAsync(
                "Номер за сигурност",
                $"{contact.Nickname}\n\n{fingerprint}\n\nТози контакт е потвърден.",
                "Добре");
            return;
        }

        // Re-show the safety number and offer the out-of-band comparison that
        // marks the contact verified.
        var matches = await DisplayAlertAsync(
            "Сравнихте ли номера за сигурност?",
            $"Сравнете този номер с показания от {contact.Nickname}:\n\n{fingerprint}",
            "Да, съвпадат",
            "Не");

        if (matches)
        {
            _contacts.VerifyContact(contact.Id);
            Refresh();
        }
    }

    private async void OnDeleteContact(object? sender, EventArgs e)
    {
        if ((sender as SwipeItem)?.CommandParameter is not ContactItem item)
            return;

        var contact = _contacts.GetById(item.Id);
        if (contact is null)
            return;

        var confirmed = await DisplayAlertAsync(
            "Изтриване на контакт",
            $"Сигурни ли сте, че искате да изтриете „{contact.Nickname}“?",
            "Изтрий",
            "Отказ");

        if (!confirmed)
            return;

        _contacts.RemoveContact(contact.Id);
        Refresh();
    }

    private async void OnAddContactClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync(nameof(AddContactPage));
    }

    private async void OnSettingsClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync(nameof(SettingsPage));
    }

    private sealed record ContactItem(int Id, string Name, string Badge, Color BadgeColor);
}
