using System.Collections.ObjectModel;

namespace Fibonacci1_0;

public partial class MainPage : ContentPage
{
    // Collection of generated Fibonacci numbers.
    private readonly ObservableCollection<int> numbers = new();

    public MainPage()
    {
        InitializeComponent();

        // Connect the collection to the UI list.
        boxNumbers.ItemsSource = numbers;
    }

    // Generate button event handler.
    private async void Button_Click(object sender, EventArgs e)
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

        // Remove previous results.
        numbers.Clear();

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
    }
}