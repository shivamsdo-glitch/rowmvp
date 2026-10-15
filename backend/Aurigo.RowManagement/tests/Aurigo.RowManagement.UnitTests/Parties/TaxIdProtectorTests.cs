using System.Security.Cryptography;
using Aurigo.RowManagement.Infrastructure.Security;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Aurigo.RowManagement.UnitTests.Parties;

public class TaxIdProtectorTests
{
    private static TaxIdOptions Options(byte fill = 7) => new() { Key = Enumerable.Repeat(fill, 32).ToArray() };
    private static readonly Guid PartyA = Guid.NewGuid();
    private static readonly Guid PartyB = Guid.NewGuid();

    [Fact]
    public void ROW_018_A_protected_number_opens_for_its_own_party()
    {
        var p = new AesGcmTaxIdProtector(Options());
        p.Unprotect(p.Protect("123456789", PartyA), PartyA).Should().Be("123456789");
    }

    [Fact]
    public void ROW_018_The_stored_text_never_contains_the_number_or_its_last_four()
    {
        var stored = new AesGcmTaxIdProtector(Options()).Protect("123456789", PartyA);
        stored.Should().StartWith("v1.").And.NotContain("123456789").And.NotContain("6789");
    }

    [Fact]
    public void ROW_018_The_same_number_protected_twice_gives_different_text()
    {
        var p = new AesGcmTaxIdProtector(Options());
        p.Protect("123456789", PartyA).Should().NotBe(p.Protect("123456789", PartyA));
    }

    [Fact]
    public void ROW_018_A_value_moved_to_another_partys_record_does_not_open()
    {
        var p = new AesGcmTaxIdProtector(Options());
        var stored = p.Protect("123456789", PartyA);
        var act = () => p.Unprotect(stored, PartyB);
        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void ROW_018_A_different_key_does_not_open_it()
    {
        var stored = new AesGcmTaxIdProtector(Options(1)).Protect("123456789", PartyA);
        var act = () => new AesGcmTaxIdProtector(Options(2)).Unprotect(stored, PartyA);
        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void ROW_018_A_tampered_value_does_not_open()
    {
        var p = new AesGcmTaxIdProtector(Options());
        var bytes = Convert.FromBase64String(p.Protect("123456789", PartyA)[3..]);
        bytes[^1] ^= 0x01;
        var act = () => p.Unprotect("v1." + Convert.ToBase64String(bytes), PartyA);
        act.Should().Throw<CryptographicException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("plain 123456789")]
    [InlineData("v2.AAAA")]
    [InlineData("v1.AAAA")]
    public void ROW_018_Unknown_or_damaged_values_are_refused_not_returned(string stored)
    {
        var act = () => new AesGcmTaxIdProtector(Options()).Unprotect(stored, PartyA);
        act.Should().Throw<CryptographicException>();
    }

    // ── the key ──

    private static IConfiguration Config(string? key) =>
        new ConfigurationBuilder().AddInMemoryCollection(key is null ? new Dictionary<string, string?>() : new Dictionary<string, string?> { ["Security:TaxIdKey"] = key }).Build();

    private static IHostEnvironment Env(string name) => new FakeEnv(name);

    [Fact]
    public void ROW_018_Development_has_a_working_32_byte_key_without_configuration() =>
        TaxIdOptions.Resolve(Config(null), Env(Environments.Development)).Key.Should().HaveCount(32);

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void ROW_018_Outside_development_a_missing_key_stops_startup(string env)
    {
        var act = () => TaxIdOptions.Resolve(Config(null), Env(env));
        act.Should().Throw<InvalidOperationException>().WithMessage("*Security:TaxIdKey*");
    }

    [Fact]
    public void ROW_018_A_configured_key_must_be_base64_of_exactly_32_bytes()
    {
        TaxIdOptions.Resolve(Config(Convert.ToBase64String(new byte[32])), Env("Production")).Key.Should().HaveCount(32);
        ((Action)(() => TaxIdOptions.Resolve(Config(Convert.ToBase64String(new byte[16])), Env("Production")))).Should().Throw<InvalidOperationException>().WithMessage("*32 bytes*");
        ((Action)(() => TaxIdOptions.Resolve(Config("not base64!"), Env("Production")))).Should().Throw<InvalidOperationException>().WithMessage("*base64*");
    }
}
