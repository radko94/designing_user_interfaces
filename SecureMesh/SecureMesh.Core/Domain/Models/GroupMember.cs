using SQLite;

namespace SecureMesh.Core.Domain;

/// <summary>Many-to-many membership of a <see cref="Contact"/> in a <see cref="Group"/>.</summary>
[Table("GroupMember")]
public sealed class GroupMember
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int GroupId { get; set; }

    [Indexed]
    public int ContactId { get; set; }
}
