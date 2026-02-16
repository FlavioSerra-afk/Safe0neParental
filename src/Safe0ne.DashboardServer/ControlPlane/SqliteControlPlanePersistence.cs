using Microsoft.Data.Sqlite;

namespace Safe0ne.DashboardServer.ControlPlane;

/// <summary>
/// Optional persistence backend: SQLite single-row key/value store.
/// Stores the full SSOT JSON blob unchanged (same schema as file backend).
/// </summary>
public sealed class SqliteControlPlanePersistence : IControlPlanePersistence
{
    private const string KeyName = "control-plane.v1";
    private readonly string _dbPath;

    public SqliteControlPlanePersistence(string? overrideDbPath = null)
    {
        if (!string.IsNullOrWhiteSpace(overrideDbPath))
        {
            _dbPath = overrideDbPath;
            var dir0 = Path.GetDirectoryName(_dbPath) ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(dir0)) Directory.CreateDirectory(dir0);
        }
        else
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Safe0ne",
                "DashboardServer");
            Directory.CreateDirectory(dir);
            _dbPath = Path.Combine(dir, "control-plane.v1.db");
        }
    }

    public string? LoadOrNull()
    {
        EnsureSchema();

        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT json FROM kv WHERE k=$k LIMIT 1";
        cmd.Parameters.AddWithValue("$k", KeyName);
        var result = cmd.ExecuteScalar();
        return result as string;
    }

    public void Save(string json)
    {
        if (json is null) json = "";
        EnsureSchema();

        using var conn = Open();
        using var tx = conn.BeginTransaction();
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
INSERT INTO kv(k, json, updatedUtc)
VALUES($k, $json, $u)
ON CONFLICT(k) DO UPDATE SET
  json=excluded.json,
  updatedUtc=excluded.updatedUtc;";
        cmd.Parameters.AddWithValue("$k", KeyName);
        cmd.Parameters.AddWithValue("$json", json);
        cmd.Parameters.AddWithValue("$u", DateTimeOffset.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
        tx.Commit();
    }

    public void HealthProbe()
    {
        EnsureSchema();

        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1";
        _ = cmd.ExecuteScalar();
    }

    private SqliteConnection Open()
    {
        var cs = new SqliteConnectionStringBuilder
        {
            DataSource = _dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
        var conn = new SqliteConnection(cs);
        conn.Open();
        return conn;
    }

    private void EnsureSchema()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS kv(
  k TEXT PRIMARY KEY,
  json TEXT NOT NULL,
  updatedUtc TEXT NOT NULL
);";
        cmd.ExecuteNonQuery();
    }
}
