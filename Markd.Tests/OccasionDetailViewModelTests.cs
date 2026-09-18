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
        await LoadAsync_BuildsReachedAndUpcomingMilestoneStates();
        await AddMilestoneCommand_AddsMilestoneAndReloadsOccasion();
        await EditCommand_NavigatesToOccasionFormRoute();
        await PinCommand_PinsUnpinnedOccasion();
        await PinCommand_RaisesPinStateForTheSameTrackedInstance();
        await LoadAsync_AfterAnEdit_RaisesTheNewTitle();
        await RemoveMilestoneCommand_ConfirmsBeforeRemovingOnIos();
        await RemoveMilestoneCommand_RemovesAndToastsOnWindows();
        await RemoveMilestoneCommand_OffersUndoOnAndroid();
        await ShareCommand_SharesOccasionWithNextMilestoneContext();
        await DeleteCommand_ConfirmsOnWindowsAndNavigatesBack();
        await DeleteCommand_OnAndroid_DeletesWithoutConfirmAndUndoRestores();

        Console.WriteLine("Occasion detail smoke verification passed.");
    }

    private static OccasionDetailViewModel Create(
        FakeOccasionService services,
        FakeAppShellService shell,
        FakeShareService? share = null,
        FakeFeedbackService? feedback = null,
        MilestoneEditorResult? editorResult = null) =>
        new(services, shell, new FakeMilestoneEditorService(editorResult), share ?? new FakeShareService(), feedback ?? new FakeFeedbackService());

    private static async Task LoadAsync_BuildsReachedAndUpcomingMilestoneStates()
    {
        var occasion = SampleOccasions.OurAnniversary();
        var viewModel = Create(new FakeOccasionService(occasion), new FakeAppShellService(DevicePlatform.WinUI));

        await viewModel.LoadAsync(occasion.Id);

        Ensure(viewModel.MilestoneStates.Count == 3, "Expected three milestone states.");
        Ensure(viewModel.MilestoneStates[0].IsReached, "Expected the first milestone to be reached.");
        Ensure(!viewModel.MilestoneStates[1].IsReached, "Expected the second milestone to be upcoming.");
        Ensure(viewModel.MilestoneStates[0].StatusText.StartsWith("reached ", StringComparison.Ordinal), "Reached milestone text missing.");
        Ensure(viewModel.MilestoneStates[1].StatusText == "26 days away", "Upcoming milestone text missing.");
        Ensure(viewModel.NextMilestoneHeading == "Next · Four years", "Next milestone heading was wrong.");
        Ensure(viewModel.DisplayDays == 1435 && viewModel.UnitLabel == "days", "Count or unit was wrong.");
    }

    private static async Task AddMilestoneCommand_AddsMilestoneAndReloadsOccasion()
    {
        var occasion = SampleOccasions.SoberDays();
        var services = new FakeOccasionService(occasion);
        var viewModel = Create(services, new FakeAppShellService(DevicePlatform.WinUI), editorResult: new MilestoneEditorResult("Seven hundred", 700));

        await viewModel.LoadAsync(occasion.Id);
        await viewModel.AddMilestoneCommand.ExecuteAsync(null);

        Ensure(services.GetOccasion(occasion.Id).Milestones.Any(milestone => milestone.Label == "Seven hundred"), "Milestone was not added to the store.");
        Ensure(viewModel.MilestoneStates.Any(milestone => milestone.Label == "Seven hundred"), "Milestone view state was not refreshed.");
    }

    private static async Task EditCommand_NavigatesToOccasionFormRoute()
    {
        var occasion = SampleOccasions.TripToLisbon();
        var shell = new FakeAppShellService(DevicePlatform.WinUI);
        var viewModel = Create(new FakeOccasionService(occasion), shell);

        await viewModel.LoadAsync(occasion.Id);
        await viewModel.EditCommand.ExecuteAsync(null);

        Ensure(shell.LastRoute == $"{nameof(OccasionFormPage)}?id={occasion.Id}", "Edit navigation route was incorrect.");
    }

    private static async Task PinCommand_PinsUnpinnedOccasion()
    {
        var occasion = SampleOccasions.EllasAge();
        var services = new FakeOccasionService(occasion);
        var viewModel = Create(services, new FakeAppShellService(DevicePlatform.WinUI));

        await viewModel.LoadAsync(occasion.Id);
        await viewModel.PinCommand.ExecuteAsync(null);

        Ensure(services.GetOccasion(occasion.Id).IsPinned, "Pin command did not pin the occasion.");
        Ensure(viewModel.PinButtonText == "Unpin", "Pinned occasion did not surface unpin text.");
    }

    private static async Task PinCommand_RaisesPinStateForTheSameTrackedInstance()
    {
        var occasion = SampleOccasions.EllasAge();
        var viewModel = Create(new FakeOccasionService(occasion), new FakeAppShellService(DevicePlatform.WinUI));
        await viewModel.LoadAsync(occasion.Id);
        var raised = new List<string?>();
        viewModel.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        await viewModel.PinCommand.ExecuteAsync(null);

        Ensure(raised.Contains(nameof(OccasionDetailViewModel.IsPinned)), "Pinning did not raise IsPinned.");
        Ensure(raised.Contains(nameof(OccasionDetailViewModel.PinToHomeText)), "Pinning did not raise PinToHomeText.");
    }

    private static async Task LoadAsync_AfterAnEdit_RaisesTheNewTitle()
    {
        var occasion = SampleOccasions.TripToLisbon();
        var services = new FakeOccasionService(occasion);
        var viewModel = Create(services, new FakeAppShellService(DevicePlatform.WinUI));
        await viewModel.LoadAsync(occasion.Id);
        var raised = new List<string?>();
        viewModel.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        services.GetOccasion(occasion.Id).Title = "Lisbon, finally";
        await viewModel.LoadAsync(occasion.Id);

        Ensure(raised.Contains(nameof(OccasionDetailViewModel.Title)) && viewModel.Title == "Lisbon, finally", "Reload after an edit did not raise the new title.");
    }

    private static async Task RemoveMilestoneCommand_ConfirmsBeforeRemovingOnIos()
    {
        var occasion = SampleOccasions.OurAnniversary();
        var services = new FakeOccasionService(occasion);
        var shell = new FakeAppShellService(DevicePlatform.iOS) { NextConfirmationResult = true };
        var viewModel = Create(services, shell);

        await viewModel.LoadAsync(occasion.Id);
        await viewModel.RemoveMilestoneCommand.ExecuteAsync(viewModel.MilestoneStates[1].Milestone);

        Ensure(shell.ConfirmationRequests.Count == 1, "iOS milestone removal should confirm.");
        Ensure(!services.GetOccasion(occasion.Id).Milestones.Any(milestone => milestone.Label == "Four years"), "Milestone was not removed after confirmation.");
    }

    private static async Task RemoveMilestoneCommand_RemovesAndToastsOnWindows()
    {
        var occasion = SampleOccasions.OurAnniversary();
        var services = new FakeOccasionService(occasion);
        var shell = new FakeAppShellService(DevicePlatform.WinUI);
        var feedback = new FakeFeedbackService();
        var viewModel = Create(services, shell, feedback: feedback);

        await viewModel.LoadAsync(occasion.Id);
        await viewModel.RemoveMilestoneCommand.ExecuteAsync(viewModel.MilestoneStates[1].Milestone);

        Ensure(shell.ConfirmationRequests.Count == 0, "Desktop milestone removal uses the hover affordance, not a dialog.");
        Ensure(feedback.Messages.Contains("Milestone removed"), "Desktop milestone removal should raise a toast.");
        Ensure(!services.GetOccasion(occasion.Id).Milestones.Any(milestone => milestone.Label == "Four years"), "Milestone was not removed.");
    }

    private static async Task RemoveMilestoneCommand_OffersUndoOnAndroid()
    {
        var occasion = SampleOccasions.OurAnniversary();
        var services = new FakeOccasionService(occasion);
        var shell = new FakeAppShellService(DevicePlatform.Android);
        var feedback = new FakeFeedbackService { UndoResult = true };
        var viewModel = Create(services, shell, feedback: feedback);

        await viewModel.LoadAsync(occasion.Id);
        await viewModel.RemoveMilestoneCommand.ExecuteAsync(viewModel.MilestoneStates[0].Milestone);

        Ensure(shell.ConfirmationRequests.Count == 0, "Android milestone removal should skip confirmation.");
        Ensure(feedback.UndoOffers == 1, "Android milestone removal should offer undo.");
        var restored = services.GetOccasion(occasion.Id).Milestones.SingleOrDefault(milestone => milestone.Label == "Four digits");
        Ensure(restored is { Notified: true }, "Undo should restore the milestone with its notified flag.");
    }

    private static async Task ShareCommand_SharesOccasionWithNextMilestoneContext()
    {
        var occasion = SampleOccasions.OurAnniversary();
        var share = new FakeShareService();
        var viewModel = Create(new FakeOccasionService(occasion), new FakeAppShellService(DevicePlatform.WinUI), share);

        await viewModel.LoadAsync(occasion.Id);
        await viewModel.ShareCommand.ExecuteAsync(null);

        Ensure(share.LastOccasionShare is not null, "Share command did not invoke the share service.");
        Ensure(share.LastOccasionShare!.Occasion.Title == "Our anniversary", "Share request used the wrong occasion.");
        Ensure(share.LastOccasionShare.NextMilestoneLabel == "Four years", "Share request missed the next milestone context.");
    }

    private static async Task DeleteCommand_ConfirmsOnWindowsAndNavigatesBack()
    {
        var occasion = SampleOccasions.TripToLisbon();
        var services = new FakeOccasionService(occasion);
        var shell = new FakeAppShellService(DevicePlatform.WinUI) { NextConfirmationResult = true };
        var feedback = new FakeFeedbackService();
        var viewModel = Create(services, shell, feedback: feedback);

        await viewModel.LoadAsync(occasion.Id);
        await viewModel.DeleteCommand.ExecuteAsync(null);

        Ensure(services.FindOccasion(occasion.Id) is null, "Delete command did not remove the occasion.");
        Ensure(shell.LastRoute == "..", "Delete command did not navigate back.");
        Ensure(shell.ConfirmationRequests.Count == 1, "Desktop delete should be confirmed.");
        Ensure(feedback.Messages.Contains("Occasion deleted"), "Desktop delete should raise a toast.");
    }

    private static async Task DeleteCommand_OnAndroid_DeletesWithoutConfirmAndUndoRestores()
    {
        var occasion = SampleOccasions.SoberDays();
        var services = new FakeOccasionService(occasion);
        var shell = new FakeAppShellService(DevicePlatform.Android);
        var feedback = new FakeFeedbackService { UndoResult = true };
        var viewModel = Create(services, shell, feedback: feedback);

        await viewModel.LoadAsync(occasion.Id);
        await viewModel.DeleteCommand.ExecuteAsync(null);

        Ensure(shell.ConfirmationRequests.Count == 0, "Android delete is reversible, not confirmed.");
        Ensure(shell.LastRoute == "..", "Android delete did not navigate back.");
        Ensure(services.FindOccasion(occasion.Id) is null, "Android delete did not remove the original record.");
        var restored = (await services.GetAllAsync()).SingleOrDefault(o => o.Title == "Sober days");
        Ensure(restored is not null && restored.Milestones.Count == 2, "Undo did not restore the occasion with its milestones.");
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

        // Like the app's long-lived EF context, reads hand back the same tracked instance.
        public Task<Occasion?> GetByIdAsync(int id) => Task.FromResult(FindOccasion(id));

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

        public Task<Occasion> RestoreAsync(Occasion snapshot)
        {
            var restored = CloneOccasion(snapshot);
            restored.Id = _occasions.Count == 0 ? 100 : _occasions.Max(occasion => occasion.Id) + 100;
            foreach (var milestone in restored.Milestones)
            {
                milestone.Id = _nextMilestoneId++;
                milestone.OccasionId = restored.Id;
            }

            _occasions.Add(restored);
            return Task.FromResult(CloneOccasion(restored));
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

        public int GetDays(Occasion occasion) => OccasionDates.GetDays(occasion, DateTime.Now);

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
        public List<(string Title, string Message, string Accept, string Cancel, bool Destructive)> ConfirmationRequests { get; } = [];

        public Task GoToAsync(string route)
        {
            LastRoute = route;
            return Task.CompletedTask;
        }

        public Task<bool> DisplayAlertAsync(string title, string message, string accept, string cancel, bool destructive = false)
        {
            ConfirmationRequests.Add((title, message, accept, cancel, destructive));
            return Task.FromResult(NextConfirmationResult);
        }

        public Task ShowMessageAsync(string title, string message, string close) => Task.CompletedTask;
    }

    private sealed class FakeFeedbackService : IFeedbackService
    {
        public bool UndoResult { get; init; }
        public int UndoOffers { get; private set; }
        public List<string> Messages { get; } = [];

        public Task ShowAsync(string title, string? detail = null)
        {
            Messages.Add(title);
            return Task.CompletedTask;
        }

        public Task<bool> ShowUndoAsync(string message)
        {
            UndoOffers++;
            Messages.Add(message);
            return Task.FromResult(UndoResult);
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

    private sealed class FakeMilestoneEditorService(MilestoneEditorResult? result) : IMilestoneEditorService
    {
        public Task<MilestoneEditorResult?> PromptAsync() => Task.FromResult(result);
    }

    private static DateTime LocalMidnightUtc(int dayOffset) => DateTime.Today.AddDays(dayOffset).ToUniversalTime();

    private static class SampleOccasions
    {
        public static Occasion OurAnniversary() =>
            new()
            {
                Id = 1,
                Title = "Our anniversary",
                Emoji = "💕",
                ColorHex = "#8E24AA",
                AnchorDate = LocalMidnightUtc(-1435),
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
                AnchorDate = LocalMidnightUtc(-564),
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
                AnchorDate = LocalMidnightUtc(92),
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
                AnchorDate = LocalMidnightUtc(-955),
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
