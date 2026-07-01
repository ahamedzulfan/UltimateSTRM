using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using MediaBrowser.Common.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.UltimateStrm.Strm.Data;

/// <summary>
/// Stores .strm file metadata in the shared SQLite database (located alongside
/// Jellyfin's own database, in the server's DataPath) and keeps the corresponding
/// .strm files on disk in sync with that metadata.
/// </summary>
public class StrmRepository
{
    private readonly string _connectionString;
    private readonly ILogger<StrmRepository> _logger;
    private readonly object _syncRoot = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="StrmRepository"/> class.
    /// </summary>
    /// <param name="applicationPaths">Jellyfin application paths, used to find the shared DataPath.</param>
    /// <param name="logger">Logger instance.</param>
    public StrmRepository(IApplicationPaths applicationPaths, ILogger<StrmRepository> logger)
    {
        _logger = logger;

        var dbPath = Path.Combine(applicationPaths.DataPath, "ultimatestrm.db");
        _connectionString = $"Data Source={dbPath}";

        try
        {
            SQLitePCL.Batteries_V2.Init();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "SQLitePCL provider initialization skipped/failed (likely already initialized by host).");
        }

        Initialize();
    }

    private void Initialize()
    {
        lock (_syncRoot)
        {
            using var connection = OpenConnection();

            using var command = connection.CreateCommand();
            command.CommandText =
                """
                CREATE TABLE IF NOT EXISTS StrmEntries (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Title TEXT NOT NULL,
                    FolderPath TEXT NOT NULL,
                    ContentUrl TEXT NOT NULL,
                    FilePath TEXT NOT NULL,
                    CreatedUtc TEXT NOT NULL,
                    UpdatedUtc TEXT NOT NULL
                );
                """;
            command.ExecuteNonQuery();
        }
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

    /// <summary>
    /// Gets every managed .strm entry, ordered by title.
    /// </summary>
    /// <returns>List of entries.</returns>
    public List<StrmEntry> GetAll()
    {
        lock (_syncRoot)
        {
            using var connection = OpenConnection();

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT Id, Title, FolderPath, ContentUrl, FilePath, CreatedUtc, UpdatedUtc FROM StrmEntries ORDER BY Title COLLATE NOCASE;";

            var results = new List<StrmEntry>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                results.Add(ReadEntry(reader));
            }

            return results;
        }
    }

    /// <summary>
    /// Gets a single entry by id.
    /// </summary>
    /// <param name="id">The row id.</param>
    /// <returns>The entry, or null if not found.</returns>
    public StrmEntry GetById(int id)
    {
        lock (_syncRoot)
        {
            using var connection = OpenConnection();

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT Id, Title, FolderPath, ContentUrl, FilePath, CreatedUtc, UpdatedUtc FROM StrmEntries WHERE Id = @id;";
            command.Parameters.AddWithValue("@id", id);

            using var reader = command.ExecuteReader();
            return reader.Read() ? ReadEntry(reader) : null;
        }
    }

    /// <summary>
    /// Creates a new .strm file on disk and stores its metadata.
    /// </summary>
    /// <param name="title">Title, also used as the file name.</param>
    /// <param name="folderPath">Destination folder.</param>
    /// <param name="contentUrl">URL/content written into the .strm file.</param>
    /// <returns>The newly created entry.</returns>
    public StrmEntry Add(string title, string folderPath, string contentUrl)
    {
        ValidateInput(title, folderPath, contentUrl);

        var filePath = BuildFilePath(folderPath, title);
        WriteStrmFile(filePath, contentUrl);

        lock (_syncRoot)
        {
            using var connection = OpenConnection();

            var now = DateTime.UtcNow.ToString("O");

            using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO StrmEntries (Title, FolderPath, ContentUrl, FilePath, CreatedUtc, UpdatedUtc)
                VALUES (@title, @folderPath, @contentUrl, @filePath, @createdUtc, @updatedUtc);
                SELECT last_insert_rowid();
                """;
            command.Parameters.AddWithValue("@title", title);
            command.Parameters.AddWithValue("@folderPath", folderPath);
            command.Parameters.AddWithValue("@contentUrl", contentUrl);
            command.Parameters.AddWithValue("@filePath", filePath);
            command.Parameters.AddWithValue("@createdUtc", now);
            command.Parameters.AddWithValue("@updatedUtc", now);

            var newId = Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);

            return new StrmEntry
            {
                Id = newId,
                Title = title,
                FolderPath = folderPath,
                ContentUrl = contentUrl,
                FilePath = filePath,
                CreatedUtc = DateTime.Parse(now, null, DateTimeStyles.RoundtripKind),
                UpdatedUtc = DateTime.Parse(now, null, DateTimeStyles.RoundtripKind)
            };
        }
    }

    /// <summary>
    /// Creates multiple .strm files/entries in one batch. Each is validated and written
    /// independently; failures for one row don't prevent the others from being created.
    /// </summary>
    /// <param name="rows">The rows to add.</param>
    /// <returns>A result containing the created entries and any per-row errors.</returns>
    public BatchAddResult AddMany(IEnumerable<(string Title, string FolderPath, string ContentUrl)> rows)
    {
        var created = new List<StrmEntry>();
        var errors = new List<BatchAddError>();

        var index = 0;
        foreach (var row in rows)
        {
            try
            {
                created.Add(Add(row.Title, row.FolderPath, row.ContentUrl));
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
            {
                errors.Add(new BatchAddError { Index = index, Title = row.Title, Message = ex.Message });
            }

            index++;
        }

        return new BatchAddResult { Created = created, Errors = errors };
    }

    /// <summary>
    /// Updates an existing entry. If the title or folder changed, the old .strm file is
    /// removed and a new one is written at the new location.
    /// </summary>
    /// <param name="id">The row id to update.</param>
    /// <param name="title">New title.</param>
    /// <param name="folderPath">New destination folder.</param>
    /// <param name="contentUrl">New URL/content.</param>
    /// <returns>The updated entry, or null if no entry with that id exists.</returns>
    public StrmEntry Update(int id, string title, string folderPath, string contentUrl)
    {
        ValidateInput(title, folderPath, contentUrl);

        var existing = GetById(id);
        if (existing is null)
        {
            return null;
        }

        var newFilePath = BuildFilePath(folderPath, title);

        if (!string.Equals(newFilePath, existing.FilePath, StringComparison.OrdinalIgnoreCase) && File.Exists(existing.FilePath))
        {
            try
            {
                File.Delete(existing.FilePath);
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "Could not delete old .strm file at {OldPath} while renaming/moving entry {Id}.", existing.FilePath, id);
            }
        }

        WriteStrmFile(newFilePath, contentUrl);

        lock (_syncRoot)
        {
            using var connection = OpenConnection();

            var now = DateTime.UtcNow.ToString("O");

            using var command = connection.CreateCommand();
            command.CommandText =
                """
                UPDATE StrmEntries
                SET Title = @title, FolderPath = @folderPath, ContentUrl = @contentUrl, FilePath = @filePath, UpdatedUtc = @updatedUtc
                WHERE Id = @id;
                """;
            command.Parameters.AddWithValue("@title", title);
            command.Parameters.AddWithValue("@folderPath", folderPath);
            command.Parameters.AddWithValue("@contentUrl", contentUrl);
            command.Parameters.AddWithValue("@filePath", newFilePath);
            command.Parameters.AddWithValue("@updatedUtc", now);
            command.Parameters.AddWithValue("@id", id);
            command.ExecuteNonQuery();
        }

        return GetById(id);
    }

    /// <summary>
    /// Deletes an entry's database row and, if present, its .strm file on disk.
    /// </summary>
    /// <param name="id">The row id to delete.</param>
    /// <returns>True if an entry was found and deleted.</returns>
    public bool Delete(int id)
    {
        var existing = GetById(id);
        if (existing is null)
        {
            return false;
        }

        if (File.Exists(existing.FilePath))
        {
            try
            {
                File.Delete(existing.FilePath);
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "Could not delete .strm file at {Path} for entry {Id}.", existing.FilePath, id);
            }
        }

        lock (_syncRoot)
        {
            using var connection = OpenConnection();

            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM StrmEntries WHERE Id = @id;";
            command.Parameters.AddWithValue("@id", id);
            command.ExecuteNonQuery();
        }

        return true;
    }

    private static void ValidateInput(string title, string folderPath, string contentUrl)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("Title is required.", nameof(title));
        }

        if (string.IsNullOrWhiteSpace(folderPath))
        {
            throw new ArgumentException("Path is required.", nameof(folderPath));
        }

        if (string.IsNullOrWhiteSpace(contentUrl))
        {
            throw new ArgumentException("Content URL is required.", nameof(contentUrl));
        }
    }

    private static void WriteStrmFile(string filePath, string contentUrl)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(filePath, contentUrl);
    }

    private static string BuildFilePath(string folderPath, string title)
    {
        var fileName = SanitizeFileName(title) + ".strm";
        return Path.Combine(folderPath, fileName);
    }

    private static string SanitizeFileName(string name)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Where(c => !invalidChars.Contains(c)).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "untitled" : sanitized;
    }

    private static StrmEntry ReadEntry(SqliteDataReader reader)
    {
        return new StrmEntry
        {
            Id = reader.GetInt32(0),
            Title = reader.GetString(1),
            FolderPath = reader.GetString(2),
            ContentUrl = reader.GetString(3),
            FilePath = reader.GetString(4),
            CreatedUtc = DateTime.Parse(reader.GetString(5), null, DateTimeStyles.RoundtripKind),
            UpdatedUtc = DateTime.Parse(reader.GetString(6), null, DateTimeStyles.RoundtripKind)
        };
    }
}
