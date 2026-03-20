using System.Collections.Generic;
using System.Threading.Tasks;

namespace AIDatabaseAnalyzer.Services;
public class SchemaFilterConfig
{
    public List<string> SelectedTables { get; set; } = new();
    public bool IncludeViews { get; set; } = true;
    public bool IncludeIndexes { get; set; } = true;
    public bool IncludeTriggers { get; set; } = true;
    public bool IncludeSps { get; set; } = true;
    public bool IncludeConstraints { get; set; } = true;
    public bool IncludeSampleData { get; set; } = true;
}

public interface IDbSchemaService
{
    Task<List<string>> GetDatabasesAsync(DbSchemaService.DbEngine engine, string connString);
    Task<List<string>> GetTablesAsync(DbSchemaService.DbEngine engine, string connString);
    Task<string> ExtractSchemaAsJsonAsync(DbSchemaService.DbEngine engine, string connString, SchemaFilterConfig filter);
    string ConvertJsonToMermaid(string jsonSchema);
}