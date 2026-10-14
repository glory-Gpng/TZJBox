using TZJBox.Sorting;

namespace TZJBox.App.Services;

public interface IUserDialogs
{
    Task<string?> PickFolderAsync();

    Task<bool> ConfirmRenameAsync(IReadOnlyList<RenamePlanItem> changes);

    Task AlertAsync(string title, string message);
}
