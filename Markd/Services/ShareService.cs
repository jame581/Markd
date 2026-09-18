using Markd.Core.Domain;
using Markd.Core.Localization;
using Markd.ViewModels;

namespace Markd.Services;

public interface IShareService
{
    Task ShareOccasionAsync(OccasionShareRequest request);
    Task ShareMilestoneAsync(MilestoneShareRequest request);
}

public sealed record OccasionShareRequest(
    Occasion Occasion,
    int Days,
    string TimeBreakdown,
    string? NextMilestoneLabel,
    string? NextMilestoneStatus);

public sealed record MilestoneShareRequest(
    Occasion Occasion,
    string MilestoneLabel,
    int ThresholdDays,
    string? NextMilestoneLabel);

public sealed class ShareService : IShareService
{
    public Task ShareOccasionAsync(OccasionShareRequest request)
    {
        var title = string.Format(Strings.Share_Title, request.Occasion.Title);
        var dateText = OccasionMath.FormatShortDate(OccasionDates.ToLocalDate(request.Occasion.AnchorDate));
        var daysPhrase = FormatDaysPhrase(request.Occasion.Direction, request.Days, dateText);
        var text = $"{request.Occasion.Emoji} {request.Occasion.Title}\n" +
                   $"{daysPhrase}\n" +
                   $"{request.TimeBreakdown}";

        if (!string.IsNullOrWhiteSpace(request.NextMilestoneLabel))
            text += $"\n{string.Format(Strings.Share_NextMilestone, request.NextMilestoneLabel, request.NextMilestoneStatus)}";

        if (!string.IsNullOrWhiteSpace(request.Occasion.Notes))
            text += $"\n\n{request.Occasion.Notes.Trim()}";

        return Share.Default.RequestAsync(new ShareTextRequest
        {
            Title = title,
            Subject = request.Occasion.Title,
            Text = text
        });
    }

    public Task ShareMilestoneAsync(MilestoneShareRequest request)
    {
        var title = string.Format(Strings.Share_Title, request.MilestoneLabel);
        var dateText = OccasionMath.FormatShortDate(OccasionDates.ToLocalDate(request.Occasion.AnchorDate));
        var daysPhrase = FormatDaysPhrase(request.Occasion.Direction, request.ThresholdDays, dateText);
        var text = $"{request.Occasion.Emoji} {request.Occasion.Title}\n" +
                   $"{request.MilestoneLabel} · {daysPhrase}";

        if (!string.IsNullOrWhiteSpace(request.NextMilestoneLabel))
            text += $"\n{string.Format(Strings.Share_NextMilestoneShort, request.NextMilestoneLabel)}";

        return Share.Default.RequestAsync(new ShareTextRequest
        {
            Title = title,
            Subject = request.MilestoneLabel,
            Text = text
        });
    }

    private static string FormatDaysPhrase(OccasionDirection direction, int days, string dateText)
    {
        var culture = LocalizationManager.Instance.Culture;
        var baseKey = direction == OccasionDirection.Since ? "Share_DaysSince" : "Share_DaysUntil";
        var pattern = Strings.ResourceManager.GetString($"{baseKey}_{Plural.Select(days, culture)}", culture) ?? baseKey;
        return string.Format(culture, pattern, days, dateText);
    }
}
