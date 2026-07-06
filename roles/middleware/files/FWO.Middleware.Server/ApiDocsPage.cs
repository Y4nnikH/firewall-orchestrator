using System.Net;
using System.Text.Json;

namespace FWO.Middleware.Server;

/// <summary>
/// Renders the local API documentation page for browser debugging.
/// </summary>
public static class ApiDocsPage
{
    /// <summary>
    /// Creates a self-contained HTML page that reads the generated OpenAPI document.
    /// </summary>
    public static string Render(string openApiDocumentPath)
    {
        if (string.IsNullOrWhiteSpace(openApiDocumentPath))
        {
            throw new ArgumentException("OpenAPI document path must not be empty.", nameof(openApiDocumentPath));
        }

        string encodedOpenApiDocumentPath = WebUtility.HtmlEncode(openApiDocumentPath);
        string jsonOpenApiDocumentPath = JsonSerializer.Serialize(openApiDocumentPath);

        return $$"""
            <!doctype html>
            <html lang="en">
            <head>
                <meta charset="utf-8">
                <meta name="viewport" content="width=device-width, initial-scale=1">
                <title>FWO Middleware API Documentation</title>
                <style>
                    :root { color-scheme: light dark; --border: #d0d7de; --muted: #57606a; --bg: #f6f8fa; --accent: #0969da; }
                    body { margin: 0; font-family: Arial, Helvetica, sans-serif; line-height: 1.45; color: CanvasText; background: Canvas; }
                    header { padding: 24px 32px 18px; border-bottom: 1px solid var(--border); background: var(--bg); }
                    main { max-width: 1180px; padding: 24px 32px 40px; }
                    h1 { margin: 0 0 8px; font-size: 28px; }
                    h2 { margin: 28px 0 12px; font-size: 20px; }
                    a { color: var(--accent); }
                    input { width: min(620px, 100%); padding: 10px 12px; border: 1px solid var(--border); border-radius: 6px; font-size: 15px; }
                    .muted { color: var(--muted); }
                    .endpoint { padding: 12px 0; border-bottom: 1px solid var(--border); }
                    .method { display: inline-block; min-width: 56px; font-weight: 700; text-transform: uppercase; }
                    .path { font-family: Consolas, "Liberation Mono", monospace; overflow-wrap: anywhere; }
                    .summary { margin: 5px 0 0 60px; color: var(--muted); }
                    .error { color: #cf222e; }
                </style>
            </head>
            <body>
                <header>
                    <h1>FWO Middleware API Documentation</h1>
                    <div class="muted">Generated from <a href="{{encodedOpenApiDocumentPath}}">{{encodedOpenApiDocumentPath}}</a></div>
                </header>
                <main>
                    <input id="filter" type="search" placeholder="Filter endpoints" autocomplete="off">
                    <div id="status" class="muted">Loading OpenAPI document...</div>
                    <section id="endpoints"></section>
                </main>
                <script>
                    const documentUrl = {{jsonOpenApiDocumentPath}};
                    const filter = document.getElementById('filter');
                    const status = document.getElementById('status');
                    const endpoints = document.getElementById('endpoints');
                    let operations = [];

                    function escapeHtml(value) {
                        return String(value ?? '').replace(/[&<>"']/g, char => ({
                            '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'
                        }[char]));
                    }

                    function render() {
                        const query = filter.value.trim().toLowerCase();
                        const visible = operations.filter(operation =>
                            operation.path.toLowerCase().includes(query) ||
                            operation.method.toLowerCase().includes(query) ||
                            operation.summary.toLowerCase().includes(query));

                        status.textContent = `${visible.length} of ${operations.length} endpoints`;
                        endpoints.innerHTML = visible.map(operation => `
                            <article class="endpoint">
                                <span class="method">${escapeHtml(operation.method)}</span>
                                <span class="path">${escapeHtml(operation.path)}</span>
                                <p class="summary">${escapeHtml(operation.summary)}</p>
                            </article>
                        `).join('');
                    }

                    fetch(documentUrl)
                        .then(response => response.ok ? response.json() : Promise.reject(new Error(`${response.status} ${response.statusText}`)))
                        .then(document => {
                            operations = Object.entries(document.paths ?? {}).flatMap(([path, pathItem]) =>
                                Object.entries(pathItem)
                                    .filter(([, operation]) => operation && operation.responses)
                                    .map(([method, operation]) => ({
                                        path,
                                        method,
                                        summary: operation.summary || operation.description || ''
                                    })));
                            render();
                        })
                        .catch(error => {
                            status.innerHTML = `<span class="error">Could not load OpenAPI document: ${escapeHtml(error.message)}</span>`;
                        });

                    filter.addEventListener('input', render);
                </script>
            </body>
            </html>
            """;
    }
}
