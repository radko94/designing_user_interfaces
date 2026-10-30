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
        _chat.SessionEstablished += OnSessionEstablished;
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
                LoadHistory();
            }
        }

        UpdateConnectionLabel();
    }

    /// <summary>
    /// Populates the bubble list from the persisted plaintext history before any
    /// live <see cref="OnMessageDelivered"/> event, then scrolls to the newest
    /// message. Runs once per page instance (guarded by <see cref="_ready"/>).
    /// </summary>
    private void LoadHistory()
    {
        var history = _chat.GetHistory(_conversationId);
        foreach (var message in history)
            _bubbles.Add(BubbleFor(message));

        // Opening the chat marks the newest sequence as seen, which drives the
        // conversation list's unread badge.
        if (history.Count > 0)
            UnreadState.MarkSeen(_conversationId, history[^1].Seq);

        // Defer the scroll until the CollectionView has realized its items.
        MainThread.BeginInvokeOnMainThread(ScrollToEnd);
    }

    /// <summary>Maps a stored <see cref="Message"/> to the same bubble style the live paths use.</summary>
    private static ChatBubble BubbleFor(Message message)
    {
        if (message.SenderId == 0)
        {
            return new ChatBubble(
                message.Plaintext,
                message.Timestamp.ToLocalTime().ToString("HH:mm"),
                StatusLabel(message.Status),
                LayoutOptions.End,
                Color.FromArgb("#1B6EF3"),
                Colors.White,
                StatusColorFor(message.Status));
        }

        return new ChatBubble(
            message.Plaintext,
            message.Timestamp.ToLocalTime().ToString("HH:mm"),
            Status: "",
            LayoutOptions.Start,
            Colors.White,
            Colors.Black,
            StatusColor: Colors.Transparent);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _chat.MessageDelivered -= OnMessageDelivered;
        _chat.SessionEstablished -= OnSessionEstablished;
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

    private void OnSessionEstablished(int conversationId)
    {
        if (!_ready || conversationId != _conversationId)
            return;

        MainThread.BeginInvokeOnMainThread(UpdateConnectionLabel);
    }

    private void UpdateConnectionLabel()
    {
        if (_ready && _chat.IsEstablished(_conversationId))
        {
            ConnectionLabel.Text = "Свързан";
            ConnectionLabel.TextColor = Colors.Green;
        }
        else if (_connected)
        {
            ConnectionLabel.Text = "Създаване на сигурна сесия…";
            ConnectionLabel.TextColor = Color.FromArgb("#B45309");
        }
        else
        {
            ConnectionLabel.Text = "Няма връзка";
            ConnectionLabel.TextColor = Color.FromArgb("#B45309");
        }
    }

    private void OnMessageDelivered(IncomingMessage message)
    {
        if (!_ready || message.ConversationId != _conversationId)
            return;

        // Never surface the internal bare-prekey marker (empty plaintext) as a bubble.
        if (string.IsNullOrEmpty(message.Plaintext))
            return;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            _bubbles.Add(new ChatBubble(
                message.Plaintext,
                message.Timestamp.ToLocalTime().ToString("HH:mm"),
                Status: "",
                LayoutOptions.Start,
                Colors.White,
                Colors.Black,
                StatusColor: Colors.Transparent));
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

        // Optimistic bubble, honestly starting as "sending" until SendText resolves.
        var bubble = new ChatBubble(
            text,
            DateTime.Now.ToString("HH:mm"),
            StatusLabel(MessageStatus.Pending),
            LayoutOptions.End,
            Color.FromArgb("#1B6EF3"),
            Colors.White,
            StatusColorFor(MessageStatus.Pending));
        _bubbles.Add(bubble);
        ScrollToEnd();

        try
        {
            var message = await _chat.SendText(_conversationId, text);
            UpdateBubbleStatus(bubble, message.Status);
        }
        catch (Exception)
        {
            UpdateBubbleStatus(bubble, MessageStatus.Failed);
        }
    }

    private void UpdateBubbleStatus(ChatBubble bubble, MessageStatus status)
    {
        var index = IndexOfRef(bubble);
        if (index < 0)
            return;

        _bubbles[index] = bubble with
        {
            Status = StatusLabel(status),
            StatusColor = StatusColorFor(status),
        };
    }

    private int IndexOfRef(ChatBubble bubble)
    {
        for (var i = 0; i < _bubbles.Count; i++)
        {
            if (ReferenceEquals(_bubbles[i], bubble))
                return i;
        }
        return -1;
    }

    private static string StatusLabel(MessageStatus status) => status switch
    {
        MessageStatus.Pending => "изпраща се…",
        MessageStatus.Delivered => "изпратено",
        MessageStatus.Failed => "неуспешно",
        _ => "",
    };

    private static Color StatusColorFor(MessageStatus status) => status switch
    {
        MessageStatus.Failed => Color.FromArgb("#FCA5A5"),
        _ => Colors.White,
    };

    private void ScrollToEnd()
    {
        if (_bubbles.Count > 0)
            MessagesView.ScrollTo(_bubbles.Count - 1, position: ScrollToPosition.End, animate: true);
    }

    private sealed record ChatBubble(
        string Text,
        string Time,
        string Status,
        LayoutOptions Align,
        Color BubbleColor,
        Color TextColor,
        Color StatusColor)
    {
        public bool HasStatus => Status.Length > 0;
    }
}
