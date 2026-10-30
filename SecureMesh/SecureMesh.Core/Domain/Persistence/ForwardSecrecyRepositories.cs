using SQLite;

namespace SecureMesh.Core.Domain.Persistence;

/// <summary>
/// Persists skipped message keys (see <see cref="SkippedKey"/>). The table is
/// created idempotently here because the shared schema owner
/// (<see cref="Database"/>) is not responsible for these forward-secrecy
/// auxiliary tables.
/// </summary>
public sealed class SkippedKeyRepository
{
    private readonly SQLiteConnection _db;

    public SkippedKeyRepository(SQLiteConnection db)
    {
        _db = db;
        _db.CreateTable<SkippedKey>();
    }

    public int Insert(SkippedKey key)
    {
        _db.Insert(key);
        return key.Id;
    }

    /// <summary>Returns the skipped keys for one channel + sender, ordered by sequence.</summary>
    public List<SkippedKey> ForChannelAndSender(byte[] channelId, int senderId)
    {
        var result = new List<SkippedKey>();
        foreach (var k in _db.Table<SkippedKey>())
        {
            if (k.SenderId == senderId && k.ChannelId.AsSpan().SequenceEqual(channelId))
                result.Add(k);
        }
        result.Sort((a, b) => a.SenderSeq.CompareTo(b.SenderSeq));
        return result;
    }

    /// <summary>Removes every skipped key for one channel + sender.</summary>
    public void DeleteForChannelAndSender(byte[] channelId, int senderId)
    {
        foreach (var k in ForChannelAndSender(channelId, senderId))
            _db.Delete<SkippedKey>(k.Id);
    }
}

/// <summary>
/// Persists sealed outgoing envelopes awaiting delivery (the outbox). The table
/// is created idempotently here for the same reason as <see cref="SkippedKeyRepository"/>.
/// </summary>
public sealed class PendingMessageRepository
{
    private readonly SQLiteConnection _db;

    public PendingMessageRepository(SQLiteConnection db)
    {
        _db = db;
        _db.CreateTable<PendingMessage>();
    }

    public int Insert(PendingMessage pending)
    {
        _db.Insert(pending);
        return pending.Id;
    }

    public PendingMessage? Find(int conversationId, long senderSeq)
    {
        foreach (var p in _db.Table<PendingMessage>())
        {
            if (p.ConversationId == conversationId && p.SenderSeq == senderSeq)
                return p;
        }
        return null;
    }

    public List<PendingMessage> ForConversation(int conversationId)
    {
        var result = new List<PendingMessage>();
        foreach (var p in _db.Table<PendingMessage>())
        {
            if (p.ConversationId == conversationId)
                result.Add(p);
        }
        return result;
    }

    public void Delete(int conversationId, long senderSeq) =>
        _db.Execute("delete from PendingMessage where ConversationId = ? and SenderSeq = ?", conversationId, senderSeq);
}
