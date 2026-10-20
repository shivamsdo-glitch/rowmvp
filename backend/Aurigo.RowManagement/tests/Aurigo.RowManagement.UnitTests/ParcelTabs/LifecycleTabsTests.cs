using Aurigo.RowManagement.Application.ParcelTabs;
using FluentAssertions;

namespace Aurigo.RowManagement.UnitTests.ParcelTabs;

/// <summary>Phase 1.5 slice 8 (ADR 0020): Condemnation, Property Management and Relocation rules that need no database. Matches AT-NE-43, AT-PM-43, AT-RA-41.</summary>
public class LifecycleTabsTests
{
    private static DateOnly D(int m, int d) => new(2026, m, d);

    private static PropertyDetails Property(decimal? price = null, DateOnly? submitted = null, DateOnly? approved = null) =>
        new(submitted, approved, null, null, null, null, null, null, null, null, null, false, price, null, null, null, null, null);

    private static bool Valid(PropertyDetails d, IReadOnlyList<PropertyFundingInput>? funding = null, IReadOnlyList<StatusNoteInput>? notes = null) =>
        new SavePropertyCommandValidator().Validate(new SavePropertyCommand(Guid.NewGuid(), d, notes, funding)).IsValid;

    private static RelocationDetails Relocation(string number = "R-1", string name = "Kamali", string type = "Residential", DateOnly? mailed = null, DateOnly? delivered = null) =>
        new(number, null, name, false, null, null, null, type, null, mailed, delivered, null, null, null, null, null, null, null, null, null, false, null, null, false,
            null, null, null, null, null, null, null, null, null);

    private static bool Valid(RelocationDetails d, IReadOnlyList<RelocationContactInput>? contacts = null) =>
        new AddRelocationCommandValidator().Validate(new AddRelocationCommand(Guid.NewGuid(), d, null, null, contacts, null, null)).IsValid;

    [Fact]
    public void AT_PM_43_The_sale_price_is_zero_or_more_and_the_memo_cannot_be_approved_before_it_is_submitted()
    {
        Valid(Property(price: 0m)).Should().BeTrue();
        Valid(Property(price: -1m)).Should().BeFalse();
        Valid(Property(submitted: D(3, 1), approved: D(3, 1))).Should().BeTrue();
        Valid(Property(submitted: D(3, 2), approved: D(3, 1))).Should().BeFalse();
    }

    [Fact]
    public void AT_PM_43_Funding_rows_need_a_fund_type_and_percentages_stay_within_one_hundred()
    {
        Valid(Property(), new[] { new PropertyFundingInput("Federal", null, 10m, 60m), new PropertyFundingInput("Local", null, 5m, 40m) }).Should().BeTrue();
        Valid(Property(), new[] { new PropertyFundingInput("", null, null, null) }).Should().BeFalse();
        Valid(Property(), new[] { new PropertyFundingInput("F", null, -1m, null) }).Should().BeFalse();
        Valid(Property(), new[] { new PropertyFundingInput("F", null, null, 101m) }).Should().BeFalse();
        Valid(Property(), new[] { new PropertyFundingInput("F", null, null, 60m), new PropertyFundingInput("G", null, null, 41m) }).Should().BeFalse();
    }

    [Fact]
    public void AT_PM_43_Status_notes_need_text_and_there_are_at_most_fifty()
    {
        Valid(Property(), notes: new[] { new StatusNoteInput(D(3, 1), "Memo sent.") }).Should().BeTrue();
        Valid(Property(), notes: new[] { new StatusNoteInput(D(3, 1), " ") }).Should().BeFalse();
        Valid(Property(), notes: Enumerable.Range(0, 51).Select(i => new StatusNoteInput(D(3, 1), $"n{i}")).ToList()).Should().BeFalse();
    }

    [Theory]
    [InlineData("R-1", "Kamali", "Residential", true)]
    [InlineData("", "Kamali", "Residential", false)]
    [InlineData("R-1", " ", "Residential", false)]
    [InlineData("R-1", "Kamali", "", false)]
    public void AT_RA_41_A_relocation_needs_a_number_a_displacee_name_and_a_type(string number, string name, string type, bool valid) =>
        Valid(Relocation(number, name, type)).Should().Be(valid);

    [Fact]
    public void AT_RA_41_Dates_cannot_go_backwards_and_only_one_contact_is_primary()
    {
        Valid(Relocation(mailed: D(3, 1), delivered: D(3, 1))).Should().BeTrue();
        Valid(Relocation(mailed: D(3, 2), delivered: D(3, 1))).Should().BeFalse();
        Valid(Relocation(), new[] { new RelocationContactInput("A", null, null, null, null, true), new RelocationContactInput("B", null, null, null, null, false) }).Should().BeTrue();
        Valid(Relocation(), new[] { new RelocationContactInput("A", null, null, null, null, true), new RelocationContactInput("B", null, null, null, null, true) }).Should().BeFalse();
        Valid(Relocation(), new[] { new RelocationContactInput("", null, null, null, null, false) }).Should().BeFalse();
    }
}
