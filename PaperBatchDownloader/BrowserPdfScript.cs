using System.Text.Json;

namespace PaperBatchDownloader;

internal static class BrowserPdfScript
{
    public static string Create(string url, string fileName, int timeoutSeconds) => $$"""
        (async () => {
            const controller = new AbortController();
            const timeout = setTimeout(() => controller.abort(), {{timeoutSeconds * 1000}});
            const visited = new Set();
            async function getPdf(url, depth) {
                if (depth > 3 || visited.has(url)) throw new Error('No PDF in IEEE stamp page');
                visited.add(url);
                const r = await fetch(url, { credentials: 'include', redirect: 'follow',
                    signal: controller.signal });
                if (!r.ok) throw new Error('HTTP ' + r.status);
                const blob = await r.blob();
                if (await blob.slice(0, 5).text() === '%PDF-') return blob;

                // stamp.jsp is an HTML wrapper, not the PDF itself. Follow the actual
                // document exposed by IEEE, retaining the current browser session.
                const source = new URL(r.url);
                if (source.hostname !== 'ieeexplore.ieee.org' ||
                    !/^\/stamp\/stamp(?:pdf)?\.jsp$/i.test(source.pathname))
                    throw new Error('NOT_PDF:' + (r.headers.get('content-type') || 'unknown'));
                const doc = new DOMParser().parseFromString(await blob.text(), 'text/html');
                const nodes = doc.querySelectorAll('iframe[src],embed[src],object[data],meta[name="citation_pdf_url"],a[href]');
                for (const el of nodes) {
                    const value = el.getAttribute('src') || el.getAttribute('data') ||
                        el.getAttribute('content') || el.getAttribute('href');
                    if (!value) continue;
                    const next = new URL(value, r.url);
                    if (next.hostname !== source.hostname || next.protocol !== 'https:' ||
                        !(/\.pdf$/i.test(next.pathname) || /\/(ielx\w*|stamp)\//i.test(next.pathname))) continue;
                    try { return await getPdf(next.href, depth + 1); }
                    catch (e) { if (controller.signal.aborted) throw e; }
                }
                throw new Error('IEEE stamp page has no accessible PDF; check login/access');
            }
            try {
                const blob = await getPdf({{JsonSerializer.Serialize(url)}}, 0);
                const u = URL.createObjectURL(blob);
                const a = document.createElement('a');
                a.href = u;
                a.download = {{JsonSerializer.Serialize(fileName)}};
                a.style.display = 'none';
                document.body.appendChild(a);
                a.click();
                setTimeout(() => { URL.revokeObjectURL(u); a.remove(); }, 30000);
                return 'OK';
            } catch (e) {
                return 'ERR:' + (e && e.message ? e.message : String(e));
            } finally { clearTimeout(timeout); }
        })()
        """;
}
