using Microsoft.EntityFrameworkCore;
using Sellora.CatalogService.Infrastructure.Persistence;
using Sellora.CatalogService.Infrastructure.Persistence.Seeding;

namespace Sellora.CatalogService.Api.Hosting;

public static class DatabaseInitialization
{
    /// <summary>
    /// Applies pending migrations (and the staging seed). Returns false after
    /// logging when initialization fails so the host can keep serving 503s.
    /// </summary>
    public static async Task<bool> TryInitializeAsync(WebApplication app)
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

            return true;
        }
        catch (Exception exception)
        {
            app.Logger.LogCritical(exception,
                "Catalog database initialization failed. Serving HTTP 503 with outbox processing disabled. Repair the database and restart the service.");
            return false;
        }
    }

    // Liveness remains available without database access. Readiness (/health) is
    // unhealthy after a failed migration. Never expose database exception details.
    public static async Task GateRequestsAsync(
        HttpContext context,
        RequestDelegate next,
        bool databaseReady)
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
    }
}

// Do not construct or start the database-backed relay when initialization fails.
internal sealed class DatabaseUnavailableService : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
