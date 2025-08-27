using Flurl.Http;
using System;
using System.Text;
using System.Threading.Tasks;

class Program
{
    static async Task Main(string[] args)
    {
        // Prefer OAuth1 user credentials (required to POST tweets)
        var consumerKey = Environment.GetEnvironmentVariable("TWITTER_CONSUMER_KEY");
        var consumerSecret = Environment.GetEnvironmentVariable("TWITTER_CONSUMER_SECRET");
        var accessToken = Environment.GetEnvironmentVariable("TWITTER_ACCESS_TOKEN");
        var accessTokenSecret = Environment.GetEnvironmentVariable("TWITTER_ACCESS_TOKEN_SECRET");

        var hashtags = new[] { "BharosaTumneTodaHai", "DoubleBharosaTodaHai" };

        int daysSince = DateUtils.DaysSince(new DateTime(2024, 6, 27));

        // Build tweet text using StringBuilder and include emojis for emphasis and readability
        var sb = new StringBuilder();
        sb.AppendLine("🔔 Gentle reminder @AxisMaxLifeIns");
        sb.AppendLine();
        sb.AppendFormat("⚠️ It has been {0} days since you overcharged me INR 352000.", daysSince);
        sb.AppendLine();
        sb.AppendLine();
        sb.AppendLine("📄 IRDAI Token no.07-25-012692");
        sb.AppendLine();
        foreach (var tag in hashtags)
        {
            sb.AppendLine("🔹 #" + tag);
        }

        string tweetText = sb.ToString();

        Console.WriteLine("Tweet text:");
        Console.WriteLine(tweetText);

        // Hardcoded image path for now - change to any local image path on your system
        var imagePath = "/Users/nilesh/Dropbox/Axis Max Life Escalation/response-pradeep-kumar-Saturday-23-Aug-2025-05.png"; // <-- update this path

        try
        {
            if (!string.IsNullOrWhiteSpace(consumerKey) && !string.IsNullOrWhiteSpace(consumerSecret)
                && !string.IsNullOrWhiteSpace(accessToken) && !string.IsNullOrWhiteSpace(accessTokenSecret))
            {
                // 1) Upload media to Twitter via TwitterUtils (v1.1 upload endpoint under the hood)
                if (!System.IO.File.Exists(imagePath))
                {
                    Console.Error.WriteLine($"Image not found at path: {imagePath}");
                    return;
                }

                Console.WriteLine($"Uploading media from: {imagePath}");
                var mediaId = await TwitterUtils.UploadMediaAsync(imagePath, consumerKey, consumerSecret, accessToken, accessTokenSecret);
                if (string.IsNullOrWhiteSpace(mediaId))
                {
                    Console.Error.WriteLine("Failed to upload media, no media_id returned.");
                    return;
                }

                Console.WriteLine($"Media uploaded successfully, media_id: {mediaId}");

                // 2) Post the tweet referencing the uploaded media
                Console.WriteLine("Posting tweet with media...");
                var tweetResponse = await TwitterUtils.PostTweetAsync(tweetText, new[] { mediaId }, consumerKey, consumerSecret, accessToken, accessTokenSecret);

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
            Console.Error.WriteLine("Failed to post/upload media or parse response:");
            Console.Error.WriteLine(err);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Unexpected error:");
            Console.Error.WriteLine(ex.ToString());
        }
    }
}

