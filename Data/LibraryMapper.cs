using Microsoft.Data.Sqlite;
using NineLivesAudio.Models;
using LB = NineLivesAudio.Data.Db.Library;

namespace NineLivesAudio.Data;

/// <summary>
/// Maps SqliteDataReader rows to <see cref="Library"/> instances.
/// </summary>
public static class LibraryMapper
{
    public static Library Map(SqliteDataReader reader)
    {
        return new Library
        {
            Id = reader.GetString(reader.GetOrdinal(LB.Id)),
            Name = reader.GetString(reader.GetOrdinal(LB.Name)),
            DisplayOrder = reader.GetInt32OrDefault(LB.DisplayOrder),
            Icon = reader.GetNullableString(LB.Icon) ?? "audiobook",
            MediaType = reader.GetNullableString(LB.MediaType) ?? "book",
            Folders = reader.DeserializeJson(LB.FoldersJson, new List<Folder>())
        };
    }
}
