using Microsoft.Extensions.DependencyInjection;
using SecureMesh.Core.Domain.Persistence;

namespace SecureMesh.Core.Domain;

/// <summary>Manages the local contact book.</summary>
public sealed class ContactsService
{
    private readonly ContactRepository _contacts;

    [ActivatorUtilitiesConstructor]
    public ContactsService(Database db)
        : this(new ContactRepository(db.Connection))
    {
    }

    public ContactsService(ContactRepository contacts) => _contacts = contacts;

    public Contact AddContact(string nickname, byte[] ed25519Pub, byte[] x25519Pub)
        => AddContact(nickname, ed25519Pub, x25519Pub, Array.Empty<byte>());

    /// <summary>
    /// Adds a contact. <paramref name="ed25519Pub"/> authenticates signed
    /// messages, <paramref name="x25519Pub"/> is the peer's long-term X25519
    /// identity key, and <paramref name="ephemeralX25519Pub"/> is the peer's
    /// signed prekey (SPK) used in X3DH-lite session establishment.
    /// </summary>
    public Contact AddContact(string nickname, byte[] ed25519Pub, byte[] x25519Pub, byte[] ephemeralX25519Pub)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nickname);
        ArgumentNullException.ThrowIfNull(ed25519Pub);
        ArgumentNullException.ThrowIfNull(x25519Pub);
        ArgumentNullException.ThrowIfNull(ephemeralX25519Pub);

        var contact = new Contact
        {
            Nickname = nickname,
            Ed25519Pub = (byte[])ed25519Pub.Clone(),
            X25519Pub = (byte[])x25519Pub.Clone(),
            EphemeralX25519Pub = (byte[])ephemeralX25519Pub.Clone(),
            Verified = false,
            AddedAt = DateTime.UtcNow,
        };

        _contacts.Insert(contact);
        return contact;
    }

    public IReadOnlyList<Contact> ListContacts() => _contacts.All();

    public Contact? GetById(int id) => _contacts.Get(id);

    public Contact? FindByEd25519Pub(byte[] edPublic) => _contacts.FindByEd25519Pub(edPublic);

    /// <summary>Marks a contact's key as verified out-of-band.</summary>
    public void VerifyContact(int id)
    {
        var contact = _contacts.Get(id)
            ?? throw new KeyNotFoundException($"Contact {id} not found.");
        contact.Verified = true;
        _contacts.Update(contact);
    }

    public void RemoveContact(int id) => _contacts.Delete(id);
}
