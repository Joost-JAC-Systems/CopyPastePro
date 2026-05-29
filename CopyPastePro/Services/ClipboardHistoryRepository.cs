using System.IO;
using System.Security.Cryptography;
using System.Text;
using CopyPastePro.Models;
using Microsoft.Data.Sqlite;

namespace CopyPastePro.Services;

public sealed class ClipboardHistoryRepository : IDisposable
{
  private readonly string _dbPath;
  private readonly string _dataDir;
  private SqliteConnection? _connection;

  public ClipboardHistoryRepository()
  {
    _dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CopyPastePro", "data");
    Directory.CreateDirectory(_dataDir);
    _dbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CopyPastePro", "history.db");
    Initialize();
  }

  private void Initialize()
  {
    _connection = new SqliteConnection($"Data Source={_dbPath}");
    _connection.Open();

    // Base table (works for new installs and existing DBs from v1)
    Execute("""
      CREATE TABLE IF NOT EXISTS clipboard_history (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        content_type INTEGER NOT NULL,
        preview TEXT NOT NULL,
        text_content TEXT,
        payload_path TEXT,
        file_paths_json TEXT,
        content_hash TEXT NOT NULL,
        captured_at TEXT NOT NULL,
        is_pinned INTEGER NOT NULL DEFAULT 0,
        size_bytes INTEGER NOT NULL DEFAULT 0
      );
      """);

    MigrateSchema();
    EnsureIndexes();
  }

  private void MigrateSchema()
  {
    // SQLite: ADD COLUMN cannot use NOT NULL without DEFAULT reliably on existing rows — use DEFAULT only
    AddColumnIfMissing("category", "TEXT", "'General'");
    AddColumnIfMissing("tags", "TEXT", "''");
    AddColumnIfMissing("source_app", "TEXT", "''");
    AddColumnIfMissing("file_extension", "TEXT", "''");
    AddColumnIfMissing("is_favorite", "INTEGER", "0");
    AddColumnIfMissing("is_sensitive", "INTEGER", "0");
    AddColumnIfMissing("export_path", "TEXT", "''");
    AddColumnIfMissing("sync_path", "TEXT", "''");

    Execute("UPDATE clipboard_history SET category = 'General' WHERE category IS NULL OR category = ''");
    Execute("UPDATE clipboard_history SET tags = '' WHERE tags IS NULL");
    Execute("UPDATE clipboard_history SET source_app = '' WHERE source_app IS NULL");
    Execute("UPDATE clipboard_history SET file_extension = '' WHERE file_extension IS NULL");
    Execute("UPDATE clipboard_history SET is_favorite = 0 WHERE is_favorite IS NULL");
    Execute("UPDATE clipboard_history SET is_sensitive = 0 WHERE is_sensitive IS NULL");
    Execute("UPDATE clipboard_history SET export_path = '' WHERE export_path IS NULL");
    Execute("UPDATE clipboard_history SET sync_path = '' WHERE sync_path IS NULL");
  }

  private void AddColumnIfMissing(string name, string sqlType, string defaultSql)
  {
    if (ColumnExists(name)) return;
    Execute($"ALTER TABLE clipboard_history ADD COLUMN {name} {sqlType} DEFAULT {defaultSql}");
  }

  private bool ColumnExists(string name)
  {
    using var cmd = _connection!.CreateCommand();
    cmd.CommandText = "PRAGMA table_info(clipboard_history)";
    using var r = cmd.ExecuteReader();
    while (r.Read())
    {
      if (string.Equals(r.GetString(1), name, StringComparison.OrdinalIgnoreCase))
        return true;
    }
    return false;
  }

  private void EnsureIndexes()
  {
    Execute("CREATE INDEX IF NOT EXISTS idx_history_captured ON clipboard_history(captured_at DESC)");
    Execute("CREATE INDEX IF NOT EXISTS idx_history_hash ON clipboard_history(content_hash)");
    if (ColumnExists("category"))
      Execute("CREATE INDEX IF NOT EXISTS idx_history_category ON clipboard_history(category)");
    if (ColumnExists("is_favorite"))
      Execute("CREATE INDEX IF NOT EXISTS idx_history_favorite ON clipboard_history(is_favorite)");
  }

  private void Execute(string sql)
  {
    using var cmd = _connection!.CreateCommand();
    cmd.CommandText = sql;
    cmd.ExecuteNonQuery();
  }

  public string DataDirectory => _dataDir;

  public string DatabaseFilePath => _dbPath;

  /// <summary>Copies the SQLite database to <paramref name="destinationPath"/> (WAL checkpointed first).</summary>
  public void ExportDatabaseTo(string destinationPath)
  {
    VacuumAndCheckpoint();
    var dir = Path.GetDirectoryName(destinationPath);
    if (!string.IsNullOrEmpty(dir))
      Directory.CreateDirectory(dir);
    File.Copy(_dbPath, destinationPath, overwrite: true);
  }

  /// <summary>Removes all rows from clipboard history (same as <see cref="ClearAll"/>).</summary>
  public int EmptyDatabase(int securePasses = 0, bool securePayload = true) =>
      ClearAll(securePasses, securePayload);

  public string StorePayload(byte[] data, string extension, bool encrypt = false)
  {
    var path = Path.Combine(_dataDir, $"{Guid.NewGuid():N}{extension}");
    if (encrypt)
    {
      var protectedBytes = DataProtectionHelper.ProtectBytes(data);
      File.WriteAllBytes(path, protectedBytes);
    }
    else
      File.WriteAllBytes(path, data);
    return path;
  }

  public byte[]? ReadPayload(string path, bool encrypted = false)
  {
    if (!File.Exists(path)) return null;
    var bytes = File.ReadAllBytes(path);
    return encrypted ? DataProtectionHelper.UnprotectBytes(bytes) : bytes;
  }

  public long Add(ClipboardEntry entry)
  {
    using var cmd = _connection!.CreateCommand();
    cmd.CommandText = """
      INSERT INTO clipboard_history
      (content_type, preview, text_content, payload_path, file_paths_json, content_hash, captured_at,
       is_pinned, size_bytes, category, tags, source_app, file_extension, is_favorite, is_sensitive, export_path, sync_path)
      VALUES (@type, @preview, @text, @payload, @files, @hash, @at, @pinned, @size, @cat, @tags, @app, @ext, @fav, @sens, @export, @sync);
      SELECT last_insert_rowid();
      """;
    BindEntry(cmd, entry);
    var id = (long)(cmd.ExecuteScalar() ?? 0L);
    entry.Id = id;
    return id;
  }

  private static void BindEntry(SqliteCommand cmd, ClipboardEntry entry)
  {
    cmd.Parameters.AddWithValue("@type", (int)entry.ContentType);
    cmd.Parameters.AddWithValue("@preview", entry.Preview);
    cmd.Parameters.AddWithValue("@text", (object?)entry.TextContent ?? DBNull.Value);
    cmd.Parameters.AddWithValue("@payload", (object?)entry.PayloadPath ?? DBNull.Value);
    cmd.Parameters.AddWithValue("@files", (object?)entry.FilePathsJson ?? DBNull.Value);
    cmd.Parameters.AddWithValue("@hash", entry.ContentHash);
    cmd.Parameters.AddWithValue("@at", entry.CapturedAt.ToString("O"));
    cmd.Parameters.AddWithValue("@pinned", entry.IsPinned ? 1 : 0);
    cmd.Parameters.AddWithValue("@size", entry.SizeBytes);
    cmd.Parameters.AddWithValue("@cat", entry.Category);
    cmd.Parameters.AddWithValue("@tags", entry.Tags);
    cmd.Parameters.AddWithValue("@app", entry.SourceApp);
    cmd.Parameters.AddWithValue("@ext", entry.FileExtension);
    cmd.Parameters.AddWithValue("@fav", entry.IsFavorite ? 1 : 0);
    cmd.Parameters.AddWithValue("@sens", entry.IsSensitive ? 1 : 0);
    cmd.Parameters.AddWithValue("@export", (object?)entry.ExportPath ?? "");
    cmd.Parameters.AddWithValue("@sync", (object?)entry.SyncPath ?? "");
  }

  public void SetExportPath(long id, string path)
  {
    using var cmd = _connection!.CreateCommand();
    cmd.CommandText = "UPDATE clipboard_history SET export_path = @p WHERE id = @id";
    cmd.Parameters.AddWithValue("@p", path);
    cmd.Parameters.AddWithValue("@id", id);
    cmd.ExecuteNonQuery();
  }

  public void SetSyncPath(long id, string path)
  {
    using var cmd = _connection!.CreateCommand();
    cmd.CommandText = "UPDATE clipboard_history SET sync_path = @p WHERE id = @id";
    cmd.Parameters.AddWithValue("@p", path);
    cmd.Parameters.AddWithValue("@id", id);
    cmd.ExecuteNonQuery();
  }

  public bool ExistsByHash(string hash)
  {
    using var cmd = _connection!.CreateCommand();
    cmd.CommandText = "SELECT 1 FROM clipboard_history WHERE content_hash = @hash LIMIT 1";
    cmd.Parameters.AddWithValue("@hash", hash);
    return cmd.ExecuteScalar() != null;
  }

  public List<ClipboardEntry> Query(HistoryQuery q)
  {
    var list = new List<ClipboardEntry>();
    using var cmd = _connection!.CreateCommand();
    var sql = new StringBuilder("""
      SELECT id, content_type, preview, text_content, payload_path, file_paths_json,
             content_hash, captured_at, is_pinned, size_bytes, category, tags, source_app, file_extension, is_favorite, is_sensitive, export_path, sync_path
      FROM clipboard_history WHERE 1=1
      """);
    if (q.ContentType.HasValue) { sql.Append(" AND content_type = @type"); cmd.Parameters.AddWithValue("@type", (int)q.ContentType); }
    if (!string.IsNullOrWhiteSpace(q.Category) && !q.Category.Equals("All", StringComparison.OrdinalIgnoreCase))
    { sql.Append(" AND category = @cat"); cmd.Parameters.AddWithValue("@cat", q.Category); }
    if (q.FavoritesOnly) sql.Append(" AND is_favorite = 1");
    if (q.PinnedOnly) sql.Append(" AND is_pinned = 1");
    if (!string.IsNullOrWhiteSpace(q.Search))
    {
      sql.Append(" AND (preview LIKE @q OR text_content LIKE @q OR file_paths_json LIKE @q OR tags LIKE @q OR category LIKE @q)");
      cmd.Parameters.AddWithValue("@q", $"%{q.Search}%");
    }
    sql.Append(" ORDER BY ").Append(SortSql(q.Sort));
    if (q.Take.HasValue) sql.Append($" LIMIT {q.Take.Value}");
    cmd.CommandText = sql.ToString();
    using var reader = cmd.ExecuteReader();
    while (reader.Read()) list.Add(Map(reader));
    return list;
  }

  public List<ClipboardEntry> GetAll(string? search = null, ClipboardContentType? filter = null) =>
      Query(new HistoryQuery { Search = search, ContentType = filter, Sort = HistorySortMode.PinnedThenNewest });

  private static string SortSql(HistorySortMode sort) => sort switch
  {
    HistorySortMode.NewestFirst => "captured_at DESC",
    HistorySortMode.OldestFirst => "captured_at ASC",
    HistorySortMode.LargestFirst => "size_bytes DESC",
    HistorySortMode.SmallestFirst => "size_bytes ASC",
    HistorySortMode.CategoryThenNewest => "category ASC, captured_at DESC",
    HistorySortMode.TypeThenNewest => "content_type ASC, captured_at DESC",
    HistorySortMode.AlphabeticalPreview => "preview COLLATE NOCASE ASC",
    _ => "is_pinned DESC, is_favorite DESC, captured_at DESC"
  };

  public Dictionary<string, int> GetCategoryCounts()
  {
    var d = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    using var cmd = _connection!.CreateCommand();
    cmd.CommandText = "SELECT category, COUNT(*) FROM clipboard_history GROUP BY category ORDER BY COUNT(*) DESC";
    using var r = cmd.ExecuteReader();
    while (r.Read()) d[r.GetString(0)] = r.GetInt32(1);
    return d;
  }

  public ClipboardEntry? GetById(long id)
  {
    using var cmd = _connection!.CreateCommand();
    cmd.CommandText = """
      SELECT id, content_type, preview, text_content, payload_path, file_paths_json,
             content_hash, captured_at, is_pinned, size_bytes, category, tags, source_app, file_extension, is_favorite, is_sensitive, export_path, sync_path
      FROM clipboard_history WHERE id = @id
      """;
    cmd.Parameters.AddWithValue("@id", id);
    using var reader = cmd.ExecuteReader();
    return reader.Read() ? Map(reader) : null;
  }

  public void SetPinned(long id, bool pinned)
  {
    using var cmd = _connection!.CreateCommand();
    cmd.CommandText = "UPDATE clipboard_history SET is_pinned = @p WHERE id = @id";
    cmd.Parameters.AddWithValue("@p", pinned ? 1 : 0);
    cmd.Parameters.AddWithValue("@id", id);
    cmd.ExecuteNonQuery();
  }

  public void SetFavorite(long id, bool favorite)
  {
    using var cmd = _connection!.CreateCommand();
    cmd.CommandText = "UPDATE clipboard_history SET is_favorite = @f WHERE id = @id";
    cmd.Parameters.AddWithValue("@f", favorite ? 1 : 0);
    cmd.Parameters.AddWithValue("@id", id);
    cmd.ExecuteNonQuery();
  }

  /// <summary>Moves item to the top of history (by recency).</summary>
  public void BumpToRecent(long id)
  {
    using var cmd = _connection!.CreateCommand();
    cmd.CommandText = "UPDATE clipboard_history SET captured_at = @at WHERE id = @id";
    cmd.Parameters.AddWithValue("@at", DateTime.Now.ToString("O"));
    cmd.Parameters.AddWithValue("@id", id);
    cmd.ExecuteNonQuery();
  }

  public void UpdateCategory(long id, string category)
  {
    using var cmd = _connection!.CreateCommand();
    cmd.CommandText = "UPDATE clipboard_history SET category = @c WHERE id = @id";
    cmd.Parameters.AddWithValue("@c", category);
    cmd.Parameters.AddWithValue("@id", id);
    cmd.ExecuteNonQuery();
  }

  public void RecategorizeAll(Func<ClipboardEntry, string> classifier)
  {
    foreach (var e in Query(new HistoryQuery { Sort = HistorySortMode.OldestFirst }))
      UpdateCategory(e.Id, classifier(e));
  }

  public void Delete(long id, int securePasses = 0, bool securePayload = true) =>
      DeleteEntry(GetById(id), securePasses, securePayload);

  private void DeletePayloadFile(ClipboardEntry entry, int securePasses, bool securePayload)
  {
    if (entry.PayloadPath == null || !File.Exists(entry.PayloadPath))
      return;
    try
    {
      if (securePayload && securePasses > 0)
        SecureDeleteHelper.DeleteFile(entry.PayloadPath, securePasses);
      else
        File.Delete(entry.PayloadPath);
    }
    catch { }
  }

  private void DeleteEntry(ClipboardEntry? entry, int securePasses, bool securePayload)
  {
    if (entry == null) return;
    DeletePayloadFile(entry, securePasses, securePayload);
    using var cmd = _connection!.CreateCommand();
    cmd.CommandText = "DELETE FROM clipboard_history WHERE id = @id";
    cmd.Parameters.AddWithValue("@id", entry.Id);
    cmd.ExecuteNonQuery();
  }

  public int ClearUnpinned(int securePasses = 0, bool securePayload = true)
  {
    var toDelete = Query(new HistoryQuery { Sort = HistorySortMode.OldestFirst }).Where(x => !x.IsPinned).ToList();
    foreach (var e in toDelete)
      DeletePayloadFile(e, securePasses, securePayload);

    if (toDelete.Count == 0)
      return 0;

    using var cmd = _connection!.CreateCommand();
    cmd.CommandText = "DELETE FROM clipboard_history WHERE is_pinned = 0";
    var deleted = cmd.ExecuteNonQuery();
    VacuumAndCheckpoint();
    return deleted;
  }

  public int ClearAll(int securePasses = 0, bool securePayload = true)
  {
    var all = Query(new HistoryQuery { Sort = HistorySortMode.OldestFirst }).ToList();
    foreach (var e in all)
      DeletePayloadFile(e, securePasses, securePayload);

    if (all.Count == 0)
      return 0;

    using var cmd = _connection!.CreateCommand();
    cmd.CommandText = "DELETE FROM clipboard_history";
    var deleted = cmd.ExecuteNonQuery();
    VacuumAndCheckpoint();
    return deleted;
  }

  public int GetCount()
  {
    using var cmd = _connection!.CreateCommand();
    cmd.CommandText = "SELECT COUNT(*) FROM clipboard_history";
    return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
  }

  public void VacuumAndCheckpoint()
  {
    try
    {
      using var cmd = _connection!.CreateCommand();
      cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE)";
      cmd.ExecuteNonQuery();
    }
    catch { }
  }

  public void DeleteSensitive(int securePasses = 0, bool securePayload = true)
  {
    foreach (var e in Query(new HistoryQuery { Sort = HistorySortMode.OldestFirst }).Where(x => x.IsSensitive))
      Delete(e.Id, securePasses, securePayload);
  }

  public void DeleteOlderThanMinutes(int minutes, bool securePasses = false)
  {
    if (minutes <= 0) return;
    var cutoff = DateTime.Now.AddMinutes(-minutes);
    var toDelete = Query(new HistoryQuery { Sort = HistorySortMode.OldestFirst })
        .Where(e => !e.IsPinned && e.CapturedAt < cutoff).ToList();
    var passes = securePasses ? 1 : 0;
    foreach (var e in toDelete) Delete(e.Id, passes, true);
  }

  public void DeleteOlderThan(int days, bool pinnedOnly = false)
  {
    if (days <= 0) return;
    var cutoff = DateTime.Now.AddDays(-days);
    var toDelete = Query(new HistoryQuery { Sort = HistorySortMode.OldestFirst })
        .Where(e => e.CapturedAt < cutoff && (pinnedOnly ? e.IsPinned : !e.IsPinned))
        .ToList();
    foreach (var e in toDelete) Delete(e.Id);
  }

  public void DeleteCategoryOlderThan(string category, int days)
  {
    if (days <= 0) return;
    var cutoff = DateTime.Now.AddDays(-days);
    foreach (var e in Query(new HistoryQuery { Category = category, Sort = HistorySortMode.OldestFirst })
                 .Where(x => !x.IsPinned && x.CapturedAt < cutoff))
      Delete(e.Id);
  }

  public void TrimCategory(string category, int maxItems)
  {
    var items = Query(new HistoryQuery { Category = category, Sort = HistorySortMode.OldestFirst });
    var unpinned = items.Where(e => !e.IsPinned).ToList();
    var excess = unpinned.Count - maxItems;
    if (excess <= 0) return;
    foreach (var e in unpinned.Take(excess)) Delete(e.Id);
  }

  public void TrimToMax(int maxItems, IEnumerable<long> pinnedIds)
  {
    var pinned = pinnedIds.ToHashSet();
    var unpinned = Query(new HistoryQuery { Sort = HistorySortMode.OldestFirst }).Where(e => !pinned.Contains(e.Id)).ToList();
    var excess = unpinned.Count - maxItems;
    if (excess <= 0) return;
    foreach (var e in unpinned.Take(excess)) Delete(e.Id);
  }

  public static string ComputeHash(params byte[][] parts)
  {
    using var sha = SHA256.Create();
    return Convert.ToHexString(sha.ComputeHash(parts.SelectMany(p => p).ToArray()));
  }

  private static ClipboardEntry Map(SqliteDataReader r) => new()
  {
    Id = r.GetInt64(0),
    ContentType = (ClipboardContentType)r.GetInt32(1),
    Preview = r.GetString(2),
    TextContent = r.IsDBNull(3) ? null : r.GetString(3),
    PayloadPath = r.IsDBNull(4) ? null : r.GetString(4),
    FilePathsJson = r.IsDBNull(5) ? null : r.GetString(5),
    ContentHash = r.GetString(6),
    CapturedAt = DateTime.Parse(r.GetString(7)),
    IsPinned = r.GetInt32(8) == 1,
    SizeBytes = r.GetInt64(9),
    Category = r.IsDBNull(10) ? "General" : r.GetString(10),
    Tags = r.IsDBNull(11) ? "" : r.GetString(11),
    SourceApp = r.IsDBNull(12) ? "" : r.GetString(12),
    FileExtension = r.IsDBNull(13) ? "" : r.GetString(13),
    IsFavorite = r.FieldCount > 14 && r.GetInt32(14) == 1,
    IsSensitive = r.FieldCount > 15 && !r.IsDBNull(15) && r.GetInt32(15) == 1,
    ExportPath = r.FieldCount > 16 && !r.IsDBNull(16) ? NullIfEmpty(r.GetString(16)) : null,
    SyncPath = r.FieldCount > 17 && !r.IsDBNull(17) ? NullIfEmpty(r.GetString(17)) : null
  };

  private static string? NullIfEmpty(string s) => string.IsNullOrEmpty(s) ? null : s;

  public void Dispose() => _connection?.Dispose();
}
