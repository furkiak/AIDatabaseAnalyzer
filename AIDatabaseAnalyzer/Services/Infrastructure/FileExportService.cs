using System.Text;

namespace AIDatabaseAnalyzer.Services.Infrastructure;

public interface IFileExportService
{
    /// <summary>Lets the user choose where to save; returns the final path, or <c>null</c> when the dialog was cancelled.</summary>
    Task<string?> SaveAsync(string suggestedFileName, byte[] content);

    Task<string?> SaveTextAsync(string suggestedFileName, string text);

    /// <summary>Copies an existing file to a location chosen by the user.</summary>
    Task<string?> SaveCopyAsync(string existingFile, string suggestedFileName);

    /// <summary>Opens a file with its default application.</summary>
    Task OpenAsync(string path);

    /// <summary>Shows a folder in Explorer / Finder.</summary>
    Task OpenFolderAsync(string folder);
}

/// <summary>
/// Cross-platform "Save as…". Windows uses the native save picker, macOS (Mac Catalyst) the document export sheet,
/// everything else falls back to the application's export folder.
/// </summary>
public sealed class FileExportService : IFileExportService
{
    public Task<string?> SaveTextAsync(string suggestedFileName, string text) =>
        SaveAsync(suggestedFileName, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text));

    public async Task<string?> SaveCopyAsync(string existingFile, string suggestedFileName) =>
        File.Exists(existingFile) ? await SaveAsync(suggestedFileName, await File.ReadAllBytesAsync(existingFile)) : null;

    public async Task<string?> SaveAsync(string suggestedFileName, byte[] content)
    {
        string name = SanitizeFileName(suggestedFileName);

#if WINDOWS
        return await SaveWindowsAsync(name, content);
#elif MACCATALYST || IOS
        return await SaveAppleAsync(name, content);
#else
        return await SaveToExportFolderAsync(name, content);
#endif
    }

    public async Task OpenAsync(string path)
    {
        if (!File.Exists(path)) return;
        await MainThread.InvokeOnMainThreadAsync(() =>
            Launcher.Default.OpenAsync(new OpenFileRequest(Path.GetFileName(path), new ReadOnlyFile(path))));
    }

    public async Task OpenFolderAsync(string folder)
    {
        if (!Directory.Exists(folder)) return;
        await MainThread.InvokeOnMainThreadAsync(() => Launcher.Default.TryOpenAsync(new Uri(folder)));
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        string cleaned = new(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "export" : cleaned;
    }

    /// <summary>Fallback: writes into the app's export folder and returns the path.</summary>
    private static async Task<string?> SaveToExportFolderAsync(string name, byte[] content)
    {
        string folder = Path.Combine(FileSystem.AppDataDirectory, "Exports");
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, name);
        await File.WriteAllBytesAsync(path, content);
        return path;
    }

#if WINDOWS
    private static async Task<string?> SaveWindowsAsync(string name, byte[] content)
    {
        try
        {
            string extension = Path.GetExtension(name);
            string? path = await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                var picker = new global::Windows.Storage.Pickers.FileSavePicker
                {
                    SuggestedStartLocation = global::Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary,
                    SuggestedFileName = Path.GetFileNameWithoutExtension(name)
                };
                picker.FileTypeChoices.Add(string.IsNullOrEmpty(extension) ? "File" : extension.TrimStart('.').ToUpperInvariant() + " file",
                    new List<string> { string.IsNullOrEmpty(extension) ? "." : extension });

                var handle = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
                global::WinRT.Interop.InitializeWithWindow.Initialize(picker, handle);

                var file = await picker.PickSaveFileAsync();
                return file?.Path;
            });

            if (path is null) return null;
            await File.WriteAllBytesAsync(path, content);
            return path;
        }
        catch (Exception)
        {
            // Picker unavailable (e.g. elevated process): never lose the user's export.
            return await SaveToExportFolderAsync(name, content);
        }
    }
#endif

#if MACCATALYST || IOS
    private static async Task<string?> SaveAppleAsync(string name, byte[] content)
    {
        string temp = Path.Combine(FileSystem.CacheDirectory, name);
        await File.WriteAllBytesAsync(temp, content);

        var completion = new TaskCompletionSource<string?>();
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            var url = global::Foundation.NSUrl.FromFilename(temp);
            var picker = new global::UIKit.UIDocumentPickerViewController(new[] { url }, true);
            picker.DidPickDocumentAtUrls += (_, e) => completion.TrySetResult(e.Urls.FirstOrDefault()?.Path ?? temp);
            picker.WasCancelled += (_, _) => completion.TrySetResult(null);

            var controller = Platform.GetCurrentUIViewController();
            if (controller is null)
            {
                completion.TrySetResult(null);
                return;
            }
            controller.PresentViewController(picker, true, null);
        });

        return await completion.Task;
    }
#endif
}
