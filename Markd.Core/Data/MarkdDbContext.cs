using Markd.Core.Domain;
using Markd.Core.Localization;
using Microsoft.EntityFrameworkCore;

namespace Markd.Core.Data
{
    public class MarkdDbContext : DbContext
    {
        public MarkdDbContext(DbContextOptions<MarkdDbContext> options) : base(options) { }

        public DbSet<Occasion> Occasions => Set<Occasion>();
        public DbSet<Category> Categories => Set<Category>();
        public DbSet<Milestone> Milestones => Set<Milestone>();
        public DbSet<AppSettings> AppSettings => Set<AppSettings>();
        public DbSet<SyncState> SyncStates => Set<SyncState>();

        /// <summary>
        /// When false, SaveChanges leaves UpdatedAt alone. Snapshot apply sets it so timestamps
        /// coming from another device are stored as they are.
        /// </summary>
        public bool StampTimestamps { get; set; } = true;

        public override int SaveChanges(bool acceptAllChangesOnSuccess)
        {
            Stamp();
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }

        public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        {
            Stamp();
            return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }

        private void Stamp()
        {
            if (!StampTimestamps)
                return;

            ChangeTracker.DetectChanges();
            var now = DateTime.UtcNow;
            foreach (var entry in ChangeTracker.Entries<ISyncEntity>())
            {
                if (entry.State == EntityState.Added)
                {
                    entry.Entity.UpdatedAt = now;
                }
                else if (entry.State == EntityState.Modified)
                {
                    // Notified is device-local, so flipping it is not a change other devices care about.
                    var meaningful = entry.Properties.Any(p => p.IsModified
                        && p.Metadata.Name != nameof(ISyncEntity.UpdatedAt)
                        && !(entry.Entity is Milestone && p.Metadata.Name == nameof(Milestone.Notified)));
                    if (meaningful)
                        entry.Entity.UpdatedAt = now;
                }
            }
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Occasion>(entity =>
            {
                entity.HasKey(o => o.Id);
                entity.HasIndex(o => o.SyncId).IsUnique();
                entity.HasQueryFilter(o => o.DeletedAt == null);
                entity.Property(o => o.Title).IsRequired().HasMaxLength(100);
                entity.Property(o => o.Emoji).HasMaxLength(8);
                entity.Property(o => o.ColorHex).HasMaxLength(7);
                entity.Property(o => o.IsPinned).IsRequired().HasDefaultValue(false);
                entity.HasIndex(o => o.IsPinned)
                      .HasFilter("\"IsPinned\" = 1 AND \"DeletedAt\" IS NULL")
                      .IsUnique();
                entity.Property(o => o.Direction).HasConversion<string>();
                entity.Property(o => o.AnchorDate).IsRequired();
                entity.Property(o => o.CreatedAt).IsRequired();

                entity.HasOne(o => o.Category)
                      .WithMany(c => c.Occasions)
                      .HasForeignKey(o => o.CategoryId)
                      .OnDelete(DeleteBehavior.SetNull);

                entity.HasMany(o => o.Milestones)
                      .WithOne(m => m.Occasion)
                      .HasForeignKey(m => m.OccasionId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Category>(entity =>
            {
                entity.HasKey(c => c.Id);
                entity.HasIndex(c => c.SyncId).IsUnique();
                entity.HasQueryFilter(c => c.DeletedAt == null);
                entity.Property(c => c.Name).IsRequired().HasMaxLength(50);
                entity.Property(c => c.Emoji).HasMaxLength(8);
                entity.Property(c => c.ColorHex).HasMaxLength(7);
            });

            modelBuilder.Entity<Milestone>(entity =>
            {
                entity.HasKey(m => m.Id);
                entity.HasIndex(m => m.SyncId).IsUnique();
                entity.HasQueryFilter(m => m.DeletedAt == null);
                entity.Property(m => m.Label).IsRequired().HasMaxLength(100);
                entity.Property(m => m.ThresholdDays).IsRequired();
            });

            modelBuilder.Entity<SyncState>(entity =>
            {
                entity.HasKey(s => s.Id);
                entity.Property(s => s.ProviderId).HasMaxLength(32);
            });

            modelBuilder.Entity<AppSettings>(entity =>
            {
                entity.HasKey(a => a.Id);
                entity.Property(a => a.Theme).HasMaxLength(20);
                entity.Property(a => a.Language).HasMaxLength(10);
                entity.Property(a => a.NotificationTimeOfDay)
                      .HasColumnType("TEXT")
                      .HasDefaultValue(new TimeSpan(9, 0, 0));

                // Seed default settings row
                entity.HasData(new AppSettings
                {
                    Id = 1,
                    Theme = "System",
                    Language = LanguageSetting.System,
                    NotificationsEnabled = true,
                    NotificationTimeOfDay = new TimeSpan(9, 0, 0)
                });
            });
        }
    }
}
