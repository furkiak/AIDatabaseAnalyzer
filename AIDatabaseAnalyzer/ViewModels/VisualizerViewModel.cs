using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AIDatabaseAnalyzer.ViewModels;

public partial class VisualizerViewModel : ObservableObject
{
    [ObservableProperty] private string htmlSource;
    [ObservableProperty] private string fullJsonSchema; 
    private readonly string _mermaidCode;

    public VisualizerViewModel(string mermaidCode, string fullJsonSchema)
    {
        _mermaidCode = mermaidCode;
        FullJsonSchema = fullJsonSchema;
        LoadHtmlContent();
    }

    private void LoadHtmlContent()
    {
        HtmlSource = $@"
<!DOCTYPE html>
<html>
<head>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <script src=""https://cdn.jsdelivr.net/npm/mermaid/dist/mermaid.min.js""></script>
    <script src=""https://cdn.jsdelivr.net/npm/svg-pan-zoom@3.6.1/dist/svg-pan-zoom.min.js""></script>
    <style>
        body {{ 
            margin: 0; padding: 0; overflow: hidden; 
            background-color: #f8f9fa; 
            font-family: 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; 
        }}
        #wrapper {{ width: 100vw; height: 100vh; position: relative; }}
        
        /* SOL ÜST LEJANT (LEGEND) */
        #legend {{
            position: absolute; top: 20px; left: 20px; z-index: 1000;
            background: white; padding: 12px 18px;
            border-radius: 12px; border: 1px solid #e0e0e0;
            box-shadow: 0 4px 15px rgba(0,0,0,0.08);
        }}
        .legend-title {{ font-weight: 800; font-size: 14px; margin-bottom: 10px; color: #2c3e50; border-bottom: 1px solid #eee; padding-bottom: 5px; }}
        .legend-item {{ display: flex; align-items: center; margin-bottom: 6px; font-size: 12px; color: #444; }}
        .dot {{ width: 14px; height: 14px; border-radius: 4px; margin-right: 10px; }}
        
        /* KONTROL BUTONLARI (SAĞ ALT) */
        #controls {{ position: absolute; bottom: 30px; right: 30px; z-index: 1000; display: flex; gap: 8px; }}
        .btn {{ 
            width: 45px; height: 45px; background: #2d3436; color: white; border: none; 
            cursor: pointer; border-radius: 50%; font-size: 18px; font-weight: bold;
            box-shadow: 0 4px 10px rgba(0,0,0,0.3); transition: transform 0.2s;
        }}
        .btn:hover {{ transform: scale(1.1); background: #000; }}
        .btn-reset {{ width: auto; border-radius: 25px; padding: 0 15px; font-size: 14px; }}

        #diagram-container {{ width: 100%; height: 100%; cursor: grab; }}
        #diagram-container:active {{ cursor: grabbing; }}
        
        /* MERMAID CUSTOM OVERRIDES */
        .node rect {{ stroke-width: 2px !important; }}
        .node:hover rect {{ filter: brightness(0.9); }}
    </style>
</head>
<body>
    <div id=""wrapper"">
        <div id=""legend"">
            <div class='legend-title'>Schema Legend</div>
            <div class='legend-item'><div class='dot' style='background:#E3F2FD; border:1px solid #0D6EFD;'></div><span>Table (Primary)</span></div>
            <div class='legend-item'><div class='dot' style='background:#F1F8E9; border:1px solid #4CAF50;'></div><span>View (Virtual)</span></div>
            <div class='legend-item'><div class='dot' style='background:#ffffff; border:1px solid #FF9800;'></div><span>Relationship (FK)</span></div>
            <div style='margin-top:8px; font-size:10px; color:#999; font-style:italic;'>💡 Click on table to see sample data</div>
        </div>

        <div id=""controls"">
            <button class=""btn"" onclick=""zoomIn()"">＋</button>
            <button class=""btn"" onclick=""zoomOut()"">－</button>
            <button class=""btn btn-reset"" onclick=""resetZoom()"">⟲ Reset View</button>
        </div>

        <div id=""diagram-container"">
            <pre class=""mermaid"">
                {_mermaidCode}
            </pre>
        </div>
    </div>

    <script>
        var panZoomInstance;

        // Mermaid Konfigürasyonu
        mermaid.initialize({{ 
            startOnLoad: true, 
            theme: 'base',
            securityLevel: 'loose',
            themeVariables: {{
                'primaryColor': '#E3F2FD',
                'primaryTextColor': '#2c3e50',
                'primaryBorderColor': '#0D6EFD',
                'lineColor': '#FF9800', 
                'secondaryColor': '#F1F8E9', 
                'tertiaryColor': '#ffffff',
                'fontFamily': 'Segoe UI'
            }},
            er: {{
                useMaxWidth: false,
                layoutDirection: 'TB',
                entityPadding: 20
            }}
        }});

        function initZoom() {{
            const svgElement = document.querySelector('svg');
            if (svgElement) {{
                panZoomInstance = svgPanZoom(svgElement, {{
                    zoomEnabled: true,
                    controlIconsEnabled: false,
                    fit: true,
                    center: true,
                    minZoom: 0.05,
                    maxZoom: 20,
                    mouseWheelZoomEnabled: true
                }});
            }} else {{
                setTimeout(initZoom, 200);
            }}
        }}

        function setupTableClicks() {{
            const nodes = document.querySelectorAll('.node');
            if(nodes.length === 0) {{ setTimeout(setupTableClicks, 500); return; }}

            nodes.forEach(node => {{
                node.style.cursor = 'pointer';
                node.onclick = function() {{
                    const idParts = this.id.split('-');
                    const tableName = idParts[1] || this.textContent.trim().split(' ')[0];
                    window.location.href = 'app://tableclick?name=' + tableName;
                }};
            }});
        }}

        window.onload = () => {{
            initZoom();
            setupTableClicks();
        }};

        function zoomIn() {{ if(panZoomInstance) panZoomInstance.zoomIn(); }}
        function zoomOut() {{ if(panZoomInstance) panZoomInstance.zoomOut(); }}
        function resetZoom() {{ 
            if(panZoomInstance) {{
                panZoomInstance.reset(); 
                panZoomInstance.fit(); 
                panZoomInstance.center(); 
            }}
        }}
    </script>
</body>
</html>";

    }
    [RelayCommand]
    private async Task CloseAsync() => await Shell.Current.Navigation.PopModalAsync();
}