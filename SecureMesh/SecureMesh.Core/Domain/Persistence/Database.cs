using SQLite;

namespace SecureMesh.Core.Domain.Persistence;

/// <summary>
/// Owns a SQLite connection and creates the domain schema. Uses the
/// <c>e_sqlite3</c> provider bundled with sqlite-net-pcl; callers do not need
/// to add a separate provider/batteries package.
/// </summary>
public sealed class Database : IDisposable
{
    private static readonly object InitLock = new();
    private static bool _providerInitialized;

    public SQLiteConnection Connection { get; }

    public Database(string path)
    {
        EnsureProvider();
        Connection = new SQLiteConnection(path);
        CreateSchema();
    }

    public Database(SQLiteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        EnsureProvider();
        Connection = connection;
        CreateSchema();
    }

    /// <summary>A private in-memory database (useful for tests).</summary>
    public static Database InMemory()
    {
        EnsureProvider();
        return new Database(new SQLiteConnection(":memory:"));
    }

    private void CreateSchema()
    {
        Connection.CreateTable<Contact>();
        Connection.CreateTable<Conversation>();
        Connection.CreateTable<Message>();
        Connection.CreateTable<Group>();
        Connection.CreateTable<GroupMember>();
        Connection.CreateTable<GroupMemberKey>();
        Connection.CreateTable<GroupMessageEnvelope>();
        Connection.CreateTable<SyncState>();

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
    }

    private static void EnsureProvider()
    {
        if (_providerInitialized)
            return;

        lock (InitLock)
        {
            if (_providerInitialized)
                return;

            // The e_sqlite3 provider is already a dependency of sqlite-net-pcl.
            SQLitePCL.raw.SetProvider(new SQLitePCL.SQLite3Provider_e_sqlite3());
            _providerInitialized = true;
        }
    }

    public void Dispose() => Connection.Dispose();
}
