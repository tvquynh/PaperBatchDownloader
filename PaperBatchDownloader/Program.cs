namespace PaperBatchDownloader;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        string preference = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PaperBatchDownloader", "access_mode.txt");
        AccessMode mode = AccessMode.PtitIp;
        try { if (File.Exists(preference) && File.ReadAllText(preference).Trim() == "VNU_ACCOUNT") mode = AccessMode.VnuAccount; } catch { }
        if (args.Any(a => a.Equals("--vnu", StringComparison.OrdinalIgnoreCase))) mode = AccessMode.VnuAccount;
        if (args.Any(a => a.Equals("--ptit", StringComparison.OrdinalIgnoreCase))) mode = AccessMode.PtitIp;
        (string CsvPath, string DownloadFolder)? inputPaths = null;
        while (true)
        {
            using var form = new MainForm(null, mode, inputPaths);
            Application.Run(form);
            if (form.RequestedAccessMode is not { } next) break;
            inputPaths = form.RequestedInputPaths;
            mode = next;
            try { Directory.CreateDirectory(Path.GetDirectoryName(preference)!); File.WriteAllText(preference, mode == AccessMode.VnuAccount ? "VNU_ACCOUNT" : "PTIT_IP"); } catch { }
        }
    }
}
