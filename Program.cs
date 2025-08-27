using Flurl.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Text.Json;

class Program
{
    static async Task Main(string[] args)
    {
        // Prefer OAuth1 user credentials (required to POST tweets)
        var consumerKey = Environment.GetEnvironmentVariable("TWITTER_CONSUMER_KEY");
        var consumerSecret = Environment.GetEnvironmentVariable("TWITTER_CONSUMER_SECRET");
        var accessToken = Environment.GetEnvironmentVariable("TWITTER_ACCESS_TOKEN");
        var accessTokenSecret = Environment.GetEnvironmentVariable("TWITTER_ACCESS_TOKEN_SECRET");

        // Bearer token (app-only) is not sufficient to post tweets
        var bearerToken = Environment.GetEnvironmentVariable("TWITTER_BEARER_TOKEN") ?? (args.Length > 0 ? args[0] : null);

        var hashtags = new[] { "BharosaTumneTodaHai", "DoubleBharosaTodaHai" };

        int daysSince = DateUtils.DaysSince(new DateTime(2024, 6, 27));

        // Build tweet text using StringBuilder for clarity and easier maintenance
        var sb = new StringBuilder();
        sb.AppendLine("Gentle reminder @AxisMaxLifeIns");
        sb.AppendLine();
        sb.AppendFormat("It has been {0} days since you overcharged me INR 352000.", daysSince);
        sb.AppendLine();
        sb.AppendLine();
        sb.AppendLine("IRDAI Token no.07-25-012692");
        sb.AppendLine();
        // Append each hashtag on its own line
        foreach (var tag in hashtags)
        {
            sb.AppendLine('#' + tag);
        }

        string tweetText = sb.ToString();

        Console.WriteLine("Tweet text:");
        Console.WriteLine(tweetText);

        // Hardcoded image path for now - change to any local image path on your system
        var imagePath = "/Users/nilesh/Dropbox/Axis Max Life Escalation/response-pradeep-kumar-Saturday-23-Aug-2025-05.png"; // <-- update this path

        var url = "https://api.twitter.com/2/tweets";
        var jsonBody = new { text = tweetText };

        try
        {
            if (!string.IsNullOrWhiteSpace(consumerKey) && !string.IsNullOrWhiteSpace(consumerSecret)
                && !string.IsNullOrWhiteSpace(accessToken) && !string.IsNullOrWhiteSpace(accessTokenSecret))
            {
                // 1) Upload media to Twitter via the v1.1 upload endpoint
                var uploadUrl = "https://upload.twitter.com/1.1/media/upload.json";

                if (!File.Exists(imagePath))
                {
                    Console.Error.WriteLine($"Image not found at path: {imagePath}");
                    return;
                }

                Console.WriteLine($"Uploading media from: {imagePath}");

                // Upload as multipart/form-data (binary). For multipart uploads OAuth1 signature SHOULD NOT include the binary body params,
                // so build the Authorization header without including the media param.
                var mediaId = await UploadMediaAsync(uploadUrl, imagePath, consumerKey, consumerSecret, accessToken, accessTokenSecret);
                if (string.IsNullOrWhiteSpace(mediaId))
                {
                    Console.Error.WriteLine("Failed to upload media, no media_id returned.");
                    return;
                }

                Console.WriteLine($"Media uploaded successfully, media_id: {mediaId}");
                // 2) Post the tweet referencing the uploaded media
                var tweetJson = new
                {
                    text = tweetText,
                    media = new { media_ids = new[] { mediaId } }
                };

                var tweetAuth = BuildOAuth1Header("POST", url, consumerKey, consumerSecret, accessToken, accessTokenSecret);

                Console.WriteLine("Posting tweet with media...");
                var tweetResponse = await url
                    .WithHeader("Authorization", tweetAuth)
                    .WithHeader("Content-Type", "application/json")
                    .PostJsonAsync(tweetJson)
                    .ReceiveString();

                Console.WriteLine("Tweet with image posted successfully:");
                Console.WriteLine(tweetResponse);
                return;
            }

            // If we reach here, OAuth1 credentials are not present
            Console.Error.WriteLine("Error: POST /2/tweets requires user context. An application-only Bearer token cannot create tweets.");
            Console.Error.WriteLine("Provide these environment variables for OAuth 1.0a user context:");
            Console.Error.WriteLine("  TWITTER_CONSUMER_KEY, TWITTER_CONSUMER_SECRET, TWITTER_ACCESS_TOKEN, TWITTER_ACCESS_TOKEN_SECRET");
            Console.Error.WriteLine("Alternatively implement OAuth2 user context (Authorization Code + PKCE) to obtain a user-scoped token with tweet.write scope.");
        }
        catch (Flurl.Http.FlurlHttpException ex)
        {
            var err = await ex.GetResponseStringAsync();
            Console.Error.WriteLine("Failed to post tweet:");
            Console.Error.WriteLine(err);
        }
    }

    static string BuildOAuth1Header(string httpMethod, string url, string consumerKey, string consumerSecret, string token, string tokenSecret, IDictionary<string, string> extraParams = null)
    {
        string oauthNonce = Guid.NewGuid().ToString("N");
        string oauthTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

        // OAuth parameters (do not include oauth_signature)
        var oauthParams = new Dictionary<string, string>
        {
            { "oauth_consumer_key", consumerKey ?? string.Empty },
            { "oauth_nonce", oauthNonce },
            { "oauth_signature_method", "HMAC-SHA1" },
            { "oauth_timestamp", oauthTimestamp },
            { "oauth_token", token ?? string.Empty },
            { "oauth_version", "1.0" }
        };

        // Collect all parameters for the signature base string: oauth params + extra params
        var allParams = new List<KeyValuePair<string, string>>();
        foreach (var kv in oauthParams) allParams.Add(new KeyValuePair<string, string>(kv.Key, kv.Value));
        if (extraParams != null)
        {
            foreach (var kv in extraParams) allParams.Add(new KeyValuePair<string, string>(kv.Key, kv.Value));
        }

        // Percent-encode keys and values, then sort by encoded key then encoded value
        var encodedParams = allParams
            .Select(kv => new { Key = PercentEncode(kv.Key), Value = PercentEncode(kv.Value) })
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .ThenBy(kv => kv.Value, StringComparer.Ordinal)
            .ToList();

        var parameterString = string.Join("&", encodedParams.Select(kv => kv.Key + "=" + kv.Value));

        var uri = new Uri(url);
        // Normalize base URL: scheme://host/path (omit default ports)
        var baseUrl = uri.Scheme + "://" + uri.Host + uri.AbsolutePath;

        var baseString = string.Join("&", new[] { httpMethod.ToUpperInvariant(), PercentEncode(baseUrl), PercentEncode(parameterString) });

        var signingKey = PercentEncode(consumerSecret) + "&" + PercentEncode(tokenSecret ?? string.Empty);

        using var hasher = new HMACSHA1(Encoding.UTF8.GetBytes(signingKey));
        var signatureBytes = hasher.ComputeHash(Encoding.UTF8.GetBytes(baseString));
        var signature = Convert.ToBase64String(signatureBytes);

        // Debug output when requested
        if (Environment.GetEnvironmentVariable("TWITTER_DEBUG") == "1")
        {
            string Mask(string s) => string.IsNullOrEmpty(s) ? string.Empty : (s.Length <= 4 ? new string('*', s.Length) : s.Substring(0, 4) + new string('*', s.Length - 4));
            Console.Error.WriteLine("--- OAuth1 debug ---");
            Console.Error.WriteLine($"HTTP Method: {httpMethod}");
            Console.Error.WriteLine($"Base URL: {baseUrl}");
            Console.Error.WriteLine($"Base string: {baseString}");
            Console.Error.WriteLine($"Signing key (masked): {Mask(consumerSecret)}&{Mask(tokenSecret)}");
            Console.Error.WriteLine($"Signature: {signature}");
            Console.Error.WriteLine("--- end OAuth1 debug ---");
        }

        // Build the Authorization header (only oauth_* params)
        var headerParams = new SortedDictionary<string, string>(oauthParams);
        headerParams["oauth_signature"] = signature;

        var header = "OAuth " + string.Join(", ", headerParams.Select(kv => $"{PercentEncode(kv.Key)}=\"{PercentEncode(kv.Value)}\""));
        return header;
    }

    static string PercentEncode(string s)
    {
        if (s == null) return string.Empty;
        // RFC3986
        return Uri.EscapeDataString(s).Replace("%7E", "~");
    }

    // Upload a file as multipart/form-data to the v1.1 media/upload endpoint and return media_id_string
    static async Task<string?> UploadMediaAsync(string uploadUrl, string filePath, string consumerKey, string consumerSecret, string accessToken, string accessTokenSecret)
    {
        // Build OAuth1 header without including the binary body in the signature (extraParams null)
        var auth = BuildOAuth1Header("POST", uploadUrl, consumerKey, consumerSecret, accessToken, accessTokenSecret, null);

        // Use Flurl to POST multipart content with the file under the 'media' field
        var resp = await uploadUrl
            .WithHeader("Authorization", auth)
            .PostMultipartAsync(mp => mp.AddFile("media", filePath));

        // If upload failed, surface the status and body for debugging
        if (resp.StatusCode < 200 || resp.StatusCode >= 300)
        {
            var body = await resp.GetStringAsync();
            Console.Error.WriteLine($"Media upload failed. HTTP {resp.StatusCode}");
            Console.Error.WriteLine(body);
            return null;
        }

        // Read response body as string and parse using System.Text.Json
        var responseBody = await resp.GetStringAsync();
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;
            if (root.TryGetProperty("media_id_string", out var idStrElem))
            {
                return idStrElem.GetString();
            }
            if (root.TryGetProperty("media_id", out var idElem))
            {
                // numeric id -> convert to string
                return idElem.GetRawText().Trim('"');
            }

            Console.Error.WriteLine("Media upload response did not contain media_id_string or media_id:");
            Console.Error.WriteLine(responseBody);
            return null;
        }
        catch (JsonException je)
        {
            Console.Error.WriteLine("Failed to parse media upload response as JSON:");
            Console.Error.WriteLine(responseBody);
            Console.Error.WriteLine(je.ToString());
            return null;
        }
    }
}

