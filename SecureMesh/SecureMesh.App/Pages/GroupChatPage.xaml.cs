using System.Collections.ObjectModel;
using SecureMesh.App.Services;
using SecureMesh.Core.Domain;

namespace SecureMesh.App.Pages;

public partial class GroupChatPage : ContentPage, IQueryAttributable
{
    private readonly GroupChatService _groups;
    private readonly ContactsService _contacts;
    private readonly ITransport _transport;

    private readonly ObservableCollection<GroupBubble> _bubbles = new();
    private int? _groupId;
    private int _conversationId;
    private bool _ready;
    private bool _connected;

    public GroupChatPage()
    {
        InitializeComponent();
        _groups = ServiceHelper.Get<GroupChatService>();
        _contacts = ServiceHelper.Get<ContactsService>();
        _transport = ServiceHelper.Get<ITransport>();

        MessagesView.ItemsSource = _bubbles;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("groupId", out var value) &&
            int.TryParse(value?.ToString(), out var id))
        {
            _groupId = id;
        }
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        _groups.MessageDelivered += OnMessageDelivered;
        _transport.PeerConnected += OnPeerConnected;
        _transport.PeerDisconnected += OnPeerDisconnected;

        if (!_ready && _groupId is int groupId)
        {
            var group = _groups.GetGroup(groupId);
            if (group is not null && group.ConversationId is int conversationId)
            {
                Title = group.Name;
                _conversationId = conversationId;
                _ready = true;
            }
        }

        UpdateConnectionLabel();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _groups.MessageDelivered -= OnMessageDelivered;
        _transport.PeerConnected -= OnPeerConnected;
        _transport.PeerDisconnected -= OnPeerDisconnected;
    }

    private void OnPeerConnected(string peerId)
    {
        _connected = true;
        MainThread.BeginInvokeOnMainThread(UpdateConnectionLabel);
    }

    private void OnPeerDisconnected(string peerId)
    {
        _connected = false;
        MainThread.BeginInvokeOnMainThread(UpdateConnectionLabel);
    }

    private void UpdateConnectionLabel()
    {
        if (_ready && _groupId is int groupId && _groups.IsEstablished(groupId))
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
            var sender = _contacts.GetById(message.SenderId)?.Nickname ?? "Непознат";
            _bubbles.Add(new GroupBubble(
                message.Plaintext,
                message.Timestamp.ToLocalTime().ToString("HH:mm"),
                Sender: sender,
                Status: "",
                Align: LayoutOptions.Start,
                BubbleColor: Colors.White,
                TextColor: Colors.Black,
                SenderColor: Color.FromArgb("#6B7280"),
                StatusColor: Colors.Transparent));
            ScrollToEnd();
            UpdateConnectionLabel();
        });
    }

    private async void OnSendClicked(object? sender, EventArgs e)
    {
        if (!_ready || _groupId is not int groupId)
            return;

        var text = MessageEntry.Text?.Trim();
        if (string.IsNullOrWhiteSpace(text))
            return;

        MessageEntry.Text = "";

        // Optimistic bubble, honestly starting as "sending" until SendGroupMessage resolves.
        var bubble = new GroupBubble(
            text,
            DateTime.Now.ToString("HH:mm"),
            Sender: "",
            StatusLabel(MessageStatus.Pending),
            Align: LayoutOptions.End,
            BubbleColor: Color.FromArgb("#1B6EF3"),
            TextColor: Colors.White,
            SenderColor: Colors.Transparent,
            StatusColorFor(MessageStatus.Pending));
        _bubbles.Add(bubble);
        ScrollToEnd();

        try
        {
            var message = await _groups.SendGroupMessage(groupId, text);
            UpdateBubbleStatus(bubble, message.Status);
            UpdateConnectionLabel();
        }
        catch (Exception)
        {
            UpdateBubbleStatus(bubble, MessageStatus.Failed);
        }
    }

    private void UpdateBubbleStatus(GroupBubble bubble, MessageStatus status)
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

    private int IndexOfRef(GroupBubble bubble)
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

    private sealed record GroupBubble(
        string Text,
        string Time,
        string Sender,
        string Status,
        LayoutOptions Align,
        Color BubbleColor,
        Color TextColor,
        Color SenderColor,
        Color StatusColor)
    {
        public bool HasSender => Sender.Length > 0;
        public bool HasStatus => Status.Length > 0;
    }
}
