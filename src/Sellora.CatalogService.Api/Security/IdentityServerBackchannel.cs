using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Sellora.CatalogService.Api.Security;

/// <summary>
/// The shared development Identity Server uses a self-signed certificate that
/// is not trusted by developer machines or the staging host. Instead of
/// disabling TLS validation, Development and Staging trust that one pinned
/// certificate (SHA-256 thumbprint) in addition to normally trusted ones.
/// Production must use a trusted certificate and never configures a pin.
/// </summary>
public static class IdentityServerBackchannel
{
    public const string PinnedThumbprintKey = "Jwt:BackchannelCertificateSha256";

    public static HttpClientHandler? CreateHandler(
        IHostEnvironment environment,
        IConfiguration configuration)
    {
        if (!environment.IsDevelopment() && !environment.IsStaging())
        {
            return null;
        }

        var pinnedThumbprint = NormalizeThumbprint(configuration[PinnedThumbprintKey]);
        if (pinnedThumbprint.Length == 0)
        {
            return null;
        }

        return new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, certificate, _, errors) =>
                IsTrusted(certificate, errors, pinnedThumbprint)
        };
    }

    public static bool IsTrusted(
        X509Certificate2? certificate,
        SslPolicyErrors errors,
        string pinnedThumbprint)
    {
        if (errors == SslPolicyErrors.None)
        {
            return true;
        }

        // The pin identifies the exact certificate, so chain and host-name errors
        // (self-signed, CN=localhost) are tolerated only for that certificate.
        if (certificate is null ||
            errors.HasFlag(SslPolicyErrors.RemoteCertificateNotAvailable))
        {
            return false;
        }

        var expected = NormalizeThumbprint(pinnedThumbprint);
        var actual = certificate.GetCertHashString(HashAlgorithmName.SHA256);

        return expected.Length > 0 &&
            string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeThumbprint(string? thumbprint) =>
        string.IsNullOrWhiteSpace(thumbprint)
            ? string.Empty
            : thumbprint.Replace(":", string.Empty).Replace(" ", string.Empty).Trim().ToUpperInvariant();
}
