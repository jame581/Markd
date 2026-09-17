using Markd.Core.Domain;
using Markd.Core.Services;
using Markd.Services;
using Markd.ViewModels;
using Microsoft.Maui.Devices;

namespace Markd.Tests;

public static class OccasionDetailViewModelTests
{
    public static async Task RunAsync()
    {
        await LoadAsync_BuildsReachedAndUpcomingMilestoneStates_FromNotifiedFlag();
        await AddMilestoneCommand_AddsMilestoneAndReloadsOccasion();
        await EditCommand_NavigatesToOccasionFormRoute();
        await PinCommand_PinsUnpinnedOccasion();
        await RemoveMilestoneCommand_ConfirmsBeforeRemovingOnNonAndroid();
        await RemoveMilestoneCommand_SkipsConfirmationOnAndroid();
        await ShareCommand_SharesOccasionWithNextMilestoneContext();
        await DeleteCommand_DeletesOccasionAndNavigatesBack();

        Console.WriteLine("Occasion detail smoke verification passed.");
    }

    private static async Task LoadAsync_BuildsReachedAndUpcomingMilestoneStates_FromNotifiedFlag()
    {
        var occasion = SampleOccasions.OurAnniversary();
        var services = new FakeOccasionService(occasion);
        var shell = new FakeAppShellService(DevicePlatform.WinUI);
        var share = new FakeShareService();
        var viewModel = new OccasionDetailViewModel(services, shell, share);

        await viewModel.LoadAsync(occasion.Id);

        Ensure(viewModel.MilestoneStates.Count == 3, "Expected three milestone states.");
        Ensure(viewModel.MilestoneStates[0].IsReached, "Expected the first milestone to be reached.");
        Ensure(!viewModel.MilestoneStates[1].IsReached, "Expected the second milestone to be upcoming.");
        Ensure(viewModel.MilestoneStates[0].StatusText.Contains("Reached", StringComparison.Ordinal), "Reached milestone text missing.");
        Ensure(viewModel.MilestoneStates[1].StatusText.Contains("away", StringComparison.Ordinal), "Upcoming milestone text missing.");
    }

    private static async Task AddMilestoneCommand_AddsMilestoneAndReloadsOccasion()
    {
        var occasion = SampleOccasions.SoberDays();
        var services = new FakeOccasionService(occasion);
        var shell = new FakeAppShellService(DevicePlatform.WinUI);
        var share = new FakeShareService();
        var viewModel = new OccasionDetailViewModel(services, shell, share);

        await viewModel.LoadAsync(occasion.Id);
        viewModel.NewMilestoneLabel = "Seven hundred";
        viewModel.NewMilestoneThresholdDays = "700";

        await viewModel.AddMilestoneCommand.ExecuteAsync(null);

        Ensure(services.GetOccasion(occasion.Id).Milestones.Any(milestone => milestone.Label == "Seven hundred"), "Milestone was not added to the store.");
        Ensure(viewModel.MilestoneStates.Any(milestone => milestone.Label == "Seven hundred"), "Milestone view state was not refreshed.");
    }

    private static async Task EditCommand_NavigatesToOccasionFormRoute()
    {
        var occasion = SampleOccasions.TripToLisbon();
        var services = new FakeOccasionService(occasion);
        var shell = new FakeAppShellService(DevicePlatform.WinUI);
        var share = new FakeShareService();
        var viewModel = new OccasionDetailViewModel(services, shell, share);

        await viewModel.LoadAsync(occasion.Id);
        await viewModel.EditCommand.ExecuteAsync(null);

        Ensure(shell.LastRoute == $"{nameof(OccasionFormPage)}?id={occasion.Id}", "Edit navigation route was incorrect.");
    }

    private static async Task PinCommand_PinsUnpinnedOccasion()
    {
        var occasion = SampleOccasions.EllasAge();
        var services = new FakeOccasionService(occasion);
        var shell = new FakeAppShellService(DevicePlatform.WinUI);
        var share = new FakeShareService();
        var viewModel = new OccasionDetailViewModel(services, shell, share);

        await viewModel.LoadAsync(occasion.Id);
        await viewModel.PinCommand.ExecuteAsync(null);

        Ensure(services.GetOccasion(occasion.Id).IsPinned, "Pin command did not pin the occasion.");
        Ensure(viewModel.PinButtonText == "Unpin", "Pinned occasion did not surface unpin text.");
    }

    private static async Task RemoveMilestoneCommand_ConfirmsBeforeRemovingOnNonAndroid()
    {
        var occasion = SampleOccasions.OurAnniversary();
        var services = new FakeOccasionService(occasion);
        var shell = new FakeAppShellService(DevicePlatform.WinUI) { NextConfirmationResult = true };
        var share = new FakeShareService();
        var viewModel = new OccasionDetailViewModel(services, shell, share);

        await viewModel.LoadAsync(occasion.Id);
        await viewModel.RemoveMilestoneCommand.ExecuteAsync(viewModel.MilestoneStates[1].Milestone);

        Ensure(shell.ConfirmationRequests.Count == 1, "Non-Android milestone removal should confirm.");
        Ensure(!services.GetOccasion(occasion.Id).Milestones.Any(milestone => milestone.Label == "Four years"), "Milestone was not removed after confirmation.");
    }

    private static async Task RemoveMilestoneCommand_SkipsConfirmationOnAndroid()
    {
        var occasion = SampleOccasions.OurAnniversary();
        var services = new FakeOccasionService(occasion);
        var shell = new FakeAppShellService(DevicePlatform.Android);
        var share = new FakeShareService();
        var viewModel = new OccasionDetailViewModel(services, shell, share);

        await viewModel.LoadAsync(occasion.Id);
        await viewModel.RemoveMilestoneCommand.ExecuteAsync(viewModel.MilestoneStates[1].Milestone);

        Ensure(shell.ConfirmationRequests.Count == 0, "Android milestone removal should skip confirmation.");
        Ensure(!services.GetOccasion(occasion.Id).Milestones.Any(milestone => milestone.Label == "Four years"), "Android milestone removal did not persist.");
    }

    private static async Task ShareCommand_SharesOccasionWithNextMilestoneContext()
    {
        var occasion = SampleOccasions.OurAnniversary();
        var services = new FakeOccasionService(occasion);
        var shell = new FakeAppShellService(DevicePlatform.WinUI);
        var share = new FakeShareService();
        var viewModel = new OccasionDetailViewModel(services, shell, share);

        await viewModel.LoadAsync(occasion.Id);
        await viewModel.ShareCommand.ExecuteAsync(null);

        Ensure(share.LastOccasionShare is not null, "Share command did not invoke the share service.");
        Ensure(share.LastOccasionShare!.Occasion.Title == "Our anniversary", "Share request used the wrong occasion.");
        Ensure(share.LastOccasionShare.NextMilestoneLabel == "Four years", "Share request missed the next milestone context.");
    }

    private static async Task DeleteCommand_DeletesOccasionAndNavigatesBack()
    {
        var occasion = SampleOccasions.TripToLisbon();
        var services = new FakeOccasionService(occasion);
        var shell = new FakeAppShellService(DevicePlatform.WinUI) { NextConfirmationResult = true };
        var share = new FakeShareService();
        var viewModel = new OccasionDetailViewModel(services, shell, share);

        await viewModel.LoadAsync(occasion.Id);
        await viewModel.DeleteCommand.ExecuteAsync(null);

        Ensure(services.FindOccasion(occasion.Id) is null, "Delete command did not remove the occasion.");
        Ensure(shell.LastRoute == "..", "Delete command did not navigate back.");
    }

    private sealed class FakeOccasionService : IOccasionService
    {
        private readonly List<Occasion> _occasions;
        private int _nextMilestoneId = 500;

        public FakeOccasionService(params Occasion[] occasions)
        {
            _occasions = occasions.Select(CloneOccasion).ToList();
        }

        public Occasion GetOccasion(int id) => _occasions.Single(occasion => occasion.Id == id);

        public Occasion? FindOccasion(int id) => _occasions.FirstOrDefault(occasion => occasion.Id == id);

        public Task<List<Occasion>> GetAllAsync() => Task.FromResult(_occasions.Select(CloneOccasion).ToList());

        public Task<Occasion?> GetByIdAsync(int id) => Task.FromResult(FindOccasion(id) is { } occasion ? CloneOccasion(occasion) : null);

        public Task<Occasion> CreateAsync(Occasion occasion)
        {
            _occasions.Add(CloneOccasion(occasion));
            return Task.FromResult(occasion);
        }

        public Task<Occasion> UpdateAsync(Occasion occasion)
        {
            var existing = GetOccasion(occasion.Id);
            existing.Title = occasion.Title;
            existing.Emoji = occasion.Emoji;
            existing.ColorHex = occasion.ColorHex;
            existing.AnchorDate = occasion.AnchorDate;
            existing.Direction = occasion.Direction;
            existing.Notes = occasion.Notes;
            existing.IsPinned = occasion.IsPinned;
            existing.CategoryId = occasion.CategoryId;
            return Task.FromResult(CloneOccasion(existing));
        }

        public Task DeleteAsync(int id)
        {
            _occasions.RemoveAll(occasion => occasion.Id == id);
            return Task.CompletedTask;
        }

        public Task DeleteAllAsync()
        {
            _occasions.Clear();
            return Task.CompletedTask;
        }

        public Task SetPinnedAsync(int id)
        {
            foreach (var occasion in _occasions)
                occasion.IsPinned = occasion.Id == id;

            return Task.CompletedTask;
        }

        public Task<Milestone> AddMilestoneAsync(int occasionId, int thresholdDays, string label)
        {
            var milestone = new Milestone
            {
                Id = _nextMilestoneId++,
                OccasionId = occasionId,
                ThresholdDays = thresholdDays,
                Label = label
            };

            GetOccasion(occasionId).Milestones.Add(milestone);
            return Task.FromResult(milestone);
        }

        public Task RemoveMilestoneAsync(int milestoneId)
        {
            foreach (var occasion in _occasions)
            {
                var milestone = occasion.Milestones.FirstOrDefault(item => item.Id == milestoneId);
                if (milestone is not null)
                {
                    occasion.Milestones.Remove(milestone);
                    break;
                }
            }

            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<CalendarMark>> GetCalendarMarksAsync(int year, int month) =>
            Task.FromResult<IReadOnlyList<CalendarMark>>([]);

        public int GetDays(Occasion occasion)
        {
            var anchor = occasion.AnchorDate.Date;
            var today = DateTime.UtcNow.Date;
            return occasion.Direction == OccasionDirection.Since
                ? (today - anchor).Days
                : (anchor - today).Days;
        }

        public Task<List<(Occasion, Milestone)>> GetPendingMilestonesAsync() =>
            Task.FromResult(new List<(Occasion, Milestone)>());

        public Task MarkMilestoneNotifiedAsync(int milestoneId)
        {
            var milestone = _occasions
                .SelectMany(occasion => occasion.Milestones)
                .Single(item => item.Id == milestoneId);

            milestone.Notified = true;
            return Task.CompletedTask;
        }

        private static Occasion CloneOccasion(Occasion source) =>
            new()
            {
                Id = source.Id,
                Title = source.Title,
                Emoji = source.Emoji,
                ColorHex = source.ColorHex,
                IsPinned = source.IsPinned,
                AnchorDate = source.AnchorDate,
                Direction = source.Direction,
                Notes = source.Notes,
                CreatedAt = source.CreatedAt,
                CategoryId = source.CategoryId,
                Category = source.Category,
                Milestones = source.Milestones
                    .Select(milestone => new Milestone
                    {
                        Id = milestone.Id,
                        OccasionId = milestone.OccasionId == 0 ? source.Id : milestone.OccasionId,
                        ThresholdDays = milestone.ThresholdDays,
                        Label = milestone.Label,
                        Notified = milestone.Notified
                    })
                    .ToList()
            };
    }

    private sealed class FakeAppShellService(DevicePlatform platform) : IAppShellService
    {
        public DevicePlatform Platform { get; } = platform;
        public bool NextConfirmationResult { get; set; } = true;
        public string? LastRoute { get; private set; }
        public List<(string Title, string Message, string Accept, string Cancel)> ConfirmationRequests { get; } = [];

        public Task GoToAsync(string route)
        {
            LastRoute = route;
            return Task.CompletedTask;
        }

        public Task<bool> DisplayAlertAsync(string title, string message, string accept, string cancel)
        {
            ConfirmationRequests.Add((title, message, accept, cancel));
            return Task.FromResult(NextConfirmationResult);
        }
    }

    private sealed class FakeShareService : IShareService
    {
        public OccasionShareRequest? LastOccasionShare { get; private set; }
        public MilestoneShareRequest? LastMilestoneShare { get; private set; }

        public Task ShareOccasionAsync(OccasionShareRequest request)
        {
            LastOccasionShare = request;
            return Task.CompletedTask;
        }

        public Task ShareMilestoneAsync(MilestoneShareRequest request)
        {
            LastMilestoneShare = request;
            return Task.CompletedTask;
        }
    }

    private static class SampleOccasions
    {
        public static Occasion OurAnniversary() =>
            new()
            {
                Id = 1,
                Title = "Our anniversary",
                Emoji = "💕",
                ColorHex = "#8E24AA",
                AnchorDate = new DateTime(2022, 10, 14, 0, 0, 0, DateTimeKind.Utc),
                Direction = OccasionDirection.Since,
                IsPinned = true,
                Notes = "Dinner plans locked in.",
                Milestones =
                [
                    new Milestone { Id = 11, OccasionId = 1, ThresholdDays = 1000, Label = "Four digits", Notified = true },
                    new Milestone { Id = 12, OccasionId = 1, ThresholdDays = 1461, Label = "Four years", Notified = false },
                    new Milestone { Id = 13, OccasionId = 1, ThresholdDays = 1500, Label = "Fifteen hundred", Notified = false }
                ]
            };

        public static Occasion SoberDays() =>
            new()
            {
                Id = 2,
                Title = "Sober days",
                Emoji = "🌱",
                ColorHex = "#0B8043",
                AnchorDate = new DateTime(2025, 3, 3, 0, 0, 0, DateTimeKind.Utc),
                Direction = OccasionDirection.Since,
                Notes = "One day at a time.",
                Milestones =
                [
                    new Milestone { Id = 21, OccasionId = 2, ThresholdDays = 365, Label = "One year", Notified = true },
                    new Milestone { Id = 22, OccasionId = 2, ThresholdDays = 565, Label = "Five sixty-five", Notified = false }
                ]
            };

        public static Occasion TripToLisbon() =>
            new()
            {
                Id = 3,
                Title = "Trip to Lisbon",
                Emoji = "✈️",
                ColorHex = "#039BE5",
                AnchorDate = new DateTime(2026, 12, 19, 0, 0, 0, DateTimeKind.Utc),
                Direction = OccasionDirection.Until,
                Notes = "Double-check passports.",
                Milestones =
                [
                    new Milestone { Id = 31, OccasionId = 3, ThresholdDays = 30, Label = "One month to go", Notified = false }
                ]
            };

        public static Occasion EllasAge() =>
            new()
            {
                Id = 4,
                Title = "Ella's age",
                Emoji = "👶",
                ColorHex = "#F6BF26",
                AnchorDate = new DateTime(2024, 2, 6, 0, 0, 0, DateTimeKind.Utc),
                Direction = OccasionDirection.Since,
                Notes = "First kindergarten visit this month.",
                Milestones =
                [
                    new Milestone { Id = 41, OccasionId = 4, ThresholdDays = 1000, Label = "1000 days old", Notified = false }
                ]
            };
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
