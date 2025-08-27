using Flurl.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Text.Json;

/// <summary>
/// Utility helpers for Twitter API interactions (OAuth1 signing, media upload, tweet posting).
/// </summary>
static class TwitterUtils
{
    private const string UploadUrl = "https://upload.twitter.com/1.1/media/upload.json";
    private const string TweetUrl = "https://api.twitter.com/2/tweets";

    public static async Task<string?> UploadMediaAsync(string filePath, string consumerKey, string consumerSecret, string accessToken, string accessTokenSecret)
    {
        if (!File.Exists(filePath)) throw new FileNotFoundException("Media file not found", filePath);

        var auth = BuildOAuth1Header("POST", UploadUrl, consumerKey, consumerSecret, accessToken, accessTokenSecret, null);

        var resp = await UploadUrlWithAuth(UploadUrl, auth, filePath);

        if (resp.StatusCode < 200 || resp.StatusCode >= 300)
        {
            var body = await resp.GetStringAsync();
            Console.Error.WriteLine($"Media upload failed. HTTP {resp.StatusCode}");
            Console.Error.WriteLine(body);
            return null;
        }

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

    public static async Task<string> PostTweetAsync(string text, string[]? mediaIds, string consumerKey, string consumerSecret, string accessToken, string accessTokenSecret)
    {
        object payload;
        if (mediaIds != null && mediaIds.Length > 0)
        {
            payload = new { text = text, media = new { media_ids = mediaIds } };
        }
        else
        {
            payload = new { text = text };
        }

        var auth = BuildOAuth1Header("POST", TweetUrl, consumerKey, consumerSecret, accessToken, accessTokenSecret, null);

        var resp = await TweetUrl
            .WithHeader("Authorization", auth)
            .WithHeader("Content-Type", "application/json")
            .PostJsonAsync(payload);

        var body = await resp.GetStringAsync();
        if (resp.StatusCode < 200 || resp.StatusCode >= 300)
        {
            Console.Error.WriteLine($"Tweet post failed. HTTP {resp.StatusCode}");
            Console.Error.WriteLine(body);
            throw new Exception("Tweet post failed: " + body);
        }

        return body;
    }

    // Small helper to post multipart upload with auth header
    private static async Task<Flurl.Http.IFlurlResponse> UploadUrlWithAuth(string uploadUrl, string authHeader, string filePath)
    {
        return await uploadUrl
            .WithHeader("Authorization", authHeader)
            .PostMultipartAsync(mp => mp.AddFile("media", filePath));
    }

    // OAuth1 header builder (same algorithm as before)
    private static string BuildOAuth1Header(string httpMethod, string url, string consumerKey, string consumerSecret, string token, string tokenSecret, IDictionary<string, string>? extraParams = null)
    {
        string oauthNonce = Guid.NewGuid().ToString("N");
        string oauthTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

        var oauthParams = new Dictionary<string, string>
        {
            { "oauth_consumer_key", consumerKey ?? string.Empty },
            { "oauth_nonce", oauthNonce },
            { "oauth_signature_method", "HMAC-SHA1" },
            { "oauth_timestamp", oauthTimestamp },
            { "oauth_token", token ?? string.Empty },
            { "oauth_version", "1.0" }
        };

        var allParams = new List<KeyValuePair<string, string>>();
        foreach (var kv in oauthParams) allParams.Add(new KeyValuePair<string, string>(kv.Key, kv.Value));
        if (extraParams != null)
        {
            foreach (var kv in extraParams) allParams.Add(new KeyValuePair<string, string>(kv.Key, kv.Value));
        }

        var encodedParams = allParams
            .Select(kv => new { Key = PercentEncode(kv.Key), Value = PercentEncode(kv.Value) })
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .ThenBy(kv => kv.Value, StringComparer.Ordinal)
            .ToList();

        var parameterString = string.Join("&", encodedParams.Select(kv => kv.Key + "=" + kv.Value));

        var uri = new Uri(url);
        var baseUrl = uri.Scheme + "://" + uri.Host + uri.AbsolutePath;

        var baseString = string.Join("&", new[] { httpMethod.ToUpperInvariant(), PercentEncode(baseUrl), PercentEncode(parameterString) });

        var signingKey = PercentEncode(consumerSecret) + "&" + PercentEncode(tokenSecret ?? string.Empty);

        using var hasher = new HMACSHA1(Encoding.UTF8.GetBytes(signingKey));
        var signatureBytes = hasher.ComputeHash(Encoding.UTF8.GetBytes(baseString));
        var signature = Convert.ToBase64String(signatureBytes);

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

        var headerParams = new SortedDictionary<string, string>(oauthParams);
        headerParams["oauth_signature"] = signature;

        var header = "OAuth " + string.Join(", ", headerParams.Select(kv => $"{PercentEncode(kv.Key)}=\"{PercentEncode(kv.Value)}\""));
        return header;
    }

    private static string PercentEncode(string s)
    {
        if (s == null) return string.Empty;
        return Uri.EscapeDataString(s).Replace("%7E", "~");
    }
}
