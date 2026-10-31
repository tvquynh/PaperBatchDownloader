namespace PaperBatchDownloader;

internal static class PublisherResolver
{
    public static string Resolve(DownloadJob job)
    {
        string doi = (job.Doi ?? "").ToLowerInvariant();
        string url = (job.Url ?? "").ToLowerInvariant();
        string publisher = (job.Publisher ?? "").ToLowerInvariant();
        string source = (job.Source ?? "").ToLowerInvariant();

        if (doi.StartsWith("10.1109/") || url.Contains("ieeexplore.ieee.org") || publisher.Contains("ieee") || source.Contains("ieee")) return "IEEE";
        if (doi.StartsWith("10.1002/") || doi.StartsWith("10.1155/") || url.Contains("onlinelibrary.wiley.com") || publisher.Contains("wiley") || source.Contains("wiley")) return "Wiley";
        if (doi.StartsWith("10.1016/") || url.Contains("sciencedirect.com") || url.Contains("elsevier.com") || publisher.Contains("elsevier") || source.Contains("elsevier")) return "Elsevier";
        if (doi.StartsWith("10.1007/") || url.Contains("springer.com") || url.Contains("springerlink.com") || publisher.Contains("springer") || source.Contains("springer")) return "Springer";
        if (url.Contains("nature.com") || publisher.Contains("nature") || source.Contains("nature")) return "Nature";
        if (doi.StartsWith("10.3390/") || url.Contains("mdpi.com") || publisher.Contains("mdpi") || source.Contains("mdpi")) return "MDPI";
        if (url.Contains("tandfonline.com") || publisher.Contains("taylor") || source.Contains("taylor")) return "Taylor & Francis";
        if (url.Contains("sagepub.com") || publisher.Contains("sage") || source.Contains("sage")) return "SAGE";
        if (url.Contains("frontiersin.org") || publisher.Contains("frontiers") || source.Contains("frontiers")) return "Frontiers";
        if (url.Contains("plos.org") || publisher.Contains("plos") || source.Contains("plos")) return "PLOS";
        if (url.Contains("acm.org") || doi.StartsWith("10.1145/") || publisher.Contains("acm") || source.Contains("acm")) return "ACM";
        if (url.Contains("mecs-press.org")) return "MECS Press";
        if (url.Contains("direct.mit.edu")) return "MIT Press";
        if (!string.IsNullOrWhiteSpace(job.Publisher)) return job.Publisher.Trim();
        if (Uri.TryCreate(job.Url, UriKind.Absolute, out var uri)) return uri.Host;
        return "Other";
    }

    public static string LoginUrl(string publisherKey)
    {
        return publisherKey switch
        {
            "IEEE" => "https://ieeexplore.ieee.org/",
            "Wiley" => "https://onlinelibrary.wiley.com/",
            "Elsevier" => "https://www.sciencedirect.com/",
            "Springer" => "https://link.springer.com/",
            "Nature" => "https://www.nature.com/",
            "MDPI" => "https://www.mdpi.com/",
            "Taylor & Francis" => "https://www.tandfonline.com/",
            "SAGE" => "https://journals.sagepub.com/",
            "Frontiers" => "https://www.frontiersin.org/",
            "PLOS" => "https://journals.plos.org/",
            "ACM" => "https://dl.acm.org/",
            _ => ""
        };
    }
}
