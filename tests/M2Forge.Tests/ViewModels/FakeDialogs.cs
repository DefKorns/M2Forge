using M2Forge.App.Services;

namespace M2Forge.Tests.ViewModels;

public sealed class FakeDialogs : IDialogService
{
    public string? Folder { get; set; }
    public string? OpenFile { get; set; }
    public string? SaveFile { get; set; }
    public bool Confirm { get; set; } = true;
    public int ConfirmCalls { get; private set; }
    public string? LastSuggestedName { get; private set; }

    public Task<string?> PickFolderAsync(string title) => Task.FromResult(Folder);

    public Task<string?> OpenFileAsync(string title, string filterName, params string[] patterns) => Task.FromResult(OpenFile);

    public Task<string?> SaveFileAsync(string title, string suggestedName, string extension)
    {
        LastSuggestedName = suggestedName;
        return Task.FromResult(SaveFile);
    }

    public Task<bool> ConfirmAsync(string title, string message, string accept)
    {
        ConfirmCalls++;
        return Task.FromResult(Confirm);
    }
}
