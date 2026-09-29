using Confluent.Kafka;
using Sellora.CatalogService.Infrastructure.Kafka;
using Xunit;

namespace Sellora.CatalogService.Tests;

public sealed class KafkaSaslConfiguratorTests
{
    [Fact]
    public void Applies_sasl_when_a_username_is_configured()
    {
        var config = new ProducerConfig();

        KafkaSaslConfigurator.Apply(config, "CONFLUENT-KEY", "secret");

        Assert.Equal(SecurityProtocol.SaslSsl, config.SecurityProtocol);
        Assert.Equal(SaslMechanism.Plain, config.SaslMechanism);
        Assert.Equal("CONFLUENT-KEY", config.SaslUsername);
        Assert.Equal("secret", config.SaslPassword);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Leaves_a_local_broker_untouched(string? username)
    {
        // Docker Compose and CI run a plaintext broker with no credentials.
        var config = new ProducerConfig();

        KafkaSaslConfigurator.Apply(config, username, null);

        Assert.Null(config.SecurityProtocol);
        Assert.Null(config.SaslUsername);
    }
}
