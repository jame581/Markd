using System.Text.Json;
using Markd.Core.Localization;

namespace Markd.Core.Services
{
    public static class ExportModelJson
    {
        private static readonly JsonSerializerOptions Indented = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private static readonly JsonSerializerOptions Compact = new()
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public static string Serialize(ExportModel model, bool indented) =>
            JsonSerializer.Serialize(model, indented ? Indented : Compact);

        /// <summary>Parses and validates. Accepts schema "1" and "2". Errors are localized InvalidOperationExceptions.</summary>
        public static ExportModel Deserialize(string json)
        {
            ExportModel? model;
            try
            {
                model = JsonSerializer.Deserialize<ExportModel>(json, Compact);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException(Strings.Import_InvalidJson, ex);
            }

            if (model == null)
                throw new InvalidOperationException(Strings.Import_InvalidJson);

            Validate(model);
            return model;
        }

        public static void Validate(ExportModel model)
        {
            if (string.IsNullOrWhiteSpace(model.SchemaVersion))
                throw new InvalidOperationException(Strings.Import_MissingSchemaVersion);

            var isV2 = string.Equals(model.SchemaVersion, ExportModel.CurrentSchemaVersion, StringComparison.Ordinal);
            if (!isV2 && !string.Equals(model.SchemaVersion, "1", StringComparison.Ordinal))
                throw new InvalidOperationException(string.Format(LocalizationManager.Instance.Culture, Strings.Import_UnsupportedSchema, model.SchemaVersion));

            if (model.Categories.Any(c => string.IsNullOrWhiteSpace(c.Name)))
                throw new InvalidOperationException(Strings.Import_MissingCategoryName);

            if (model.Occasions.Any(o => string.IsNullOrWhiteSpace(o.Title)))
                throw new InvalidOperationException(Strings.Import_MissingOccasionTitle);

            if (model.Milestones.Any(m => string.IsNullOrWhiteSpace(m.Label)))
                throw new InvalidOperationException(Strings.Import_MissingMilestoneLabel);

            if (isV2)
            {
                var ids = model.Categories.Select(c => c.SyncId)
                    .Concat(model.Occasions.Select(o => o.SyncId))
                    .Concat(model.Milestones.Select(m => m.SyncId))
                    .ToList();
                if (ids.Contains(Guid.Empty) || ids.Distinct().Count() != ids.Count)
                    throw new InvalidOperationException(Strings.Import_InvalidJson);

                // v2 carries the anchor only as a calendar day.
                if (model.Occasions.Any(o => o.AnchorDay == default))
                    throw new InvalidOperationException(Strings.Import_InvalidJson);
            }
        }
    }
}
