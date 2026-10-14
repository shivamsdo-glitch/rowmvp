using Aurigo.RowManagement.Infrastructure.Auth;
using FluentAssertions;

namespace Aurigo.RowManagement.UnitTests.Auth;

public class Pbkdf2PasswordHasherTests
{
    private readonly Pbkdf2PasswordHasher _hasher = new();

    [Fact]
    public void Hash_then_verify_round_trips()
    {
        var hash = _hasher.Hash("correct horse battery staple");
        _hasher.Verify("correct horse battery staple", hash).Should().BeTrue();
    }

    [Fact]
    public void Verify_rejects_wrong_password()
    {
        var hash = _hasher.Hash("correct horse battery staple");
        _hasher.Verify("Correct horse battery staple", hash).Should().BeFalse();
    }

    [Fact]
    public void Same_password_hashes_differently_each_time_because_of_the_salt()
    {
        _hasher.Hash("same-password").Should().NotBe(_hasher.Hash("same-password"));
    }

    [Fact]
    public void Hash_has_iterations_salt_hash_format_and_never_contains_the_password()
    {
        var hash = _hasher.Hash("plaintext-secret");
        hash.Split('.').Should().HaveCount(3);
        hash.Should().NotContain("plaintext-secret");
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-hash")]
    [InlineData("abc.def.ghi")]
    [InlineData("0.AAAA.AAAA")]
    [InlineData("100000..")]
    public void Verify_returns_false_for_malformed_hashes_instead_of_throwing(string stored)
    {
        _hasher.Verify("anything", stored).Should().BeFalse();
    }

    [Fact]
    public void Verify_returns_false_for_empty_password()
    {
        _hasher.Verify("", _hasher.Hash("x-password")).Should().BeFalse();
    }

    [Fact]
    public void Hash_rejects_empty_password()
    {
        var act = () => _hasher.Hash("");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void The_dummy_hash_used_for_unknown_emails_is_well_formed_and_matches_nothing()
    {
        // Mirrors LoginCommandHandler.DummyHash: it must burn PBKDF2 time, not throw or short-circuit.
        const string dummy = "100000.AAAAAAAAAAAAAAAAAAAAAA==.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";
        var act = () => _hasher.Verify("whatever", dummy);
        act.Should().NotThrow();
        _hasher.Verify("whatever", dummy).Should().BeFalse();
    }
}
