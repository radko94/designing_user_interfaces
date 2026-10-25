
using AngleSharp.Html.Parser;

namespace HtmlDownloader1_0;

public partial class MainPage : ContentPage
{
    // Reuse HttpClient across requests.
    private static readonly HttpClient Client = new()
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    public MainPage()
    {
        InitializeComponent();
    }

    // Download button event handler.
    private async void OnDownloadClicked(
        object sender,
        EventArgs e)
    {
        string url = UrlEntry.Text?.Trim() ?? string.Empty;

        // Validate the URL.
        if (!Uri.TryCreate(
                url,
                UriKind.Absolute,
                out Uri? uri) ||
            (uri.Scheme != Uri.UriSchemeHttp &&
             uri.Scheme != Uri.UriSchemeHttps))
        {
            await DisplayAlertAsync(
                "Invalid URL",
                "Please enter a valid HTTP or HTTPS URL.",
                "OK");

            return;
        }

        // Prepare the interface.
        DownloadButton.IsEnabled = false;
        LoadingIndicator.IsRunning = true;
        StatusLabel.Text = "Downloading...";
        HtmlEditor.Text = string.Empty;

        try
        {
            // Download the HTML document asynchronously.
            string html = await DownloadAsync(uri);

            // Parse the downloaded HTML.
            HtmlParser parser = new HtmlParser();
            var document = parser.ParseDocument(html);

            // Extract the text from the HTML body.
            string extractedText =
                document.Body?.TextContent?.Trim()
                ?? string.Empty;

            // Display the extracted text.
            HtmlEditor.Text = extractedText;

            StatusLabel.Text =
                string.IsNullOrWhiteSpace(extractedText)
                    ? "No body text found."
                    : "Download completed successfully.";
        }
        catch (HttpRequestException ex)
        {
            StatusLabel.Text = "Download failed.";

            await DisplayAlertAsync(
                "Network error",
                $"Unable to download the webpage.\n\n{ex.Message}",
                "OK");
        }
        catch (TaskCanceledException)
        {
            StatusLabel.Text = "Request timed out.";

            await DisplayAlertAsync(
                "Timeout",
                "The server did not respond within 20 seconds.",
                "OK");
        }
        catch (Exception ex)
        {
            StatusLabel.Text = "Processing failed.";

            await DisplayAlertAsync(
                "Error",
                $"Unable to process the webpage.\n\n{ex.Message}",
                "OK");
        }
        finally
        {
            // Restore the interface.
            LoadingIndicator.IsRunning = false;
            DownloadButton.IsEnabled = true;
        }
    }

    // Download webpage content.
    private static async Task<string> DownloadAsync(Uri uri)
    {
        return await Client.GetStringAsync(uri);
    }
}
