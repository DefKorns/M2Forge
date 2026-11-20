using System.Threading.Tasks;

namespace M2Forge.App.Services;

public interface IDialogService
{
    Task<string?> PickFolderAsync(string title);

    Task<string?> OpenFileAsync(string title, string filterName, params string[] patterns);

    Task<string?> SaveFileAsync(string title, string suggestedName, string extension);

    Task<bool> ConfirmAsync(string title, string message, string accept);
}
