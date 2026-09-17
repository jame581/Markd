namespace Markd.Core.Services
{
    public sealed record CalendarMark(
        DateOnly Date,
        int OccasionId,
        string Title,
        string? Emoji,
        string? ColorHex,
        CalendarMarkKind Kind,
        string? Label,
        int? ThresholdDays,
        bool Notified);
}
