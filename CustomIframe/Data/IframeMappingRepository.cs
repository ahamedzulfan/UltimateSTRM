using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Jellyfin.Plugin.UltimateStrm.Yt2Strm.Services;
using MediaBrowser.Common.Configuration;
using Microsoft.Data.Sqlite;

namespace Jellyfin.Plugin.UltimateStrm.CustomIframe.Data;

/// <summary>
/// Persists Custom Iframe mappings in the shared <c>ultimatestrm.db</c> SQLite database,
/// replacing the previous XML plugin-config approach.
/// </summary>
public class IframeMappingRepository
{
    private readonly string _connectionString;
    private readonly ActivityLog _activityLog;

    /// <summary>
    /// Initializes a new instance of the <see cref="IframeMappingRepository"/> class.
    /// </summary>
    public IframeMappingRepository(IApplicationPaths applicationPaths, ActivityLog activityLog)
    {
        _activityLog = activityLog;
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
                CREATE TABLE IF NOT EXISTS IframeMappings (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    JellyfinId TEXT NOT NULL,
                    EmbedUrl TEXT NOT NULL
                );";
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>Returns all mappings.</summary>
    public List<IframeMapping> GetAll()
    {
        var results = new List<IframeMapping>();
        using (var connection = OpenConnection())
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT Id, JellyfinId, EmbedUrl FROM IframeMappings ORDER BY Id;";
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    results.Add(new IframeMapping
                    {
                        Id = reader.GetInt32(0),
                        JellyfinId = reader.GetString(1),
                        EmbedUrl = reader.GetString(2)
                    });
                }
            }
        }

        return results;
    }

    /// <summary>Inserts a new mapping and returns its new id.</summary>
    public int Add(string jellyfinId, string embedUrl)
    {
        using (var connection = OpenConnection())
        {
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "INSERT INTO IframeMappings (JellyfinId, EmbedUrl) VALUES (@jid, @url);";
                cmd.Parameters.AddWithValue("@jid", jellyfinId);
                cmd.Parameters.AddWithValue("@url", embedUrl);
                cmd.ExecuteNonQuery();
            }

            using (var idCmd = connection.CreateCommand())
            {
                idCmd.CommandText = "SELECT last_insert_rowid();";
                var id = Convert.ToInt32(idCmd.ExecuteScalar(), CultureInfo.InvariantCulture);
                _activityLog.Log($"[CustomIframe] Added mapping #{id}: {jellyfinId} → {embedUrl}", "info");
                return id;
            }
        }
    }

    /// <summary>Deletes a mapping by id. Returns true if a row was deleted.</summary>
    public bool Delete(int id)
    {
        using (var connection = OpenConnection())
        using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "DELETE FROM IframeMappings WHERE Id = @id;";
            cmd.Parameters.AddWithValue("@id", id);
            var affected = cmd.ExecuteNonQuery();
            if (affected > 0)
            {
                _activityLog.Log($"[CustomIframe] Deleted mapping #{id}", "info");
            }

            return affected > 0;
        }
    }
}
