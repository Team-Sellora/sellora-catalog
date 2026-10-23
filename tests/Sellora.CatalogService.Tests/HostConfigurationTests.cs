using System.Net;
using System.Net.Http.Json;
using System.Net.Security;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Sellora.CatalogService.Api.Identity;
using Sellora.CatalogService.Api.Security;
using Sellora.CatalogService.Infrastructure.Persistence.Seeding;
using Xunit;

namespace Sellora.CatalogService.Tests;

public sealed class HostConfigurationTests(PostgreSqlConstraintFixture database) : IClassFixture<PostgreSqlConstraintFixture>
{
    [Fact]
    public void Pinned_certificate_is_trusted_despite_chain_and_name_errors()
    {
        using var certificate = CreateSelfSignedCertificate();
        var pin = certificate.GetCertHashString(HashAlgorithmName.SHA256);
        var colonSeparatedPin = string.Join(':', pin.Chunk(2).Select(pair => new string(pair))).ToLowerInvariant();
        const SslPolicyErrors selfSignedForIp =
            SslPolicyErrors.RemoteCertificateChainErrors | SslPolicyErrors.RemoteCertificateNameMismatch;

        Assert.True(IdentityServerBackchannel.IsTrusted(certificate, selfSignedForIp, pin));
        Assert.True(IdentityServerBackchannel.IsTrusted(certificate, SslPolicyErrors.RemoteCertificateChainErrors, colonSeparatedPin));
    }

    [Fact]
    public void Unpinned_or_missing_certificates_are_rejected()
    {
        using var certificate = CreateSelfSignedCertificate();
        using var otherCertificate = CreateSelfSignedCertificate();
        var pin = certificate.GetCertHashString(HashAlgorithmName.SHA256);

        Assert.False(IdentityServerBackchannel.IsTrusted(otherCertificate, SslPolicyErrors.RemoteCertificateChainErrors, pin));
        Assert.False(IdentityServerBackchannel.IsTrusted(certificate, SslPolicyErrors.RemoteCertificateChainErrors, ""));
        Assert.False(IdentityServerBackchannel.IsTrusted(null, SslPolicyErrors.RemoteCertificateNotAvailable, pin));
        Assert.False(IdentityServerBackchannel.IsTrusted(certificate, SslPolicyErrors.RemoteCertificateNotAvailable, pin));
        // Certificates that pass normal validation are always trusted.
        Assert.True(IdentityServerBackchannel.IsTrusted(otherCertificate, SslPolicyErrors.None, pin));
    }

    [Theory]
    [InlineData("Production", "AB12", false)]
    [InlineData("Testing", "AB12", false)]
    [InlineData("Development", null, false)]
    [InlineData("Staging", "  ", false)]
    [InlineData("Development", "AB12", true)]
    [InlineData("Staging", "AB:12", true)]
    public void Pinned_backchannel_is_only_used_in_development_and_staging(string environment, string? pin, bool expectHandler)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [IdentityServerBackchannel.PinnedThumbprintKey] = pin })
            .Build();

        using var handler = IdentityServerBackchannel.CreateHandler(new TestHostEnvironment(environment), configuration);

        Assert.Equal(expectHandler, handler is not null);
        if (handler is not null)
        {
            Assert.NotNull(handler.ServerCertificateCustomValidationCallback);
            Assert.False(handler.ServerCertificateCustomValidationCallback!(
                new HttpRequestMessage(), null, null, SslPolicyErrors.RemoteCertificateNotAvailable));
        }
    }

    [Fact]
    public void Current_user_prefers_sub_and_falls_back_to_name_identifier()
    {
        var accessor = new HttpContextAccessor();
        var currentUser = new HttpCurrentUserContext(accessor);
        Assert.Null(currentUser.Subject);

        accessor.HttpContext = WithClaims(new Claim(ClaimTypes.NameIdentifier, "name-id"));
        Assert.Equal("name-id", currentUser.Subject);

        accessor.HttpContext = WithClaims(new Claim(ClaimTypes.NameIdentifier, "name-id"), new Claim("sub", "subject"));
        Assert.Equal("subject", currentUser.Subject);

        accessor.HttpContext = WithClaims();
        Assert.Null(currentUser.Subject);
    }

    [Fact]
    public async Task Whoami_returns_the_authenticated_claims()
    {
        using var factory = new CatalogApiFactory(database.ConnectionString);
        using var client = factory.Client(Guid.NewGuid().ToString(), subject: "whoami-user");

        var claims = await client.GetFromJsonAsync<ClaimDto[]>("/whoami");

        Assert.Contains(claims!, claim => claim.Type == "sub" && claim.Value == "whoami-user");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
    }

    [Fact]
    public void Missing_connection_string_stops_startup()
    {
        using var factory = new EnvironmentFactory("Testing", " ");

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("connection string is not configured", exception.Message);
    }

    [Fact]
    public async Task Development_startup_migrates_exposes_swagger_and_pins_identity_server()
    {
        using var factory = new EnvironmentFactory("Development", database.ConnectionString);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/swagger/v1/swagger.json")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
        var jwtOptions = factory.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);
        Assert.IsType<HttpClientHandler>(jwtOptions.BackchannelHttpHandler);
    }

    [Fact]
    public async Task Staging_startup_seeds_the_demo_catalogue()
    {
        using var factory = new EnvironmentFactory("Staging", database.ConnectionString);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/swagger/v1/swagger.json")).StatusCode);
        await using var db = database.CreateDbContext(DevelopmentCatalogSeedIds.CompanyId);
        Assert.Equal(5, await db.Products.CountAsync());
    }

    private static DefaultHttpContext WithClaims(params Claim[] claims) =>
        new() { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")) };

    private static X509Certificate2 CreateSelfSignedCertificate()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
    }

    private sealed record ClaimDto(string Type, string Value);

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Sellora.CatalogService.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class EnvironmentFactory(string environment, string connectionString) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environment);
            builder.UseSetting("ConnectionStrings:Default", connectionString);
        }
    }
}
