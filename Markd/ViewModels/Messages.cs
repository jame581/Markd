namespace Markd.ViewModels;

/// <summary>Sent after occasions or milestones change so every open view can reload.</summary>
public sealed record OccasionsChangedMessage(int? OccasionId = null, object? Source = null);

/// <summary>Sent after categories change.</summary>
public sealed record CategoriesChangedMessage(object? Source = null);
