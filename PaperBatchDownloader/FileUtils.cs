using System.Text.RegularExpressions;

namespace PaperBatchDownloader;

internal static class FileUtils
{
    public static string DownloadTargetPath(string folder, string saveAs, string fallbackName)
    {
        if (string.IsNullOrWhiteSpace(saveAs)) return UniquePath(folder, fallbackName);
        if (saveAs != Path.GetFileName(saveAs) || saveAs.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || !saveAs.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Invalid save_as: expected a PDF filename without a folder path.");
        Directory.CreateDirectory(folder);
        string target = Path.Combine(folder, saveAs);
        if (File.Exists(target)) throw new IOException("File already exists: " + saveAs + ". Move or verify it before retrying; the requested filename will not be changed.");
        return target;
    }
    public static string SafeFileName(string value)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) value = value.Replace(c, '_');
        value = Regex.Replace(value, @"\s+", " ").Trim();
        if (value.Length > 180) value = value[..180];
        return string.IsNullOrWhiteSpace(value) ? "paper.pdf" : value;
    }

    public static string UniquePath(string folder, string fileName)
    {
        Directory.CreateDirectory(folder);
        fileName = SafeFileName(fileName);
        if (!fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) fileName += ".pdf";
        var path = Path.Combine(folder, fileName);
        if (!File.Exists(path)) return path;
        var stem = Path.GetFileNameWithoutExtension(fileName);
        for (int i = 1; ; i++)
        {
            path = Path.Combine(folder, $"{stem} ({i}).pdf");
            if (!File.Exists(path)) return path;
        }
    }

    public static bool HasPdfMagic(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            Span<byte> b = stackalloc byte[5];
            if (fs.Read(b) != 5) return false;
            return b[0] == 0x25 && b[1] == 0x50 && b[2] == 0x44 && b[3] == 0x46 && b[4] == 0x2D;
        }
        catch { return false; }
    }

    public static bool HasCompletePdf(string path)
    {
        if (!HasPdfMagic(path)) return false;
        try
        {
            using var fs = File.OpenRead(path);
            fs.Seek(Math.Max(0, fs.Length - 4096), SeekOrigin.Begin);
            byte[] tail = new byte[(int)(fs.Length - fs.Position)];
            fs.ReadExactly(tail);
            return System.Text.Encoding.ASCII.GetString(tail).Contains("%%EOF", StringComparison.Ordinal);
        }
        catch { return false; }
    }
}
