namespace Primes2_0;

public partial class MainPage : ContentPage
{
    // Practical upper limit to avoid excessive resource usage.
    private const int MaxLimit = 1_000_000;

    public MainPage()
    {
        InitializeComponent();
    }

    // Generate button event handler.
    private async void Button_Click(object sender, EventArgs e)
    {
        // Validate the entered limit.
        if (!int.TryParse(boxLimit.Text, out int limit)
            || limit <= 0
            || limit > MaxLimit)
        {
            await DisplayAlertAsync(
                "Invalid limit",
                $"Enter a positive whole number up to {MaxLimit}.",
                "OK");

            return;
        }

        // Prevent repeated clicks during generation.
        GenerateButton.IsEnabled = false;

        try
        {
            // Calculate without blocking the UI thread.
            List<int> primes = await Task.Run(
                () => GeneratePrimes(limit));

            // Replace previous results.
            boxNumbers.ItemsSource = primes;
        }
        finally
        {
            GenerateButton.IsEnabled = true;
        }
    }

    // Generate all prime numbers strictly below limit.
    private static List<int> GeneratePrimes(int limit)
    {
        List<int> primes = new();

        for (int number = 2; number < limit; number++)
        {
            bool isPrime = true;

            // Test divisors only up to the square root.
            for (int divisor = 2;
                 divisor <= number / divisor;
                 divisor++)
            {
                if (number % divisor == 0)
                {
                    isPrime = false;
                    break;
                }
            }

            if (isPrime)
            {
                primes.Add(number);
            }
        }

        return primes;
    }
}
