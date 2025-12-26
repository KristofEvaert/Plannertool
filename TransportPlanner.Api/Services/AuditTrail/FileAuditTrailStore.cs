using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TransportPlanner.Api.Options;
using TransportPlanner.Application.DTOs;

namespace TransportPlanner.Api.Services.AuditTrail;

public class FileAuditTrailStore : IAuditTrailStore
{
    private readonly AuditTrailOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public FileAuditTrailStore(Microsoft.Extensions.Options.IOptions<AuditTrailOptions> options)
    {
        _options = options.Value;
    }

    public async Task WriteAsync(AuditTrailEntryDto entry, CancellationToken cancellationToken)
    {
        var path = ResolvePath();
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var payload = JsonSerializer.Serialize(entry, _jsonOptions) + Environment.NewLine;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await File.AppendAllTextAsync(path, payload, Encoding.UTF8, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<PagedResult<AuditTrailEntryDto>> QueryAsync(AuditTrailQuery query, CancellationToken cancellationToken)
    {
        var path = ResolvePath();
        if (!File.Exists(path))
        {
            return new PagedResult<AuditTrailEntryDto>
            {
                Page = query.Page,
                PageSize = query.PageSize,
                TotalCount = 0
            };
        }

        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 500);
        var skip = (page - 1) * pageSize;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var lines = await File.ReadAllLinesAsync(path, cancellationToken);
            var results = new List<AuditTrailEntryDto>();
            var matched = 0;

            for (var i = lines.Length - 1; i >= 0; i--)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var line = lines[i];
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                AuditTrailEntryDto? entry;
                try
                {
                    entry = JsonSerializer.Deserialize<AuditTrailEntryDto>(line);
                }
                catch
                {
                    continue;
                }

                if (entry == null || !Matches(entry, query))
                {
                    continue;
                }

                matched++;
                if (matched <= skip)
                {
                    continue;
                }

                if (results.Count >= pageSize)
                {
                    continue;
                }

                results.Add(entry);
            }

            return new PagedResult<AuditTrailEntryDto>
            {
                Items = results,
                Page = page,
                PageSize = pageSize,
                TotalCount = matched
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    private bool Matches(AuditTrailEntryDto entry, AuditTrailQuery query)
    {
        if (query.FromUtc.HasValue && entry.TimestampUtc < query.FromUtc.Value)
        {
            return false;
        }

        if (query.ToUtc.HasValue && entry.TimestampUtc > query.ToUtc.Value)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(query.Method) &&
            !string.Equals(entry.Method, query.Method, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (query.StatusCode.HasValue && entry.StatusCode != query.StatusCode.Value)
        {
            return false;
        }

        if (query.UserId.HasValue && entry.UserId != query.UserId.Value)
        {
            return false;
        }

        if (query.OwnerId.HasValue && entry.OwnerId != query.OwnerId.Value)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(query.PathContains) &&
            (string.IsNullOrWhiteSpace(entry.Path) ||
             !entry.Path.Contains(query.PathContains, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(query.UserEmailContains) &&
            (string.IsNullOrWhiteSpace(entry.UserEmail) ||
             !entry.UserEmail.Contains(query.UserEmailContains, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(query.Search) && !MatchesSearch(entry, query.Search))
        {
            return false;
        }

        return true;
    }

    private static bool MatchesSearch(AuditTrailEntryDto entry, string search)
    {
        return Contains(entry.Path, search)
               || Contains(entry.Query, search)
               || Contains(entry.UserEmail, search)
               || Contains(entry.UserName, search)
               || Contains(entry.Endpoint, search)
               || Contains(entry.RequestHeaders, search)
               || Contains(entry.Body, search)
               || Contains(entry.ResponseBody, search)
               || Contains(entry.TraceId, search);
    }

    private static bool Contains(string? value, string search)
    {
        return !string.IsNullOrWhiteSpace(value)
               && value.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private string ResolvePath()
    {
        if (Path.IsPathRooted(_options.Path))
        {
            return _options.Path;
        }

        return Path.Combine(AppContext.BaseDirectory, _options.Path);
    }
}
