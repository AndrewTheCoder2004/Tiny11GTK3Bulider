using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Tiny11Builder.Portable;

public sealed record OfficialMedia(Uri Url, string FileName, string Description, string Language,
    string Architecture, string? Sha256);
public sealed record PortableDownloadResult(string Path, string Sha256, bool VerifiedAgainstPublishedHash);

public sealed class MicrosoftMediaCatalog(HttpClient client)
{
    private const string Profile = "606624d44113";
    private const string Instance = "560dc9f3-1aa5-4a2f-b63c-9e18f8d0e175";
    private const string X64Page = "https://www.microsoft.com/software-download/windows11";
    private const string Arm64Page = "https://www.microsoft.com/software-download/windows11arm64";

    public static HttpClient CreateClient() => new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = true })
    { Timeout = TimeSpan.FromSeconds(60) };

    public async Task<OfficialMedia> ResolveAsync(string architecture, CultureInfo culture, Action<string>? progress,
        CancellationToken cancellationToken)
    {
        if (architecture is not ("x64" or "arm64")) throw new InvalidDataException("Only x64 and ARM64 downloads are supported.");
        progress?.Invoke("Checking Microsoft's latest Windows 11 release.");
        var page = architecture == "arm64" ? Arm64Page : X64Page;
        var html = await ReadAsync(new Uri(page.Replace("/software-download/", "/en-us/software-download/", StringComparison.Ordinal)), cancellationToken)
            .ConfigureAwait(false);
        var product = ParseProductId(html);
        var session = Guid.NewGuid().ToString();
        await ReadAsync(new($"https://vlscppe.microsoft.com/tags?org_id=y6jn8c31&session_id={session}"), cancellationToken).ConfigureAwait(false);
        var response = await ReadAsync(new($"https://ov-df.microsoft.com/mdt.js?instanceId={Instance}&PageId=si&session_id={session}"), cancellationToken)
            .ConfigureAwait(false);
        var w = Regex.Match(response, @"[?&]w=([A-F0-9]+)").Groups[1].Value;
        var ticks = Regex.Match(response, "rticks=\\\"\\+?(\\d+)").Groups[1].Value;
        if (w.Length == 0 || ticks.Length == 0)
            throw new IOException("Microsoft did not provide a download session. Retry later or choose a local ISO.");
        await ReadAsync(new($"https://ov-df.microsoft.com/?session_id={session}&CustomerId={Instance}&PageId=si&w={w}" +
            $"&mdt={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}&rticks={ticks}"), cancellationToken).ConfigureAwait(false);

        using var languageDocument = JsonDocument.Parse(await ReadAsync(Api("getskuinformationbyproductedition", product, "undefined", session), cancellationToken)
            .ConfigureAwait(false));
        CheckErrors(languageDocument.RootElement);
        var sku = SelectLanguage(languageDocument.RootElement.GetProperty("Skus"), culture, out var language);
        progress?.Invoke($"Getting the latest {architecture.ToUpperInvariant()} ISO in {language}.");
        using var links = JsonDocument.Parse(await ReadAsync(Api("GetProductDownloadLinksBySku", "undefined", sku, session), cancellationToken)
            .ConfigureAwait(false));
        CheckErrors(links.RootElement);
        var requestedType = architecture == "arm64" ? 2 : 1;
        foreach (var item in links.RootElement.GetProperty("ProductDownloadOptions").EnumerateArray())
        {
            if (item.GetProperty("DownloadType").GetInt32() != requestedType) continue;
            var url = OfficialIsoDownloader.ValidateUrl(item.GetProperty("Uri").GetString()!);
            var filename = Uri.UnescapeDataString(Path.GetFileName(url.AbsolutePath));
            if (filename.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new InvalidDataException("Microsoft returned an invalid ISO filename.");
            var hash = item.TryGetProperty("Sha256", out var value) && value.ValueKind == JsonValueKind.String
                ? OfficialIsoDownloader.ValidateHash(value.GetString()) : null;
            return new(url, filename, item.GetProperty("LocalizedProductDisplayName").GetString() ?? filename,
                language, architecture, hash);
        }
        throw new IOException("Microsoft did not return an ISO for this architecture. Choose a local ISO or retry later.");
    }

    private static string ParseProductId(string html)
    {
        var select = Regex.Match(html, "(?is)<select\\b[^>]*\\bid=[\"']product-edition[\"'][^>]*>(.*?)</select>");
        var option = Regex.Match(select.Groups[1].Value, "(?is)<option\\b[^>]*\\bvalue=[\"'](\\d+)[\"'][^>]*>");
        return option.Success ? option.Groups[1].Value :
            throw new InvalidDataException("Microsoft's download catalog changed. Retry later or choose a local ISO.");
    }

    private static string SelectLanguage(JsonElement skus, CultureInfo culture, out string language)
    {
        var preferred = culture.Name switch
        {
            "en-US" => "English",
            var value when value.StartsWith("en-", StringComparison.OrdinalIgnoreCase) => "English International",
            "pt-BR" => "Brazilian Portuguese",
            "zh-CN" or "zh-SG" => "Chinese Simplified",
            "zh-TW" or "zh-HK" => "Chinese Traditional",
            "fr-CA" => "French Canadian",
            "es-MX" => "Spanish (Mexico)",
            _ => culture.Parent.Name.Length > 0 ? culture.Parent.EnglishName : culture.EnglishName
        };
        JsonElement? english = null;
        foreach (var item in skus.EnumerateArray())
        {
            var name = item.GetProperty("Language").GetString();
            if (name is not null && NormalizeLanguage(name).Equals(NormalizeLanguage(preferred), StringComparison.OrdinalIgnoreCase))
            { language = name; return item.GetProperty("Id").ToString(); }
            if (name == "English") english = item;
        }
        if (english is { } fallback)
        { language = $"English (Microsoft has no {preferred} ISO)"; return fallback.GetProperty("Id").ToString(); }
        throw new IOException("Microsoft has no ISO matching the display language and no English ISO.");
    }

    private static string NormalizeLanguage(string name) => Regex.Replace(name, @"[^\p{L}]", "");

    private static Uri Api(string action, string product, string sku, string session) => new(
        $"https://www.microsoft.com/software-download-connector/api/{action}?profile={Profile}&productEditionId={product}" +
        $"&SKU={sku}&friendlyFileName=undefined&Locale=en-US&sessionID={session}");

    private async Task<string> ReadAsync(Uri uri, CancellationToken cancellationToken)
    {
        for (var redirect = 0; redirect < 5; redirect++)
        {
            if (uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length > 0 ||
                uri.IdnHost is not ("www.microsoft.com" or "vlscppe.microsoft.com" or "ov-df.microsoft.com"))
                throw new InvalidDataException("Download catalog request left Microsoft's approved hosts.");
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Referrer = new(X64Page);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
            {
                var location = response.Headers.Location ?? throw new IOException("Microsoft returned an empty redirect.");
                uri = location.IsAbsoluteUri ? location : new(uri, location);
                continue;
            }
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > 4 * 1024 * 1024)
                throw new IOException("Microsoft's catalog response is too large.");
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            var block = new byte[16 * 1024];
            int count;
            while ((count = await input.ReadAsync(block, cancellationToken).ConfigureAwait(false)) > 0)
            {
                if (buffer.Length + count > 4 * 1024 * 1024) throw new IOException("Microsoft's catalog response is too large.");
                buffer.Write(block, 0, count);
            }
            return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
        }
        throw new IOException("Too many Microsoft catalog redirects.");
    }

    private static void CheckErrors(JsonElement root)
    {
        if (root.TryGetProperty("Errors", out var errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0)
            throw new IOException("Microsoft refused the download request. " + WebUtility.HtmlDecode(errors[0].GetProperty("Value").GetString()) +
                " Retry later or choose a local ISO. No third-party mirror will be used.");
    }
}

public sealed class OfficialIsoDownloader(HttpClient client)
{
    private const long MaximumBytes = 32L * 1024 * 1024 * 1024;
    private sealed record PartialInfo(string Resource, string? ETag, long? Length);

    public static HttpClient CreateClient() => new(new HttpClientHandler { AllowAutoRedirect = false })
    { Timeout = TimeSpan.FromHours(4) };

    public static Uri ValidateUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !uri.IsDefaultPort ||
            uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 ||
            uri.IdnHost is not ("software.download.prss.microsoft.com" or "software-download.microsoft.com" or "download.microsoft.com") ||
            !uri.AbsolutePath.EndsWith(".iso", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Only official Microsoft HTTPS ISO links are accepted.");
        return uri;
    }

    public static string? ValidateHash(string? hash)
    {
        if (string.IsNullOrWhiteSpace(hash)) return null;
        var normalized = hash.Trim().ToUpperInvariant();
        if (normalized.Length != 64 || normalized.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidDataException("Microsoft's published SHA-256 is invalid.");
        return normalized;
    }

    public async Task<PortableDownloadResult> DownloadAsync(OfficialMedia media, string destination,
        Action<string>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(media);
        var uri = ValidateUrl(media.Url.AbsoluteUri);
        var expectedHash = ValidateHash(media.Sha256);
        destination = Path.GetFullPath(destination);
        if (!destination.EndsWith(".iso", StringComparison.OrdinalIgnoreCase) || File.Exists(destination))
            throw new IOException("The download destination must be a new .iso filename.");
        RejectReparsePoints(destination);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var partial = destination + ".part";
        var metadataPath = partial + ".json";
        RejectReparsePoints(partial);
        RejectReparsePoints(metadataPath);
        var resource = uri.GetLeftPart(UriPartial.Path);
        PartialInfo? saved = null;
        if (File.Exists(partial))
        {
            saved = File.Exists(metadataPath)
                ? JsonSerializer.Deserialize<PartialInfo>(await File.ReadAllTextAsync(metadataPath, cancellationToken).ConfigureAwait(false)) : null;
            if (saved?.Resource != resource)
                throw new IOException("A partial download for another Microsoft resource exists at this path.");
        }
        var offset = saved?.ETag is not null && !saved.ETag.StartsWith("W/", StringComparison.OrdinalIgnoreCase)
            ? new FileInfo(partial).Length : 0;
        using var response = await SendAsync(uri, offset, saved?.ETag, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable &&
            response.Content.Headers.ContentRange?.Length == offset && offset > 0)
            return await FinishAsync(partial, destination, metadataPath, expectedHash, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.StatusCode == HttpStatusCode.PartialContent)
        {
            if (response.Content.Headers.ContentRange?.From != offset || saved?.ETag != response.Headers.ETag?.ToString())
                throw new IOException("Microsoft's ISO changed during download. Start with a new destination.");
        }
        else if (response.StatusCode == HttpStatusCode.OK) offset = 0;
        else throw new IOException($"Unexpected Microsoft download response: {response.StatusCode}");
        var total = response.Content.Headers.ContentRange?.Length ??
            (response.Content.Headers.ContentLength is long length ? length + offset : (long?)null);
        if (total is > MaximumBytes or <= 0) throw new IOException("The ISO size is invalid or exceeds 32 GiB.");
        var drive = DriveInfo.GetDrives().Where(item => destination.StartsWith(item.Name,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            .OrderByDescending(item => item.Name.Length).FirstOrDefault();
        var free = drive?.AvailableFreeSpace ?? new DriveInfo(Path.GetPathRoot(destination)!).AvailableFreeSpace;
        if (total is long size && free < size - offset + 1024L * 1024 * 1024)
            throw new IOException("Not enough free space for the ISO and a 1 GiB safety margin.");

        await File.WriteAllTextAsync(metadataPath,
            JsonSerializer.Serialize(new PartialInfo(resource, response.Headers.ETag?.ToString(), total)), cancellationToken).ConfigureAwait(false);
        await using (var file = new FileStream(partial, offset > 0 ? FileMode.Open : FileMode.Create, FileAccess.Write,
                         FileShare.None, 1024 * 1024, useAsync: true))
        {
            file.Position = offset;
            await using var content = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var buffer = new byte[1024 * 1024];
            var timer = Stopwatch.StartNew();
            var lastReport = TimeSpan.Zero;
            long received = offset;
            while (true)
            {
                var count = await content.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (count == 0) break;
                received += count;
                if (received > MaximumBytes || (total is long limit && received > limit))
                    throw new IOException("Microsoft sent more bytes than advertised.");
                await file.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                if (timer.Elapsed - lastReport >= TimeSpan.FromSeconds(1))
                {
                    var percent = total is long bytes ? received * 100d / bytes : 0;
                    var speed = (received - offset) / (1024d * 1024) / Math.Max(timer.Elapsed.TotalSeconds, 0.001);
                    progress?.Invoke(total is null ? $"Downloaded {received / (1024d * 1024):0.0} MiB at {speed:0.0} MiB/s." :
                        $"Downloading Windows ISO: {percent:0.0}% ({received / (1024d * 1024):0.0} MiB, {speed:0.0} MiB/s).");
                    lastReport = timer.Elapsed;
                }
            }
            await file.FlushAsync(cancellationToken).ConfigureAwait(false);
            if (total is long expected && received != expected)
                throw new IOException("The download is incomplete. Retry to resume the Microsoft ISO.");
        }
        return await FinishAsync(partial, destination, metadataPath, expectedHash, cancellationToken).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendAsync(Uri uri, long offset, string? etag, CancellationToken cancellationToken)
    {
        for (var hop = 0; hop < 6; hop++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            if (offset > 0)
            {
                request.Headers.Range = new RangeHeaderValue(offset, null);
                request.Headers.IfRange = new RangeConditionHeaderValue(EntityTagHeaderValue.Parse(etag!));
            }
            var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode is not (HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect or
                HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)) return response;
            var next = response.Headers.Location;
            response.Dispose();
            if (next is null) throw new IOException("Microsoft returned a redirect without a destination.");
            uri = ValidateUrl((next.IsAbsoluteUri ? next : new Uri(uri, next)).AbsoluteUri);
        }
        throw new IOException("Too many Microsoft download redirects.");
    }

    private static async Task<PortableDownloadResult> FinishAsync(string partial, string destination,
        string metadataPath, string? expectedHash, CancellationToken cancellationToken)
    {
        string hash;
        await using (var stream = File.OpenRead(partial))
        {
            await IsoImageStructure.ValidateAsync(stream, cancellationToken).ConfigureAwait(false);
            hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
        }
        if (expectedHash is not null && hash != expectedHash)
            throw new InvalidDataException("The Microsoft-published SHA-256 does not match. The partial download was retained.");
        File.Move(partial, destination, overwrite: false);
        if (File.Exists(metadataPath)) File.Delete(metadataPath);
        return new(destination, hash, expectedHash is not null);
    }

    private static void RejectReparsePoints(string path)
    {
        for (string? current = path; current is not null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException($"Download paths cannot use symbolic links or reparse points: {current}");
    }
}
