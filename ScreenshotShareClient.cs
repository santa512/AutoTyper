using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

namespace AutoTyper;

internal sealed class ScreenshotShareClient(HttpClient client)
{
    internal const int MaximumPngBytes = 20 * 1024 * 1024;
    internal static HttpClient CreateHttpClient() => new(new HttpClientHandler { AllowAutoRedirect = false })
        { Timeout = TimeSpan.FromSeconds(60) };

    internal async Task<Uri> UploadAsync(byte[] png, SharingSettings settings, CancellationToken cancellationToken)
    {
        settings.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        if (png.Length > MaximumPngBytes) throw new InvalidOperationException("The screenshot exceeds 20 MB. Select a smaller region.");
        if (png.Length < 8 || !png.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            throw new InvalidOperationException("The screenshot is not a valid PNG image.");
        string key = "share/" + Guid.NewGuid().ToString("N") + ".png";
        var uri = new Uri($"https://{settings.EndpointHost}/{settings.BucketName}/{key}");
        using var request = new HttpRequestMessage(HttpMethod.Put, uri);
        request.Content = new ByteArrayContent(png);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        request.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("inline");
        Sign(request, png, settings.AccessKeyId, settings.SecretAccessKey, DateTimeOffset.UtcNow);
        using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException(response.StatusCode switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "R2 authorization failed. Check your keys, bucket permissions and PC clock in Settings.",
                HttpStatusCode.NotFound => "R2 bucket not found. Check the account ID, bucket name and jurisdiction in Settings.",
                HttpStatusCode.RequestEntityTooLarge => "The screenshot exceeds the upload limit. Select a smaller region.",
                HttpStatusCode.TooManyRequests => "Too many uploads. Try again later.",
                _ => "R2 could not accept the screenshot. Check Settings or try again."
            });
        return new Uri(new Uri(settings.PublicBaseUrl.TrimEnd('/') + "/"), key);
    }

    // AWS Signature V4, R2's 'auto' region. Only fixed, unescaped object paths are generated above.
    internal static void Sign(HttpRequestMessage request, byte[] payload, string accessKey, string secretKey, DateTimeOffset now)
    {
        string date = now.UtcDateTime.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        string timestamp = now.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        string hash = Hex(SHA256.HashData(payload));
        request.Headers.Host = request.RequestUri!.Host;
        request.Headers.Add("x-amz-content-sha256", hash);
        request.Headers.Add("x-amz-date", timestamp);
        const string signedHeaders = "host;x-amz-content-sha256;x-amz-date";
        string canonical = request.Method.Method + "\n" + request.RequestUri.AbsolutePath + "\n\n"
            + "host:" + request.Headers.Host + "\nx-amz-content-sha256:" + hash + "\nx-amz-date:" + timestamp + "\n\n"
            + signedHeaders + "\n" + hash;
        string scope = date + "/auto/s3/aws4_request";
        string toSign = "AWS4-HMAC-SHA256\n" + timestamp + "\n" + scope + "\n" + Hex(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        byte[] seed = Encoding.UTF8.GetBytes("AWS4" + secretKey);
        byte[] dateKey = Hmac(seed, date), regionKey = Hmac(dateKey, "auto"), serviceKey = Hmac(regionKey, "s3"), signingKey = Hmac(serviceKey, "aws4_request");
        try
        {
            request.Headers.TryAddWithoutValidation("Authorization", $"AWS4-HMAC-SHA256 Credential={accessKey}/{scope}, SignedHeaders={signedHeaders}, Signature={Hex(Hmac(signingKey, toSign))}");
        }
        finally
        {
            foreach (byte[] key in new[] { seed, dateKey, regionKey, serviceKey, signingKey }) CryptographicOperations.ZeroMemory(key);
        }
    }
    private static byte[] Hmac(byte[] key, string value) => HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(value));
    private static string Hex(byte[] value) => Convert.ToHexString(value).ToLowerInvariant();
}
