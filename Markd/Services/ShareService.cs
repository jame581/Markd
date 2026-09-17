using Markd.Core.Domain;

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
        var action = request.Occasion.Direction == OccasionDirection.Since ? "since" : "until";
        var title = $"Share {request.Occasion.Title}";
        var text = $"{request.Occasion.Emoji} {request.Occasion.Title}\n" +
                   $"{request.Days} days {action} {request.Occasion.AnchorDate.ToLocalTime():dd MMM yyyy}\n" +
                   $"{request.TimeBreakdown}";

        if (!string.IsNullOrWhiteSpace(request.NextMilestoneLabel))
            text += $"\nNext milestone: {request.NextMilestoneLabel} · {request.NextMilestoneStatus}";

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
        var action = request.Occasion.Direction == OccasionDirection.Since ? "since" : "until";
        var title = $"Share {request.MilestoneLabel}";
        var text = $"{request.Occasion.Emoji} {request.Occasion.Title}\n" +
                   $"{request.MilestoneLabel} · {request.ThresholdDays} days {action} {request.Occasion.AnchorDate.ToLocalTime():dd MMM yyyy}";

        if (!string.IsNullOrWhiteSpace(request.NextMilestoneLabel))
            text += $"\nNext milestone: {request.NextMilestoneLabel}";

        return Share.Default.RequestAsync(new ShareTextRequest
        {
            Title = title,
            Subject = request.MilestoneLabel,
            Text = text
        });
    }
}
