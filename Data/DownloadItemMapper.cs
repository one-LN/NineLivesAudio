using Microsoft.Data.Sqlite;
using NineLivesAudio.Models;
using DL = NineLivesAudio.Data.Db.Download;

namespace NineLivesAudio.Data;

/// <summary>
/// Maps SqliteDataReader rows to <see cref="DownloadItem"/> instances.
/// </summary>
public static class DownloadItemMapper
{
    public static DownloadItem Map(SqliteDataReader reader)
    {
        return new DownloadItem
        {
            Id = reader.GetString(reader.GetOrdinal(DL.Id)),
            AudioBookId = reader.GetString(reader.GetOrdinal(DL.AudioBookId)),
            Title = reader.GetString(reader.GetOrdinal(DL.Title)),
            Status = (DownloadStatus)reader.GetInt32OrDefault(DL.Status),
            TotalBytes = reader.GetInt64OrDefault(DL.TotalBytes),
            DownloadedBytes = reader.GetInt64OrDefault(DL.DownloadedBytes),
            StartedAt = reader.GetNullableDateTime(DL.StartedAt),
            CompletedAt = reader.GetNullableDateTime(DL.CompletedAt),
            ErrorMessage = reader.GetNullableString(DL.ErrorMessage),
            FilesToDownload = reader.DeserializeJson(DL.FilesToDownloadJson, new List<string>())
        };
    }
}
