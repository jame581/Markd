using Markd.ViewModels;

namespace Markd.Services;

public interface IMilestoneEditorService
{
    Task<MilestoneEditorResult?> PromptAsync();
}
