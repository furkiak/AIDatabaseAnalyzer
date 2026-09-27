using System.Text;
using System.Text.Json;

namespace AIDatabaseAnalyzer.Services;

internal static class AtomicProfileFile
{
    public static JsonDocument Read(string path)
    {
        AppProfile.AssertNoLinks(path);
        if (new FileInfo(path).Length > 65_536) throw new InvalidDataException("Profile file exceeds its supported size.");
        var document = JsonDocument.Parse(File.ReadAllBytes(path));
        try { CheckDuplicates(document.RootElement); return document; }
        catch { document.Dispose(); throw; }
    }

    private static void CheckDuplicates(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidDataException("Duplicate profile property.");
                CheckDuplicates(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) CheckDuplicates(item);
    }

    public static void Write(string path, string json)
    {
        AppProfile.AssertNoLinks(path);
        AppProfile.AssertNoLinks(path + ".bak");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(Encoding.UTF8.GetBytes(json));
                stream.Flush(true);
            }
            AppProfile.AssertNoLinks(path);
            AppProfile.AssertNoLinks(path + ".bak");
            if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
            else File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
