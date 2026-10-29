using Microsoft.Extensions.DependencyInjection;
using SecureMesh.Core.Domain.Persistence;

namespace SecureMesh.Core.Domain;

/// <summary>
/// Computes which messages a peer is missing and produces/applies sync payloads
/// to reconcile two devices' conversation history.
/// </summary>
public sealed class SyncService
{
    private readonly MessageRepository _messages;
    private readonly SyncStateRepository _syncStates;
    private readonly GroupMessageEnvelopeRepository? _groupEnvelopes;

    [ActivatorUtilitiesConstructor]
    public SyncService(Database db)
        : this(
            new MessageRepository(db.Connection),
            new SyncStateRepository(db.Connection),
            new GroupMessageEnvelopeRepository(db.Connection))
    {
    }

    public SyncService(MessageRepository messages, SyncStateRepository syncStates)
        : this(messages, syncStates, null)
    {
    }

    public SyncService(
        MessageRepository messages,
        SyncStateRepository syncStates,
        GroupMessageEnvelopeRepository? groupEnvelopes)
    {
        _messages = messages;
        _syncStates = syncStates;
        _groupEnvelopes = groupEnvelopes;
    }

    /// <summary>
    /// Returns the contiguous sequence-number ranges in
    /// <c>(knownLastSeq, targetLastSeq]</c>, i.e. the messages that must be
    /// transferred to go from <paramref name="knownLastSeq"/> to
    /// <paramref name="targetLastSeq"/>.
    /// </summary>
    public static IReadOnlyList<SeqRange> ComputeMissingRanges(long knownLastSeq, long targetLastSeq)
    {
        if (targetLastSeq <= knownLastSeq)
            return Array.Empty<SeqRange>();

        return new[] { new SeqRange(knownLastSeq + 1, targetLastSeq) };
    }

    /// <summary>
    /// Produces a payload of all messages in <paramref name="conversationId"/>
    /// with sequence numbers greater than <paramref name="afterSeq"/>.
    /// </summary>
    public SyncPayload ProducePayload(int conversationId, long afterSeq)
    {
        var messages = _messages.Range(conversationId, afterSeq, long.MaxValue);
        var payload = new SyncPayload { ConversationId = conversationId, Messages = messages };
        if (_groupEnvelopes is not null)
            payload.GroupEnvelopes = _groupEnvelopes.Range(conversationId, afterSeq, long.MaxValue);
        return payload;
    }

    /// <summary>
    /// Applies <paramref name="payload"/> to <paramref name="conversationId"/>,
    /// inserting any messages not already present and advancing the
    /// <see cref="SyncState"/> for <paramref name="peerId"/>. Returns the
    /// highest sequence number applied (0 if nothing new).
    /// </summary>
    public long ApplyPayload(int conversationId, int peerId, SyncPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        long maxApplied = 0;
        foreach (var source in payload.Messages)
        {
            if (source.Seq <= 0 || _messages.Exists(conversationId, source.Seq))
                continue;

            var message = new Message
            {
                ConversationId = conversationId,
                Seq = source.Seq,
                SenderId = source.SenderId,
                Ciphertext = source.Ciphertext,
                Nonce = source.Nonce,
                Tag = source.Tag,
                Signature = source.Signature,
                Timestamp = source.Timestamp,
                Status = MessageStatus.Received,
            };
            _messages.Insert(message);
            maxApplied = Math.Max(maxApplied, source.Seq);
        }

        // Persist any per-member group ciphertexts carried in the payload so a
        // catching-up member can later decrypt their own copy.
        if (_groupEnvelopes is not null)
        {
            foreach (var e in payload.GroupEnvelopes ?? new List<GroupMessageEnvelope>())
            {
                if (e.Seq <= 0 || _groupEnvelopes.Exists(conversationId, e.Seq, e.RecipientContactId))
                    continue;

                _groupEnvelopes.Insert(new GroupMessageEnvelope
                {
                    ConversationId = conversationId,
                    Seq = e.Seq,
                    SenderPublic = e.SenderPublic,
                    RecipientContactId = e.RecipientContactId,
                    Nonce = e.Nonce,
                    Ciphertext = e.Ciphertext,
                    Tag = e.Tag,
                    Signature = e.Signature,
                });
            }
        }

        if (maxApplied > 0)
        {
            var existing = _syncStates.Get(conversationId, peerId)?.LastSeq ?? 0;
            _syncStates.Upsert(conversationId, peerId, Math.Max(existing, maxApplied));
        }

        return maxApplied;
    }
}
