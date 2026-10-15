using Aurigo.RowManagement.Application.Calculations;
using Aurigo.RowManagement.Domain.Common;
using Aurigo.RowManagement.Domain.Enums;
using FluentAssertions;

namespace Aurigo.RowManagement.UnitTests.Calculations;

public class TaxIdAndSharesTests
{
    // ── tax ID (ROW-018) ──

    [Theory]
    [InlineData(TaxIdType.Ssn, "123-45-6789")]
    [InlineData(TaxIdType.Ssn, "123456789")]
    [InlineData(TaxIdType.Ssn, "123 45 6789")]
    [InlineData(TaxIdType.Ein, "12-3456789")]
    [InlineData(TaxIdType.Ein, "98-7654321")]
    [InlineData(TaxIdType.Itin, "912-70-1234")]
    [InlineData(TaxIdType.Itin, "900-50-0001")]
    [InlineData(TaxIdType.Itin, "999-99-9999")]
    public void ROW_018_Valid_numbers_are_accepted(TaxIdType type, string input) =>
        TaxId.Validate(type, input).Should().BeEmpty();

    [Theory]
    [InlineData(TaxIdType.Ssn, "000-12-3456")]
    [InlineData(TaxIdType.Ssn, "666-12-3456")]
    [InlineData(TaxIdType.Ssn, "900-12-3456")]
    [InlineData(TaxIdType.Ssn, "123-00-6789")]
    [InlineData(TaxIdType.Ssn, "123-45-0000")]
    [InlineData(TaxIdType.Ein, "00-1234567")]
    [InlineData(TaxIdType.Itin, "123-45-6789")]
    [InlineData(TaxIdType.Itin, "912-69-1234")]
    [InlineData(TaxIdType.Itin, "912-93-1234")]
    public void ROW_018_Numbers_the_IRS_never_issues_are_refused(TaxIdType type, string input) =>
        TaxId.Validate(type, input).Should().ContainSingle();

    [Theory]
    [InlineData("12345678")]
    [InlineData("1234567890")]
    [InlineData("")]
    [InlineData("   ")]
    public void ROW_018_A_number_without_nine_digits_is_refused(string input) =>
        TaxId.Validate(TaxIdType.Ssn, input).Should().ContainSingle().Which.Should().Contain("9 digits");

    [Theory]
    [InlineData("123-45-678a")]
    [InlineData("123.45.6789")]
    [InlineData("123/45/6789")]
    public void ROW_018_Anything_but_digits_spaces_and_hyphens_is_refused(string input) =>
        TaxId.Validate(TaxIdType.Ssn, input).Should().ContainSingle().Which.Should().Contain("only digits");

    [Fact]
    public void ROW_018_Null_is_refused_not_a_crash() => TaxId.Validate(TaxIdType.Ein, null).Should().NotBeEmpty();

    [Fact]
    public void ROW_018_Problems_never_repeat_the_number()
    {
        foreach (var input in new[] { "123-45-678a", "666-12-3456", "12345" })
            TaxId.Validate(TaxIdType.Ssn, input).Should().OnlyContain(p => !p.Contains("123") && !p.Contains("666") && !p.Contains("12345"));
    }

    [Fact]
    public void ROW_018_Digits_and_last_four_ignore_separators()
    {
        TaxId.Digits("123-45 6789").Should().Be("123456789");
        TaxId.Last4("123456789").Should().Be("6789");
    }

    [Fact]
    public void ROW_018_The_mask_shows_only_the_last_four_in_the_shape_of_the_type()
    {
        TaxId.Mask(TaxIdType.Ssn, "6789").Should().Be("***-**-6789");
        TaxId.Mask(TaxIdType.Itin, "1234").Should().Be("***-**-1234");
        TaxId.Mask(TaxIdType.Ein, "4321").Should().Be("**-***4321");
    }

    // ── ownership shares (ROW-014) ──

    [Theory]
    [InlineData(null, 0)]
    [InlineData(50.0, 0)]
    [InlineData(100.0, 0)]
    [InlineData(0.0001, 0)]
    [InlineData(33.3333, 0)]
    public void ROW_014_Fair_shares_pass(double? share, int problems) =>
        OwnershipShares.CheckShare(share is null ? null : (decimal)share.Value).Should().HaveCount(problems);

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(100.01)]
    [InlineData(250.0)]
    public void ROW_014_A_share_must_be_above_zero_and_at_most_the_whole(double share) =>
        OwnershipShares.CheckShare((decimal)share).Should().ContainSingle();

    [Fact]
    public void ROW_014_A_share_has_at_most_four_decimals() =>
        OwnershipShares.CheckShare(33.33333m).Should().ContainSingle().Which.Should().Contain("decimal");

    [Fact]
    public void ROW_014_Remaining_and_exceeds_follow_the_shares_already_given()
    {
        OwnershipShares.Remaining(new decimal?[] { 50m, 25m, null }).Should().Be(25m);
        OwnershipShares.Remaining(new decimal?[] { 70m, 70m }).Should().Be(0m, "never below zero");
        OwnershipShares.Exceeds(new decimal?[] { 50m, 25m }, 25m).Should().BeFalse("exactly the rest is fine");
        OwnershipShares.Exceeds(new decimal?[] { 50m, 25m }, 25.0001m).Should().BeTrue();
        OwnershipShares.Exceeds(Array.Empty<decimal?>(), 100m).Should().BeFalse();
        OwnershipShares.Exceeds(new decimal?[] { 100m }, null).Should().BeFalse("no share claims nothing");
    }

    [Fact]
    public void ROW_014_Thirds_that_add_up_to_the_whole_are_allowed()
    {
        var others = new decimal?[] { 33.3333m, 33.3333m };
        OwnershipShares.Exceeds(others, 33.3334m).Should().BeFalse();
    }

    // ── roles ──

    [Fact]
    public void ROW_014_Every_role_belongs_to_a_list_and_only_some_carry_shares_or_leases()
    {
        foreach (var role in Enum.GetValues<PartyRole>())
            Enum.IsDefined(PartyRoles.CategoryOf(role)).Should().BeTrue();
        PartyRoles.CategoryOf(PartyRole.FeeOwner).Should().Be(PartyCategory.Ownership);
        PartyRoles.CategoryOf(PartyRole.Attorney).Should().Be(PartyCategory.Contact);
        PartyRoles.CategoryOf(PartyRole.Tenant).Should().Be(PartyCategory.Occupant);
        PartyRoles.CategoryOf(PartyRole.InterestedParty).Should().Be(PartyCategory.Abutter);
        PartyRoles.CarriesShare(PartyRole.FeeOwner).Should().BeTrue();
        PartyRoles.CarriesShare(PartyRole.Lienholder).Should().BeFalse();
        PartyRoles.HasLease(PartyRole.Tenant).Should().BeTrue();
        PartyRoles.HasLease(PartyRole.Occupant).Should().BeFalse();
    }
}
