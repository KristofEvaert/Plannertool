using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Net;
using System.Text;
using System.Text.Json.Serialization;
using TransportPlanner.Application.Exceptions;
using TransportPlanner.Api.Services.Routing;
using TransportPlanner.Infrastructure;
using TransportPlanner.Infrastructure.Data;
using TransportPlanner.Infrastructure.Identity;
using TransportPlanner.Infrastructure.Options;
using TransportPlanner.Api.Middleware;
using TransportPlanner.Api.Options;
using TransportPlanner.Api.Services.AuditTrail;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
        // Allow reading numbers that may come as strings (e.g. "51.0713") from older clients,
        // but DO NOT write numbers as strings (it breaks the Angular map calculations).
        options.JsonSerializerOptions.NumberHandling = JsonNumberHandling.AllowReadingFromString;
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    // Use full type name to avoid conflicts between Route attribute and Route entity
    c.CustomSchemaIds(type => type.FullName?.Replace("+", "."));
    // Ignore entity types in schema generation - only use DTOs
    c.IgnoreObsoleteProperties();
    c.IgnoreObsoleteActions();
});

// Routing (road distances) - OSRM public server by default
builder.Services.AddHttpClient<IRoutingService, OsrmRoutingService>(client =>
{
    client.BaseAddress = new Uri("https://router.project-osrm.org/");
    client.Timeout = TimeSpan.FromSeconds(10);
});

// Add CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(
                "http://localhost:4200", "https://localhost:4200",
                "http://localhost:4201", "https://localhost:4201")
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});

// Add ProblemDetails
builder.Services.AddProblemDetails();

builder.Services.AddDbContext<TransportPlannerDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Register Infrastructure services (keep for DbContext and EF setup)
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.Configure<AuditTrailOptions>(builder.Configuration.GetSection(AuditTrailOptions.SectionName));
builder.Services.AddSingleton<IAuditTrailStore, FileAuditTrailStore>();

var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Key)),
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("RequireStaff", policy => policy.RequireRole(AppRoles.SuperAdmin, AppRoles.Admin, AppRoles.Planner));
    options.AddPolicy("RequireAdmin", policy => policy.RequireRole(AppRoles.SuperAdmin, AppRoles.Admin));
    options.AddPolicy("RequireDriver", policy => policy.RequireRole(AppRoles.Driver));
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// CORS must be early in the pipeline, before other middleware
app.UseCors();

app.UseHttpsRedirection();

app.UseExceptionHandler(exceptionHandlerApp =>
{
    exceptionHandlerApp.Run(async context =>
    {
        context.Response.ContentType = "application/problem+json";
        var exceptionHandlerPathFeature = context.Features.Get<IExceptionHandlerPathFeature>();
        var exception = exceptionHandlerPathFeature?.Error;

        var problemDetails = new ProblemDetails
        {
            Instance = exceptionHandlerPathFeature?.Path,
            Status = (int)HttpStatusCode.InternalServerError,
            Title = "An error occurred",
            Detail = exception?.Message
        };

        switch (exception)
        {
            case NotFoundException:
                problemDetails.Status = (int)HttpStatusCode.NotFound;
                problemDetails.Title = "Resource not found";
                break;
            case ConflictException:
                problemDetails.Status = (int)HttpStatusCode.Conflict;
                problemDetails.Title = "Conflict";
                break;
            case FluentValidation.ValidationException:
                problemDetails.Status = (int)HttpStatusCode.BadRequest;
                problemDetails.Title = "Validation error";
                break;
        }

        context.Response.StatusCode = problemDetails.Status.Value;
        await context.Response.WriteAsJsonAsync(problemDetails);
    });
});

app.UseAuthentication();
app.UseMiddleware<AuditTrailMiddleware>();
app.UseAuthorization();

app.MapControllers();

// Apply database migrations and seed on startup
using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    var dbContext = scope.ServiceProvider.GetRequiredService<TransportPlannerDbContext>();
    
    // Apply migrations
    try
    {
        logger.LogInformation("Applying database migrations...");
        await dbContext.Database.MigrateAsync();
        logger.LogInformation("Database migrations applied successfully.");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error applying database migrations: {Message}", ex.Message);
        // Continue anyway - migrations might already be applied
    }
    
    // Seed database if empty
    try
    {
        logger.LogInformation("Starting database seeding...");
        var seeder = scope.ServiceProvider.GetRequiredService<TransportPlanner.Infrastructure.Seeding.DatabaseSeeder>();
        await seeder.SeedAsync();
        logger.LogInformation("Database seeding completed.");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error seeding database: {Message}", ex.Message);
        // Don't fail startup, but log the error
    }
}

await app.RunAsync();
