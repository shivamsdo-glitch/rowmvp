using Aurigo.RowManagement.Application.Calculations;
using Aurigo.RowManagement.Domain.Enums;
using FluentAssertions;

namespace Aurigo.RowManagement.UnitTests.Calculations;

/// <summary>ROW-011: per-tenant custom parcel fields.</summary>
public class CustomFieldValidationTests
{
    private static readonly IReadOnlyList<CustomFieldInfo> Defs = new[]
    {
        new CustomFieldInfo("zoning", "Zoning", CustomFieldType.Text, Array.Empty<string>(), false, true),
        new CustomFieldInfo("frontage", "Frontage (ft)", CustomFieldType.Number, Array.Empty<string>(), false, true),
        new CustomFieldInfo("inspected", "Inspected on", CustomFieldType.Date, Array.Empty<string>(), false, true),
        new CustomFieldInfo("occupied", "Occupied", CustomFieldType.Boolean, Array.Empty<string>(), false, true),
        new CustomFieldInfo("soil", "Soil class", CustomFieldType.List, new[] { "Clay", "Sand", "Rock" }, false, true),
        new CustomFieldInfo("county", "County", CustomFieldType.Text, Array.Empty<string>(), true, true),
        new CustomFieldInfo("legacy", "Legacy code", CustomFieldType.Text, Array.Empty<string>(), false, false),
    };

    private static readonly IReadOnlyDictionary<string, string> None = new Dictionary<string, string>();

    private static CustomFieldCheck Check(Dictionary<string, string?> values, IReadOnlyDictionary<string, string>? existing = null) =>
        CustomFieldValidation.Validate(Defs, values, existing ?? None);

    [Fact]
    public void ROW_011_Values_are_checked_against_their_field_types_and_normalised()
    {
        var r = Check(new() { ["county"] = " Clark ", ["frontage"] = "120.50", ["inspected"] = "2026-03-09", ["occupied"] = "TRUE", ["soil"] = "clay", ["zoning"] = "R-2" });

        r.IsValid.Should().BeTrue(string.Join("; ", r.Errors));
        r.Values["county"].Should().Be("Clark");
        r.Values["frontage"].Should().Be("120.50");
        r.Values["occupied"].Should().Be("true");
        r.Values["soil"].Should().Be("Clay", "a list value is stored with the list's own spelling");
    }

    [Theory]
    [InlineData("frontage", "wide")]
    [InlineData("frontage", "1e400")]
    [InlineData("inspected", "03/09/2026")]
    [InlineData("inspected", "2026-02-30")]
    [InlineData("occupied", "yes")]
    [InlineData("soil", "Gravel")]
    public void ROW_011_A_value_of_the_wrong_kind_is_refused_naming_the_field(string key, string value)
    {
        var r = Check(new() { ["county"] = "Clark", [key] = value });
        r.IsValid.Should().BeFalse();
        r.Errors.Should().ContainSingle().Which.Should().Contain(Defs.First(d => d.Key == key).Label);
    }

    [Fact]
    public void ROW_011_Text_is_limited_to_500_characters_exactly()
    {
        Check(new() { ["county"] = "c", ["zoning"] = new string('x', 500) }).IsValid.Should().BeTrue();
        Check(new() { ["county"] = "c", ["zoning"] = new string('x', 501) }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void ROW_011_Required_fields_must_have_a_value_and_blanks_do_not_count()
    {
        Check(new()).Errors.Should().ContainSingle().Which.Should().Be("County is required.");
        Check(new() { ["county"] = "   " }).Errors.Should().ContainSingle().Which.Should().Be("County is required.");
    }

    [Fact]
    public void ROW_011_Unknown_fields_are_refused_and_blank_optional_values_are_dropped()
    {
        Check(new() { ["county"] = "c", ["nope"] = "x" }).Errors.Should().ContainSingle().Which.Should().Contain("'nope'");
        var r = Check(new() { ["county"] = "c", ["zoning"] = "" });
        r.IsValid.Should().BeTrue();
        r.Values.Should().NotContainKey("zoning");
    }

    [Fact]
    public void ROW_011_A_deactivated_fields_stored_value_may_stay_but_cannot_be_changed_or_added()
    {
        var existing = new Dictionary<string, string> { ["legacy"] = "A-1", ["county"] = "Clark" };

        Check(new() { ["legacy"] = "A-1", ["county"] = "Clark" }, existing).Values.Should().Contain("legacy", "A-1");
        Check(new() { ["legacy"] = "B-2", ["county"] = "Clark" }, existing).IsValid.Should().BeFalse();
        Check(new() { ["legacy"] = "A-1", ["county"] = "Clark" }).IsValid.Should().BeFalse("nothing stored, so it would be new");
    }

    [Fact]
    public void ROW_011_A_partial_submit_keeps_stored_values_it_did_not_mention()
    {
        var r = Check(new() { ["zoning"] = "R-3" }, new Dictionary<string, string> { ["county"] = "Clark", ["frontage"] = "10" });

        r.IsValid.Should().BeTrue();
        r.Values.Should().Contain(new KeyValuePair<string, string>("county", "Clark")).And.Contain(new KeyValuePair<string, string>("frontage", "10"));
    }

    [Fact]
    public void ROW_011_Clearing_a_stored_value_removes_it()
    {
        var r = Check(new() { ["zoning"] = "" }, new Dictionary<string, string> { ["county"] = "Clark", ["zoning"] = "R-2" });

        r.Values.Should().NotContainKey("zoning");
    }

    [Fact]
    public void ROW_011_No_more_than_fifty_values_at_once()
    {
        var many = Enumerable.Range(0, CustomFieldValidation.MaxValues + 1).ToDictionary(i => $"k{i}", i => (string?)"v");
        Check(many).Errors.Should().ContainSingle().Which.Should().Contain("At most");
    }
}
