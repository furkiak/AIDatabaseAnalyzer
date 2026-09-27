using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace AIDatabaseAnalyzer.Services;

public sealed record AppProfile(string DirectoryPath, bool IsIsolated)
{
    private const string Marker = "AI Database Analyzer isolated profile v1\n";
    public string Label => IsIsolated ? $"ISOLATED PROFILE — {DirectoryPath}" : "Normal local profile";

    public static AppProfile Resolve(string[] arguments, string normalDirectory)
    {
        if (arguments.Length == 0) return new(Path.GetFullPath(normalDirectory), false);
        if (arguments.Length != 2 || arguments[0] != "--data-directory" || !Path.IsPathFullyQualified(arguments[1]))
            throw new ArgumentException("Use --data-directory followed by one absolute directory. Invalid arguments never open the normal profile.");
        string requested = Path.TrimEndingDirectorySeparator(Path.GetFullPath(arguments[1]));
        string normal = Path.TrimEndingDirectorySeparator(Path.GetFullPath(normalDirectory));
        if (Within(requested, normal) || Within(normal, requested))
            throw new ArgumentException("The isolated directory must not overlap the normal application directory.");
        AssertNoLinks(requested);
        var profile = new AppProfile(requested, true);
        string marker = profile.FilePath(".aidb-profile");
        if (Directory.Exists(requested) && !File.Exists(marker) && Directory.EnumerateFileSystemEntries(requested).Any())
            throw new InvalidDataException("The directory is not an owned isolated profile. Its files were preserved.");
        Directory.CreateDirectory(requested);
        if (File.Exists(marker))
        {
            if (File.ReadAllText(marker) != Marker) throw new InvalidDataException("Unsupported profile marker. No settings were loaded.");
        }
        else
        {
            using var stream = new FileStream(marker, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using var writer = new StreamWriter(stream);
            writer.Write(Marker);
        }
        return profile;
    }

    public string FilePath(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || name != Path.GetFileName(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("Only a profile-owned file name is allowed.");
        string path = Path.GetFullPath(Path.Combine(DirectoryPath, name));
        AssertNoLinks(path);
        return path;
    }

    private static bool Within(string path, string root) => path.Equals(root, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    public static void AssertNoLinks(string path)
    {
        for (string? current = Path.GetFullPath(path); current != null; current = Path.GetDirectoryName(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Profile paths must not contain symbolic links or junctions.");
                if (OperatingSystem.IsWindows() && File.Exists(current)) RejectHardLinks(current);
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }

    private static void RejectHardLinks(string path)
    {
        using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (!GetFileInformationByHandle(handle, out var information)) throw new IOException("Profile file identity could not be checked.", new Win32Exception(Marshal.GetLastWin32Error()));
        if (information.NumberOfLinks != 1) throw new IOException("Profile files must not be hard-linked to another file.");
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInformation information);

    [StructLayout(LayoutKind.Sequential)]
    private struct FileInformation
    {
        public uint Attributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime, AccessTime, WriteTime;
        public uint VolumeSerial, SizeHigh, SizeLow, NumberOfLinks, IndexHigh, IndexLow;
    }
}
