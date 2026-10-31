namespace PaperBatchDownloader;

public enum JobStatus
{
    Pending,
    Navigating,
    LookingForPdf,
    Downloading,
    WaitingForUser,
    LoginRequired,
    BotCheck,
    InstitutionalAccessRequired,
    ManualAttention,
    Deferred,
    Completed,
    Failed,
    Skipped,
    NoUrl
}

public sealed class DownloadJob
{
    public int Id { get; set; }
    public string OpenAlexId { get; set; } = "";
    public string Doi { get; set; } = "";
    public string Title { get; set; } = "";
    public string SaveAs { get; set; } = "";
    public string PublicationYear { get; set; } = "";
    public string Publisher { get; set; } = "";
    public string Source { get; set; } = "";
    public string IsOpenAccess { get; set; } = "";
    public string OaStatus { get; set; } = "";
    public string OaUrl { get; set; } = "";
    public string BestOaPdfUrl { get; set; } = "";
    public string BestOaLandingPage { get; set; } = "";

    // URL chosen automatically from the source CSV.
    public string Url { get; set; } = "";
    public string UrlSource { get; set; } = "";
    public bool IsDirectPdfCandidate { get; set; }
    public string PublisherKey { get; set; } = "Other";
    public bool IsPreflightSample { get; set; }
    public string DeferredReason { get; set; } = "";
    public string AttentionReason { get; set; } = "";
    public string PageDecision { get; set; } = "";

    public JobStatus Status { get; set; } = JobStatus.Pending;
    public string PdfUrl { get; set; } = "";
    public string FilePath { get; set; } = "";
    public string Error { get; set; } = "";
    public int Attempts { get; set; }

    public string StatusText => Status.ToString();
}
