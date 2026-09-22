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
    public bool IncludeSampleData { get; set; } = false;
}

public interface IDbSchemaService
{
    Task<List<string>> GetDatabasesAsync(DbEngine engine, string connString);
    Task<List<string>> GetTablesAsync(DbEngine engine, string connString);
    Task<string> ExtractSchemaAsJsonAsync(DbEngine engine, string connString, SchemaFilterConfig filter);
}
