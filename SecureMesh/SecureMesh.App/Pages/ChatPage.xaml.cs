using System.Collections.ObjectModel;
using SecureMesh.App.Services;
using SecureMesh.Core.Domain;

namespace SecureMesh.App.Pages;

public partial class ChatPage : ContentPage, IQueryAttributable
{
    private readonly ChatService _chat;
    private readonly ContactsService _contacts;
    private readonly ITransport _transport;

    private readonly ObservableCollection<ChatBubble> _bubbles = new();
    private int? _contactId;
    private int _conversationId;
    private bool _ready;
    private bool _connected;

    public ChatPage()
    {
        InitializeComponent();
        _chat = ServiceHelper.Get<ChatService>();
        _contacts = ServiceHelper.Get<ContactsService>();
        _transport = ServiceHelper.Get<ITransport>();

        MessagesView.ItemsSource = _bubbles;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("contactId", out var value) &&
            int.TryParse(value?.ToString(), out var id))
        {
            _contactId = id;
        }
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        _chat.MessageDelivered += OnMessageDelivered;
        _transport.PeerConnected += OnPeerConnected;
        _transport.PeerDisconnected += OnPeerDisconnected;

        if (!_ready && _contactId is int contactId)
        {
            var contact = _contacts.GetById(contactId);
            if (contact is not null)
            {
                Title = contact.Nickname;
                _conversationId = _chat.EnsureConversation(contact).Id;
                _ready = true;
            }
        }

        UpdateConnectionLabel();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _chat.MessageDelivered -= OnMessageDelivered;
        _transport.PeerConnected -= OnPeerConnected;
        _transport.PeerDisconnected -= OnPeerDisconnected;
    }

    private void OnPeerConnected(string peerId)
    {
        _connected = true;
        UpdateConnectionLabel();
    }

    private void OnPeerDisconnected(string peerId)
    {
        _connected = false;
        UpdateConnectionLabel();
    }

    private void UpdateConnectionLabel()
    {
        if (_connected)
        {
            ConnectionLabel.Text = "Свързан";
            ConnectionLabel.TextColor = Colors.Green;
        }
        else
        {
            ConnectionLabel.Text = "Изключено";
            ConnectionLabel.TextColor = Color.FromArgb("#B45309");
        }
    }

    private void OnMessageDelivered(IncomingMessage message)
    {
        if (!_ready || message.ConversationId != _conversationId)
            return;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            _bubbles.Add(new ChatBubble(
                message.Plaintext,
                message.Timestamp.ToLocalTime().ToString("HH:mm"),
                LayoutOptions.Start,
                Colors.White,
                Colors.Black));
            ScrollToEnd();
        });
    }

    private async void OnSendClicked(object? sender, EventArgs e)
    {
        if (!_ready)
            return;

        var text = MessageEntry.Text?.Trim();
        if (string.IsNullOrWhiteSpace(text))
            return;

        MessageEntry.Text = "";
        _bubbles.Add(new ChatBubble(text, DateTime.Now.ToString("HH:mm"), LayoutOptions.End, Color.FromArgb("#1B6EF3"), Colors.White));
        ScrollToEnd();

        try
        {
            await _chat.SendText(_conversationId, text);
        }
        catch (Exception)
        {
            await DisplayAlertAsync("Внимание", "Съобщението е запазено локално, но няма активна Bluetooth връзка.", "Добре");
        }
    }

    private void ScrollToEnd()
    {
        if (_bubbles.Count > 0)
            MessagesView.ScrollTo(_bubbles.Count - 1, position: ScrollToPosition.End, animate: true);
    }

    private sealed record ChatBubble(string Text, string Time, LayoutOptions Align, Color BubbleColor, Color TextColor);
}
