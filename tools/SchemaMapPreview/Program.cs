using AIDatabaseAnalyzer.Services;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: SchemaMapPreview <schema.json> <new-preview.html>");
    return 2;
}
var output = Path.GetFullPath(args[1]);
if (File.Exists(output))
{
    Console.Error.WriteLine("Preview exists; choose a new path.");
    return 2;
}
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
await File.WriteAllTextAsync(output, SchemaDiagramHtml.Create(await File.ReadAllTextAsync(args[0])));
Console.WriteLine(output);
return 0;
