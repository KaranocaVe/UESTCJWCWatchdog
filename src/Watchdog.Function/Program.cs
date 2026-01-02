using System.Text.Json;
using Watchdog.Function;

static string? GetEnv(params string[] keys)
{
    foreach (var key in keys)
    {
        var value = Environment.GetEnvironmentVariable(key);
        if (!string.IsNullOrWhiteSpace(value))
            return value.Trim();
    }

    return null;
}

static string? Normalize(string? value)
{
    if (string.IsNullOrWhiteSpace(value))
        return null;
    return value.Trim();
}

static InvokeRequest BuildInvokeRequest(InvokeRequestDto? body, out string? error)
{
    error = null;

    var topic = Normalize(body?.Topic) ?? GetEnv("WATCHDOG_TOPIC", "NTFY_TOPIC", "TOPIC", "topic");
    var account = Normalize(body?.Account) ?? GetEnv("WATCHDOG_ACCOUNT", "ACCOUNT", "account");
    var password = Normalize(body?.Password) ?? GetEnv("WATCHDOG_PASSWORD", "PASSWORD", "password");

    if (string.IsNullOrWhiteSpace(topic))
        error = "Missing topic. Provide request JSON field 'topic' or env WATCHDOG_TOPIC.";
    else if (string.IsNullOrWhiteSpace(account))
        error = "Missing account. Provide request JSON field 'account' or env WATCHDOG_ACCOUNT.";
    else if (password is null)
        error = "Missing password. Provide request JSON field 'password' or env WATCHDOG_PASSWORD.";

    if (error is not null)
        throw new ArgumentException(error);

    var ntfyServerBaseUrl =
        Normalize(body?.NtfyServerBaseUrl) ??
        GetEnv("WATCHDOG_NTFY_SERVER_BASE_URL", "NTFY_SERVER_BASE_URL", "WATCHDOG_NTFY_SERVER", "NTFY_SERVER");

    var semesterId =
        Normalize(body?.SemesterId) ??
        GetEnv("WATCHDOG_SEMESTER_ID", "SEMESTER_ID", "semesterId");

    return new InvokeRequest
    {
        Topic = topic!,
        Account = account!,
        Password = password!,
        NtfyServerBaseUrl = ntfyServerBaseUrl,
        SemesterId = semesterId,
    };
}

var webJson = new JsonSerializerOptions(JsonSerializerDefaults.Web);

static FunctionGraphResponse EnvelopeJson(int statusCode, string bodyJson)
{
    var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["content-type"] = "application/json",
    };

    return new FunctionGraphResponse(
        IsBase64Encoded: false,
        StatusCode: statusCode,
        Headers: headers,
        Body: bodyJson);
}

static FunctionGraphResponse EnvelopeText(int statusCode, string bodyText)
{
    var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["content-type"] = "text/plain; charset=utf-8",
    };

    return new FunctionGraphResponse(
        IsBase64Encoded: false,
        StatusCode: statusCode,
        Headers: headers,
        Body: bodyText ?? string.Empty);
}

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNameCaseInsensitive = true;
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
});

var app = builder.Build();

app.MapGet("/healthz", () => Results.Ok(new { ok = true }));

app.MapPost("/init", () => Results.Json(EnvelopeText(statusCode: 200, bodyText: "ok")));

app.MapPost("/invoke", async (HttpRequest request, CancellationToken cancellationToken) =>
{
    InvokeRequestDto? body = null;
    try
    {
        if (request.ContentLength is > 0)
            body = await JsonSerializer.DeserializeAsync<InvokeRequestDto>(request.Body, webJson, cancellationToken);
    }
    catch
    {
        body = null;
    }

    InvokeRequest invokeRequest;
    try
    {
        invokeRequest = BuildInvokeRequest(body, out _);
    }
    catch (ArgumentException ex)
    {
        return Results.Json(EnvelopeJson(statusCode: 400, bodyJson: JsonSerializer.Serialize(new { error = ex.Message }, webJson)));
    }

    try
    {
        var result = await WatchdogRunner.RunAsync(invokeRequest, cancellationToken);
        return Results.Json(EnvelopeJson(statusCode: 200, bodyJson: JsonSerializer.Serialize(result, webJson)));
    }
    catch (ArgumentException ex)
    {
        return Results.Json(EnvelopeJson(statusCode: 400, bodyJson: JsonSerializer.Serialize(new { error = ex.Message }, webJson)));
    }
    catch (Exception ex)
    {
        return Results.Json(EnvelopeJson(statusCode: 500, bodyJson: JsonSerializer.Serialize(new { error = ex.Message }, webJson)));
    }
});

app.Run();

file sealed record InvokeRequestDto
{
    public string? Topic { get; init; }
    public string? Account { get; init; }
    public string? Password { get; init; }
    public string? NtfyServerBaseUrl { get; init; }
    public string? SemesterId { get; init; }
}

file sealed record FunctionGraphResponse(
    bool IsBase64Encoded,
    int StatusCode,
    IReadOnlyDictionary<string, string> Headers,
    string Body);
