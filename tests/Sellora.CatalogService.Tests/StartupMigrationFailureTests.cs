using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Sellora.CatalogService.Infrastructure.Outbox;
using Testcontainers.PostgreSql;
using Xunit;

namespace Sellora.CatalogService.Tests;

public sealed class StartupMigrationFailureTests
{
    [Fact]
    public async Task Failed_migration_keeps_host_alive_blocks_requests_and_disables_outbox()
    {
        await using var database = new PostgreSqlBuilder("postgres:16-alpine")
            .WithDatabase("startup_failure_tests")
            .WithUsername("sellora_test")
            .WithPassword("sellora_test_password")
            .Build();
        await database.StartAsync();
        // Force a real migration failure rather than bypassing production startup.
        await using var connection = new NpgsqlConnection(database.GetConnectionString());
        await connection.OpenAsync();
        await using (var command = new NpgsqlCommand("CREATE TABLE category (id integer);", connection))
            await command.ExecuteNonQueryAsync();

        using (var factory = new StartupFactory(database.GetConnectionString()))
        using (var client = factory.CreateClient())
        {
            foreach (var path in new[] { "/api/products", "/api/categories?status=Active", "/health" })
            {
                var response = await client.GetAsync(path);
                Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
                Assert.DoesNotContain("Postgres", await response.Content.ReadAsStringAsync());
            }
            var internalResponse = await client.PostAsync("/internal/catalog/products/resolve", null);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, internalResponse.StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
            Assert.DoesNotContain(factory.Services.GetServices<IHostedService>(), service => service is OutboxRelayService);
        }

        // Repair the conflicting schema and verify the next startup recovers.
        await using (var command = new NpgsqlCommand("DROP TABLE category;", connection))
            await command.ExecuteNonQueryAsync();
        using var recoveredFactory = new StartupFactory(database.GetConnectionString());
        using var recoveredClient = recoveredFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        Assert.Equal(HttpStatusCode.OK, (await recoveredClient.GetAsync("/health")).StatusCode);
        Assert.Contains(recoveredFactory.Services.GetServices<IHostedService>(), service => service is OutboxRelayService);
    }

    private sealed class StartupFactory(string connectionString) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("ConnectionStrings:Default", connectionString);
        }
    }
}
