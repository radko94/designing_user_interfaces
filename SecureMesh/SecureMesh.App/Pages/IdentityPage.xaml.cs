using SecureMesh.App.Services;

namespace SecureMesh.App.Pages;

public partial class IdentityPage : ContentPage
{
    private readonly IdentityService _identity;
    private bool _navigating;

    public IdentityPage()
    {
        InitializeComponent();
        _identity = ServiceHelper.Get<IdentityService>();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // If the identity was already set up on a previous launch, skip ahead.
        if (_identity.IsInitialized)
        {
            if (_navigating)
                return;
            _navigating = true;
            await Shell.Current.GoToAsync("//contacts");
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

        var payload = PeerQrPayload.FromIdentity(_identity.Nickname, _identity.Identity).Encode();
        QrView.Value = payload;

        HintLabel.Text = "Идентичността е създадена. Сканирайте този код с другото устройство.";
        CreateBtn.IsVisible = false;
        NicknameEntry.IsVisible = false;
        QrSection.IsVisible = true;
    }

    private async void OnContinueClicked(object? sender, EventArgs e)
    {
        if (!_identity.IsInitialized || _navigating)
            return;
        _navigating = true;
        await Shell.Current.GoToAsync("//contacts");
    }
}
