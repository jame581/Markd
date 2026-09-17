using Markd.Core.Domain;
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

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Occasion>(entity =>
            {
                entity.HasKey(o => o.Id);
                entity.Property(o => o.Title).IsRequired().HasMaxLength(100);
                entity.Property(o => o.Emoji).HasMaxLength(8);
                entity.Property(o => o.ColorHex).HasMaxLength(7);
                entity.Property(o => o.IsPinned).IsRequired().HasDefaultValue(false);
                entity.HasIndex(o => o.IsPinned)
                      .HasFilter("\"IsPinned\" = 1")
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
                entity.Property(c => c.Name).IsRequired().HasMaxLength(50);
                entity.Property(c => c.Emoji).HasMaxLength(8);
                entity.Property(c => c.ColorHex).HasMaxLength(7);
            });

            modelBuilder.Entity<Milestone>(entity =>
            {
                entity.HasKey(m => m.Id);
                entity.Property(m => m.Label).IsRequired().HasMaxLength(100);
                entity.Property(m => m.ThresholdDays).IsRequired();
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
                    Language = "en",
                    NotificationsEnabled = true,
                    NotificationTimeOfDay = new TimeSpan(9, 0, 0)
                });
            });
        }
    }
}
