using System;
using System.Collections.Generic;
using System.Linq;
using Markd.Core.Localization;

namespace Markd.Core.Services
{
    public class ExportModel
    {
        public const string CurrentSchemaVersion = "2";

        public string SchemaVersion { get; set; } = CurrentSchemaVersion;
        public string AppVersion { get; set; } = "0.0.0";
        public DateTime ExportedAt { get; set; }
        public string? DeviceId { get; set; }

        /// <summary>Identifies one lineage of the cloud file.</summary>
        public Guid? SyncEpoch { get; set; }

        public List<CategoryDto> Categories { get; set; } = new();
        public List<OccasionDto> Occasions { get; set; } = new();
        public List<MilestoneDto> Milestones { get; set; } = new();

        public AppSettingsDto? Settings { get; set; }
    }

    public class CategoryDto
    {
        public int SourceId { get; set; }
        public Guid SyncId { get; set; }
        public DateTime UpdatedAt { get; set; }
        public DateTime? DeletedAt { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Emoji { get; set; }
        public string? ColorHex { get; set; }

        public CategoryDto Clone() => (CategoryDto)MemberwiseClone();
    }

    public class OccasionDto
    {
        public int SourceId { get; set; }
        public Guid SyncId { get; set; }
        public DateTime UpdatedAt { get; set; }
        public DateTime? DeletedAt { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Emoji { get; set; }
        public string? ColorHex { get; set; }
        public bool IsPinned { get; set; }
        public DateTime AnchorDate { get; set; }
        public string Direction { get; set; } = "Since";
        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; }
        public int? CategorySourceId { get; set; }
        public Guid? CategorySyncId { get; set; }

        public OccasionDto Clone() => (OccasionDto)MemberwiseClone();
    }

    public class MilestoneDto
    {
        public int SourceId { get; set; }
        public Guid SyncId { get; set; }
        public DateTime UpdatedAt { get; set; }
        public DateTime? DeletedAt { get; set; }
        public int OccasionSourceId { get; set; }
        public Guid OccasionSyncId { get; set; }
        public int ThresholdDays { get; set; }
        public string Label { get; set; } = string.Empty;
        public bool Notified { get; set; }

        public MilestoneDto Clone() => (MilestoneDto)MemberwiseClone();
    }

    public class AppSettingsDto
    {
        public string Theme { get; set; } = "System";
        public string Language { get; set; } = LanguageSetting.System;
        public bool NotificationsEnabled { get; set; } = true;
    }

    public static class ExportModelExtensions
    {
        /// <summary>Copies the record lists so a merge can change records without touching its inputs.</summary>
        public static ExportModel CloneModel(this ExportModel model) => new()
        {
            SchemaVersion = model.SchemaVersion,
            AppVersion = model.AppVersion,
            ExportedAt = model.ExportedAt,
            DeviceId = model.DeviceId,
            SyncEpoch = model.SyncEpoch,
            Categories = model.Categories.Select(c => c.Clone()).ToList(),
            Occasions = model.Occasions.Select(o => o.Clone()).ToList(),
            Milestones = model.Milestones.Select(m => m.Clone()).ToList(),
            Settings = model.Settings,
        };
    }
}
