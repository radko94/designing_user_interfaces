namespace Suma1_0;

public partial class MainPage : ContentPage
{
    public MainPage()
    {
        InitializeComponent();
    }

    private void OnButtonClicked(object sender, EventArgs e)
    {
        errorLabel.IsVisible = false;
        boxAB.Text = string.Empty;

        if (!int.TryParse(boxA.Text, out int a) ||
            !int.TryParse(boxB.Text, out int b))
        {
            errorLabel.Text = "Please enter two valid integers.";
            errorLabel.IsVisible = true;
            return;
        }

        try
        {
            int result = checked(a + b);
            boxAB.Text = result.ToString();
        }
        catch (OverflowException)
        {
            errorLabel.Text = "The result exceeds the integer range.";
            errorLabel.IsVisible = true;
        }
    }
}