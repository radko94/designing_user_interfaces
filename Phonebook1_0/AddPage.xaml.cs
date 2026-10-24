
using Phonebook1_0.Models;

namespace Phonebook1_0;

public partial class AddPage : ContentPage
{
    public AddPage()
    {
        InitializeComponent();
    }

    private async void OnSaveClicked(object sender, EventArgs e)
    {
        // Read input values.
        string name = boxName.Text?.Trim() ?? string.Empty;
        string phone = boxPhone.Text?.Trim() ?? string.Empty;
        string pictureUrl = boxPicture.Text?.Trim() ?? string.Empty;

        // Validate required fields.
        // Picture URL is optional.
        if (string.IsNullOrWhiteSpace(name) ||
            string.IsNullOrWhiteSpace(phone))
        {
            await DisplayAlertAsync(
                "Missing information",
                "Please enter a name and phone number.",
                "OK");

            return;
        }

        // Validate the picture URL only if provided.
        if (!string.IsNullOrWhiteSpace(pictureUrl))
        {
            bool isValidUrl =
                Uri.TryCreate(
                    pictureUrl,
                    UriKind.Absolute,
                    out Uri? uri) &&
                (uri.Scheme == Uri.UriSchemeHttp ||
                 uri.Scheme == Uri.UriSchemeHttps);

            if (!isValidUrl)
            {
                await DisplayAlertAsync(
                    "Invalid picture",
                    "Please enter a valid HTTP or HTTPS URL.",
                    "OK");

                return;
            }
        }

        // Prevent repeated submissions.
        SaveButton.IsEnabled = false;

        try
        {
            // Create the contact.
            Models.Contact contact = new Models.Contact(
                name,
                phone,
                pictureUrl
            );

            // Add contact to the shared collection.
            App.Contacts.Add(contact);

            // Return to MainPage.
            await Navigation.PopAsync();
        }
        finally
        {
            SaveButton.IsEnabled = true;
        }
    }
}
