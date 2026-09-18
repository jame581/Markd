using Markd.Core.Domain;
using Markd.Core.Localization;

namespace Markd.Services;

public sealed record MilestoneMoment(Occasion Occasion, Milestone Milestone, Milestone? Next, int Days)
{
    public string Number => Milestone.ThresholdDays.ToString();

    public string Sentence
    {
        get
        {
            var culture = LocalizationManager.Instance.Culture;
            var leadKey = Occasion.Direction == OccasionDirection.Since ? "Milestone_Sentence_Since" : "Milestone_Sentence_Until";
            var leadPattern = Strings.ResourceManager.GetString($"{leadKey}_{Plural.Select(Milestone.ThresholdDays, culture)}", culture) ?? leadKey;
            var lead = string.Format(culture, leadPattern, Milestone.ThresholdDays, Occasion.Title);

            if (Next is null)
                return $"{lead} {Strings.Milestone_Sentence_LastTracked}";

            var away = Occasion.Direction == OccasionDirection.Since
                ? Next.ThresholdDays - Days
                : Days - Next.ThresholdDays;
            var nextPattern = Strings.ResourceManager.GetString($"Milestone_Sentence_NextUp_{Plural.Select(away, culture)}", culture) ?? "Milestone_Sentence_NextUp";
            return $"{lead} {string.Format(culture, nextPattern, Next.Label, away)}";
        }
    }
}

/// <summary>Shows the "milestone reached" moment: a Material dialog on phones, an overlay card on the desktop.</summary>
public interface IMilestoneMomentPresenter
{
    Task ShowAsync(MilestoneMoment moment);
}
