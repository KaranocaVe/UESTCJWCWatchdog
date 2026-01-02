using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Watchdog.Core.Notifications;

public static partial class NtfyClient
{
    private static readonly HttpClient Http = new();

    [JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
    [JsonSerializable(typeof(PublishResponseDto))]
    [JsonSerializable(typeof(SubscribeEventDto))]
    private partial class NtfyJsonContext : JsonSerializerContext
    {
    }

    public sealed record PublishResult(
        string Id,
        DateTimeOffset Time,
        string Topic,
        string Message,
        string? Title);

    public sealed record LatestMessageResult(
        string Id,
        DateTimeOffset Time,
        string Topic,
        string Message,
        string? Title);

    private sealed class PublishResponseDto
    {
        public string? Id { get; init; }
        public long? Time { get; init; }
        public string? Topic { get; init; }
        public string? Message { get; init; }
        public string? Title { get; init; }
    }

    private sealed class SubscribeEventDto
    {
        public string? Id { get; init; }
        public long? Time { get; init; }
        public string? Topic { get; init; }
        public string? Event { get; init; }
        public string? Message { get; init; }
        public string? Title { get; init; }
    }

    public static async Task<PublishResult> SendAsync(
        string serverBaseUrl,
        string topic,
        string message,
        string? title = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(topic))
            throw new ArgumentException("Topic is required.", nameof(topic));

        var baseUrlRaw = string.IsNullOrWhiteSpace(serverBaseUrl) ? "https://ntfy.sh" : serverBaseUrl.Trim();
        var baseUrl = NormalizeBaseUrl(baseUrlRaw);
        var allowHttpFallback = ShouldTryHttpFallback(baseUrl);
        var url = BuildPublishUrl(baseUrl, topic, title);

        try
        {
            return await SendOnceAsync(
                url,
                message,
                cancellationToken);
        }
        catch (Exception ex) when (
            allowHttpFallback &&
            baseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
            ex is HttpRequestException or InvalidOperationException)
        {
            // Some environments (e.g. TLS-intercepting proxies) may respond with a 2xx that isn't ntfy.
            // Retry with http:// for ntfy.sh-style deployments that support it.
            var httpBaseUrl = $"http://{baseUrl[8..]}";
            var httpUrl = BuildPublishUrl(httpBaseUrl, topic, title);
            return await SendOnceAsync(
                httpUrl,
                message,
                cancellationToken);
        }
    }

    public static async Task<LatestMessageResult?> GetLatestMessageAsync(
        string serverBaseUrl,
        string topic,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(topic))
            throw new ArgumentException("Topic is required.", nameof(topic));

        var baseUrlRaw = string.IsNullOrWhiteSpace(serverBaseUrl) ? "https://ntfy.sh" : serverBaseUrl.Trim();
        var baseUrl = NormalizeBaseUrl(baseUrlRaw);
        var allowHttpFallback = ShouldTryHttpFallback(baseUrl);
        var url = BuildSubscribeLatestUrl(baseUrl, topic);

        try
        {
            return await GetLatestMessageOnceAsync(
                url,
                cancellationToken);
        }
        catch (Exception ex) when (
            allowHttpFallback &&
            baseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
            ex is HttpRequestException or InvalidOperationException)
        {
            var httpBaseUrl = $"http://{baseUrl[8..]}";
            var httpUrl = BuildSubscribeLatestUrl(httpBaseUrl, topic);
            return await GetLatestMessageOnceAsync(
                httpUrl,
                cancellationToken);
        }
    }

    private static string BuildPublishUrl(string baseUrl, string topic, string? title)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
            throw new ArgumentException("Invalid ntfy server base URL.", nameof(baseUrl));

        var topicSegment = topic.Trim().TrimStart('/');
        if (topicSegment.Length == 0)
            throw new ArgumentException("Topic is required.", nameof(topic));

        var builder = new UriBuilder(new Uri(baseUri, topicSegment));
        if (!string.IsNullOrWhiteSpace(title))
            builder.Query = $"title={Uri.EscapeDataString(title)}";

        return builder.Uri.ToString();
    }

    private static string BuildSubscribeLatestUrl(string baseUrl, string topic)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri))
            throw new ArgumentException("Invalid ntfy server base URL.", nameof(baseUrl));

        var topicSegment = topic.Trim().TrimStart('/');
        if (topicSegment.Length == 0)
            throw new ArgumentException("Topic is required.", nameof(topic));

        var builder = new UriBuilder(new Uri(baseUri, $"{topicSegment}/json"))
        {
            Query = "poll=1&since=latest",
        };

        return builder.Uri.ToString();
    }

    private static bool ShouldTryHttpFallback(string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
            return false;

        return string.Equals(uri.Host, "ntfy.sh", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeBaseUrl(string baseUrl)
    {
        var trimmed = baseUrl.Trim();
        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return trimmed;

        return $"https://{trimmed}";
    }

    private static async Task<LatestMessageResult?> GetLatestMessageOnceAsync(
        string url,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);

        request.Version = HttpVersion.Version11;
        request.VersionPolicy = HttpVersionPolicy.RequestVersionExact;

        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: true);

        SubscribeEventDto? lastMessage = null;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var line = await reader.ReadLineAsync();
            if (line is null)
                break;
            if (string.IsNullOrWhiteSpace(line))
                continue;

            SubscribeEventDto? dto;
            try
            {
                dto = JsonSerializer.Deserialize(line, NtfyJsonContext.Default.SubscribeEventDto);
            }
            catch (JsonException)
            {
                continue;
            }

            if (dto is null)
                continue;

            if (!string.Equals(dto.Event, "message", StringComparison.OrdinalIgnoreCase))
                continue;

            if (string.IsNullOrWhiteSpace(dto.Id) || dto.Time is null || string.IsNullOrWhiteSpace(dto.Topic))
                continue;

            lastMessage = dto;
        }

        if (lastMessage is null)
            return null;

        return new LatestMessageResult(
            Id: lastMessage.Id!,
            Time: DateTimeOffset.FromUnixTimeSeconds(lastMessage.Time!.Value),
            Topic: lastMessage.Topic!,
            Message: lastMessage.Message ?? string.Empty,
            Title: lastMessage.Title);
    }

    private static async Task<PublishResult> SendOnceAsync(
        string url,
        string message,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(message ?? string.Empty, Encoding.UTF8, "text/plain"),
        };

        request.Version = HttpVersion.Version11;
        request.VersionPolicy = HttpVersionPolicy.RequestVersionExact;

        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await Http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        PublishResponseDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize(body, NtfyJsonContext.Default.PublishResponseDto);
        }
        catch (JsonException ex)
        {
            var preview = BuildPreview(body);
            throw new InvalidOperationException($"Invalid response from ntfy server ({url}). {ex.Message} Body: {preview}", ex);
        }

        if (dto is null || string.IsNullOrWhiteSpace(dto.Id) || dto.Time is null || string.IsNullOrWhiteSpace(dto.Topic))
        {
            var preview = BuildPreview(body);
            throw new InvalidOperationException($"Unexpected response from ntfy server ({url}). Body: {preview}");
        }

        return new PublishResult(
            Id: dto.Id,
            Time: DateTimeOffset.FromUnixTimeSeconds(dto.Time.Value),
            Topic: dto.Topic,
            Message: dto.Message ?? string.Empty,
            Title: dto.Title);
    }

    private static string BuildPreview(string body)
    {
        var normalized = body.ReplaceLineEndings(" ").Trim();
        const int maxLength = 220;
        return normalized.Length <= maxLength ? normalized : $"{normalized[..maxLength]}…";
    }
}
