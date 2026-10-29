using SQLite;

namespace SecureMesh.Core.Domain.Persistence;

public sealed class ContactRepository
{
    private readonly SQLiteConnection _db;

    public ContactRepository(SQLiteConnection db) => _db = db;

    public int Insert(Contact contact)
    {
        _db.Insert(contact);
        return contact.Id;
    }

    public List<Contact> All()
    {
        var list = new List<Contact>();
        foreach (var c in _db.Table<Contact>())
            list.Add(c);
        return list;
    }

    public Contact? Get(int id) => _db.Find<Contact>(id);

    public Contact? FindByEd25519Pub(byte[] edPublic)
    {
        ArgumentNullException.ThrowIfNull(edPublic);
        foreach (var c in _db.Table<Contact>())
        {
            if (c.Ed25519Pub.AsSpan().SequenceEqual(edPublic))
                return c;
        }
        return null;
    }

    public void Update(Contact contact) => _db.Update(contact);

    public void Delete(int id) => _db.Delete<Contact>(id);
}

public sealed class ConversationRepository
{
    private readonly SQLiteConnection _db;

    public ConversationRepository(SQLiteConnection db) => _db = db;

    public int Insert(Conversation conversation)
    {
        _db.Insert(conversation);
        return conversation.Id;
    }

    public List<Conversation> All()
    {
        var list = new List<Conversation>();
        foreach (var c in _db.Table<Conversation>())
            list.Add(c);
        return list;
    }

    public Conversation? Get(int id) => _db.Find<Conversation>(id);

    public Conversation? FindByPeerId(int peerId)
    {
        foreach (var c in _db.Table<Conversation>())
        {
            if (!c.IsGroup && c.PeerId == peerId)
                return c;
        }
        return null;
    }

    public void Update(Conversation conversation) => _db.Update(conversation);

    public void Delete(int id) => _db.Delete<Conversation>(id);
}

public sealed class MessageRepository
{
    private readonly SQLiteConnection _db;

    public MessageRepository(SQLiteConnection db) => _db = db;

    public int Insert(Message message)
    {
        _db.Insert(message);
        return message.Id;
    }

    public List<Message> ForConversation(int conversationId)
    {
        var list = new List<Message>();
        foreach (var m in _db.Table<Message>())
        {
            if (m.ConversationId == conversationId)
                list.Add(m);
        }
        list.Sort((a, b) => a.Seq.CompareTo(b.Seq));
        return list;
    }

    public List<Message> Range(int conversationId, long afterSeq, long upToSeq)
    {
        var list = new List<Message>();
        foreach (var m in _db.Table<Message>())
        {
            if (m.ConversationId == conversationId && m.Seq > afterSeq && m.Seq <= upToSeq)
                list.Add(m);
        }
        list.Sort((a, b) => a.Seq.CompareTo(b.Seq));
        return list;
    }

    public long MaxSeq(int conversationId)
    {
        long max = 0;
        foreach (var m in _db.Table<Message>())
        {
            if (m.ConversationId == conversationId && m.Seq > max)
                max = m.Seq;
        }
        return max;
    }

    public bool Exists(int conversationId, long seq)
    {
        foreach (var m in _db.Table<Message>())
        {
            if (m.ConversationId == conversationId && m.Seq == seq)
                return true;
        }
        return false;
    }
}

public sealed class GroupRepository
{
    private readonly SQLiteConnection _db;

    public GroupRepository(SQLiteConnection db) => _db = db;

    public int Insert(Group group)
    {
        _db.Insert(group);
        return group.Id;
    }

    public List<Group> All()
    {
        var list = new List<Group>();
        foreach (var g in _db.Table<Group>())
            list.Add(g);
        return list;
    }

    public Group? Get(int id) => _db.Find<Group>(id);

    public Group? FindByKey(string groupKey)
    {
        ArgumentNullException.ThrowIfNull(groupKey);
        foreach (var g in _db.Table<Group>())
        {
            if (string.Equals(g.GroupKey, groupKey, StringComparison.Ordinal))
                return g;
        }
        return null;
    }

    public void Update(Group group) => _db.Update(group);

    public void Delete(int id) => _db.Delete<Group>(id);
}

public sealed class GroupMemberKeyRepository
{
    private readonly SQLiteConnection _db;

    public GroupMemberKeyRepository(SQLiteConnection db) => _db = db;

    public int Insert(GroupMemberKey key)
    {
        _db.Insert(key);
        return key.Id;
    }

    public GroupMemberKey? Get(int id) => _db.Find<GroupMemberKey>(id);

    public GroupMemberKey? FindByGroupAndContact(int groupId, int contactId)
    {
        foreach (var k in _db.Table<GroupMemberKey>())
        {
            if (k.GroupId == groupId && k.MemberContactId == contactId)
                return k;
        }
        return null;
    }

    public List<GroupMemberKey> ForGroup(int groupId)
    {
        var list = new List<GroupMemberKey>();
        foreach (var k in _db.Table<GroupMemberKey>())
        {
            if (k.GroupId == groupId)
                list.Add(k);
        }
        return list;
    }

    public void Update(GroupMemberKey key) => _db.Update(key);

    public void Delete(int id) => _db.Delete<GroupMemberKey>(id);

    public void DeleteForGroupAndContact(int groupId, int contactId) =>
        _db.Execute("delete from GroupMemberKey where GroupId = ? and MemberContactId = ?", groupId, contactId);
}

public sealed class GroupMessageEnvelopeRepository
{
    private readonly SQLiteConnection _db;

    public GroupMessageEnvelopeRepository(SQLiteConnection db) => _db = db;

    public int Insert(GroupMessageEnvelope envelope)
    {
        _db.Insert(envelope);
        return envelope.Id;
    }

    public List<GroupMessageEnvelope> Range(int conversationId, long afterSeq, long upToSeq)
    {
        var list = new List<GroupMessageEnvelope>();
        foreach (var e in _db.Table<GroupMessageEnvelope>())
        {
            if (e.ConversationId == conversationId && e.Seq > afterSeq && e.Seq <= upToSeq)
                list.Add(e);
        }
        list.Sort((a, b) => a.Seq.CompareTo(b.Seq));
        return list;
    }

    public List<GroupMessageEnvelope> ForSeq(int conversationId, long seq)
    {
        var list = new List<GroupMessageEnvelope>();
        foreach (var e in _db.Table<GroupMessageEnvelope>())
        {
            if (e.ConversationId == conversationId && e.Seq == seq)
                list.Add(e);
        }
        return list;
    }

    public bool Exists(int conversationId, long seq, int recipientContactId)
    {
        foreach (var e in _db.Table<GroupMessageEnvelope>())
        {
            if (e.ConversationId == conversationId && e.Seq == seq && e.RecipientContactId == recipientContactId)
                return true;
        }
        return false;
    }
}

public sealed class GroupMemberRepository
{
    private readonly SQLiteConnection _db;

    public GroupMemberRepository(SQLiteConnection db) => _db = db;

    public int Insert(GroupMember member)
    {
        _db.Insert(member);
        return member.Id;
    }

    public List<GroupMember> ForGroup(int groupId)
    {
        var list = new List<GroupMember>();
        foreach (var m in _db.Table<GroupMember>())
        {
            if (m.GroupId == groupId)
                list.Add(m);
        }
        return list;
    }

    public void Remove(int groupId, int contactId) =>
        _db.Execute("delete from GroupMember where GroupId = ? and ContactId = ?", groupId, contactId);
}

public sealed class SyncStateRepository
{
    private readonly SQLiteConnection _db;

    public SyncStateRepository(SQLiteConnection db) => _db = db;

    public SyncState? Get(int conversationId, int peerId)
    {
        foreach (var s in _db.Table<SyncState>())
        {
            if (s.ConversationId == conversationId && s.PeerId == peerId)
                return s;
        }
        return null;
    }

    public void Upsert(int conversationId, int peerId, long lastSeq)
    {
        var existing = Get(conversationId, peerId);
        if (existing is null)
        {
            _db.Insert(new SyncState { ConversationId = conversationId, PeerId = peerId, LastSeq = lastSeq });
        }
        else
        {
            existing.LastSeq = lastSeq;
            _db.Update(existing);
        }
    }

    public List<SyncState> All()
    {
        var list = new List<SyncState>();
        foreach (var s in _db.Table<SyncState>())
            list.Add(s);
        return list;
    }
}
