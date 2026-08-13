using System.Windows;
using Forms = System.Windows.Forms;
using WpfOpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace LocalLlmConsole.Services;

public sealed record FolderPickerRequest(string InitialPath);

public sealed record OpenFilePickerRequest(
    string Title,
    string Filter,
    bool CheckFileExists,
    bool AddExtension,
    string DefaultExt,
    string FileName,
    string InitialDirectory)
;

public sealed class FileSystemDialogService
{
    private readonly Func<FolderPickerRequest, string?> _pickFolder;
    private readonly Func<OpenFilePickerRequest, Window?, string?> _pickOpenFile;

    public FileSystemDialogService(
        Func<FolderPickerRequest, string?> pickFolder,
        Func<OpenFilePickerRequest, Window?, string?> pickOpenFile)
    {
        _pickFolder = pickFolder ?? throw new ArgumentNullException(nameof(pickFolder));
        _pickOpenFile = pickOpenFile ?? throw new ArgumentNullException(nameof(pickOpenFile));
    }

    public string? PickFolder(string initialPath)
        => _pickFolder(new FolderPickerRequest(initialPath));

    public string? PickOpenFile(OpenFilePickerRequest request, Window? owner = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _pickOpenFile(request, owner);
    }

    public static string ExistingDirectoryOrEmpty(string path)
        => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path) ? path : "";

    public static string? ShowFolderDialog(FolderPickerRequest request)
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            SelectedPath = ExistingDirectoryOrEmpty(request.InitialPath)
        };
        return dialog.ShowDialog() == Forms.DialogResult.OK ? dialog.SelectedPath : null;
    }

    public static string? ShowOpenFileDialog(OpenFilePickerRequest request, Window? owner)
    {
        var dialog = new WpfOpenFileDialog
        {
            Title = request.Title,
            Filter = request.Filter,
            CheckFileExists = request.CheckFileExists,
            AddExtension = request.AddExtension,
            DefaultExt = request.DefaultExt,
            FileName = request.FileName
        };
        if (!string.IsNullOrWhiteSpace(request.InitialDirectory))
            dialog.InitialDirectory = request.InitialDirectory;

        var accepted = owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
        return accepted == true ? dialog.FileName : null;
    }
}
