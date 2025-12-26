using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.Net.Http.Headers;
using TransportPlanner.Api.Options;
using TransportPlanner.Api.Services.AuditTrail;
using TransportPlanner.Application.DTOs;

namespace TransportPlanner.Api.Middleware;

public class AuditTrailMiddleware
{
    private static readonly HashSet<string> ExcludedPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/api/auth/login",
        "/api/audit-trail"
    };

    private readonly RequestDelegate _next;

    public AuditTrailMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext context,
        IAuditTrailStore store,
        Microsoft.Extensions.Options.IOptions<AuditTrailOptions> options,
        ILogger<AuditTrailMiddleware> logger)
    {
        if (IsExcluded(context.Request.Path))
        {
            await _next(context);
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        string? body = null;
        var bodyTruncated = false;
        string? responseBody = null;
        var responseBodyTruncated = false;

        if (ShouldCaptureBody(context.Request))
        {
            context.Request.EnableBuffering();
        }

        var originalBody = context.Response.Body;
        await using var responseBuffer = new MemoryStream();
        context.Response.Body = responseBuffer;

        await _next(context);
        stopwatch.Stop();

        if (ShouldCaptureBody(context.Request))
        {
            (body, bodyTruncated) = await ReadBodyAsync(context.Request, options.Value.MaxBodyBytes, context.RequestAborted);
        }

        (responseBody, responseBodyTruncated) = await ReadResponseBodyAsync(context.Response, responseBuffer, options.Value.MaxBodyBytes, context.RequestAborted);
        responseBuffer.Position = 0;
        await responseBuffer.CopyToAsync(originalBody, context.RequestAborted);
        context.Response.Body = originalBody;

        try
        {
            var entry = BuildEntry(context, stopwatch.ElapsedMilliseconds, body, bodyTruncated, responseBody, responseBodyTruncated);
            await store.WriteAsync(entry, context.RequestAborted);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to write audit trail entry: {Message}", ex.Message);
        }
    }

    private static bool IsExcluded(PathString path)
    {
        var value = path.Value;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return ExcludedPaths.Any(p => value.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ShouldCaptureBody(HttpRequest request)
    {
        if (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method))
        {
            return false;
        }

        var contentType = request.ContentType ?? string.Empty;

        if (contentType.Contains("multipart/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (contentType.Contains("octet-stream", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static async Task<(string? Body, bool Truncated)> ReadBodyAsync(HttpRequest request, int maxBytes, CancellationToken cancellationToken)
    {
        request.EnableBuffering();
        request.Body.Position = 0;

        var encoding = GetEncoding(request.ContentType);
        var buffer = new byte[8192];
        var total = 0;
        var truncated = false;

        using var stream = new MemoryStream();
        while (true)
        {
            var remaining = (maxBytes + 1) - total;
            if (remaining <= 0)
            {
                truncated = true;
                break;
            }

            var read = await request.Body.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, remaining)), cancellationToken);
            if (read == 0)
            {
                break;
            }

            await stream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            total += read;
        }

        request.Body.Position = 0;

        if (stream.Length == 0)
        {
            return (null, false);
        }

        var bytes = stream.ToArray();
        if (bytes.Length > maxBytes)
        {
            truncated = true;
            bytes = bytes.Take(maxBytes).ToArray();
        }

        var content = encoding.GetString(bytes);
        return (string.IsNullOrWhiteSpace(content) ? null : content, truncated);
    }

    private static Encoding GetEncoding(string? contentType)
    {
        if (!MediaTypeHeaderValue.TryParse(contentType, out var parsed) || !parsed.Charset.HasValue)
        {
            return Encoding.UTF8;
        }

        try
        {
            return Encoding.GetEncoding(parsed.Charset.Value);
        }
        catch
        {
            return Encoding.UTF8;
        }
    }

    private static AuditTrailEntryDto BuildEntry(
        HttpContext context,
        long elapsedMs,
        string? body,
        bool bodyTruncated,
        string? responseBody,
        bool responseBodyTruncated)
    {
        var user = context.User;
        var roles = user?.FindAll(ClaimTypes.Role).Select(r => r.Value).Distinct().ToList() ?? new List<string>();
        var userId = Guid.TryParse(user?.FindFirstValue("uid"), out var uid) ? uid : (Guid?)null;
        var ownerId = int.TryParse(user?.FindFirstValue("ownerId"), out var owner) ? owner : (int?)null;
        ownerId ??= ResolveOwnerId(context, body);
        var userEmail = user?.FindFirstValue(ClaimTypes.Email);
        var userName = user?.FindFirstValue(ClaimTypes.Name) ?? user?.FindFirstValue(JwtRegisteredClaimNames.UniqueName);

        return new AuditTrailEntryDto
        {
            TimestampUtc = DateTime.UtcNow,
            Method = context.Request.Method,
            Path = context.Request.Path.Value ?? string.Empty,
            Query = context.Request.QueryString.HasValue ? context.Request.QueryString.Value : null,
            StatusCode = context.Response.StatusCode,
            DurationMs = (int)Math.Min(int.MaxValue, elapsedMs),
            UserId = userId,
            UserEmail = userEmail,
            UserName = userName,
            Roles = roles,
            OwnerId = ownerId,
            IpAddress = ResolveIpAddress(context),
            UserAgent = context.Request.Headers.UserAgent.ToString(),
            Endpoint = context.GetEndpoint()?.DisplayName,
            RequestHeaders = BuildHeadersJson(context.Request.Headers),
            Body = body,
            BodyTruncated = bodyTruncated,
            ResponseBody = responseBody,
            ResponseBodyTruncated = responseBodyTruncated,
            TraceId = context.TraceIdentifier
        };
    }

    private static int? ResolveOwnerId(HttpContext context, string? body)
    {
        if (context.Request.Query.TryGetValue("ownerId", out var ownerQuery) &&
            int.TryParse(ownerQuery.ToString(), out var ownerFromQuery))
        {
            return ownerFromQuery;
        }

        if (context.Request.RouteValues.TryGetValue("ownerId", out var ownerRoute) &&
            int.TryParse(ownerRoute?.ToString(), out var ownerFromRoute))
        {
            return ownerFromRoute;
        }

        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                if (TryGetOwnerId(root, "ownerId", out var ownerFromBody))
                {
                    return ownerFromBody;
                }
                if (TryGetOwnerId(root, "ownerIdForDriver", out ownerFromBody))
                {
                    return ownerFromBody;
                }
                if (TryGetOwnerId(root, "ownerIdForStaff", out ownerFromBody))
                {
                    return ownerFromBody;
                }
            }
            catch
            {
                // Ignore JSON parsing issues.
            }
        }

        return null;
    }

    private static bool TryGetOwnerId(JsonElement root, string propertyName, out int ownerId)
    {
        ownerId = 0;
        if (!root.TryGetProperty(propertyName, out var prop))
        {
            return false;
        }

        if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var numeric))
        {
            ownerId = numeric;
            return true;
        }

        if (prop.ValueKind == JsonValueKind.String && int.TryParse(prop.GetString(), out var parsed))
        {
            ownerId = parsed;
            return true;
        }

        return false;
    }

    private static readonly HashSet<string> AllowedHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Content-Type",
        "Content-Length",
        "User-Agent",
        "Referer",
        "Origin",
        "X-Forwarded-For",
        "X-Request-Id"
    };

    private static string? BuildHeadersJson(IHeaderDictionary headers)
    {
        var filtered = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var header in headers)
        {
            if (!AllowedHeaders.Contains(header.Key))
            {
                continue;
            }

            if (IsSensitiveHeader(header.Key))
            {
                continue;
            }

            var value = string.Join(", ", header.Value.ToArray());
            if (!string.IsNullOrWhiteSpace(value))
            {
                filtered[header.Key] = value;
            }
        }

        if (filtered.Count == 0)
        {
            return null;
        }

        return JsonSerializer.Serialize(filtered);
    }

    private static bool IsSensitiveHeader(string headerName)
    {
        return headerName.Equals("Authorization", StringComparison.OrdinalIgnoreCase)
               || headerName.Equals("Cookie", StringComparison.OrdinalIgnoreCase)
               || headerName.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase)
               || headerName.Equals("X-Api-Key", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<(string? Body, bool Truncated)> ReadResponseBodyAsync(
        HttpResponse response,
        MemoryStream buffer,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        if (!ShouldCaptureResponseBody(response))
        {
            return (null, false);
        }

        buffer.Position = 0;
        using var reader = new StreamReader(buffer, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        var chars = new char[maxBytes];
        var read = await reader.ReadBlockAsync(chars, 0, chars.Length);
        var content = new string(chars, 0, read);
        var truncated = !reader.EndOfStream;
        return (string.IsNullOrWhiteSpace(content) ? null : content, truncated);
    }

    private static bool ShouldCaptureResponseBody(HttpResponse response)
    {
        if (response.ContentType is null)
        {
            return false;
        }

        return response.ContentType.Contains("json", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ResolveIpAddress(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue("X-Forwarded-For", out var forwarded))
        {
            var raw = forwarded.ToString();
            if (!string.IsNullOrWhiteSpace(raw))
            {
                var first = raw.Split(',')[0].Trim();
                if (!string.IsNullOrWhiteSpace(first))
                {
                    return first;
                }
            }
        }

        var remote = context.Connection.RemoteIpAddress;
        if (remote == null || System.Net.IPAddress.IsLoopback(remote))
        {
            return null;
        }

        return remote.ToString();
    }
}
