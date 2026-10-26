namespace FFmpegStudio.Shared;

public interface IEditorPrompts
{
    Task<bool> ConfirmAsync(string title, string message);
    Task<string?> AskAsync(string title, string label, string initial);
    Task<IReadOnlyList<string>?> PickOpenFilesAsync();
    Task<string?> PickOpenFileAsync(string title);
    Task<string?> PickFolderAsync(string title);
    Task<string?> PickSaveFileAsync(string title, string suggestedName);
}

public sealed class RejectingPrompts : IEditorPrompts
{
    public Task<bool> ConfirmAsync(string title, string message) => Task.FromResult(false);
    public Task<string?> AskAsync(string title, string label, string initial) => Task.FromResult<string?>(null);
    public Task<IReadOnlyList<string>?> PickOpenFilesAsync() => Task.FromResult<IReadOnlyList<string>?>(null);
    public Task<string?> PickOpenFileAsync(string title) => Task.FromResult<string?>(null);
    public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
    public Task<string?> PickSaveFileAsync(string title, string suggestedName) => Task.FromResult<string?>(null);
}
