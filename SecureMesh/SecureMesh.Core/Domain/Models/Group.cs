using SQLite;

namespace SecureMesh.Core.Domain;

[Table("Group")]
public sealed class Group
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    public string Name { get; set; } = "";

    /// <summary>Stable cross-device identifier shared by every member of the group.</summary>
    public string GroupKey { get; set; } = "";

    /// <summary>
    /// Local conversation id used to key messages and sync state. Nullable so
    /// adding the column does not break legacy rows; it is always set when a
    /// group is created via <c>GroupChatService.CreateGroup</c>.
    /// </summary>
    public int? ConversationId { get; set; }
}
