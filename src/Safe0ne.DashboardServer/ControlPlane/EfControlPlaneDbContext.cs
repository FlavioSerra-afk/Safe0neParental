using Microsoft.EntityFrameworkCore;

namespace Safe0ne.DashboardServer.ControlPlane;

internal sealed class EfControlPlaneDbContext : DbContext
{
    public EfControlPlaneDbContext(DbContextOptions<EfControlPlaneDbContext> options) : base(options)
    {
    }

    public DbSet<KvRow> Kv => Set<KvRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var kv = modelBuilder.Entity<KvRow>();
        kv.ToTable("kv");
        kv.HasKey(x => x.K);
        kv.Property(x => x.K).HasColumnName("k");
        kv.Property(x => x.Json).HasColumnName("json").IsRequired();
        kv.Property(x => x.UpdatedUtc).HasColumnName("updatedUtc").IsRequired();
    }

    internal sealed class KvRow
    {
        public string K { get; set; } = "";
        public string Json { get; set; } = "";
        public string UpdatedUtc { get; set; } = "";
    }
}
