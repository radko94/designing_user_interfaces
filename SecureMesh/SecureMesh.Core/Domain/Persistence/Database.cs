using SQLite;

namespace SecureMesh.Core.Domain.Persistence;

/// <summary>
/// Owns a SQLite connection and creates the domain schema. On-disk databases
/// are opened with an encryption provider installed by the host (SQLite3
/// Multiple Ciphers); the encryption key is supplied by the host and held in
/// Android Keystore-backed storage. In-memory databases used by tests are not
/// encrypted.
/// </summary>
public sealed class Database : IDisposable
{
    /// <summary>
    /// Bumped whenever the schema changes in a breaking way. On mismatch the
    /// tables are dropped and recreated — acceptable for this pre-release schema.
    /// </summary>
    private const int SchemaVersion = 4;

    private static readonly object InitLock = new();
    private static bool _providerInitialized;
    private static SQLitePCL.ISQLite3Provider? _configuredProvider;

    public SQLiteConnection Connection { get; }

    /// <summary>
    /// Opens (or creates) an on-disk database encrypted with
    /// <paramref name="password"/> as the encryption key. The host must install
    /// the encryption provider via <see cref="ConfigureProvider"/> first.
    /// </summary>
    public Database(string path, string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        EnsureProvider();
        Connection = OpenEncrypted(path, password);
        CreateSchema();
    }

    public Database(SQLiteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        EnsureProvider();
        Connection = connection;
        CreateSchema();
    }

    /// <summary>A private in-memory database (useful for tests; not encrypted).</summary>
    public static Database InMemory()
    {
        EnsureProvider();
        return new Database(new SQLiteConnection(":memory:"));
    }

    /// <summary>
    /// Selects the SQLitePCL provider to use before the first database is opened.
    /// The app calls this with the SQLite3 Multiple Ciphers provider; tests and
    /// in-memory databases fall back to the default <c>e_sqlite3</c> provider.
    /// </summary>
    public static void ConfigureProvider(SQLitePCL.ISQLite3Provider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        lock (InitLock)
        {
            _configuredProvider = provider;
            _providerInitialized = false; // (re)apply it on the next EnsureProvider
        }
    }

    private static SQLiteConnection OpenEncrypted(string path, string password)
    {
        var connection = new SQLiteConnection(new SQLiteConnectionString(path, true, password));
        try
        {
            // Touch the database so the encryption provider decrypts the first
            // page. A legacy plaintext DB (or one opened with the wrong key) fails here.
            connection.ExecuteScalar<int>("pragma user_version");
            return connection;
        }
        catch (SQLiteException)
        {
            // Not an encrypted database we can open. Pre-release: recreate it.
            connection.Dispose();
            if (File.Exists(path))
                File.Delete(path);
            return new SQLiteConnection(new SQLiteConnectionString(path, true, password));
        }
    }

    private void CreateSchema()
    {
        if (Connection.ExecuteScalar<int>("pragma user_version") != SchemaVersion)
            DropAllTables();

        Connection.CreateTable<Contact>();
        Connection.CreateTable<Conversation>();
        Connection.CreateTable<Message>();
        Connection.CreateTable<Group>();
        Connection.CreateTable<GroupMember>();
        Connection.CreateTable<GroupMemberKey>();
        Connection.CreateTable<GroupMessageEnvelope>();
        Connection.CreateTable<SyncState>();
        Connection.CreateTable<SkippedKey>();
        Connection.CreateTable<PendingMessage>();

        Connection.CreateIndex(
            "IX_Message_ConversationSeq", "Message", new[] { "ConversationId", "Seq" }, unique: true);
        Connection.CreateIndex(
            "IX_GroupMember_GroupContact", "GroupMember", new[] { "GroupId", "ContactId" }, unique: true);
        Connection.CreateIndex(
            "IX_GroupMemberKey_GroupContact", "GroupMemberKey", new[] { "GroupId", "MemberContactId" }, unique: true);
        Connection.CreateIndex(
            "IX_GroupMessageEnvelope_ConversationSeqRecipient",
            "GroupMessageEnvelope", new[] { "ConversationId", "Seq", "RecipientContactId" }, unique: true);
        Connection.CreateIndex(
            "IX_SyncState_ConversationPeer", "SyncState", new[] { "ConversationId", "PeerId" }, unique: true);

        Connection.Execute($"pragma user_version = {SchemaVersion}");
    }

    private void DropAllTables()
    {
        Connection.Execute("drop table if exists \"Contact\"");
        Connection.Execute("drop table if exists \"Conversation\"");
        Connection.Execute("drop table if exists \"Message\"");
        Connection.Execute("drop table if exists \"Group\"");
        Connection.Execute("drop table if exists \"GroupMember\"");
        Connection.Execute("drop table if exists \"GroupMemberKey\"");
        Connection.Execute("drop table if exists \"GroupMessageEnvelope\"");
        Connection.Execute("drop table if exists \"SyncState\"");
        Connection.Execute("drop table if exists \"SkippedKey\"");
        Connection.Execute("drop table if exists \"PendingMessage\"");
    }

    private static void EnsureProvider()
    {
        if (_providerInitialized)
            return;

        lock (InitLock)
        {
            if (_providerInitialized)
                return;

            // sqlite-net-pcl's SQLiteConnection type has a static constructor that
            // calls its private SQLitePCL.Batteries_V2.Init(), which unconditionally
            // does raw.SetProvider(new SQLite3Provider_e_sqlite3()). That static
            // constructor runs lazily on the FIRST new SQLiteConnection(...) — i.e.
            // after any encryption provider was installed here — and would silently
            // replace it with the plaintext provider, making `PRAGMA key` a no-op.
            // Force it to run now, before we install our provider, so it cannot
            // override the encryption provider afterwards.
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(
                typeof(SQLiteConnection).TypeHandle);

            SQLitePCL.raw.SetProvider(
                _configuredProvider ?? new SQLitePCL.SQLite3Provider_e_sqlite3());
            _providerInitialized = true;
        }
    }

    public void Dispose() => Connection.Dispose();
}
