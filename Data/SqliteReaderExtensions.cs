using Microsoft.Data.Sqlite;
using System.Globalization;
using System.Text.Json;

namespace NineLivesAudio.Data;

/// <summary>
/// Extension methods for SqliteDataReader to reduce boilerplate and improve safety.
/// </summary>
public static class SqliteReaderExtensions
{
    /// <summary>
    /// Gets a nullable string from the reader, returning null if the column is DBNull.
    /// </summary>
    public static string? GetNullableString(this SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    /// <summary>
    /// Gets a double from the reader, returning 0 if the column is DBNull.
    /// </summary>
    public static double GetDoubleOrDefault(this SqliteDataReader reader, string column, double defaultValue = 0)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? defaultValue : reader.GetDouble(ordinal);
    }

    /// <summary>
    /// Gets an int from the reader, returning 0 if the column is DBNull.
    /// </summary>
    public static int GetInt32OrDefault(this SqliteDataReader reader, string column, int defaultValue = 0)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? defaultValue : reader.GetInt32(ordinal);
    }

    /// <summary>
    /// Gets a long from the reader, returning 0 if the column is DBNull.
    /// </summary>
    public static long GetInt64OrDefault(this SqliteDataReader reader, string column, long defaultValue = 0)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? defaultValue : reader.GetInt64(ordinal);
    }

    /// <summary>
    /// Gets a boolean from an INTEGER column (SQLite convention: 0=false, 1=true).
    /// </summary>
    public static bool GetBoolFromInt(this SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return !reader.IsDBNull(ordinal) && reader.GetInt32(ordinal) == 1;
    }

    /// <summary>
    /// Parses a nullable DateTime from an ISO 8601 string column, with RoundtripKind.
    /// Returns null if the column is DBNull or the value cannot be parsed.
    /// </summary>
    public static DateTime? GetNullableDateTime(this SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        if (reader.IsDBNull(ordinal)) return null;

        var str = reader.GetString(ordinal);
        return DateTime.TryParse(str, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt)
            ? dt
            : null;
    }

    /// <summary>
    /// Deserializes a JSON column to the given type. Returns defaultValue on null/empty/parse failure.
    /// </summary>
    public static T DeserializeJson<T>(this SqliteDataReader reader, string column, T defaultValue) where T : class
    {
        var json = reader.GetNullableString(column);
        if (string.IsNullOrEmpty(json)) return defaultValue;

        try
        {
            return JsonSerializer.Deserialize<T>(json) ?? defaultValue;
        }
        catch (JsonException)
        {
            return defaultValue;
        }
    }
}
