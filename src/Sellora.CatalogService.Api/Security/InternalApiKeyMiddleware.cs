using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Sellora.CatalogService.Api.Security;

public sealed class InternalApiOptions
{
    public const string SectionName = "InternalApi";
    public const string HeaderName = "X-Internal-Api-Key";

    public string ApiKey { get; set; } = string.Empty;
}

public sealed class InternalApiKeyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly InternalApiOptions _options;
    private readonly ILogger<InternalApiKeyMiddleware> _logger;

    public InternalApiKeyMiddleware(
        RequestDelegate next,
        IOptions<InternalApiOptions> options,
        ILogger<InternalApiKeyMiddleware> logger)
    {
        _next = next;
        _options = options.Value;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/internal"))
        {
            await _next(context);
            return;
        }

        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            _logger.LogError(
                "The internal API key has not been configured.");

            context.Response.StatusCode =
                StatusCodes.Status503ServiceUnavailable;

            await context.Response.WriteAsJsonAsync(new
            {
                Message = "The internal API is not configured."
            });

            return;
        }

        var suppliedKey = context.Request.Headers[
            InternalApiOptions.HeaderName].ToString();

        if (!IsValidKey(suppliedKey, _options.ApiKey))
        {
            context.Response.StatusCode =
                StatusCodes.Status401Unauthorized;

            await context.Response.WriteAsJsonAsync(new
            {
                Message = "A valid internal API key is required."
            });

            return;
        }

        await _next(context);
    }

    private static bool IsValidKey(
        string suppliedKey,
        string expectedKey)
    {
        if (string.IsNullOrWhiteSpace(suppliedKey))
        {
            return false;
        }

        var suppliedHash = SHA256.HashData(
            Encoding.UTF8.GetBytes(suppliedKey));

        var expectedHash = SHA256.HashData(
            Encoding.UTF8.GetBytes(expectedKey));

        return CryptographicOperations.FixedTimeEquals(
            suppliedHash,
            expectedHash);
    }
}