using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace PaperBatchDownloader;

public sealed partial class MainForm
{
    private readonly CheckBox chkSaveCheckbox = new()
    {
        Text = "Checkbox Save your job and exit.: chờ 5 giây, click và thử lại đến khi được chọn", Checked = true, AutoSize = true
    };
    private readonly System.Windows.Forms.Timer saveCheckboxTimer = new() { Interval = 1000 };
    private bool saveCheckboxBusy;
    private string? saveCheckboxPendingToken;
    private string? saveCheckboxPendingSource;
    private long? saveCheckboxDetectedAt;

    private sealed class SaveCheckboxTarget
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public string Token { get; set; } = "";
        public bool Checked { get; set; }
    }

    private void InitializeSaveCheckbox(TableLayoutPanel top)
    {
        var row = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        row.Controls.Add(chkSaveCheckbox);
        top.RowCount = 6;
        top.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        top.Controls.Add(row, 0, 4);
        top.SetColumnSpan(row, 4);
        saveCheckboxTimer.Tick += async (_, _) => await CheckSaveCheckboxAsync();
        chkSaveCheckbox.CheckedChanged += (_, _) => ResetSaveCheckboxWait();
        saveCheckboxTimer.Start();
    }

    private void ResetSaveCheckboxWait()
    {
        saveCheckboxPendingToken = null;
        saveCheckboxPendingSource = null;
        saveCheckboxDetectedAt = null;
    }

    private void ScheduleSaveCheckbox(SaveCheckboxTarget target, string source)
    {
        saveCheckboxPendingToken = target.Token;
        saveCheckboxPendingSource = source;
        saveCheckboxDetectedAt = Stopwatch.GetTimestamp();
    }

    private async Task CheckSaveCheckboxAsync()
    {
        if (saveCheckboxBusy || IsDisposed || Disposing || !initialized
            || !chkSaveCheckbox.Checked || logoutRequested || logoutInProgress || movingQueue
            || web.CoreWebView2 is null || !web.Visible) return;
        saveCheckboxBusy = true;
        try
        {
            string source = web.CoreWebView2.Source;
            string raw = await web.CoreWebView2.ExecuteScriptAsync(SaveCheckboxScript);
            if (IsDisposed || Disposing || !chkSaveCheckbox.Checked || web.CoreWebView2.Source != source) return;
            var target = JsonSerializer.Deserialize<SaveCheckboxTarget>(raw, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (target is null || target.Checked || target.Width <= 0 || target.Height <= 0 || target.Token.Length == 0)
            {
                ResetSaveCheckboxWait();
                return;
            }
            if (saveCheckboxPendingToken != target.Token || saveCheckboxPendingSource != source || saveCheckboxDetectedAt is null)
            {
                ScheduleSaveCheckbox(target, source);
                return;
            }
            if (Stopwatch.GetElapsedTime(saveCheckboxDetectedAt.Value).TotalSeconds < 5) return;
            // A failed click can retry only after another complete five-second interval.
            ScheduleSaveCheckbox(target, source);
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
            web.Focus();
            Point screen = web.PointToScreen(new Point(
                (int)Math.Round(target.X * web.ClientSize.Width / target.Width),
                (int)Math.Round(target.Y * web.ClientSize.Height / target.Height)));
            if (!MoveSaveCheckboxPointer(screen.X, screen.Y))
                throw new InvalidOperationException("Cannot move the pointer to the checkbox.");
            string pressed = JsonSerializer.Serialize(new { type = "mousePressed", x = target.X, y = target.Y, button = "left", clickCount = 1 });
            string released = JsonSerializer.Serialize(new { type = "mouseReleased", x = target.X, y = target.Y, button = "left", clickCount = 1 });
            try { await web.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent", pressed); }
            finally { await web.CoreWebView2.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent", released); }
            if (IsDisposed || Disposing || !chkSaveCheckbox.Checked || web.CoreWebView2.Source != source)
            {
                ResetSaveCheckboxWait();
                return;
            }
            string afterRaw = await web.CoreWebView2.ExecuteScriptAsync(SaveCheckboxScript);
            var after = JsonSerializer.Deserialize<SaveCheckboxTarget>(afterRaw, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            bool selected = after?.Token == target.Token && after.Checked;
            LogDiagnostic("save-checkbox-clicked", null, new { selected, retryAfterSeconds = selected ? (int?)null : 5 });
            if (selected || after is null || after.Token != target.Token) ResetSaveCheckboxWait();
            else ScheduleSaveCheckbox(after, source);
        }
        catch (Exception ex)
        {
            if (saveCheckboxPendingToken is not null) saveCheckboxDetectedAt = Stopwatch.GetTimestamp();
            LogDiagnostic("save-checkbox-error", null, ex.Message);
        }
        finally { saveCheckboxBusy = false; }
    }

    [DllImport("user32.dll", EntryPoint = "SetCursorPos", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveSaveCheckboxPointer(int x, int y);

    private const string SaveCheckboxScript = """
        (() => {
          const normalize = s => (s || '').replace(/\s+/g, ' ').trim();
          const phrase = s => /^Save your job and exit\.$/i.test(normalize(s));
          const visible = el => {
            const r = el.getBoundingClientRect(), s = el.ownerDocument.defaultView.getComputedStyle(el);
            return r.width > 0 && r.height > 0 && s.visibility !== 'hidden' && s.display !== 'none';
          };
          const search = (doc, ox, oy, sx, sy) => {
            const matches = new Set();
            for (const el of doc.querySelectorAll('label, span, p, div')) {
              if (!phrase(el.innerText) || !visible(el)) continue;
              let boxes = [];
              if (el.tagName === 'LABEL' && el.control) boxes = [el.control];
              else boxes = Array.from(el.querySelectorAll('input[type="checkbox"]'));
              if (!boxes.length && el.previousElementSibling?.matches('input[type="checkbox"]'))
                boxes = [el.previousElementSibling];
              if (!boxes.length && el.parentElement && phrase(el.parentElement.innerText))
                boxes = Array.from(el.parentElement.querySelectorAll('input[type="checkbox"]'));
              if (boxes.length !== 1) continue;
              const box = boxes[0];
              if (box.type !== 'checkbox' || box.disabled || !visible(box)) continue;
              const walker = doc.createTreeWalker(el, 4);
              let node, phraseNode = null;
              while (node = walker.nextNode()) if (phrase(node.textContent)) { phraseNode = node; break; }
              if (!phraseNode || !(box.compareDocumentPosition(phraseNode) & 4)) continue;
              matches.add(box);
            }
            if (matches.size === 1) {
              const box = Array.from(matches)[0];
              if (!box.checked) box.scrollIntoView({block: 'center', inline: 'nearest', behavior: 'instant'});
              const r = box.getBoundingClientRect(), x = r.left + r.width / 2, y = r.top + r.height / 2;
              if (doc.elementFromPoint(x, y) !== box) return null;
              if (!box.dataset.pbdSaveCheckboxToken)
                box.dataset.pbdSaveCheckboxToken = Date.now().toString(36) + Math.random().toString(36).slice(2);
              return {x: ox + x * sx, y: oy + y * sy, token: box.dataset.pbdSaveCheckboxToken, checked: box.checked};
            }
            if (matches.size > 1) return null;
            for (const frame of doc.querySelectorAll('iframe')) {
              if (!visible(frame)) continue;
              try {
                const child = frame.contentDocument;
                if (!child) continue;
                const r = frame.getBoundingClientRect();
                const fx = r.width / frame.offsetWidth, fy = r.height / frame.offsetHeight;
                const result = search(child, ox + (r.left + frame.clientLeft * fx) * sx,
                  oy + (r.top + frame.clientTop * fy) * sy, sx * fx, sy * fy);
                if (result) return result;
              } catch {}
            }
            return null;
          };
          const result = search(document, 0, 0, 1, 1);
          return result ? {...result, width: innerWidth, height: innerHeight} : null;
        })()
        """;
}
