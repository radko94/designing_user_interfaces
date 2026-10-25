using System.Text.Json;
using System.Text.Json.Serialization;
using AngleSharp.Html.Parser;
using Microsoft.Maui.Media;

namespace JsonReader2_0;

public partial class MainPage : ContentPage
{
    private static readonly Uri JokeUri =
        new("https://api.chucknorris.io/jokes/random");

    private static readonly HttpClient Client = new()
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    private static readonly HtmlParser Parser = new();

    private bool _isBusy;

    public MainPage()
    {
        InitializeComponent();
    }

    // "New joke" button event handler.
    private async void OnNewJokeClicked(object? sender, EventArgs e)
    {
        if (_isBusy)
        {
            return;
        }

        _isBusy = true;
        NewJokeButton.IsEnabled = false;
        LoadingIndicator.IsRunning = true;

        try
        {
            string json = await Client.GetStringAsync(JokeUri);
            var joke = JsonSerializer.Deserialize<Root>(json);
            string text = joke?.value ?? string.Empty;

            // Strip any HTML markup from the joke text.
            if (!string.IsNullOrWhiteSpace(text))
            {
                var document = Parser.ParseDocument(text);
                text = document.Body?.TextContent?.Trim() ?? text;
            }

            Joke.Text = string.IsNullOrWhiteSpace(text)
                ? "(empty joke)"
                : text;
        }
        catch (HttpRequestException ex)
        {
            await DisplayAlertAsync("Network error",
                $"Unable to download a joke.\n\n{ex.Message}", "OK");
        }
        catch (TaskCanceledException)
        {
            await DisplayAlertAsync("Timeout",
                "The server did not respond within 20 seconds.", "OK");
        }
        catch (JsonException ex)
        {
            await DisplayAlertAsync("Error",
                $"Unable to parse the response.\n\n{ex.Message}", "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error",
                $"Unable to load a joke.\n\n{ex.Message}", "OK");
        }
        finally
        {
            _isBusy = false;
            NewJokeButton.IsEnabled = true;
            LoadingIndicator.IsRunning = false;
        }
    }

    // "Tell joke" button event handler (text-to-speech).
    private async void OnTellJokeClicked(object? sender, EventArgs e)
    {
        string text = Joke.Text ?? string.Empty;

        if (string.IsNullOrWhiteSpace(text) || text == "(empty joke)")
        {
            return;
        }

        try
        {
            await TextToSpeech.Default.SpeakAsync(text);
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Speech unavailable",
                $"Could not speak the joke.\n\n{ex.Message}", "OK");
        }
    }
}

// Data model matching the chucknorris.io response.
public class Root
{
    [JsonPropertyName("categories")] public List<object>? categories { get; set; }
    [JsonPropertyName("created_at")] public string? created_at { get; set; }
    [JsonPropertyName("icon_url")] public string? icon_url { get; set; }
    [JsonPropertyName("id")] public string? id { get; set; }
    [JsonPropertyName("updated_at")] public string? updated_at { get; set; }
    [JsonPropertyName("url")] public string? url { get; set; }
    [JsonPropertyName("value")] public string? value { get; set; }
}
