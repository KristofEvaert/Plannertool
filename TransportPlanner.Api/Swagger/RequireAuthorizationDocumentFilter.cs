using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace TransportPlanner.Api.Swagger;

public class RequireAuthorizationDocumentFilter : IDocumentFilter
{
    public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
    {
        swaggerDoc.Security ??= new List<OpenApiSecurityRequirement>();

        swaggerDoc.Security.Add(new OpenApiSecurityRequirement
        {
            {
                new OpenApiSecuritySchemeReference("Bearer", swaggerDoc, string.Empty),
                new List<string>()
            }
        });
    }
}
