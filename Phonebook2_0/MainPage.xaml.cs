namespace Phonebook2_0;

public partial class MainPage : ContentPage
{
    public MainPage()
    {
        InitializeComponent();

        // Bind the shared contact collection.
        boxContacts.ItemsSource = App.Contacts;
    }

    private async void OnAddClicked(object sender, EventArgs e)
    {
        // Navigate to the Add screen.
        await Navigation.PushAsync(new AddPage());
    }
}
