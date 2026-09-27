# Production schema-map preview

This .NET 9 console helper links the application's `SchemaDiagramHtml.cs`; it does not maintain a separate diagram implementation. Pass an owned JSON input and a new HTML output path. Existing output files are refused.

```powershell
dotnet run --project tools/SchemaMapPreview -- synthetic-schema.json new-preview.html
```

Open the resulting file locally for layout and keyboard checks. Table activation uses the app's `app://tableclick` command and is only handled inside the real MAUI WebView; do not activate it in an ordinary browser.
