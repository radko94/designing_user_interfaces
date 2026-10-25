namespace Fibonacci2_0;

public partial class MainPage : ContentPage
{
    public MainPage()
    {
        InitializeComponent();
    }

    // Generate button event handler.
    private async void OnButtonClicked(object sender, EventArgs e)
    {
        // Validate user input.
        if (!int.TryParse(boxLimit.Text, out int limit) || limit <= 0)
        {
            await DisplayAlertAsync(
                "Invalid limit",
                "Please enter a positive whole number.",
                "OK");

            return;
        }

        // Build a fresh list on every click: results replace, never accumulate.
        List<int> numbers = new();

        // Start with the lecturer's sequence: 1, 1.
        long a = 1;
        long b = 1;

        // Generate numbers strictly below the limit.
        while (a < limit)
        {
            numbers.Add((int)a);

            long next = a + b;
            a = b;
            b = next;
        }

        boxNumbers.ItemsSource = numbers;
    }
}
