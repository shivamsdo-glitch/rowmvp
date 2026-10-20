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

    // ── Ownership Details (slice 5c) ──

    private static AddOwnershipCommand Owner(
        string name = "Kamali", IReadOnlyList<OwnershipAddressInput>? addresses = null, IReadOnlyList<OwnershipContactInput>? contacts = null) =>
        new(Guid.NewGuid(), name, "Fee Owner", addresses, contacts);

    private static OwnershipContactInput C(string name = "Dev", bool primary = false, string? phone = null, string? email = null) =>
        new("Broker", name, phone, email, null, true, primary);

    private static bool Valid(AddOwnershipCommand c) => new AddOwnershipCommandValidator().Validate(c).IsValid;

    [Theory]
    [InlineData("Kamali", true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    public void AT_DE_42_An_ownership_needs_an_owner_name(string name, bool valid) => Valid(Owner(name)).Should().Be(valid);

    [Fact]
    public void AT_DE_42_Only_one_contact_can_be_primary()
    {
        Valid(Owner(contacts: new[] { C("A", true), C("B") })).Should().BeTrue();
        Valid(Owner(contacts: new[] { C("A"), C("B") })).Should().BeTrue();
        Valid(Owner(contacts: new[] { C("A", true), C("B", true) })).Should().BeFalse();
    }

    [Theory]
    [InlineData("(112) 345-6789", true)]
    [InlineData("+1 512-555-0100 x12", true)]
    [InlineData("abc", false)]
    [InlineData("", true)]
    public void AT_DE_42_A_contact_phone_must_look_like_a_phone_number(string phone, bool valid) => Valid(Owner(contacts: new[] { C(phone: phone) })).Should().Be(valid);

    [Theory]
    [InlineData("kamali@aurigo.com", true)]
    [InlineData("kamali", false)]
    [InlineData("", true)]
    public void AT_DE_42_A_contact_email_must_be_an_email_address(string email, bool valid) => Valid(Owner(contacts: new[] { C(email: email) })).Should().Be(valid);

    [Fact]
    public void AT_DE_42_Each_contact_needs_a_name_and_text_has_length_limits()
    {
        Valid(Owner(contacts: new[] { C("") })).Should().BeFalse();
        Valid(Owner(new string('n', 201))).Should().BeFalse();
        Valid(Owner(addresses: new[] { new OwnershipAddressInput(null, new string('a', 301), null, null, null, null) })).Should().BeFalse();
        Valid(Owner(addresses: new[] { new OwnershipAddressInput(null, null, null, null, null, new string('9', 21)) })).Should().BeFalse();
        Valid(Owner(addresses: Enumerable.Range(0, 51).Select(_ => new OwnershipAddressInput(null, "x", null, null, null, null)).ToList())).Should().BeFalse();
    }

    [Theory]
    [InlineData("ProjectManager", "R", "R")]
    [InlineData("Designer", "CRUD", "CRUD")]
    [InlineData("DesignApprover", "R", "R")]
    [InlineData("Surveyor", "R", "CRU")]
    [InlineData("TitleResearcher", "RU", "R")]
    [InlineData("Appraiser", "R", "R")]
    [InlineData("AppraisalApprover", "R", "R")]
    [InlineData("Negotiator", "CRU", "R")]
    [InlineData("RelocationAgent", "R", "R")]
    [InlineData("Finance", "R", "R")]
    [InlineData("Admin", "CRUD", "CRUD")]
    public void AT_X_48_The_default_access_to_Ownership_and_Parcel_Location_follows_the_approved_matrix(string role, string ownership, string location)
    {
        static string Letters(PermissionFlags f) => (f.Create ? "C" : "") + (f.Read ? "R" : "") + (f.Update ? "U" : "") + (f.Delete ? "D" : "");

        Letters(PermissionTemplateCalculator.FlagsFor(DefaultRolePermissions.TemplateFor(role, PageKeys.Ownership))).Should().Be(ownership, $"{role} Ownership");
        Letters(PermissionTemplateCalculator.FlagsFor(DefaultRolePermissions.TemplateFor(role, PageKeys.ParcelLocation))).Should().Be(location, $"{role} Parcel Location");
    }

    // ── Title Encumbrances (slice 6a) ──

    private static AddEncumbranceCommand Enc(
        string type = "Mortgage Lien", string title = "Deed of Trust", string grantor = "Kamali", string grantee = "Bank", bool unrecorded = false,
        DateOnly? recorded = null, DateOnly? submitted = null, DateOnly? approved = null, string? analysis = null, IReadOnlyList<ContactLogLinkInput>? links = null) =>
        new(Guid.NewGuid(), type, title, null, recorded, null, unrecorded, grantor, grantee, submitted, approved, analysis, null, null, null, links);

    private static bool Valid(AddEncumbranceCommand c) => new AddEncumbranceCommandValidator().Validate(c).IsValid;

    [Theory]
    [InlineData("Mortgage Lien", "Deed", "A", "B", true)]
    [InlineData("", "Deed", "A", "B", false)]
    [InlineData("Mortgage Lien", "", "A", "B", false)]
    [InlineData("Mortgage Lien", "Deed", "", "B", false)]
    [InlineData("Mortgage Lien", "Deed", "A", "   ", false)]
    public void AT_TE_40_An_encumbrance_needs_a_type_a_document_title_a_grantor_and_a_grantee(string type, string title, string grantor, string grantee, bool valid) =>
        Valid(Enc(type, title, grantor, grantee)).Should().Be(valid);

    [Fact]
    public void AT_TE_40_An_unrecorded_document_cannot_have_a_recording_date()
    {
        var d = new DateOnly(2019, 3, 15);
        Valid(Enc(unrecorded: true, recorded: d)).Should().BeFalse();
        Valid(Enc(unrecorded: true)).Should().BeTrue();
        Valid(Enc(unrecorded: false, recorded: d)).Should().BeTrue();
    }

    [Fact]
    public void AT_TE_40_An_exception_cannot_be_approved_before_it_is_submitted()
    {
        var d = new DateOnly(2026, 2, 1);
        Valid(Enc(submitted: d, approved: d.AddDays(-1))).Should().BeFalse();
        Valid(Enc(submitted: d, approved: d)).Should().BeTrue();
        Valid(Enc(submitted: null, approved: d)).Should().BeTrue();
    }

    [Fact]
    public void AT_TE_40_Encumbrance_text_and_linked_records_have_limits()
    {
        Valid(Enc(analysis: new string('a', 4000))).Should().BeTrue();
        Valid(Enc(analysis: new string('a', 4001))).Should().BeFalse();
        Valid(Enc(title: new string('t', 301))).Should().BeFalse();
        Valid(Enc(links: new[] { new ContactLogLinkInput("", "x", null) })).Should().BeFalse();
        Valid(Enc(links: Enumerable.Range(0, 51).Select(i => new ContactLogLinkInput($"R{i}", null, null)).ToList())).Should().BeFalse();
    }

    [Theory]
    [InlineData("ProjectManager", "R")]
    [InlineData("Designer", "R")]
    [InlineData("DesignApprover", "R")]
    [InlineData("Surveyor", "R")]
    [InlineData("TitleResearcher", "CRUD")]
    [InlineData("Appraiser", "R")]
    [InlineData("AppraisalApprover", "R")]
    [InlineData("Negotiator", "R")]
    [InlineData("RelocationAgent", "R")]
    [InlineData("Finance", "R")]
    [InlineData("Admin", "CRUD")]
    public void AT_X_49_The_default_access_to_Title_Encumbrances_follows_the_approved_matrix(string role, string expected)
    {
        var f = PermissionTemplateCalculator.FlagsFor(DefaultRolePermissions.TemplateFor(role, PageKeys.TitleEncumbrances));
        ((f.Create ? "C" : "") + (f.Read ? "R" : "") + (f.Update ? "U" : "") + (f.Delete ? "D" : "")).Should().Be(expected, role);
    }

    // ── Title and Closing (slice 6b) ──

    private static SaveTitleClosingCommand TC(
        decimal? amount = null, DateOnly? recvd = null, DateOnly? exec = null, DateOnly? subm = null, DateOnly? appr = null, string? gf = null,
        IReadOnlyList<ConveyanceDocumentInput>? conveyances = null, IReadOnlyList<TitleCommitmentInput>? commitments = null) =>
        new(Guid.NewGuid(), null, null, null, gf, null, null, null, amount, recvd, exec, false, subm, appr, null, null, null, conveyances, commitments);

    private static bool Valid(SaveTitleClosingCommand c) => new SaveTitleClosingCommandValidator().Validate(c).IsValid;

    [Fact]
    public void AT_TE_41_An_empty_title_and_closing_record_is_valid_and_the_amount_cannot_be_negative()
    {
        Valid(TC()).Should().BeTrue();
        Valid(TC(amount: 0m)).Should().BeTrue();
        Valid(TC(amount: -0.01m)).Should().BeFalse();
        Valid(TC(gf: new string('g', 101))).Should().BeFalse();
    }

    [Fact]
    public void AT_TE_41_Closing_dates_must_come_in_order()
    {
        var d = new DateOnly(2026, 4, 10);
        Valid(TC(recvd: d, exec: d.AddDays(-1))).Should().BeFalse();
        Valid(TC(recvd: d, exec: d)).Should().BeTrue();
        Valid(TC(subm: d, appr: d.AddDays(-1))).Should().BeFalse();
        Valid(TC(subm: d, appr: d.AddDays(2))).Should().BeTrue();
        Valid(TC(subm: null, appr: d)).Should().BeTrue();
    }

    [Fact]
    public void AT_TE_41_Conveyances_are_recorded_after_they_are_signed_and_commitments_need_a_vested_owner_name()
    {
        var d = new DateOnly(2026, 4, 20);
        Valid(TC(conveyances: new[] { new ConveyanceDocumentInput("Deed", d, d.AddDays(-1), null) })).Should().BeFalse();
        Valid(TC(conveyances: new[] { new ConveyanceDocumentInput("Deed", d, d, "2026-1") })).Should().BeTrue();
        Valid(TC(commitments: new[] { new TitleCommitmentInput("", null, null, null, null, null, null) })).Should().BeFalse();
        Valid(TC(commitments: new[] { new TitleCommitmentInput("Kamali", null, null, null, null, d, d.AddDays(-1)) })).Should().BeFalse();
        Valid(TC(commitments: new[] { new TitleCommitmentInput("Kamali", null, null, null, null, d, d) })).Should().BeTrue();
        Valid(TC(commitments: Enumerable.Range(0, 51).Select(i => new TitleCommitmentInput($"O{i}", null, null, null, null, null, null)).ToList())).Should().BeFalse();
    }

    [Theory]
    [InlineData("ProjectManager", "R")]
    [InlineData("Designer", "R")]
    [InlineData("DesignApprover", "R")]
    [InlineData("Surveyor", "R")]
    [InlineData("TitleResearcher", "CRU")]
    [InlineData("Appraiser", "R")]
    [InlineData("AppraisalApprover", "R")]
    [InlineData("Negotiator", "CRU")]
    [InlineData("RelocationAgent", "R")]
    [InlineData("Finance", "R")]
    [InlineData("Admin", "CRUD")]
    public void AT_X_50_The_default_access_to_Title_and_Closing_follows_the_approved_matrix(string role, string expected)
    {
        var f = PermissionTemplateCalculator.FlagsFor(DefaultRolePermissions.TemplateFor(role, PageKeys.TitleClosing));
        ((f.Create ? "C" : "") + (f.Read ? "R" : "") + (f.Update ? "U" : "") + (f.Delete ? "D" : "")).Should().Be(expected, role);
    }
}
