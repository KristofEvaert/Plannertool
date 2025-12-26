using System.Collections.Generic;

using System.Collections.Generic;

namespace TransportPlanner.Application.DTOs;

public class BulkInsertServiceLocationsRequest
{
    public int ServiceTypeId { get; set; } // Required - applies to all items
    public int OwnerId { get; set; } // Required - applies to all items
    public List<BulkServiceLocationInsertDto> Items { get; set; } = new List<BulkServiceLocationInsertDto>();
}

