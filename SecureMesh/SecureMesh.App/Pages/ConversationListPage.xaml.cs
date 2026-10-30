using Microsoft.Maui.Storage;
using SecureMesh.App.Services;
using SecureMesh.Core.Domain;
using SecureMesh.Core.Domain.Persistence;

namespace SecureMesh.App.Pages;

public partial class ConversationListPage : ContentPage
{
    private readonly ChatService _chat;
    private readonly ContactsService _contacts;
    private readonly ConversationRepository _conversations;

    public ConversationListPage()
    {
        InitializeComponent();
        _chat = ServiceHelper.Get<ChatService>();
        _contacts = ServiceHelper.Get<ContactsService>();
        _conversations = ServiceHelper.Get<ConversationRepository>();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        Refresh();
    }

    /// <summary>
    /// Lists the user's 1:1 conversations (most recent first) with a last-message
    /// preview and a simple unread badge derived from received messages above the
    /// per-conversation "seen" mark.
    /// </summary>
    private void Refresh()
    {
        var items = new List<ConversationItem>();
        foreach (var conversation in _conversations.All())
        {
            if (conversation.IsGroup || conversation.PeerId is not int contactId)
                continue;

            var contact = _contacts.GetById(contactId);
            if (contact is null)
                continue;

            var history = _chat.GetHistory(conversation.Id);
            var lastSeen = UnreadState.GetLastSeen(conversation.Id);
            var unread = history.Count(m => m.Status == MessageStatus.Received && m.Seq > lastSeen);

            var preview = "";
            var time = "";
            long lastSeq = 0;
            if (history.Count > 0)
            {
                var last = history[^1];
                preview = last.Plaintext;
                time = last.Timestamp.ToLocalTime().ToString("HH:mm");
                lastSeq = last.Seq;
            }

            items.Add(new ConversationItem(
                conversation.Id,
                contactId,
                contact.Nickname,
                preview,
                time,
                unread,
                unread > 0,
                lastSeq));
        }

        items.Sort((a, b) => b.LastSeq.CompareTo(a.LastSeq));
        ConversationsView.ItemsSource = items;
    }

    private async void OnConversationSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not ConversationItem item)
            return;

        ConversationsView.SelectedItem = null;
        await Shell.Current.GoToAsync($"{nameof(ChatPage)}?contactId={item.ContactId}");
    }

    private sealed record ConversationItem(
        int ConversationId,
        int ContactId,
        string Name,
        string Preview,
        string Time,
        int UnreadCount,
        bool HasUnread,
        long LastSeq);
}

/// <summary>
/// Tracks, per conversation, the highest message sequence the user has seen.
/// <see cref="ConversationListPage"/> uses it to derive a simple, honest unread
/// badge from <see cref="MessageStatus.Received"/> messages above that mark, and
/// <see cref="ChatPage"/> advances the mark when a conversation is opened.
/// </summary>
internal static class UnreadState
{
    private const string KeyPrefix = "securemesh.unread.seen.";

    public static long GetLastSeen(int conversationId) =>
        Preferences.Default.Get($"{KeyPrefix}{conversationId}", 0L);

    public static void MarkSeen(int conversationId, long lastSeq)
    {
        if (lastSeq <= GetLastSeen(conversationId))
            return;
        Preferences.Default.Set($"{KeyPrefix}{conversationId}", lastSeq);
    }
}
