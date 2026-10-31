using System.Text;

namespace PaperBatchDownloader;

internal static class CsvUtils
{
    public static List<DownloadJob> ReadJobs(string path)
    {
        using var sr = new StreamReader(path, Encoding.UTF8, true);
        using var rows = ReadCsvRows(sr).GetEnumerator();
        var jobs = new List<DownloadJob>();
        if (!rows.MoveNext()) return jobs;

        var header = rows.Current;
        var columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < header.Count; i++)
        {
            var name = header[i].Trim().TrimStart('\uFEFF');
            if (!columns.ContainsKey(name)) columns[name] = i;
        }

        // Accept both the user's research CSV and a simple CSV with a single "url" column.
        bool hasResearchFields = columns.ContainsKey("best_oa_pdf_url") || columns.ContainsKey("doi") || columns.ContainsKey("oa_url") || columns.ContainsKey("pdf_url") || columns.ContainsKey("landing_page") || columns.ContainsKey("doi_url");
        int id = 1;

        while (rows.MoveNext())
        {
            var fields = rows.Current;
            if (fields.Count == 0 || fields.All(string.IsNullOrWhiteSpace)) continue;

            string Get(string name)
            {
                return columns.TryGetValue(name, out var idx) && idx < fields.Count ? fields[idx].Trim() : "";
            }

            var job = new DownloadJob
            {
                Id = id,
                OpenAlexId = !string.IsNullOrWhiteSpace(Get("openalex_id")) ? Get("openalex_id") : Get("record_id"),
                Doi = NormalizeDoi(!string.IsNullOrWhiteSpace(Get("doi")) ? Get("doi") : Get("doi_url")),
                Title = Get("title"),
                SaveAs = string.IsNullOrWhiteSpace(Get("save_as")) ? Get("file_name") : Get("save_as"),
                PublicationYear = string.IsNullOrWhiteSpace(Get("publication_year")) ? Get("year") : Get("publication_year"),
                Publisher = Get("publisher"),
                Source = Get("source"),
                IsOpenAccess = Get("is_open_access"),
                OaStatus = Get("oa_status"),
                OaUrl = !string.IsNullOrWhiteSpace(Get("pdf_url")) ? Get("pdf_url") : Get("oa_url"),
                BestOaPdfUrl = Get("best_oa_pdf_url"),
                BestOaLandingPage = !string.IsNullOrWhiteSpace(Get("best_oa_landing_page")) ? Get("best_oa_landing_page") : Get("landing_page")
            };

            if (hasResearchFields)
            {
                SelectBestUrl(job);
                if (job.UrlSource == "oa_url" && !string.IsNullOrWhiteSpace(Get("pdf_url"))) job.UrlSource = "pdf_url";
                if (string.IsNullOrWhiteSpace(job.Url))
                {
                    job.Url = Get("url");
                    job.UrlSource = "url";
                    job.IsDirectPdfCandidate = LooksLikePdfUrl(job.Url);
                }
            }
            else
            {
                job.Url = Get("url");
                if (string.IsNullOrWhiteSpace(job.Url) && fields.Count == 1) job.Url = fields[0].Trim();
                job.UrlSource = "url";
                job.IsDirectPdfCandidate = LooksLikePdfUrl(job.Url);
            }

            job.PublisherKey = PublisherResolver.Resolve(job);

            if (string.IsNullOrWhiteSpace(job.Url) || !Uri.TryCreate(job.Url, UriKind.Absolute, out _))
            {
                job.Status = JobStatus.NoUrl;
                job.Error = "No usable URL found in best_oa_pdf_url, oa_url, best_oa_landing_page, DOI, or url.";
            }

            jobs.Add(job);
            id++;
        }

        return jobs;
    }

    private static void SelectBestUrl(DownloadJob job)
    {
        if (IsAbsoluteHttpUrl(job.BestOaPdfUrl))
        {
            job.Url = job.BestOaPdfUrl;
            job.UrlSource = "best_oa_pdf_url";
            job.IsDirectPdfCandidate = true;
            return;
        }

        if (IsAbsoluteHttpUrl(job.OaUrl))
        {
            job.Url = job.OaUrl;
            job.UrlSource = "oa_url";
            job.IsDirectPdfCandidate = LooksLikePdfUrl(job.OaUrl);
            return;
        }

        if (IsAbsoluteHttpUrl(job.BestOaLandingPage))
        {
            job.Url = job.BestOaLandingPage;
            job.UrlSource = "best_oa_landing_page";
            job.IsDirectPdfCandidate = LooksLikePdfUrl(job.BestOaLandingPage);
            return;
        }

        if (!string.IsNullOrWhiteSpace(job.Doi))
        {
            job.Url = "https://doi.org/" + job.Doi;
            job.UrlSource = "doi";
            job.IsDirectPdfCandidate = false;
        }
    }

    public static void WriteResults(string path, IEnumerable<DownloadJob> jobs)
    {
        using var sw = new StreamWriter(path, false, new UTF8Encoding(true));
        sw.WriteLine("id,openalex_id,doi,title,publication_year,publisher,publisher_key,source,is_open_access,oa_status,url_source,selected_url,status,preflight,page_decision,attention_reason,deferred_reason,pdf_url,file_path,attempts,error,save_as");
        foreach (var j in jobs)
        {
            sw.WriteLine(string.Join(",", new[]
            {
                j.Id.ToString(), Q(j.OpenAlexId), Q(j.Doi), Q(j.Title), Q(j.PublicationYear), Q(j.Publisher), Q(j.PublisherKey), Q(j.Source), Q(j.IsOpenAccess), Q(j.OaStatus), Q(j.UrlSource), Q(j.Url), Q(j.StatusText),
                Q(j.IsPreflightSample ? "yes" : "no"), Q(j.PageDecision), Q(j.AttentionReason), Q(j.DeferredReason), Q(j.PdfUrl), Q(j.FilePath),
                j.Attempts.ToString(), Q(j.Error), Q(j.SaveAs)
            }));
        }
    }

    private static bool IsAbsoluteHttpUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps);

    private static bool LooksLikePdfUrl(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var lower = value.ToLowerInvariant();
        return lower.EndsWith(".pdf") || lower.Contains(".pdf?") || lower.Contains("/content/pdf/") || lower.Contains("/pdf/") || lower.Contains("/pdfft");
    }

    private static string NormalizeDoi(string doi)
    {
        doi = (doi ?? "").Trim();
        if (doi.StartsWith("https://doi.org/", StringComparison.OrdinalIgnoreCase)) doi = doi[16..];
        else if (doi.StartsWith("http://doi.org/", StringComparison.OrdinalIgnoreCase)) doi = doi[15..];
        else if (doi.StartsWith("doi:", StringComparison.OrdinalIgnoreCase)) doi = doi[4..].Trim();
        return doi;
    }

    private static string Q(string value) => "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";

    // Streaming RFC-4180 style parser. Unlike File.ReadAllLines, this correctly handles
    // commas, escaped quotes, and newlines inside quoted abstract/title fields.
    private static IEnumerable<List<string>> ReadCsvRows(TextReader reader)
    {
        var row = new List<string>();
        var field = new StringBuilder();
        bool quoted = false;

        while (true)
        {
            int n = reader.Read();
            if (n < 0)
            {
                if (quoted || field.Length > 0 || row.Count > 0)
                {
                    row.Add(field.ToString());
                    yield return row;
                }
                yield break;
            }

            char c = (char)n;
            if (quoted)
            {
                if (c == '"')
                {
                    if (reader.Peek() == '"')
                    {
                        reader.Read();
                        field.Append('"');
                    }
                    else quoted = false;
                }
                else field.Append(c);
            }
            else
            {
                if (c == '"' && field.Length == 0)
                {
                    quoted = true;
                }
                else if (c == ',')
                {
                    row.Add(field.ToString());
                    field.Clear();
                }
                else if (c == '\r')
                {
                    if (reader.Peek() == '\n') reader.Read();
                    row.Add(field.ToString());
                    field.Clear();
                    yield return row;
                    row = new List<string>();
                }
                else if (c == '\n')
                {
                    row.Add(field.ToString());
                    field.Clear();
                    yield return row;
                    row = new List<string>();
                }
                else field.Append(c);
            }
        }
    }
}
