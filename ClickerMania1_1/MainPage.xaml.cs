
using System.Globalization;
using Microsoft.Maui.Storage;

namespace ClickerMania1_1;

public partial class MainPage : ContentPage
{
    private const string FileName = "clicker.txt";

    private readonly string _filePath =
        Path.Combine(FileSystem.Current.AppDataDirectory, FileName);

    private int _counter = 0;

    public MainPage()
    {
        InitializeComponent();

        // Disable clicking until the saved value is loaded.
        ClickButton.IsEnabled = false;

        // Load the saved counter when the page first appears.
        Loaded += OnPageLoaded;
    }

    // Read the previously saved number of clicks.
    private async void OnPageLoaded(object? sender, EventArgs e)
    {
        Loaded -= OnPageLoaded;

        try
        {
            if (File.Exists(_filePath))
            {
                string content = await File.ReadAllTextAsync(_filePath);

                if (int.TryParse(
                        content,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out int savedCounter) &&
                    savedCounter >= 0)
                {
                    _counter = savedCounter;
                }
                else
                {
                    // Invalid saved data: start from zero.
                    _counter = 0;

                    await DisplayAlertAsync(
                        "Invalid saved data",
                        "The saved counter is invalid. " +
                        "Starting from zero.",
                        "OK");
                }
            }
        }
        catch (IOException)
        {
            await DisplayAlertAsync(
                "Read error",
                "The saved counter could not be read.",
                "OK");
        }
        catch (UnauthorizedAccessException)
        {
            await DisplayAlertAsync(
                "Access denied",
                "The application cannot access its saved data.",
                "OK");
        }
        finally
        {
            // Update the counter display.
            ClicksLabel.Text = _counter.ToString(
                CultureInfo.InvariantCulture);

            ClickButton.IsEnabled = true;
        }
    }

    // Increment and persist the counter.
    private async void OnClickButtonClicked(
        object sender,
        EventArgs e)
    {
        // Prevent integer overflow.
        if (_counter == int.MaxValue)
        {
            await DisplayAlertAsync(
                "Counter limit",
                "The maximum counter value has been reached.",
                "OK");

            return;
        }

        // Prevent overlapping file writes.
        ClickButton.IsEnabled = false;

        int nextCounter = _counter + 1;

        try
        {
            // Save the new value in application storage.
            await File.WriteAllTextAsync(
                _filePath,
                nextCounter.ToString(CultureInfo.InvariantCulture));

            // Update the UI after a successful save.
            _counter = nextCounter;

            ClicksLabel.Text = _counter.ToString(
                CultureInfo.InvariantCulture);
        }
        catch (IOException)
        {
            await DisplayAlertAsync(
                "Save error",
                "The counter could not be saved.",
                "OK");
        }
        catch (UnauthorizedAccessException)
        {
            await DisplayAlertAsync(
                "Access denied",
                "The application cannot save its data.",
                "OK");
        }
        finally
        {
            ClickButton.IsEnabled = true;
        }
    }
}
