using SecureMesh.Core;
using SecureMesh.Core.Domain;
using SecureMesh.Core.Domain.Persistence;

namespace SecureMesh.Core.Tests;

public class ChatHistoryTests
{
    [Fact]
    public void GetHistory_ReturnsReadableMessagesInSeqOrder_ExcludingSystemAndEmpty()
    {
        using var db = Database.InMemory();
        var chat = new ChatService(KeyGeneration.CreateIdentity(), new MockTransport("test"), db);

        var conversations = new ConversationRepository(db.Connection);
        var messages = new MessageRepository(db.Connection);

        var conversation = new Conversation { PeerId = 7, IsGroup = false };
        conversations.Insert(conversation);

        messages.Insert(new Message
        {
            ConversationId = conversation.Id,
            Seq = 1,
            SenderSeq = 1,
            SenderId = 0,
            Plaintext = "first out",
            Status = MessageStatus.Delivered,
        });
        messages.Insert(new Message
        {
            ConversationId = conversation.Id,
            Seq = 2,
            SenderSeq = 1,
            SenderId = 7,
            Plaintext = "first in",
            Status = MessageStatus.Received,
        });
        // The initiator's bare auto-initiated prekey / ACK marker: System + empty plaintext.
        messages.Insert(new Message
        {
            ConversationId = conversation.Id,
            Seq = 3,
            SenderSeq = 2,
            SenderId = 0,
            Plaintext = "",
            Status = MessageStatus.System,
        });
        messages.Insert(new Message
        {
            ConversationId = conversation.Id,
            Seq = 4,
            SenderSeq = 3,
            SenderId = 0,
            Plaintext = "second out",
            Status = MessageStatus.Pending,
        });
        // Defensive: empty plaintext with a non-System status is also excluded.
        messages.Insert(new Message
        {
            ConversationId = conversation.Id,
            Seq = 5,
            SenderSeq = 4,
            SenderId = 0,
            Plaintext = "",
            Status = MessageStatus.Pending,
        });

        var history = chat.GetHistory(conversation.Id);

        Assert.Equal(3, history.Count);
        Assert.Equal(new[] { "first out", "first in", "second out" }, history.Select(m => m.Plaintext).ToArray());
        Assert.Equal(new long[] { 1, 2, 4 }, history.Select(m => m.Seq).ToArray());
    }

    [Fact]
    public void GetHistory_UnknownConversation_ReturnsEmpty()
    {
        using var db = Database.InMemory();
        var chat = new ChatService(KeyGeneration.CreateIdentity(), new MockTransport("test"), db);

        Assert.Empty(chat.GetHistory(12345));
    }
}
