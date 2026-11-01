using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using PaperBatchDownloader;
using System.Collections;
using System.Reflection;
using System.Text;
using System.Text.Json;

internal static class SharedQueueRegression
{
    public static async Task Run()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object Field(MainForm app, string name) => typeof(MainForm).GetField(name, flags)!.GetValue(app)!;
        object? Call(MainForm app, string name, params object[] args) => typeof(MainForm).GetMethod(name, flags)!.Invoke(app, args);
        List<DownloadJob> Jobs(MainForm app) => ((IEnumerable)Field(app,"jobs")).Cast<DownloadJob>().ToList();
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
        async Task Until(Func<bool> condition)
        {
            var end=DateTime.UtcNow.AddSeconds(30);
            while (!condition()) { if (DateTime.UtcNow>end) throw new TimeoutException("Shared queue fixture timed out"); await Task.Delay(80); }
        }
        IEnumerable<Control> Descendants(Control parent) => parent.Controls.Cast<Control>()
            .SelectMany(c=>new[] {c}.Concat(Descendants(c)));
        void Switch(MainForm app, int index)
        {
            ((ComboBox)Field(app,"cmbAccessMode")).SelectedIndex=index;
            Descendants(app).OfType<Button>().Single(b=>b.Text=="Đổi hồ sơ").PerformClick();
        }
        var root=Path.Combine(Path.GetTempPath(),"SharedQueueRegression-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string csv=Path.Combine(root,"papers.csv"), output=Path.Combine(root,"pdfs"), shared=Path.Combine(root,"shared");
        Directory.CreateDirectory(output);
        File.WriteAllText(csv,"doi,landing_page,save_as\n10.1007/one,https://shared.fixture.invalid/article/1,one.pdf\n10.1234/two,https://shared.fixture.invalid/article/2,two.pdf\n10.1234/three,https://shared.fixture.invalid/article/3,\n");
        const string pdf="%PDF-1.4\nfixture\n%%EOF";
        string one=Path.Combine(output,"one.pdf"),three=Path.Combine(output,"publisher-suggested.pdf");
        File.WriteAllText(one,pdf); File.WriteAllText(three,pdf);
        string legacyProfile=Path.Combine(root,"legacy"),legacyState=Path.Combine(legacyProfile,"QueueState");
        Directory.CreateDirectory(legacyState);
        string elsewhere=Path.Combine(root,"elsewhere"); Directory.CreateDirectory(elsewhere);
        string wrongFolderPdf=Path.Combine(elsewhere,"two.pdf"); File.WriteAllText(wrongFolderPdf,pdf);
        File.WriteAllText(Path.Combine(legacyState,"state_v51.json"),JsonSerializer.Serialize(new { CsvPath=csv, Jobs=new[] {
            new {Id=1,Doi="10.1007/one",Status=JobStatus.Completed,FilePath=one,PdfUrl="https://link.springer.com/content/pdf/10.1007/unrelated.pdf"},
            new {Id=3,Doi="10.1234/three",Status=JobStatus.Completed,FilePath=three,PdfUrl="https://shared.fixture.invalid/three.pdf"},
            new {Id=2,Doi="10.1234/two",Status=JobStatus.Completed,FilePath=wrongFolderPdf,PdfUrl="https://shared.fixture.invalid/two.pdf"} }}));
        using(var legacy=new MainForm(legacyProfile,AccessMode.VnuAccount,null,Path.Combine(root,"upgrade-shared")))
        {
            ((TextBox)Field(legacy,"txtCsv")).Text=csv; ((TextBox)Field(legacy,"txtFolder")).Text=output;
            Call(legacy,"LoadCsv");
            Check(Jobs(legacy)[2].Status==JobStatus.Completed,"Upgrade lost a verified legacy PDF with suggested filename");
            Check(Jobs(legacy)[1].Status==JobStatus.Pending,"Upgrade adopted a PDF from another output folder");
            Check(Jobs(legacy)[0].Status==JobStatus.Deferred,"Filename recovery accepted a known unrelated Springer PDF");
        }
        using var ip=new MainForm(Path.Combine(root,"ip"),AccessMode.PtitIp,null,shared) {Opacity=0,ShowInTaskbar=false};
        ip.Show(); await Until(()=>(bool)Field(ip,"initialized"));
        ((TextBox)Field(ip,"txtCsv")).Text=csv; ((TextBox)Field(ip,"txtFolder")).Text=output;
        Call(ip,"LoadCsv");
        var rows=Jobs(ip);
        rows[0].Status=JobStatus.Completed; rows[0].FilePath=one;
        rows[1].Status=JobStatus.Deferred; rows[1].Error="No subscription on PTIT"; rows[1].PdfUrl="https://expired.invalid/signed.pdf";
        rows[2].Status=JobStatus.Completed; rows[2].FilePath=three;
        Call(ip,"SaveStateSnapshot");
        string before=File.ReadAllText(one)+File.ReadAllText(three);
        Switch(ip,1);
        using var vnu=new MainForm(Path.Combine(root,"vnu"),ip.RequestedAccessMode!.Value,ip.RequestedInputPaths,shared) {Opacity=0,ShowInTaskbar=false};
        vnu.Show(); await Until(()=>(bool)Field(vnu,"initialized") && Jobs(vnu).Count==3);
        rows=Jobs(vnu);
        Check(rows[0].Status==JobStatus.Completed && rows[2].Status==JobStatus.Completed,"VNU lost PTIT completions, including suggested filename");
        Check(rows[1].Status==JobStatus.Pending && rows[1].PdfUrl=="","VNU did not requeue incomplete paper with a fresh URL");
        var core=((WebView2)Field(vnu,"web")).CoreWebView2;
        var articleRequests=new List<string>();
        core.AddWebResourceRequestedFilter("https://shared.fixture.invalid/*",CoreWebView2WebResourceContext.All);
        core.WebResourceRequested+=(_,e)=>
        {
            string path=new Uri(e.Request.Uri).AbsolutePath;
            if (path.StartsWith("/__paper_downloader__/"))return;
            if (path.StartsWith("/article/"))articleRequests.Add(path);
            bool isPdf=path.EndsWith(".pdf");
            string body=isPdf?pdf:"<html><meta name='citation_doi' content='10.1234/two'><meta name='citation_pdf_url' content='https://shared.fixture.invalid/two.pdf'><body><a href='https://shared.fixture.invalid/two.pdf'>Download PDF</a></body></html>";
            e.Response=core.Environment.CreateWebResourceResponse(new MemoryStream(Encoding.UTF8.GetBytes(body)),200,"OK","Content-Type: "+(isPdf?"application/pdf":"text/html"));
        };
        await (Task)Call(vnu,"StartQueueAsync")!;
        await Until(()=>Jobs(vnu).All(j=>j.Status==JobStatus.Completed)
            && !(bool)typeof(MainForm).GetProperty("HasActiveTransfers",flags)!.GetValue(vnu)!);
        Check(articleRequests.SequenceEqual(new[]{"/article/2"}),"VNU revisited already completed articles");
        Check(File.ReadAllText(one)+File.ReadAllText(three)==before,"Existing PDFs were overwritten");
        string diagnostics=(string)typeof(MainForm).GetProperty("DiagnosticDirectory",flags)!.GetValue(vnu)!;
        await Until(()=>Directory.GetFiles(diagnostics,"run-summary-*.json").Length>0);
        using(var report=JsonDocument.Parse(File.ReadAllText(Directory.GetFiles(diagnostics,"run-summary-*.json").Single())))
            Check(report.RootElement.GetProperty("alreadyCompletedAtStart").GetInt32()==2
                && report.RootElement.GetProperty("completedDuringRun").GetInt32()==1,"Run summary counted old PDFs as new downloads");
        Switch(vnu,0);
        using var returned=new MainForm(Path.Combine(root,"returned-ip"),vnu.RequestedAccessMode!.Value,vnu.RequestedInputPaths,shared) {Opacity=0,ShowInTaskbar=false};
        returned.Show(); await Until(()=>(bool)Field(returned,"initialized") && Jobs(returned).Count==3);
        Check(Jobs(returned).All(j=>j.Status==JobStatus.Completed),"Switching back lost the VNU completion");
        Call(returned,"BuildQueuePlan",new Func<DownloadJob,bool>(j=>j.Status==JobStatus.Pending));
        Check(((IList)Field(returned,"queuePlan")).Count==0,"Completed articles were scheduled again");
        File.Delete(Path.Combine(output,"two.pdf"));
        Call(returned,"LoadCsv");
        Check(Jobs(returned)[1].Status==JobStatus.Pending,"Missing completed PDF was not requeued");
        string changedOutput=Path.Combine(root,"other-pdfs"); Directory.CreateDirectory(changedOutput);
        ((TextBox)Field(returned,"txtFolder")).Text=changedOutput;
        Call(returned,"LoadCsv");
        Check(Jobs(returned).All(j=>j.Status==JobStatus.Pending),"Different output folder inherited old completions");
        ((TextBox)Field(returned,"txtFolder")).Text="";
        Call(returned,"LoadCsv");
        ((TextBox)Field(returned,"txtFolder")).Text=output;
        Check((bool)Call(returned,"ReloadForChangedOutputFolder")!
            && Jobs(returned)[2].Status==JobStatus.Completed,"Selecting an output folder after loading did not restore its shared progress");
        ((TextBox)Field(returned,"txtFolder")).Text=output;
        File.AppendAllText(csv,"10.1234/four,https://shared.fixture.invalid/article/4,four.pdf\n");
        Call(returned,"LoadCsv");
        Check(Jobs(returned).Count==4 && Jobs(returned)[2].Status==JobStatus.Pending,"Changed CSV reused stale state fingerprint");
        File.WriteAllText(csv,File.ReadAllText(csv).Replace("10.1007/one","10.1007/different"));
        Call(returned,"LoadCsv");
        Check(Jobs(returned)[0].Status==JobStatus.Pending,"An unrelated CSV row was completed solely because its filename already existed");
        returned.Close();
        Console.WriteLine("PASS: PTIT/VNU shared queue downloads only missing paper, retains suggested filenames, survives switch back, repairs missing PDFs and isolates changed CSV/folder");
    }
}
