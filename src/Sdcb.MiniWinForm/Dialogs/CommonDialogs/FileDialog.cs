using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Controls.Dialogs;
using static Windows.Win32.UI.Controls.Dialogs.OPEN_FILENAME_FLAGS;

namespace Sdcb.MiniWinForm;

public abstract unsafe class FileDialog : CommonDialog
{
    private const int FileBufferSize = 8192;

    private string _title = string.Empty;
    private string _initialDirectory = string.Empty;
    private string _defaultExt = string.Empty;
    private string _filter = string.Empty;
    private string[] _fileNames = [];

    protected FileDialog()
    {
        Reset();
    }

    public bool AddExtension { get; set; }

    public virtual bool CheckFileExists { get; set; }

    public bool CheckPathExists { get; set; }

    public string DefaultExt
    {
        get => _defaultExt;
        set => _defaultExt = NormalizeExtension(value);
    }

    public string FileName
    {
        get => _fileNames.Length > 0 ? _fileNames[0] : string.Empty;
        set => _fileNames = string.IsNullOrEmpty(value) ? [] : [value];
    }

    public string[] FileNames => _fileNames.Length == 0 ? [] : (string[])_fileNames.Clone();

    public string Filter
    {
        get => _filter;
        set
        {
            value ??= string.Empty;
            if (value.Length > 0 && value.Count(static c => c == '|') % 2 == 0)
            {
                throw new ArgumentException("Filter must contain description/pattern pairs separated by '|'.", nameof(value));
            }

            _filter = value;
        }
    }

    public int FilterIndex { get; set; }

    public string InitialDirectory
    {
        get => _initialDirectory;
        set => _initialDirectory = value ?? string.Empty;
    }

    public bool RestoreDirectory { get; set; }

    public string Title
    {
        get => _title;
        set => _title = value ?? string.Empty;
    }

    public override void Reset()
    {
        AddExtension = true;
        CheckFileExists = false;
        CheckPathExists = true;
        DefaultExt = string.Empty;
        FileName = string.Empty;
        Filter = string.Empty;
        FilterIndex = 1;
        InitialDirectory = string.Empty;
        RestoreDirectory = false;
        Title = string.Empty;
    }

    private protected override bool RunDialog(HWND owner)
    {
        char[] fileBuffer = new char[FileBufferSize];
        string fileName = FileName;
        fileName.AsSpan(0, Math.Min(fileName.Length, fileBuffer.Length - 1)).CopyTo(fileBuffer);

        string? filter = MakeFilterString(Filter);
        OPENFILENAMEW openFileName = new()
        {
            lStructSize = (uint)Marshal.SizeOf<OPENFILENAMEW>(),
            hwndOwner = owner,
            hInstance = Application.Instance,
            nFilterIndex = (uint)Math.Max(1, FilterIndex),
            nMaxFile = (uint)fileBuffer.Length,
            Flags = BuildOptions(),
        };

        fixed (char* fileBufferPointer = fileBuffer)
        fixed (char* filterPointer = filter)
        fixed (char* initialDirectoryPointer = InitialDirectory.Length == 0 ? null : InitialDirectory)
        fixed (char* titlePointer = Title.Length == 0 ? null : Title)
        fixed (char* defaultExtPointer = DefaultExt.Length == 0 ? null : DefaultExt)
        {
            openFileName.lpstrFile = fileBufferPointer;
            openFileName.lpstrFilter = filterPointer;
            openFileName.lpstrInitialDir = initialDirectoryPointer;
            openFileName.lpstrTitle = titlePointer;
            openFileName.lpstrDefExt = defaultExtPointer;

            bool accepted = RunFileDialog(ref openFileName);
            if (!accepted)
            {
                return false;
            }
        }

        FilterIndex = (int)openFileName.nFilterIndex;
        _fileNames = ParseFileNames(fileBuffer, AllowMultiSelect);
        return true;
    }

    private protected abstract bool RunFileDialog(ref OPENFILENAMEW openFileName);

    private protected virtual bool AllowMultiSelect => false;

    private protected virtual OPEN_FILENAME_FLAGS BuildOptions()
    {
        OPEN_FILENAME_FLAGS flags = OFN_EXPLORER;

        if (CheckFileExists)
        {
            flags |= OFN_FILEMUSTEXIST;
        }

        if (CheckPathExists)
        {
            flags |= OFN_PATHMUSTEXIST;
        }

        if (RestoreDirectory)
        {
            flags |= OFN_NOCHANGEDIR;
        }

        if (AllowMultiSelect)
        {
            flags |= OFN_ALLOWMULTISELECT;
        }

        return flags;
    }

    private static string NormalizeExtension(string? extension)
    {
        if (string.IsNullOrEmpty(extension))
        {
            return string.Empty;
        }

        return extension[0] == '.' ? extension[1..] : extension;
    }

    private static string? MakeFilterString(string filter)
    {
        return filter.Length == 0 ? null : filter.Replace('|', '\0') + "\0\0";
    }

    private static string[] ParseFileNames(char[] buffer, bool multiselect)
    {
        List<string> parts = [];
        int start = 0;
        for (int index = 0; index < buffer.Length; index++)
        {
            if (buffer[index] != '\0')
            {
                continue;
            }

            if (index == start)
            {
                break;
            }

            parts.Add(new string(buffer, start, index - start));
            start = index + 1;
        }

        if (!multiselect || parts.Count <= 1)
        {
            return parts.Count == 0 ? [] : [parts[0]];
        }

        string directory = parts[0];
        string[] fileNames = new string[parts.Count - 1];
        for (int index = 1; index < parts.Count; index++)
        {
            fileNames[index - 1] = Path.IsPathFullyQualified(parts[index])
                ? parts[index]
                : Path.Combine(directory, parts[index]);
        }

        return fileNames;
    }
}