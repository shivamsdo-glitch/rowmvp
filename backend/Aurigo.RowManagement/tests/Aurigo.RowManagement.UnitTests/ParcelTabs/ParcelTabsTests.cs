using Aurigo.RowManagement.Application.Calculations;
using Aurigo.RowManagement.Application.ParcelTabs;
using Aurigo.RowManagement.Domain.Common;
using FluentAssertions;

namespace Aurigo.RowManagement.UnitTests.ParcelTabs;

/// <summary>Phase 1.5 slice 5b (ADR 0017): Contact Log and Easements rules that need no database. Matches AT-NE-40, AT-SU-40, AT-X-46, AT-X-47.</summary>
public class ParcelTabsTests
{
    private static AddContactLogEntryCommand Contact(
        DateTime? at = null, string? notes = null, string? mode = null, IReadOnlyList<ContactLogLinkInput>? links = null) =>
        new(Guid.NewGuid(), at ?? new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc), mode, "Pat", "Alex", notes, links);

    private static AddEasementCommand Easement(string number = "E-1", DateOnly? start = null, DateOnly? end = null, string? station = null) =>
        new(Guid.NewGuid(), number, "Permanent Easement", null, start, end, station, station);

    private static bool Valid(AddContactLogEntryCommand c) => new AddContactLogEntryCommandValidator().Validate(c).IsValid;
    private static bool Valid(AddEasementCommand c) => new AddEasementCommandValidator().Validate(c).IsValid;

    [Fact]
    public void AT_NE_40_A_contact_needs_only_a_sensible_date_and_time()
    {
        Valid(Contact()).Should().BeTrue();
        Valid(Contact(at: DateTime.MinValue)).Should().BeFalse();
        Valid(Contact(at: new DateTime(2101, 1, 1))).Should().BeFalse();
    }

    [Fact]
    public void AT_NE_40_Contact_text_has_length_limits()
    {
        Valid(Contact(notes: new string('n', 4000))).Should().BeTrue();
        Valid(Contact(notes: new string('n', 4001))).Should().BeFalse();
        Valid(Contact(mode: new string('m', 101))).Should().BeFalse();
    }

    [Fact]
    public void AT_NE_40_Each_linked_record_needs_an_identifier_and_there_are_at_most_fifty()
    {
        Valid(Contact(links: new[] { new ContactLogLinkInput("APP-1", "Appraisal", null) })).Should().BeTrue();
        Valid(Contact(links: new[] { new ContactLogLinkInput("", "Appraisal", null) })).Should().BeFalse();
        Valid(Contact(links: new[] { new ContactLogLinkInput("A", new string('m', 101), null) })).Should().BeFalse();
        Valid(Contact(links: Enumerable.Range(0, 50).Select(i => new ContactLogLinkInput($"R{i}", null, null)).ToList())).Should().BeTrue();
        Valid(Contact(links: Enumerable.Range(0, 51).Select(i => new ContactLogLinkInput($"R{i}", null, null)).ToList())).Should().BeFalse();
    }

    [Theory]
    [InlineData("E-1", true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    public void AT_SU_40_An_easement_needs_a_number(string number, bool valid) => Valid(Easement(number)).Should().Be(valid);

    [Fact]
    public void AT_SU_40_Easement_number_and_stations_have_length_limits()
    {
        Valid(Easement(new string('x', 50))).Should().BeTrue();
        Valid(Easement(new string('x', 51))).Should().BeFalse();
        Valid(Easement(station: new string('s', 100))).Should().BeTrue();
        Valid(Easement(station: new string('s', 101))).Should().BeFalse();
    }

    [Fact]
    public void AT_SU_40_An_easement_cannot_end_before_it_starts_but_may_end_the_same_day_or_have_no_end()
    {
        var d = new DateOnly(2026, 5, 1);
        Valid(Easement(start: d, end: d.AddDays(-1))).Should().BeFalse();
        Valid(Easement(start: d, end: d)).Should().BeTrue();
        Valid(Easement(start: d, end: null)).Should().BeTrue();
        Valid(Easement(start: null, end: d)).Should().BeTrue();
    }

    // The approved matrix of 2026-10-19, as the defaults new agencies get.
    [Theory]
    [InlineData("ProjectManager", "R", "R")]
    [InlineData("Designer", "CRU", "CRU")]
    [InlineData("DesignApprover", "R", "R")]
    [InlineData("Surveyor", "CRU", "CRUD")]
    [InlineData("TitleResearcher", "CRU", "R")]
    [InlineData("Appraiser", "CRU", "R")]
    [InlineData("AppraisalApprover", "R", "R")]
    [InlineData("Negotiator", "CRUD", "R")]
    [InlineData("RelocationAgent", "CRUD", "R")]
    [InlineData("Finance", "R", "R")]
    [InlineData("Admin", "CRUD", "CRUD")]
    public void AT_X_46_The_default_access_to_the_Contact_Log_and_Easements_pages_follows_the_approved_matrix(string role, string contactLog, string easements)
    {
        static string Letters(PermissionFlags f) => (f.Create ? "C" : "") + (f.Read ? "R" : "") + (f.Update ? "U" : "") + (f.Delete ? "D" : "");
        Letters(PermissionTemplateCalculator.FlagsFor(DefaultRolePermissions.TemplateFor(role, PageKeys.ContactLog))).Should().Be(contactLog, $"{role} Contact Log");
        Letters(PermissionTemplateCalculator.FlagsFor(DefaultRolePermissions.TemplateFor(role, PageKeys.Easements))).Should().Be(easements, $"{role} Easements");
    }

    [Fact]
    public void AT_X_46_An_unknown_role_has_no_access_to_the_new_pages()
    {
        DefaultRolePermissions.TemplateFor("SuperUser", PageKeys.ContactLog).Should().BeNull();
        DefaultRolePermissions.TemplateFor("SuperUser", PageKeys.Easements).Should().BeNull();
    }
}
