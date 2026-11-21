using Microsoft.Data.Sqlite;
using NineLivesAudio.Models;
using AB = NineLivesAudio.Data.Db.AudioBook;

namespace NineLivesAudio.Data;

/// <summary>
/// Maps SqliteDataReader rows to <see cref="AudioBook"/> instances.
/// Uses <see cref="SqliteReaderExtensions"/> for null-safe column access.
/// </summary>
public static class AudioBookMapper
{
    public static AudioBook Map(SqliteDataReader reader)
    {
        return new AudioBook
        {
            Id = reader.GetString(reader.GetOrdinal(AB.Id)),
            LibraryId = reader.GetNullableString(AB.LibraryId),
            Title = reader.GetString(reader.GetOrdinal(AB.Title)),
            Author = reader.GetNullableString(AB.Author) ?? string.Empty,
            Narrator = reader.GetNullableString(AB.Narrator),
            Description = reader.GetNullableString(AB.Description),
            CoverPath = reader.GetNullableString(AB.CoverPath),
            Duration = TimeSpan.FromSeconds(reader.GetDoubleOrDefault(AB.DurationSeconds)),
            AddedAt = reader.GetNullableDateTime(AB.AddedAt),
            AudioFiles = reader.DeserializeJson(AB.AudioFilesJson, new List<AudioFile>()),
            SeriesName = reader.GetNullableString(AB.SeriesName),
            SeriesSequence = reader.GetNullableString(AB.SeriesSequence),
            Genres = reader.DeserializeJson(AB.GenresJson, new List<string>()),
            Tags = reader.DeserializeJson(AB.TagsJson, new List<string>()),
            Chapters = reader.DeserializeJson(AB.ChaptersJson, new List<Chapter>()),
            CurrentTime = TimeSpan.FromSeconds(reader.GetDoubleOrDefault(AB.CurrentTimeSeconds)),
            Progress = reader.GetDoubleOrDefault(AB.Progress),
            IsFinished = reader.GetBoolFromInt(AB.IsFinished),
            IsDownloaded = reader.GetBoolFromInt(AB.IsDownloaded),
            LocalPath = reader.GetNullableString(AB.LocalPath)
        };
    }

    public static void AddParameters(SqliteCommand command, AudioBook audioBook)
    {
        command.Parameters.AddWithValue("@id", audioBook.Id);
        command.Parameters.AddWithValue("@libraryId", audioBook.LibraryId ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@title", audioBook.Title);
        command.Parameters.AddWithValue("@author", audioBook.Author ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@narrator", audioBook.Narrator ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@description", audioBook.Description ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@coverPath", audioBook.CoverPath ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@durationSeconds", audioBook.Duration.TotalSeconds);
        command.Parameters.AddWithValue("@addedAt", audioBook.AddedAt?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@audioFilesJson", System.Text.Json.JsonSerializer.Serialize(audioBook.AudioFiles));
        command.Parameters.AddWithValue("@currentTimeSeconds", audioBook.CurrentTime.TotalSeconds);
        command.Parameters.AddWithValue("@progress", audioBook.Progress);
        command.Parameters.AddWithValue("@isFinished", audioBook.IsFinished ? 1 : 0);
        command.Parameters.AddWithValue("@isDownloaded", audioBook.IsDownloaded ? 1 : 0);
        command.Parameters.AddWithValue("@localPath", audioBook.LocalPath ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@seriesName", audioBook.SeriesName ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@seriesSequence", audioBook.SeriesSequence ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@genresJson", System.Text.Json.JsonSerializer.Serialize(audioBook.Genres));
        command.Parameters.AddWithValue("@tagsJson", System.Text.Json.JsonSerializer.Serialize(audioBook.Tags));
        command.Parameters.AddWithValue("@chaptersJson", System.Text.Json.JsonSerializer.Serialize(audioBook.Chapters));
    }
}
