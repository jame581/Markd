using Markd.Core.Domain;

namespace Markd.Services;

public sealed record MilestoneMoment(Occasion Occasion, Milestone Milestone, Milestone? Next, int Days)
{
    public string Number => Milestone.ThresholdDays.ToString();

    public string Sentence
    {
        get
        {
            var lead = Occasion.Direction == OccasionDirection.Since
                ? $"{Milestone.ThresholdDays} days since {Occasion.Title}."
                : $"{Milestone.ThresholdDays} days to go until {Occasion.Title}.";

            if (Next is null)
                return $"{lead} That was the last milestone you're tracking.";

            var away = Occasion.Direction == OccasionDirection.Since
                ? Next.ThresholdDays - Days
                : Days - Next.ThresholdDays;
            return $"{lead} Next up is {Next.Label}, {away} {(away == 1 ? "day" : "days")} out.";
        }
    }
}

/// <summary>Shows the "milestone reached" moment: a Material dialog on phones, an overlay card on the desktop.</summary>
public interface IMilestoneMomentPresenter
{
    Task ShowAsync(MilestoneMoment moment);
}
