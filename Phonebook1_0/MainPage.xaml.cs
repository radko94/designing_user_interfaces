namespace Phonebook1_0;

public partial class MainPage : ContentPage
{
    public MainPage()
    {
        InitializeComponent();

        // Bind the shared contact collection.
        boxContacts.ItemsSource = App.Contacts;
    }

    private async void OnAddContactClicked(object sender, EventArgs e)
    {
        // Navigate to the Add Contact screen.
        await Navigation.PushAsync(new AddPage());
    }
}