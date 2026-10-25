namespace Suma3_0;

public partial class MainPage : ContentPage
{
    public MainPage()
    {
        InitializeComponent();
    }

    private async void OnButtonClicked(object sender, EventArgs e)
    {
        errorLabel.IsVisible = false;
        boxAB.Text = string.Empty;

        if (!int.TryParse(boxA.Text, out int a) ||
            !int.TryParse(boxB.Text, out int b))
        {
            await ShowErrorAsync("Please enter two valid integers.");
            return;
        }

        try
        {
            int result = checked(a + b);
            boxAB.Text = result.ToString();
        }
        catch (OverflowException)
        {
            await ShowErrorAsync("The result exceeds the integer range.");
        }
    }

    private async Task ShowErrorAsync(string message)
    {
        errorLabel.Text = message;
        errorLabel.IsVisible = true;
        await DisplayAlertAsync("Suma 3.0", message, "OK");
    }
}
