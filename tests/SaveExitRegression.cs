using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using PaperBatchDownloader;
using System.Diagnostics;
using System.Reflection;

internal static class SaveExitRegression
{
    public static async Task Run()
    {
        await RunCase();
    }

    private static async Task RunCase()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        string root = Path.Combine(Path.GetTempPath(), "SaveExitRegression-" + Guid.NewGuid().ToString("N"));
        using var app = new MainForm(Path.Combine(root, "profile")) { Opacity = 0, ShowInTaskbar = false };
        object? FieldOrNull(string name) => typeof(MainForm).GetField(name, flags)!.GetValue(app);
        object Field(string name) => typeof(MainForm).GetField(name, flags)!.GetValue(app)!;
        async Task CheckTick() => await (Task)typeof(MainForm).GetMethod("CheckSaveCheckboxAsync", flags)!.Invoke(app, null)!;
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
        async Task Until(Func<bool> ready)
        {
            var end = DateTime.UtcNow.AddSeconds(20);
            while (!ready()) { if (DateTime.UtcNow >= end) throw new TimeoutException("Save/exit fixture"); await Task.Delay(50); }
        }
        ((CheckBox)Field("chkSaveCheckbox")).Checked = false;
        ((System.Windows.Forms.Timer)Field("saveCheckboxTimer")).Stop();
        app.Show();
        await Until(() => (bool)Field("initialized"));
        var core = ((WebView2)Field("web")).CoreWebView2;
        async Task Load(string html)
        {
            var done = new TaskCompletionSource();
            void Loaded(object? s, CoreWebView2NavigationCompletedEventArgs e) => done.TrySetResult();
            core.NavigationCompleted += Loaded;
            core.NavigateToString("<html><body>" + html + "</body></html>");
            await done.Task.WaitAsync(TimeSpan.FromSeconds(15));
            core.NavigationCompleted -= Loaded;
        }
        string script = (string)typeof(MainForm).GetField("SaveCheckboxScript", BindingFlags.NonPublic | BindingFlags.Static)!.GetRawConstantValue()!;
        foreach (string html in new[]
        {
            "<p>Save your job and exit.</p>",
            "<label for=after>Save your job and exit.</label><input type=checkbox id=after>",
            "<label><input type=checkbox>Different action</label><p>Save your job and exit.</p>",
            "<label><input type=checkbox disabled>Save your job and exit.</label>",
            "<label style='display:none'><input type=checkbox>Save your job and exit.</label>",
            "<label><input type=checkbox>Save your job and exit.</label><label><input type=checkbox>Save your job and exit.</label>"
        })
        {
            await Load(html);
            Check(await core.ExecuteScriptAsync(script) == "null", "Unsafe checkbox match: " + html);
        }
        await Load("<label><input type=checkbox checked id=save>Save your job and exit.</label>");
        ((CheckBox)Field("chkSaveCheckbox")).Checked = true;
        await CheckTick();
        Check(FieldOrNull("saveCheckboxDetectedAt") is null, "Already selected checkbox was scheduled");
        await Load("<script>window.clicks=0;window.clickTimes=[];</script><input type=checkbox id=save onclick='window.clicks++;window.clickTimes.push(performance.now());if(window.clicks&lt;3)event.preventDefault()'><label for=save>Save your job and exit.</label>");
        // Set the handler directly to avoid HTML entity differences inside inline script attributes.
        await core.ExecuteScriptAsync("document.getElementById('save').onclick = function(e) { window.clicks++; window.clickTimes.push(performance.now()); if (window.clicks < 3) e.preventDefault(); };");
        var renderDeadline = DateTime.UtcNow.AddSeconds(10);
        while (await core.ExecuteScriptAsync(script) == "null")
        {
            Check(DateTime.UtcNow < renderDeadline, "Labeled checkbox was not found after rendering");
            await Task.Delay(50);
        }
        var elapsed = Stopwatch.StartNew();
        await CheckTick();
        await Task.Delay(1000);
        await CheckTick();
        Check(await core.ExecuteScriptAsync("window.clicks") == "0", "Clicked before the initial five-second wait");
        while (await core.ExecuteScriptAsync("document.getElementById('save').checked") != "true")
        {
            Check(elapsed.Elapsed.TotalSeconds < 25, "Retry did not select the checkbox");
            await Task.Delay(100);
            await CheckTick();
        }
        Check(elapsed.Elapsed.TotalSeconds >= 15, "Retries did not each wait five seconds");
        Check(await core.ExecuteScriptAsync("window.clicks") == "3", "Did not retry the two rejected clicks");
        Check(await core.ExecuteScriptAsync("window.clickTimes.slice(1).every((t,i)=>t-window.clickTimes[i]>=5000)") == "true", "Retry clicks were too close together");
        Check(!(bool)Field("paused") && !(bool)Field("manualLoginMode"), "Checkbox changed queue/login state");
        Check(((Button)Field("btnStart")).Enabled, "Checkbox disabled queue controls");
        Check(!((Label)Field("lblCurrent")).Text.Contains("Job saved"), "Removed notification still appeared");
        Check(typeof(MainForm).GetField("saveExitStarted", flags) is null, "Exit countdown still exists");
        await CheckTick(); await CheckTick();
        Check(await core.ExecuteScriptAsync("window.clicks") == "3", "Timer toggled a selected checkbox again");
        // A replacement node must receive a fresh five-second wait, even on the same URL.
        await core.ExecuteScriptAsync("document.getElementById('save').outerHTML = '<input type=checkbox id=save>';");
        await CheckTick();
        Check(FieldOrNull("saveCheckboxDetectedAt") is not null, "Replacement checkbox was not scheduled");
        ((CheckBox)Field("chkSaveCheckbox")).Checked = false;
        Check(FieldOrNull("saveCheckboxDetectedAt") is null, "Disabling did not cancel pending wait");
        await CheckTick();
        Check(await core.ExecuteScriptAsync("document.getElementById('save').checked") == "false", "Disabled feature still clicked");
        Check(!app.IsDisposed, "Checkbox closed the app");
        app.Close();
        Console.WriteLine("PASS: safe matching, real initial 5s wait, rejected clicks retried every 5s, stops when checked, replacement identity, cancel pending wait, no notification/exit/queue pause");
    }
}
