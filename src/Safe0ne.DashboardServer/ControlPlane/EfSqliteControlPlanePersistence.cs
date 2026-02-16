using Microsoft.EntityFrameworkCore;

namespace Safe0ne.DashboardServer.ControlPlane;

/// <summary>
/// Optional persistence backend: SQLite via EF Core.
/// Stores the full SSOT JSON blob unchanged (same schema as file backend).
/// 
/// Enabled by:
///   SAFEONE_CP_BACKEND=sqlite
///   SAFEONE_CP_SQLITE_IMPL=efcore
/// </summary>
public sealed class EfSqliteControlPlanePersistence : IControlPlanePersistence
{
    private const string KeyName = "control-plane.v1";
    private readonly string _dbPath;

    public EfSqliteControlPlanePersistence(string? overrideDbPath = null)
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
        using var db = Open();
        db.Database.EnsureCreated();
        return db.Kv.AsNoTracking().Where(x => x.K == KeyName).Select(x => x.Json).FirstOrDefault();
    }

    public void Save(string json)
    {
        if (json is null) json = "";

        using var db = Open();
        db.Database.EnsureCreated();

        var row = db.Kv.SingleOrDefault(x => x.K == KeyName);
        if (row is null)
        {
            row = new EfControlPlaneDbContext.KvRow { K = KeyName };
            db.Kv.Add(row);
        }

        row.Json = json;
        row.UpdatedUtc = DateTimeOffset.UtcNow.ToString("O");

        db.SaveChanges();
    }

    public void HealthProbe()
    {
        using var db = Open();
        db.Database.EnsureCreated();
        _ = db.Database.ExecuteSqlRaw("SELECT 1");
    }

    private EfControlPlaneDbContext Open()
    {
        var options = new DbContextOptionsBuilder<EfControlPlaneDbContext>()
            .UseSqlite($"Data Source={_dbPath};Cache=Shared")
            .EnableDetailedErrors(false)
            .EnableSensitiveDataLogging(false)
            .Options;
        return new EfControlPlaneDbContext(options);
    }
}
