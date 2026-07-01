using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using MediaBrowser.Common.Configuration;
using Microsoft.Data.Sqlite;

namespace Jellyfin.Plugin.UltimateStrm.Yt2Strm.Data;

/// <summary>
/// Each public method opens its own short-lived connection. That keeps this safe to call
/// concurrently from the API controller, the background worker, and the scheduled task
/// without sharing a connection object across threads.
/// </summary>
public class LinkRepository
{
    private readonly string _connectionString;

    /// <summary>
    /// Initializes a new instance of the <see cref="LinkRepository"/> class.
    /// </summary>
    public LinkRepository(IApplicationPaths applicationPaths)
    {
        var dbPath = Path.Combine(applicationPaths.DataPath, "ultimatestrm.db");
        _connectionString = string.Format(CultureInfo.InvariantCulture, "Data Source={0}", dbPath);
        Initialize();
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA journal_mode=WAL;";
            pragma.ExecuteNonQuery();
        }

        return connection;
    }

    private void Initialize()
    {
        using (var connection = OpenConnection())
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS Links (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Title TEXT,
                    Url TEXT NOT NULL,
                    SavePath TEXT NOT NULL,
                    Format TEXT NOT NULL DEFAULT 'best',
                    Enabled INTEGER NOT NULL DEFAULT 1,
                    ResolvedTitle TEXT,
                    Status INTEGER NOT NULL DEFAULT 0,
                    StrmPath TEXT,
                    LastError TEXT,
                    LastChecked TEXT,
                    CreatedAt TEXT NOT NULL
                );";
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>Gets all links ordered by Id descending.</summary>
    public List<LinkRecord> GetAll()
    {
        var results = new List<LinkRecord>();
        using (var connection = OpenConnection())
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT * FROM Links ORDER BY Id DESC;";
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    results.Add(ReadRecord(reader));
                }
            }
        }

        return results;
    }

    /// <summary>Gets a single link by its id, or null if not found.</summary>
    public LinkRecord GetById(int id)
    {
        using (var connection = OpenConnection())
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT * FROM Links WHERE Id = @id;";
            cmd.Parameters.AddWithValue("@id", id);
            using (var reader = cmd.ExecuteReader())
            {
                return reader.Read() ? ReadRecord(reader) : null;
            }
        }
    }

    /// <summary>Returns true if the URL already exists (optionally excluding a specific row).</summary>
    public bool UrlExists(string url, int? excludingId = null)
    {
        using (var connection = OpenConnection())
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = excludingId.HasValue
                ? "SELECT COUNT(1) FROM Links WHERE Url = @url AND Id <> @excludingId;"
                : "SELECT COUNT(1) FROM Links WHERE Url = @url;";
            cmd.Parameters.AddWithValue("@url", url);
            if (excludingId.HasValue)
            {
                cmd.Parameters.AddWithValue("@excludingId", excludingId.Value);
            }

            return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture) > 0;
        }
    }

    /// <summary>Case-insensitive, global across all links regardless of folder.</summary>
    public bool TitleExists(string title, int? excludingId = null)
    {
        using (var connection = OpenConnection())
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = excludingId.HasValue
                ? "SELECT COUNT(1) FROM Links WHERE Title = @title COLLATE NOCASE AND Id <> @excludingId;"
                : "SELECT COUNT(1) FROM Links WHERE Title = @title COLLATE NOCASE;";
            cmd.Parameters.AddWithValue("@title", title);
            if (excludingId.HasValue)
            {
                cmd.Parameters.AddWithValue("@excludingId", excludingId.Value);
            }

            return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture) > 0;
        }
    }

    /// <summary>Inserts a new link record and returns its new id.</summary>
    public int Insert(LinkRecord record)
    {
        using (var connection = OpenConnection())
        {
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = @"
                    INSERT INTO Links (Title, Url, SavePath, Format, Enabled, Status, CreatedAt)
                    VALUES (@title, @url, @savePath, @format, 1, @status, @createdAt);";
                cmd.Parameters.AddWithValue("@title", (object)record.Title ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@url", record.Url);
                cmd.Parameters.AddWithValue("@savePath", record.SavePath);
                cmd.Parameters.AddWithValue("@format", string.IsNullOrWhiteSpace(record.Format) ? "best" : record.Format);
                cmd.Parameters.AddWithValue("@status", (int)LinkStatus.Queued);
                cmd.Parameters.AddWithValue("@createdAt", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
                cmd.ExecuteNonQuery();
            }

            using (var idCmd = connection.CreateCommand())
            {
                idCmd.CommandText = "SELECT last_insert_rowid();";
                return Convert.ToInt32(idCmd.ExecuteScalar(), CultureInfo.InvariantCulture);
            }
        }
    }

    /// <summary>Updates the editable fields of an existing link.</summary>
    public void UpdateDetails(int id, string title, string url, string savePath, string format, bool enabled)
    {
        using (var connection = OpenConnection())
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
                UPDATE Links SET Title = @title, Url = @url, SavePath = @savePath,
                    Format = @format, Enabled = @enabled WHERE Id = @id;";
            cmd.Parameters.AddWithValue("@title", (object)title ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@url", url);
            cmd.Parameters.AddWithValue("@savePath", savePath);
            cmd.Parameters.AddWithValue("@format", string.IsNullOrWhiteSpace(format) ? "best" : format);
            cmd.Parameters.AddWithValue("@enabled", enabled ? 1 : 0);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>Marks a link as currently resolving.</summary>
    public void SetStatusRunning(int id)
    {
        using (var connection = OpenConnection())
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "UPDATE Links SET Status = @status WHERE Id = @id;";
            cmd.Parameters.AddWithValue("@status", (int)LinkStatus.Running);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>Marks a link as successfully resolved.</summary>
    public void SetStatusSuccess(int id, string resolvedTitle, string strmPath)
    {
        using (var connection = OpenConnection())
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
                UPDATE Links SET Status = @status, LastError = NULL, LastChecked = @lastChecked,
                    ResolvedTitle = @resolvedTitle, StrmPath = @strmPath WHERE Id = @id;";
            cmd.Parameters.AddWithValue("@status", (int)LinkStatus.Active);
            cmd.Parameters.AddWithValue("@lastChecked", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("@resolvedTitle", (object)resolvedTitle ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@strmPath", (object)strmPath ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>Marks a link as failed. Failures always clear StrmPath so it never points at a missing file.</summary>
    public void SetStatusFailed(int id, string error, string resolvedTitle)
    {
        using (var connection = OpenConnection())
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = @"
                UPDATE Links SET Status = @status, LastError = @error, LastChecked = @lastChecked,
                    ResolvedTitle = @resolvedTitle, StrmPath = NULL WHERE Id = @id;";
            cmd.Parameters.AddWithValue("@status", (int)LinkStatus.Dead);
            cmd.Parameters.AddWithValue("@error", (object)error ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@lastChecked", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("@resolvedTitle", (object)resolvedTitle ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>Deletes a link record.</summary>
    public void Delete(int id)
    {
        using (var connection = OpenConnection())
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "DELETE FROM Links WHERE Id = @id;";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }
    }

    private static LinkRecord ReadRecord(SqliteDataReader reader)
    {
        return new LinkRecord
        {
            Id = reader.GetInt32(reader.GetOrdinal("Id")),
            Title = SafeGetString(reader, "Title"),
            Url = SafeGetString(reader, "Url"),
            SavePath = SafeGetString(reader, "SavePath"),
            Format = SafeGetString(reader, "Format"),
            Enabled = reader.GetInt32(reader.GetOrdinal("Enabled")) == 1,
            ResolvedTitle = SafeGetString(reader, "ResolvedTitle"),
            Status = (LinkStatus)reader.GetInt32(reader.GetOrdinal("Status")),
            StrmPath = SafeGetString(reader, "StrmPath"),
            LastError = SafeGetString(reader, "LastError"),
            LastChecked = ParseDate(SafeGetString(reader, "LastChecked")),
            CreatedAt = ParseDate(SafeGetString(reader, "CreatedAt")) ?? DateTime.UtcNow
        };
    }

    private static string SafeGetString(SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static DateTime? ParseDate(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        return DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }
}
