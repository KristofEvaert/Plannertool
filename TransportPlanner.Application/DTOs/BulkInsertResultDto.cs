using System.Collections.Generic;

namespace TransportPlanner.Application.DTOs;

public class BulkInsertResultDto
{
    public int Inserted { get; set; }
    public int Updated { get; set; }
    public int Skipped { get; set; }
    public List<BulkErrorDto> Errors { get; set; } = new List<BulkErrorDto>();
    public List<BulkServiceLocationFailedItem> FailedItems { get; set; } = new();
}

