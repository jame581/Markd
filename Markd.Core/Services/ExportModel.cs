using System;
using System.Collections.Generic;

namespace Markd.Core.Services
{
    public class ExportModel
    {
        public string SchemaVersion { get; set; } = "1";
        public string AppVersion { get; set; } = "0.0.0";
        public DateTime ExportedAt { get; set; }
        public string? DeviceId { get; set; }

        public List<CategoryDto> Categories { get; set; } = new();
        public List<OccasionDto> Occasions { get; set; } = new();
        public List<MilestoneDto> Milestones { get; set; } = new();

        public AppSettingsDto? Settings { get; set; }
    }

    public class CategoryDto
    {
        public int SourceId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Emoji { get; set; }
        public string? ColorHex { get; set; }
    }

    public class OccasionDto
    {
        public int SourceId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Emoji { get; set; }
        public string? ColorHex { get; set; }
        public bool IsPinned { get; set; }
        public DateTime AnchorDate { get; set; }
        public string Direction { get; set; } = "Since";
        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; }
        public int? CategorySourceId { get; set; }
    }

    public class MilestoneDto
    {
        public int SourceId { get; set; }
        public int OccasionSourceId { get; set; }
        public int ThresholdDays { get; set; }
        public string Label { get; set; } = string.Empty;
        public bool Notified { get; set; }
    }

    public class AppSettingsDto
    {
        public string Theme { get; set; } = "System";
        public string Language { get; set; } = "en";
        public bool NotificationsEnabled { get; set; } = true;
    }
}