using System.Text.Json;
using System.Text.Json.Serialization;
using AIDatabaseAnalyzer.Models;

namespace AIDatabaseAnalyzer.Services.Infrastructure;

/// <summary>The small header stored next to every report so the history loads instantly.</summary>
public sealed class ReportSummary
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string DatabaseName { get; set; } = "";
    public string EngineName { get; set; } = "";
    public string Provider { get; set; } = "";
    public string Model { get; set; } = "";
    public DateTime GeneratedAtUtc { get; set; }
    public int HealthScore { get; set; }
    public string HealthGrade { get; set; } = "";
    public int SectionCount { get; set; }
    public int FailedSections { get; set; }
}

public interface IReportStore
{
    string Folder { get; }
    string PdfPath(string id);
    Task SaveAsync(AnalysisReport report);
    Task<IReadOnlyList<ReportSummary>> ListAsync();
    Task<AnalysisReport?> LoadAsync(string id);
    Task DeleteAsync(string id);
}

[JsonSerializable(typeof(AnalysisReport))]
[JsonSerializable(typeof(ReportSummary))]
[JsonSourceGenerationOptions(WriteIndented = false, DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
internal sealed partial class ReportJsonContext : JsonSerializerContext
{
}

/// <summary>Keeps finished reports in the application's data folder so they can be re-opened and re-exported.</summary>
public sealed class ReportStore : IReportStore
{
    public ReportStore() : this(Path.Combine(FileSystem.AppDataDirectory, "Reports"))
    {
    }

    public ReportStore(string folder)
    {
        Folder = folder;
        Directory.CreateDirectory(Folder);
    }

    public string Folder { get; }

    public string PdfPath(string id) => Path.Combine(Folder, SafeId(id) + ".pdf");

    public async Task SaveAsync(AnalysisReport report)
    {
        string id = SafeId(report.Id);
        var summary = new ReportSummary
        {
            Id = id,
            Title = report.Title,
            DatabaseName = report.DatabaseName,
            EngineName = report.EngineName,
            Provider = report.Provider,
            Model = report.Model,
            GeneratedAtUtc = report.GeneratedAtUtc,
            HealthScore = report.HealthScore,
            HealthGrade = report.HealthGrade,
            SectionCount = report.Sections.Count,
            FailedSections = report.FailedSections
        };

        await WriteAtomicAsync(Path.Combine(Folder, id + ".json"), JsonSerializer.Serialize(report, ReportJsonContext.Default.AnalysisReport)).ConfigureAwait(false);
        await WriteAtomicAsync(Path.Combine(Folder, id + ".meta.json"), JsonSerializer.Serialize(summary, ReportJsonContext.Default.ReportSummary)).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ReportSummary>> ListAsync()
    {
        var result = new List<ReportSummary>();
        if (!Directory.Exists(Folder)) return result;

        foreach (string file in Directory.EnumerateFiles(Folder, "*.meta.json"))
        {
            try
            {
                string json = await File.ReadAllTextAsync(file).ConfigureAwait(false);
                var summary = JsonSerializer.Deserialize(json, ReportJsonContext.Default.ReportSummary);
                if (summary is not null && !string.IsNullOrEmpty(summary.Id)) result.Add(summary);
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                // a damaged entry must not hide the other reports
            }
        }

        return result.OrderByDescending(r => r.GeneratedAtUtc).ToList();
    }

    public async Task<AnalysisReport?> LoadAsync(string id)
    {
        string path = Path.Combine(Folder, SafeId(id) + ".json");
        if (!File.Exists(path)) return null;
        try
        {
            string json = await File.ReadAllTextAsync(path).ConfigureAwait(false);
            return JsonSerializer.Deserialize(json, ReportJsonContext.Default.AnalysisReport);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return null;
        }
    }

    public Task DeleteAsync(string id)
    {
        string safe = SafeId(id);
        foreach (string suffix in new[] { ".json", ".meta.json", ".pdf" })
        {
            string path = Path.Combine(Folder, safe + suffix);
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (IOException)
            {
                // file is open in a viewer; it will be removed next time
            }
        }
        return Task.CompletedTask;
    }

    /// <summary>Ids come from our own generator, but never let an id escape the reports folder.</summary>
    private static string SafeId(string id) => new(id.Where(char.IsLetterOrDigit).ToArray());

    private static async Task WriteAtomicAsync(string path, string content)
    {
        string temp = path + ".tmp";
        await File.WriteAllTextAsync(temp, content).ConfigureAwait(false);
        File.Move(temp, path, overwrite: true);
    }
}
