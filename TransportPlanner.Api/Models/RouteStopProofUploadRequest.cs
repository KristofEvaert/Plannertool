using Microsoft.AspNetCore.Http;

namespace TransportPlanner.Api.Models;

public class RouteStopProofUploadRequest
{
    public IFormFile File { get; set; } = null!;
}
