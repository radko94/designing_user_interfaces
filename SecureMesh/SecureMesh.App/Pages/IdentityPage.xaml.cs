using Microsoft.Maui.ApplicationModel.DataTransfer;
using SecureMesh.App.Services;

namespace SecureMesh.App.Pages;

public partial class IdentityPage : ContentPage
{
    private readonly IdentityService _identity;
    private bool _navigating;
    private string _pairingCode = "";

    public IdentityPage()
    {
        InitializeComponent();
        _identity = ServiceHelper.Get<IdentityService>();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // IdentityPage is only pushed when onboarding is needed; if the identity
        // was set up in the meantime, pop back to the shell tabs.
        if (_identity.IsInitialized)
        {
            if (_navigating)
                return;
            _navigating = true;
            await Shell.Current.GoToAsync("..");
        }
    }

    private void OnCreateClicked(object? sender, EventArgs e)
    {
        var nickname = NicknameEntry.Text?.Trim();
        if (string.IsNullOrWhiteSpace(nickname))
        {
            _ = DisplayAlertAsync("Грешка", "Моля, въведете име.", "Добре");
            return;
        }

        _identity.SetNickname(nickname);

        _pairingCode = PeerQrPayload.FromIdentity(_identity.Nickname, _identity.Identity).Encode();
        QrView.Value = _pairingCode;
        PairingCodeEditor.Text = _pairingCode;

        HintLabel.Text = "Идентичността е създадена. Сканирайте този код с другото устройство.";
        CreateBtn.IsVisible = false;
        NicknameEntry.IsVisible = false;
        QrSection.IsVisible = true;
    }

    private async void OnCopyPairingCodeClicked(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_pairingCode))
            return;

        await Clipboard.Default.SetTextAsync(_pairingCode);
        await DisplayAlertAsync("Копирано", "Кодът за сдвояване е копиран в клипборда.", "Добре");
    }

    private async void OnContinueClicked(object? sender, EventArgs e)
    {
        if (!_identity.IsInitialized || _navigating)
            return;
        _navigating = true;
        await Shell.Current.GoToAsync("..");
    }
}
