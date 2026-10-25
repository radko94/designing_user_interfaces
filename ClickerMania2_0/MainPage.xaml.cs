
using System.Globalization;
using Microsoft.Maui.Dispatching;

namespace ClickerMania2_0;

public partial class MainPage : ContentPage
{
    // Number of clicks and elapsed seconds.
    private long _clicks = 0;
    private long _elapsedSeconds = 0;

    // MAUI UI-thread timer.
    private IDispatcherTimer? _timer;

    public MainPage()
    {
        InitializeComponent();
    }

    // Start the timer when the page becomes visible.
    protected override void OnAppearing()
    {
        base.OnAppearing();

        // Create the timer only once.
        if (_timer is null)
        {
            _timer = Dispatcher.CreateTimer();
            _timer.Interval = TimeSpan.FromSeconds(1);
            _timer.IsRepeating = true;
            _timer.Tick += OnTimerTick;
        }

        _timer.Start();
    }

    // Stop the timer when leaving the page.
    protected override void OnDisappearing()
    {
        _timer?.Stop();

        base.OnDisappearing();
    }

    // Called every second while the timer is running.
    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (_elapsedSeconds == long.MaxValue)
        {
            _timer?.Stop();
            return;
        }

        // Increase elapsed time.
        _elapsedSeconds++;

        // Update timer display.
        TimerLabel.Text = _elapsedSeconds.ToString(
            CultureInfo.InvariantCulture);

        // Calculate average clicks per minute.
        UpdateCpm();
    }

    // Called whenever the user clicks the button.
    private void OnClickButtonClicked(object sender, EventArgs e)
    {
        if (_clicks == long.MaxValue)
        {
            ClickButton.IsEnabled = false;
            return;
        }

        // Increase click counter.
        _clicks++;

        // Update click display.
        ClicksLabel.Text = _clicks.ToString(
            CultureInfo.InvariantCulture);

        // Refresh clicks-per-minute display.
        UpdateCpm();
    }

    // Calculate and display average clicking speed.
    private void UpdateCpm()
    {
        if (_elapsedSeconds == 0)
        {
            CpmLabel.Text = "0.00";
            return;
        }

        double clicksPerMinute =
            (double)_clicks / _elapsedSeconds * 60.0;

        CpmLabel.Text = clicksPerMinute.ToString(
            "F2",
            CultureInfo.InvariantCulture);
    }
}
