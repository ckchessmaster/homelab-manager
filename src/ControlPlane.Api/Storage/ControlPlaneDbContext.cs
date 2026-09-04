using HostEntity = ControlPlane.Api.Storage.Entities.Host;
using ControlPlane.Api.Storage.Entities;
using Microsoft.EntityFrameworkCore;

namespace ControlPlane.Api.Storage;

public class ControlPlaneDbContext : DbContext
{
    public ControlPlaneDbContext(DbContextOptions<ControlPlaneDbContext> options)
        : base(options)
    {
    }

    public DbSet<HostEntity> Hosts => Set<HostEntity>();

    public DbSet<UpdateJob> UpdateJobs => Set<UpdateJob>();

    public DbSet<StepLog> StepLogs => Set<StepLog>();

    public DbSet<ClusterLease> ClusterLeases => Set<ClusterLease>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ControlPlaneDbContext).Assembly);
    }
}
