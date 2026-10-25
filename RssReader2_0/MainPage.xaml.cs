using System.Collections.ObjectModel;
using System.Xml.Linq;
using Microsoft.Maui.ApplicationModel;

namespace RssReader2_0;

public partial class MainPage : ContentPage
{
    private static readonly HttpClient Client = new()
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    private readonly ObservableCollection<Item> _items = new();

    public MainPage()
    {
        InitializeComponent();
        RSS.ItemsSource = _items;
    }

    // Download button event handler.
    private async void OnDownloadClicked(object? sender, EventArgs e)
    {
        string url = URI.Text?.Trim() ?? string.Empty;

        // Validate the URL.
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != Uri.UriSchemeHttp &&
             uri.Scheme != Uri.UriSchemeHttps))
        {
            await DisplayAlertAsync("Invalid URL",
                "Please enter a valid HTTP or HTTPS feed URL.", "OK");
            return;
        }

        // Prepare the interface.
        DownloadButton.IsEnabled = false;
        LoadingIndicator.IsRunning = true;
        StatusLabel.IsVisible = false;
        _items.Clear();

        try
        {
            string xml = await Client.GetStringAsync(uri);
            var items = ParseFeed(xml);

            foreach (var item in items)
            {
                _items.Add(item);
            }

            if (_items.Count == 0)
            {
                StatusLabel.Text = "No items found in the feed.";
                StatusLabel.IsVisible = true;
            }
        }
        catch (HttpRequestException ex)
        {
            await DisplayAlertAsync("Network error",
                $"Unable to download the feed.\n\n{ex.Message}", "OK");
        }
        catch (TaskCanceledException)
        {
            await DisplayAlertAsync("Timeout",
                "The server did not respond within 20 seconds.", "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Error",
                $"Unable to process the feed.\n\n{ex.Message}", "OK");
        }
        finally
        {
            LoadingIndicator.IsRunning = false;
            DownloadButton.IsEnabled = true;
        }
    }

    // Parse an RSS 2.0 feed into items.
    private static List<Item> ParseFeed(string xml)
    {
        var document = XDocument.Parse(xml);
        var items = new List<Item>();

        foreach (var element in document.Descendants()
                     .Where(e => e.Name.LocalName == "item"))
        {
            items.Add(new Item
            {
                Title = element.Elements()
                    .FirstOrDefault(c => c.Name.LocalName == "title")?.Value
                    ?? "(no title)",
                Link = element.Elements()
                    .FirstOrDefault(c => c.Name.LocalName == "link")?.Value
                    ?? string.Empty,
                PublishedDate = element.Elements()
                    .FirstOrDefault(c => c.Name.LocalName == "pubDate")?.Value
                    ?? string.Empty
            });
        }

        return items;
    }

    // Open an item's link in the browser.
    private async void OnItemTapped(object? sender, TappedEventArgs e)
    {
        if (sender is BindableObject bindable &&
            bindable.BindingContext is Item item &&
            !string.IsNullOrWhiteSpace(item.Link) &&
            Uri.TryCreate(item.Link, UriKind.Absolute, out Uri? uri))
        {
            await Launcher.Default.OpenAsync(uri);
        }
    }
}

public class Item
{
    public string Title { get; set; } = string.Empty;
    public string Link { get; set; } = string.Empty;
    public string PublishedDate { get; set; } = string.Empty;
}
