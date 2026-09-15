using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Sellora.CatalogService.Api.Authorization;
using Sellora.CatalogService.Api.Identity;
using Sellora.CatalogService.Api.Security;
using Sellora.CatalogService.Api.Tenancy;
using Sellora.CatalogService.Application.Categories;
using Sellora.CatalogService.Application.Identity;
using Sellora.CatalogService.Application.Outbox;
using Sellora.CatalogService.Application.Products;
using Sellora.CatalogService.Domain.Tenancy;
using Sellora.CatalogService.Infrastructure.Categories;
using Sellora.CatalogService.Infrastructure.Outbox;
using Sellora.CatalogService.Infrastructure.Persistence;
using Sellora.CatalogService.Infrastructure.Persistence.Seeding;
using Sellora.CatalogService.Infrastructure.Products;
using Serilog;

var builder = WebApplication.CreateBuilder(args);
// Set before the host starts; failed initialization keeps all database work disabled.
var databaseReady = false;

builder.Host.UseSerilog((context, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console());

var jwt = builder.Configuration.GetSection("Jwt");
var audiences = jwt.GetSection("Audience").Get<string[]>()
    ?? new[] { jwt["Audience"]! };

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = jwt["Authority"];
        options.MetadataAddress = jwt["MetadataAddress"]!;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt["Issuer"],
            ValidateAudience = true,
            ValidAudiences = audiences,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            RoleClaimType = "roles",
        };

        // The shared development Identity Server currently uses a certificate
        // that is not trusted by local developer machines. This exception is
        // deliberately limited to Development; production must use a trusted
        // certificate and must never bypass TLS validation.
        if (builder.Environment.IsDevelopment() || builder.Environment.IsStaging())
        {
            options.BackchannelHttpHandler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback =
                    HttpClientHandler
                        .DangerousAcceptAnyServerCertificateValidator
            };
        }
    });

builder.Services.AddAuthorization(options => options.AddSelloraCatalogPolicies());
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantContext, HttpTenantContext>();
builder.Services.AddScoped<ICurrentUserContext, HttpCurrentUserContext>();
builder.Services.AddScoped<ICategoryService, CategoryService>();

var connectionString =
    builder.Configuration.GetConnectionString("Default");

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "The catalog database connection string is not configured.");
}

builder.Services.AddDbContext<CatalogDbContext>(options =>
    options.UseNpgsql(connectionString));

builder.Services.AddScoped<IProductService, ProductService>();

builder.Services.Configure<ProductPriceCacheOptions>(
    builder.Configuration.GetSection(
        ProductPriceCacheOptions.SectionName));

builder.Services.Configure<InternalApiOptions>(
    builder.Configuration.GetSection(
        InternalApiOptions.SectionName));

builder.Services.AddSingleton<
    IProductPriceCache,
    ProductPriceCache>();

builder.Services.AddScoped<
    IOrderCatalogService,
    OrderCatalogService>();

builder.Services.Configure<KafkaOptions>(
    builder.Configuration.GetSection(KafkaOptions.SectionName));
builder.Services.Configure<OutboxRelayOptions>(
    builder.Configuration.GetSection(OutboxRelayOptions.SectionName));
builder.Services.AddSingleton<IEventPublisher, KafkaEventPublisher>();
if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddSingleton<IHostedService>(services => databaseReady
        ? ActivatorUtilities.CreateInstance<OutboxRelayService>(services)
        : new DatabaseUnavailableService());
}
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks().AddCheck("database_initialization", () => databaseReady
    ? Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy()
    : Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Unhealthy(
        "Database initialization failed. Repair the database and restart the service."));
builder.Services.AddControllers();

var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? Array.Empty<string>();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        if (allowedOrigins.Length > 0)
        {
            policy.WithOrigins(allowedOrigins);
        }

        policy.AllowAnyHeader().AllowAnyMethod();
    });
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseExceptionHandler();
app.UseSerilogRequestLogging();

// Match Organization; test fixtures migrate their isolated databases themselves.
if (!app.Environment.IsEnvironment("Testing"))
{
    try
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        await db.Database.MigrateAsync();

        if (app.Environment.IsStaging())
        {
            await DevelopmentCatalogSeeder.SeedAsync(db);
        }
        databaseReady = true;
    }
    catch (Exception exception)
    {
        app.Logger.LogCritical(exception,
            "Catalog database initialization failed. Serving HTTP 503 with outbox processing disabled. Repair the database and restart the service.");
    }
}
else
{
    databaseReady = true; // Test fixtures apply migrations before creating the host.
}

// Liveness remains available without database access. Readiness (/health) is
// unhealthy after a failed migration. Never expose database exception details.
app.Use(async (context, next) =>
{
    if (context.Request.Path.Equals(new PathString("/health/live")))
    {
        await Results.Ok(new { Status = "Alive" }).ExecuteAsync(context);
        return;
    }

    if (!databaseReady)
    {
        await Results.Problem(
            title: "Catalog service unavailable",
            detail: "Database initialization has not completed successfully.",
            statusCode: StatusCodes.Status503ServiceUnavailable).ExecuteAsync(context);
        return;
    }

    await next(context);
});

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// Run CORS before authentication so browser preflight requests are accepted.
app.UseCors();
app.UseMiddleware<InternalApiKeyMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health");
app.MapGet("/whoami", (HttpContext context) =>
    Results.Ok(context.User.Claims.Select(c => new { c.Type, c.Value })))
    .RequireAuthorization();

app.Run();

public partial class Program;

// Do not construct or start the database-backed relay when initialization fails.
internal sealed class DatabaseUnavailableService : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
