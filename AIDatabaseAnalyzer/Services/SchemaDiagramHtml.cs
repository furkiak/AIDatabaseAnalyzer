using System.Text.Json;

namespace AIDatabaseAnalyzer.Services;

public static class SchemaDiagramHtml
{
    public static string Create(string schemaJson)
    {
        using var source = JsonDocument.Parse(schemaJson);
        var tables = new List<object>();
        if (source.RootElement.TryGetProperty("Tables", out var tableJson) && tableJson.ValueKind == JsonValueKind.Object)
        {
            foreach (var table in tableJson.EnumerateObject())
            {
                var columns = new List<object>();
                if (table.Value.ValueKind == JsonValueKind.Array)
                {
                    foreach (var column in table.Value.EnumerateArray())
                    {
                        columns.Add(new
                        {
                            name = GetText(column, "Column"),
                            type = GetText(column, "Type")
                        });
                    }
                }
                tables.Add(new { name = table.Name, columns });
            }
        }

        var relationships = new List<object>();
        if (source.RootElement.TryGetProperty("ForeignKeys", out var fkJson) && fkJson.ValueKind == JsonValueKind.Array)
        {
            foreach (var fk in fkJson.EnumerateArray())
                relationships.Add(new { from = GetText(fk, "From"), to = GetText(fk, "To"), name = GetText(fk, "Name") });
        }

        var data = JsonSerializer.Serialize(new { tables, relationships });
        return Template.Replace("@@SCHEMA_DATA@@", data, StringComparison.Ordinal);
    }

    private static string GetText(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? "" : "";

    private const string Template = """
        <!doctype html>
        <html lang="en">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1">
          <meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'; script-src 'unsafe-inline'">
          <title>Schema map</title>
          <style>
            :root { color-scheme: light; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif; }
            * { box-sizing: border-box; }
            body { margin: 0; color: #163452; background: radial-gradient(circle at 14% 10%, #deefff, #f4f8fc 42%, #edf3fa); }
            #toolbar { position: fixed; top: 18px; left: 18px; right: 18px; z-index: 10; display: flex; align-items: center; gap: 8px; padding: 11px 14px; border: 1px solid #d0e0ef; border-radius: 20px; background: rgba(255,255,255,.9); box-shadow: 0 16px 36px rgba(29,69,112,.1); }
            #toolbar strong { margin-right: auto; font-size: 14px; }
            #toolbar span { color: #57728e; font-size: 12px; }
            button { border: 1px solid #cfdded; border-radius: 11px; background: white; color: #1c4f80; min-width: 36px; height: 34px; font-weight: 700; cursor: pointer; }
            button:hover, button:focus-visible { background: #e6f2ff; outline: 2px solid #94c6ff; }
            #viewport { position: fixed; inset: 0; overflow: hidden; cursor: grab; touch-action: none; }
            #viewport.dragging { cursor: grabbing; }
            #scene { position: absolute; transform-origin: 0 0; width: max-content; }
            #cards { position: relative; display: grid; grid-template-columns: repeat(3, 284px); align-items: start; gap: 72px 76px; padding: 112px 72px 70px; }
            #links { position: absolute; inset: 0; overflow: visible; pointer-events: none; }
            .card { z-index: 1; position: relative; width: 284px; height: auto; min-height: 108px; padding: 0; text-align: left; border: 1px solid #c4dbee; border-radius: 20px; background: rgba(255,255,255,.94); box-shadow: 0 12px 28px rgba(28,75,124,.09); overflow: hidden; }
            .card:hover, .card:focus-visible { border-color: #71a7df; box-shadow: 0 16px 30px rgba(28,75,124,.17); transform: translateY(-2px); }
            .card-title { display: block; padding: 15px 17px; background: linear-gradient(110deg, #153d68, #2568a7); color: white; font-size: 14px; font-weight: 700; overflow-wrap: anywhere; }
            .column { display: flex; justify-content: space-between; gap: 12px; padding: 7px 17px; border-top: 1px solid #edf2f7; font-size: 12px; }
            .column-name { font-weight: 600; overflow-wrap: anywhere; }
            .column-type { color: #627d98; white-space: nowrap; }
            .empty { padding: 18px; color: #67819c; font-size: 13px; }
            #hint { position: fixed; bottom: 18px; left: 18px; max-width: min(410px, calc(100vw - 36px)); padding: 10px 14px; background: rgba(255,255,255,.88); border: 1px solid #d1e0ee; border-radius: 12px; font-size: 12px; color: #56718c; }
            @media (max-width: 740px) { #cards { grid-template-columns: repeat(2, 284px); } #toolbar span { display: none; } }
            @media (max-width: 440px) { #cards { grid-template-columns: 284px; } }
          </style>
        </head>
        <body>
          <div id="viewport" aria-label="Interactive database schema map">
            <div id="scene"><svg id="links" aria-hidden="true"></svg><div id="cards"></div></div>
          </div>
          <div id="toolbar"><strong>Schema map</strong><span id="count"></span><button type="button" id="minus" aria-label="Zoom out">−</button><button type="button" id="plus" aria-label="Zoom in">+</button><button type="button" id="reset">Fit</button></div>
          <div id="hint">Solid lines show declared foreign keys. Select a table to inspect locally available sample rows.</div>
          <script>
            const data = @@SCHEMA_DATA@@;
            const cards = document.getElementById('cards');
            const links = document.getElementById('links');
            const viewport = document.getElementById('viewport');
            const scene = document.getElementById('scene');
            const byName = new Map();
            let scale = 1, x = 0, y = 0, pointer = null, moved = false;
            function textNode(tag, value, css) {
              const el = document.createElement(tag);
              if (css) el.className = css;
              el.textContent = value;
              return el;
            }
            for (const table of data.tables) {
              const card = document.createElement('button');
              card.type = 'button'; card.className = 'card';
              card.append(textNode('span', table.name, 'card-title'));
              for (const column of table.columns) {
                const row = document.createElement('span'); row.className = 'column';
                row.append(textNode('span', column.name, 'column-name'), textNode('span', column.type, 'column-type'));
                card.append(row);
              }
              if (!table.columns.length) card.append(textNode('span', 'No column metadata', 'empty'));
              card.addEventListener('click', () => { if (!moved) location.href = 'app://tableclick?name=' + encodeURIComponent(table.name); });
              cards.append(card); byName.set(table.name, card);
            }
            const visibleRelationships = data.relationships.filter(edge => byName.has(edge.from) && byName.has(edge.to));
            document.getElementById('count').textContent = data.tables.length + ' tables · ' + visibleRelationships.length + ' displayed FKs';
            function drawLinks() {
              links.replaceChildren();
              links.setAttribute('width', cards.scrollWidth); links.setAttribute('height', cards.scrollHeight);
              for (const relationship of visibleRelationships) {
                const from = byName.get(relationship.from), to = byName.get(relationship.to);
                if (!from || !to) continue;
                const line = document.createElementNS('http://www.w3.org/2000/svg', 'line');
                line.setAttribute('x1', from.offsetLeft + from.offsetWidth / 2);
                line.setAttribute('y1', from.offsetTop + from.offsetHeight / 2);
                line.setAttribute('x2', to.offsetLeft + to.offsetWidth / 2);
                line.setAttribute('y2', to.offsetTop + to.offsetHeight / 2);
                line.setAttribute('stroke', '#66a9db'); line.setAttribute('stroke-width', '2');
                line.setAttribute('opacity', '.8');
                links.append(line);
              }
            }
            function apply() { scene.style.transform = 'translate(' + x + 'px,' + y + 'px) scale(' + scale + ')'; }
            function fit() {
              const w = cards.scrollWidth || 1, h = cards.scrollHeight || 1;
              scale = Math.min(1, Math.max(.2, Math.min(viewport.clientWidth / w, viewport.clientHeight / h)));
              x = Math.max(8, (viewport.clientWidth - w * scale) / 2);
              y = Math.max(64, (viewport.clientHeight - h * scale) / 2);
              apply(); drawLinks();
            }
            function zoom(factor) { scale = Math.max(.2, Math.min(2.4, scale * factor)); apply(); }
            document.getElementById('plus').addEventListener('click', () => zoom(1.2));
            document.getElementById('minus').addEventListener('click', () => zoom(1 / 1.2));
            document.getElementById('reset').addEventListener('click', fit);
            viewport.addEventListener('pointerdown', e => { pointer = { id: e.pointerId, px: e.clientX, py: e.clientY }; moved = false; viewport.classList.add('dragging'); });
            viewport.addEventListener('pointermove', e => {
              if (!pointer || pointer.id !== e.pointerId) return;
              const dx = e.clientX - pointer.px, dy = e.clientY - pointer.py;
              if (Math.abs(dx) + Math.abs(dy) > 3) moved = true;
              x += dx; y += dy; pointer.px = e.clientX; pointer.py = e.clientY; apply();
            });
            for (const event of ['pointerup', 'pointercancel', 'pointerleave']) viewport.addEventListener(event, () => { pointer = null; viewport.classList.remove('dragging'); setTimeout(() => { moved = false; }, 0); });
            viewport.addEventListener('wheel', e => { e.preventDefault(); zoom(e.deltaY < 0 ? 1.1 : 1 / 1.1); }, { passive: false });
            addEventListener('resize', fit);
            fit();
          </script>
        </body>
        </html>
        """;
}
