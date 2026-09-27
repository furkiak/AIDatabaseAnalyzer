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
            :root { color-scheme: light; font: 16px -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif; --text:#163452; --secondary:#4d647d; --surface:#fff; --border:#637b94; --focus:#245f93; --hover:#e6f2ff; --background:radial-gradient(circle at 14% 10%, #deefff, #f4f8fc 42%, #edf3fa); }
            :root[data-theme="dark"] { color-scheme:dark; --text:#e1efff; --secondary:#b7cde2; --surface:#172c42; --border:#91aac3; --focus:#aeccff; --hover:#254662; --background:radial-gradient(circle at 14% 10%, #224767, #0c1c2c 42%, #172b40); }
            :root[data-translucent="true"] { --surface:rgba(255,255,255,.94); }
            :root[data-theme="dark"][data-translucent="true"] { --surface:rgba(23,44,66,.94); }
            * { box-sizing: border-box; }
            body { margin: 0; color:var(--text); background:var(--background); }
            #toolbar { position: fixed; top: 18px; left: 18px; right: 18px; z-index: 10; display: flex; flex-wrap:wrap; align-items: center; gap: 8px; padding: 11px 14px; border: 1px solid var(--border); border-radius: 20px; background:var(--surface); box-shadow: 0 16px 36px rgba(29,69,112,.1); }
            #toolbar strong { margin-right: auto; font-size:.875rem; }
            #toolbar span { color:var(--secondary); font-size:.75rem; }
            button { border: 1px solid var(--border); border-radius: 11px; background:var(--surface); color:var(--text); min-width:44px; min-height:44px; padding:.5rem .65rem; font:inherit; font-weight:700; cursor:pointer; }
            button:hover { background:var(--hover); }
            button:focus-visible, #viewport:focus-visible { outline:3px solid var(--focus); outline-offset:3px; }
            button:disabled { cursor:default; opacity:.6; }
            #viewport { position: fixed; inset: 0; overflow: hidden; cursor: grab; touch-action: none; }
            #viewport.dragging { cursor: grabbing; }
            #scene { position: absolute; transform-origin: 0 0; width: max-content; }
            #cards { position: relative; display: grid; grid-template-columns: repeat(3, 284px); align-items: start; gap: 72px 76px; padding: 112px 72px 70px; }
            #links { position: absolute; inset: 0; overflow: visible; pointer-events: none; }
            .card { z-index: 1; position: relative; width: 284px; height: auto; min-height: 108px; padding: 0; text-align: left; border: 1px solid var(--border); border-radius: 20px; background:var(--surface); box-shadow: 0 12px 28px rgba(28,75,124,.09); overflow: hidden; }
            .card:hover, .card:focus-visible { box-shadow: 0 16px 30px rgba(28,75,124,.17); }
            .card-title { display: block; padding: 15px 17px; background: linear-gradient(110deg, #153d68, #2568a7); color: white; font-size:.875rem; font-weight:700; overflow-wrap:anywhere; }
            .column { display:flex; flex-wrap:wrap; justify-content:space-between; gap:12px; padding:7px 17px; border-top:1px solid var(--border); font-size:.75rem; }
            .column-name { font-weight: 600; overflow-wrap: anywhere; }
            .column-type { color:var(--secondary); overflow-wrap:anywhere; }
            .empty { display:block; padding:18px; color:var(--secondary); font-size:.8125rem; }
            .relationship { display:none; padding:7px 17px; border-top:1px solid var(--border); font-size:.75rem; color:var(--secondary); overflow-wrap:anywhere; }
            #hint { position:fixed; bottom:18px; left:18px; max-width:min(580px, calc(100vw - 36px)); padding:10px 14px; background:var(--surface); border:1px solid var(--border); border-radius:12px; font-size:.75rem; color:var(--secondary); }
            body.list-view #viewport { position:static; overflow:visible; cursor:auto; touch-action:auto; }
            body.list-view #toolbar { position:static; margin:18px; }
            body.list-view #hint { position:static; max-width:none; margin:18px; }
            body.list-view #plus, body.list-view #minus, body.list-view #reset { display:none; }
            body.list-view #scene { position:relative; width:100%; transform:none !important; }
            body.list-view #cards { display:grid; grid-template-columns:minmax(0,1fr); gap:18px; padding:18px 24px; }
            body.list-view .card { width:100%; max-width:850px; justify-self:center; }
            body.list-view #links { display:none; }
            body.list-view .relationship { display:block; }
            @media (max-width: 740px) { #cards { grid-template-columns: repeat(2, 284px); } #toolbar span { display: none; } }
            @media (max-width: 440px) { #cards { grid-template-columns: 284px; } }
          </style>
        </head>
        <body>
          <div id="toolbar"><strong>Schema map</strong><span id="count"></span><button type="button" id="minus" aria-label="Zoom out">−</button><button type="button" id="plus" aria-label="Zoom in">+</button><button type="button" id="reset">Fit</button><button type="button" id="view" aria-pressed="false">List view</button><button type="button" id="theme">Dark theme</button><button type="button" id="surface" aria-pressed="false">Translucent surfaces</button><button type="button" id="text-size">Text 100%</button></div>
          <div id="viewport" tabindex="0" role="group" aria-label="Interactive database schema map" aria-describedby="hint">
            <div id="scene"><svg id="links" aria-hidden="true"></svg><div id="cards"></div></div>
          </div>
          <div id="hint">Solid lines are declared foreign keys. Select a table for local samples. Map: arrow keys pan, Shift moves farther, Home fits. List view keeps text readable without diagram scaling.</div>
          <script>
            const data = @@SCHEMA_DATA@@;
            const cards = document.getElementById('cards');
            const links = document.getElementById('links');
            const viewport = document.getElementById('viewport');
            const scene = document.getElementById('scene');
            const byName = new Map();
            const mapHint = document.getElementById('hint').textContent;
            let scale = 1, x = 0, y = 0, pointer = null, moved = false, listView = false, textPercent = 100;
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
              for (const edge of data.relationships.filter(edge => edge.from === table.name || edge.to === table.name))
                card.append(textNode('span', edge.name + ': ' + edge.from + ' → ' + edge.to, 'relationship'));
              card.addEventListener('click', () => { if (!moved) location.href = 'app://tableclick?name=' + encodeURIComponent(table.name); });
              card.addEventListener('focus', () => ensureVisible(card));
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
                line.setAttribute('stroke', 'var(--focus)'); line.setAttribute('stroke-width', '2');
                line.setAttribute('opacity', '.8');
                links.append(line);
              }
            }
            function apply() { scene.style.transform = 'translate(' + x + 'px,' + y + 'px) scale(' + scale + ')'; }
            function layoutSpace() {
              const bottom = document.getElementById('toolbar').getBoundingClientRect().bottom + 18;
              const hintSpace = document.getElementById('hint').getBoundingClientRect().height + 36;
              document.body.style.setProperty('--toolbar-bottom', bottom + 'px');
              document.body.style.setProperty('--hint-space', hintSpace + 'px');
              return { top:bottom, bottom:Math.max(bottom + 44, viewport.clientHeight - hintSpace) };
            }
            function ensureVisible(card) {
              if (listView) { card.scrollIntoView({block:'nearest',inline:'nearest'}); return; }
              const space = layoutSpace(), box = card.getBoundingClientRect();
              if (box.left < 18) x += 18 - box.left;
              else if (box.right > viewport.clientWidth - 18) x -= box.right - viewport.clientWidth + 18;
              if (box.top < space.top) y += space.top - box.top;
              else if (box.bottom > space.bottom) y += box.height > space.bottom - space.top ? space.top - box.top : space.bottom - box.bottom;
              apply();
            }
            function fit() {
              const space = layoutSpace();
              if (listView) return;
              const w = cards.scrollWidth || 1, h = cards.scrollHeight || 1;
              scale = Math.min(1, Math.max(.2, Math.min(viewport.clientWidth / w, (space.bottom - space.top) / h)));
              x = Math.max(8, (viewport.clientWidth - w * scale) / 2);
              y = space.top + Math.max(0, (space.bottom - space.top - h * scale) / 2) - 90 * scale;
              apply(); drawLinks();
            }
            function zoom(factor) { if (listView) return; scale = Math.max(.2, Math.min(2.4, scale * factor)); apply(); }
            document.getElementById('plus').addEventListener('click', () => zoom(1.2));
            document.getElementById('minus').addEventListener('click', () => zoom(1 / 1.2));
            document.getElementById('reset').addEventListener('click', fit);
            document.getElementById('view').addEventListener('click', () => {
              listView = !listView; document.body.classList.toggle('list-view', listView);
              document.getElementById('view').setAttribute('aria-pressed', String(listView));
              document.getElementById('view').textContent = listView ? 'Map view' : 'List view';
              document.getElementById('hint').textContent = listView ? 'Scroll to read. Select a table for locally available samples.' : mapHint;
              for (const id of ['plus','minus','reset']) document.getElementById(id).disabled = listView;
              viewport.scrollTop = 0; viewport.scrollLeft = 0; window.scrollTo(0,0); fit();
            });
            document.getElementById('theme').addEventListener('click', () => {
              const dark = document.documentElement.dataset.theme !== 'dark';
              document.documentElement.dataset.theme = dark ? 'dark' : 'light';
              document.getElementById('theme').textContent = dark ? 'Light theme' : 'Dark theme';
              fit();
            });
            document.getElementById('surface').addEventListener('click', () => {
              const translucent = document.documentElement.dataset.translucent !== 'true';
              document.documentElement.dataset.translucent = String(translucent);
              document.getElementById('surface').setAttribute('aria-pressed', String(translucent));
              document.getElementById('surface').textContent = translucent ? 'Opaque surfaces' : 'Translucent surfaces';
              fit();
            });
            document.getElementById('text-size').addEventListener('click', () => {
              textPercent = textPercent === 100 ? 150 : textPercent === 150 ? 200 : 100;
              document.documentElement.style.fontSize = (16 * textPercent / 100) + 'px';
              document.getElementById('text-size').textContent = 'Text ' + textPercent + '%';
              fit();
            });
            viewport.addEventListener('keydown', e => {
              if (listView) return;
              const amount = e.shiftKey ? 108 : 36;
              if (e.key === 'Home') fit();
              else if (e.key === 'ArrowLeft') x += amount;
              else if (e.key === 'ArrowRight') x -= amount;
              else if (e.key === 'ArrowUp') y += amount;
              else if (e.key === 'ArrowDown') y -= amount;
              else return;
              e.preventDefault(); apply();
            });
            viewport.addEventListener('pointerdown', e => { if (listView) return; pointer = { id: e.pointerId, px: e.clientX, py: e.clientY }; moved = false; viewport.classList.add('dragging'); });
            viewport.addEventListener('pointermove', e => {
              if (!pointer || pointer.id !== e.pointerId) return;
              const dx = e.clientX - pointer.px, dy = e.clientY - pointer.py;
              if (Math.abs(dx) + Math.abs(dy) > 3) moved = true;
              x += dx; y += dy; pointer.px = e.clientX; pointer.py = e.clientY; apply();
            });
            for (const event of ['pointerup', 'pointercancel', 'pointerleave']) viewport.addEventListener(event, () => { pointer = null; viewport.classList.remove('dragging'); setTimeout(() => { moved = false; }, 0); });
            viewport.addEventListener('wheel', e => { if (listView) return; e.preventDefault(); zoom(e.deltaY < 0 ? 1.1 : 1 / 1.1); }, { passive: false });
            addEventListener('resize', fit);
            fit();
          </script>
        </body>
        </html>
        """;
}
