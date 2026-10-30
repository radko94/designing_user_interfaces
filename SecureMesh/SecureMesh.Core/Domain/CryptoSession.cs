using System.Security.Cryptography;
using System.Text;

namespace SecureMesh.Core.Domain;

/// <summary>
/// A per-conversation cryptographic session. The session (root) key is derived
/// once via the X3DH-lite 3-DH construction (see
/// <see cref="DeriveSessionKeyAsInitiator"/> / <see cref="DeriveSessionKeyAsResponder"/>),
/// used to seed the two direction-bound ratchet chains, and then <b>discarded</b>
/// — only the ratchet states are ever persisted. Each message consumes exactly
/// one ratchet key in each direction and is sealed into a signed, authenticated,
/// context-bound <see cref="MessageEnvelope"/>.
/// </summary>
public sealed class CryptoSession
{
    /// <summary>HKDF info context for the 1:1 root session key.</summary>
    public const string DefaultInfo = "SecureMesh.Session/v2";

    /// <summary>HKDF info context for a group pairwise root session key.</summary>
    public const string GroupInfo = "SecureMesh.GroupSession/v2";

    /// <summary>HKDF info used to seed the send ratchet chain.</summary>
    public const string RatchetSendInfo = "SecureMesh.Ratchet.Send/v1";

    /// <summary>HKDF info used to seed the receive ratchet chain.</summary>
    public const string RatchetRecvInfo = "SecureMesh.Ratchet.Recv/v1";

    /// <summary>HKDF info used to advance either ratchet chain one step.</summary>
    public const string RatchetStepInfo = "SecureMesh.Ratchet.Step/v1";

    /// <summary>HKDF salt used when deriving the root session key (all zeroes).</summary>
    public static readonly byte[] DefaultSalt = new byte[32];

    /// <summary>Maximum look-ahead steps when decrypting an out-of-order message.</summary>
    public const int MaxSkip = 32;

    /// <summary>Upper bound on the number of skipped message keys retained per sender.</summary>
    public const int MaxSkippedKeys = 64;

    private static readonly byte[] SendInfoBytes = Encoding.UTF8.GetBytes(RatchetSendInfo);
    private static readonly byte[] RecvInfoBytes = Encoding.UTF8.GetBytes(RatchetRecvInfo);
    private static readonly byte[] StepInfoBytes = Encoding.UTF8.GetBytes(RatchetStepInfo);

    private byte[] _sendState;
    private byte[] _recvState;
    private readonly byte[] _senderEdPublic;
    private readonly byte[] _senderEdPrivate;

    /// <summary>
    /// Constructs a session from its already-seeded ratchet chain states. The
    /// root session key is intentionally NOT held here — it must be discarded by
    /// the caller immediately after seeding, so a compromised session object (or
    /// a persisted row) can only advance the chain forward and never reconstruct
    /// the root key.
    /// </summary>
    public CryptoSession(
        byte[] sendRatchetState,
        byte[] recvRatchetState,
        byte[] senderEdPublic,
        byte[] senderEdPrivate)
    {
        ArgumentNullException.ThrowIfNull(sendRatchetState);
        ArgumentNullException.ThrowIfNull(recvRatchetState);
        ArgumentNullException.ThrowIfNull(senderEdPublic);
        ArgumentNullException.ThrowIfNull(senderEdPrivate);

        if (sendRatchetState.Length == 0)
            throw new ArgumentException("The send ratchet state must not be empty.", nameof(sendRatchetState));
        if (recvRatchetState.Length == 0)
            throw new ArgumentException("The receive ratchet state must not be empty.", nameof(recvRatchetState));
        if (senderEdPublic.Length != IdentityKeyPair.Ed25519PublicKeyBytes)
            throw new ArgumentException($"The sender Ed25519 public key must be {IdentityKeyPair.Ed25519PublicKeyBytes} bytes.", nameof(senderEdPublic));
        if (senderEdPrivate.Length != IdentityKeyPair.Ed25519PrivateKeyBytes)
            throw new ArgumentException($"The sender Ed25519 private key must be {IdentityKeyPair.Ed25519PrivateKeyBytes} bytes.", nameof(senderEdPrivate));

        _sendState = (byte[])sendRatchetState.Clone();
        _recvState = (byte[])recvRatchetState.Clone();
        _senderEdPublic = (byte[])senderEdPublic.Clone();
        _senderEdPrivate = (byte[])senderEdPrivate.Clone();
    }

    /// <summary>The current send ratchet chain state.</summary>
    public byte[] SendRatchetState => (byte[])_sendState.Clone();

    /// <summary>The current receive ratchet chain state.</summary>
    public byte[] RecvRatchetState => (byte[])_recvState.Clone();

    // ------------------------------------------------------------------
    // X3DH-lite session establishment (prekey message)
    // ------------------------------------------------------------------

    /// <summary>
    /// Derives the root session key as the <b>initiator</b> (first sender) of a
    /// conversation. The initiator has generated a one-time X25519 key pair
    /// (<paramref name="ekPrivate"/>) and knows the peer's identity key
    /// (<paramref name="peerIkPublic"/>) and signed prekey
    /// (<paramref name="peerSpkPublic"/>):
    /// <c>HKDF-SHA256( DH(IK_a, SPK_b) ‖ DH(EK_a, IK_b) ‖ DH(EK_a, SPK_b), zeros32, info )</c>.
    /// </summary>
    public static byte[] DeriveSessionKeyAsInitiator(
        IdentityKeyPair mine,
        byte[] ekPrivate,
        byte[] peerIkPublic,
        byte[] peerSpkPublic,
        string info)
    {
        ArgumentNullException.ThrowIfNull(mine);
        ArgumentNullException.ThrowIfNull(ekPrivate);
        ArgumentNullException.ThrowIfNull(peerIkPublic);
        ArgumentNullException.ThrowIfNull(peerSpkPublic);
        Validate32(ekPrivate, nameof(ekPrivate));
        Validate32(peerIkPublic, nameof(peerIkPublic));
        Validate32(peerSpkPublic, nameof(peerSpkPublic));
        if (!mine.HasEphemeralKeys)
            throw new InvalidOperationException("This identity has no signed prekey (SPK); the session cannot be derived.");

        var dh1 = KeyAgreement.Agree(mine.DhPrivateKey, peerSpkPublic); // DH(IK_a, SPK_b)
        var dh2 = KeyAgreement.Agree(ekPrivate, peerIkPublic);          // DH(EK_a, IK_b)
        var dh3 = KeyAgreement.Agree(ekPrivate, peerSpkPublic);         // DH(EK_a, SPK_b)
        return ConcatAndDerive(dh1, dh2, dh3, info);
    }

    /// <summary>
    /// Derives the root session key as the <b>responder</b> (receiver of a
    /// prekey message). The responder uses its own signed-prekey private key and
    /// identity private key together with the sender's public identity key
    /// (<paramref name="senderIkPublic"/>) and one-time key
    /// (<paramref name="senderEkPublic"/>), producing the same key as the
    /// initiator:
    /// <c>DH(SPK_b, IK_a) ‖ DH(IK_b, EK_a) ‖ DH(SPK_b, EK_a)</c>.
    /// </summary>
    public static byte[] DeriveSessionKeyAsResponder(
        IdentityKeyPair mine,
        byte[] senderIkPublic,
        byte[] senderEkPublic,
        string info)
    {
        ArgumentNullException.ThrowIfNull(mine);
        ArgumentNullException.ThrowIfNull(senderIkPublic);
        ArgumentNullException.ThrowIfNull(senderEkPublic);
        Validate32(senderIkPublic, nameof(senderIkPublic));
        Validate32(senderEkPublic, nameof(senderEkPublic));
        if (!mine.HasEphemeralKeys)
            throw new InvalidOperationException("This identity has no signed prekey (SPK); the session cannot be derived.");

        var dh1 = KeyAgreement.Agree(mine.EphemeralDhPrivateKey, senderIkPublic); // DH(SPK_b, IK_a) == DH(IK_a, SPK_b)
        var dh2 = KeyAgreement.Agree(mine.DhPrivateKey, senderEkPublic);          // DH(IK_b, EK_a) == DH(EK_a, IK_b)
        var dh3 = KeyAgreement.Agree(mine.EphemeralDhPrivateKey, senderEkPublic); // DH(SPK_b, EK_a) == DH(EK_a, SPK_b)
        return ConcatAndDerive(dh1, dh2, dh3, info);
    }

    /// <summary>
    /// Full initiator-side establishment: generates the one-time EK, derives the
    /// session key, seeds the ratchet states, and then <b>zeroes both the root
    /// session key and the EK private key</b> before returning. Only the two
    /// ratchet states and the EK public key leave this method, so the EK private
    /// key can never be persisted.
    /// </summary>
    public static (byte[] SendState, byte[] RecvState, byte[] EkPublic) EstablishInitiatorSession(
        IdentityKeyPair mine,
        byte[] peerIkPublic,
        byte[] peerSpkPublic,
        string info)
    {
        var ek = KeyGeneration.GenerateX25519KeyPair();
        try
        {
            var (send, recv) = EstablishInitiatorSessionWithEk(mine, ek.PrivateKey, peerIkPublic, peerSpkPublic, info);
            return (send, recv, ek.PublicKey);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(ek.PrivateKey);
        }
    }

    /// <summary>
    /// Initiator-side establishment using an already-generated one-time EK
    /// private key. Derives and seeds, zeroing only the root session key. Used
    /// when a single one-time EK is shared across several recipients of one
    /// group broadcast; the caller owns and must zero <paramref name="ekPrivate"/>
    /// itself once all recipients have been established.
    /// </summary>
    public static (byte[] SendState, byte[] RecvState) EstablishInitiatorSessionWithEk(
        IdentityKeyPair mine,
        byte[] ekPrivate,
        byte[] peerIkPublic,
        byte[] peerSpkPublic,
        string info)
    {
        var sessionKey = DeriveSessionKeyAsInitiator(mine, ekPrivate, peerIkPublic, peerSpkPublic, info);
        try
        {
            return SeedRatchetStates(sessionKey, mine.DhPublicKey, peerIkPublic);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(sessionKey);
        }
    }

    /// <summary>
    /// Full responder-side establishment from a received prekey message: derives
    /// the session key from the sender's identity + one-time EK public keys, seeds
    /// the ratchet states, and zeroes the root session key before returning.
    /// </summary>
    public static (byte[] SendState, byte[] RecvState) EstablishResponderSession(
        IdentityKeyPair mine,
        byte[] senderIkPublic,
        byte[] senderEkPublic,
        string info)
    {
        var sessionKey = DeriveSessionKeyAsResponder(mine, senderIkPublic, senderEkPublic, info);
        try
        {
            return SeedRatchetStates(sessionKey, mine.DhPublicKey, senderIkPublic);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(sessionKey);
        }
    }

    private static byte[] ConcatAndDerive(byte[] dh1, byte[] dh2, byte[] dh3, string info)
    {
        var shared = new byte[dh1.Length + dh2.Length + dh3.Length];
        dh1.CopyTo(shared, 0);
        dh2.CopyTo(shared, dh1.Length);
        dh3.CopyTo(shared, dh1.Length + dh2.Length);
        return KeyAgreement.DeriveSessionKey(shared, DefaultSalt, info);
    }

    private static void Validate32(byte[] value, string name)
    {
        if (value.Length != IdentityKeyPair.X25519PublicKeyBytes)
            throw new ArgumentException($"{name} must be {IdentityKeyPair.X25519PublicKeyBytes} bytes.", name);
    }

    /// <summary>
    /// Seeds the two direction-bound ratchet chain states from the session key.
    /// The roles are assigned deterministically from the X25519 public-key
    /// ordering so that A's send chain equals B's receive chain and vice versa.
    /// </summary>
    public static (byte[] Send, byte[] Recv) SeedRatchetStates(byte[] sessionKey, byte[] myX25519Public, byte[] peerX25519Public)
    {
        ArgumentNullException.ThrowIfNull(sessionKey);
        ArgumentNullException.ThrowIfNull(myX25519Public);
        ArgumentNullException.ThrowIfNull(peerX25519Public);
        if (sessionKey.Length != SymmetricCrypto.KeyBytes)
            throw new ArgumentException($"A session key must be {SymmetricCrypto.KeyBytes} bytes.", nameof(sessionKey));

        bool mineIsFirst = LexicographicCompare(myX25519Public, peerX25519Public) < 0;
        var sendInfo = mineIsFirst ? SendInfoBytes : RecvInfoBytes;
        var recvInfo = mineIsFirst ? RecvInfoBytes : SendInfoBytes;

        var send = HKDF.DeriveKey(HashAlgorithmName.SHA256, sessionKey, SymmetricCrypto.KeyBytes, salt: null, sendInfo);
        var recv = HKDF.DeriveKey(HashAlgorithmName.SHA256, sessionKey, SymmetricCrypto.KeyBytes, salt: null, recvInfo);
        return (send, recv);
    }

    /// <summary>
    /// Advances a ratchet chain one step and returns the new 32-byte message
    /// key together with the next chain state.
    /// </summary>
    public static (byte[] Key, byte[] State) AdvanceRatchet(byte[] state, byte[] info)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(info);
        if (state.Length == 0)
            throw new ArgumentException("The ratchet state must not be empty.", nameof(state));

        var okm = HKDF.DeriveKey(HashAlgorithmName.SHA256, state, SymmetricCrypto.KeyBytes * 2, salt: null, info);

        var key = okm.AsSpan(0, SymmetricCrypto.KeyBytes).ToArray();
        var next = okm.AsSpan(SymmetricCrypto.KeyBytes, SymmetricCrypto.KeyBytes).ToArray();
        CryptographicOperations.ZeroMemory(okm);

        return (key, next);
    }

    /// <summary>
    /// Encrypts and signs <paramref name="plaintext"/> with the NEXT send-ratchet
    /// key, binding <paramref name="context"/> into the AAD, then commits the
    /// advanced send state. Exactly one key is consumed per call; callers must
    /// persist <see cref="SendRatchetState"/> immediately after sealing and must
    /// NOT re-seal (or re-advance) when a send is retried — the identical
    /// envelope is re-sent instead.
    /// </summary>
    public MessageEnvelope Seal(byte[] plaintext, byte[] context)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        ArgumentNullException.ThrowIfNull(context);

        var (key, nextState) = AdvanceRatchet(_sendState, StepInfoBytes);
        _sendState = nextState;

        return MessageEnvelope.Seal(key, plaintext, _senderEdPublic, _senderEdPrivate, context);
    }

    /// <summary>
    /// Verifies and decrypts an incoming envelope using the receive chain,
    /// binding <paramref name="context"/> into the AAD. <paramref name="seq"/> is
    /// the sender's per-sender sequence number and keys the bounded skipped-key
    /// cache, so mildly out-of-order messages (within <see cref="MaxSkip"/>) can
    /// still be decrypted. On success the receive state is committed and the
    /// <paramref name="skippedKeys"/> cache is updated; on failure nothing is
    /// committed and a <see cref="CryptographicException"/> is thrown.
    /// </summary>
    public byte[] Open(MessageEnvelope envelope, byte[] context, long seq, IDictionary<long, byte[]> skippedKeys)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(skippedKeys);

        // A previously skipped message: decrypt with its cached key.
        if (skippedKeys.TryGetValue(seq, out var cachedKey))
        {
            var plaintext = MessageEnvelope.Open(envelope, cachedKey, context);
            skippedKeys.Remove(seq);
            return plaintext;
        }

        var candidate = (byte[])_recvState.Clone();
        var skippedInTry = new List<byte[]>();

        for (var k = 0; k <= MaxSkip; k++)
        {
            var (key, nextState) = AdvanceRatchet(candidate, StepInfoBytes);
            try
            {
                var plaintext = MessageEnvelope.Open(envelope, key, context);

                // Commit the receive chain past the matched message and stash the
                // intermediate keys (offsets 0..k-1 correspond to seqs (seq-k)..(seq-1)).
                for (var j = 0; j < k; j++)
                    AddSkippedKey(skippedKeys, seq - k + j, skippedInTry[j]);

                _recvState = nextState;
                return plaintext;
            }
            catch (CryptographicException)
            {
                skippedInTry.Add(key);
                candidate = nextState;
            }
        }

        throw new CryptographicException("Could not decrypt the message with the current receive ratchet state.");
    }

    private static void AddSkippedKey(IDictionary<long, byte[]> skippedKeys, long seq, byte[] key)
    {
        if (skippedKeys.Count >= MaxSkippedKeys)
        {
            // Evict the oldest (lowest seq) skipped key to keep the cache bounded.
            long oldest = long.MaxValue;
            foreach (var existing in skippedKeys.Keys)
            {
                if (existing < oldest)
                    oldest = existing;
            }
            if (oldest != long.MaxValue)
                skippedKeys.Remove(oldest);
        }

        skippedKeys[seq] = key;
    }

    private static int LexicographicCompare(byte[] a, byte[] b)
    {
        var len = Math.Min(a.Length, b.Length);
        for (var i = 0; i < len; i++)
        {
            var cmp = a[i].CompareTo(b[i]); // byte is unsigned: 0..255
            if (cmp != 0)
                return cmp;
        }
        return a.Length.CompareTo(b.Length);
    }
}
