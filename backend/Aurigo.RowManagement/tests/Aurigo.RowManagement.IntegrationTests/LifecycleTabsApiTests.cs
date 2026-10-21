using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aurigo.RowManagement.Application.Parcels;
using Aurigo.RowManagement.Application.ParcelTabs;
using Aurigo.RowManagement.Application.Parties;
using Aurigo.RowManagement.Application.Projects;
using Aurigo.RowManagement.IntegrationTests.Support;
using FluentAssertions;

namespace Aurigo.RowManagement.IntegrationTests;

internal static class LifecycleSupport
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public static async Task<T> Ok<T>(HttpResponseMessage r, HttpStatusCode expected = HttpStatusCode.OK)
    {
        r.StatusCode.Should().Be(expected, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadFromJsonAsync<T>(Json))!;
    }

    public static async Task<HttpStatusCode> Status(Task<HttpResponseMessage> t) => (await t).StatusCode;

    public static async Task<ParcelDto> NewParcel(PgApi api)
    {
        var admin = await api.As("Admin");
        var project = await Ok<ProjectDto>(await admin.Http.PostAsJsonAsync("/api/v1/projects", new { name = $"LC {Guid.NewGuid().ToString("N")[..6]}", oversightType = "Local" }, Json), HttpStatusCode.Created);
        return await Ok<ParcelDto>(await admin.Http.PostAsJsonAsync("/api/v1/parcels", new { projectId = project.Id, areaSqFt = 100m, acquisitionType = "Standard" }, Json), HttpStatusCode.Created);
    }

    public static IEnumerable<object[]> Roles() => Spec.Roles.Select(r => new object[] { r });
    public static HttpStatusCode Expect(bool allowed, HttpStatusCode ok) => allowed ? ok : HttpStatusCode.Forbidden;
}

/// <summary>Phase 1.5 slice 8a (ADR 0020): a parcel's Condemnation record on the real API and real Postgres. Matches AT-NE-43, AT-X-54.</summary>
public class CondemnationApiTests : IClassFixture<PgApi>
{
    private readonly PgApi _api;
    public CondemnationApiTests(PgApi api) => _api = api;
    private sealed record Envelope(CondemnationDto? Record);

    private static Dictionary<string, object?> Body(Dictionary<string, object?>? details = null, Dictionary<string, object?>? extra = null)
    {
        var d = new Dictionary<string, object?>
        {
            ["prelimPackageSubmittedToAtp"] = "2026-03-01", ["prelimPackageApprovedByAtp"] = "2026-03-05", ["cityCouncilApproval"] = "2026-04-01",
            ["edCoordinator"] = "Casey Closer", ["outsideCounsel"] = "Pat Agent", ["expertWitness1"] = "Dr. Smith", ["probateCauseNo"] = "PC-100",
            ["petitionFiledDate"] = "2026-05-01", ["objectionDeadline"] = "2026-06-01", ["objectionsFiled"] = true,
        };
        if (details is not null) foreach (var kv in details) d[kv.Key] = kv.Value;
        var b = new Dictionary<string, object?>
        {
            ["details"] = d,
            ["statusNotes"] = new[] { new { noteDate = "2026-05-02", text = "Petition filed." } },
            ["attorneys"] = new[] { new { partyRepresented = "Owner", attorneyName = "A. Lawyer", firm = "Law LLP", address = "1 Main", phone = "555", email = "a@l.com" } },
            ["commissioners"] = new[] { new { appointmentDate = "2026-05-10", appointmentType = "Court", name = "C. Commissioner", struck = false } },
            ["notices"] = new[] { new { partyToServe = "Owner", serviceSubmitted = "2026-05-12", serviceCompleted = "2026-05-15", nohFiled = "2026-05-16", comments = "Served." } },
        };
        if (extra is not null) foreach (var kv in extra) b[kv.Key] = kv.Value;
        return b;
    }

    [PgFact]
    public async Task AT_NE_43_A_negotiator_saves_and_changes_the_one_condemnation_record_and_only_admin_removes_it()
    {
        var parcel = await LifecycleSupport.NewParcel(_api);
        var ng = await _api.As("Negotiator");
        var url = $"/api/v1/parcels/{parcel.Id}/condemnation";

        (await LifecycleSupport.Ok<Envelope>(await ng.Http.GetAsync(url))).Record.Should().BeNull();
        var saved = await LifecycleSupport.Ok<CondemnationDto>(await ng.Http.PutAsJsonAsync(url, Body(), LifecycleSupport.Json));
        saved.Number.Should().Be($"CON-{parcel.Number}");
        saved.Details.ObjectionsFiled.Should().BeTrue();
        saved.Details.EdCoordinator.Should().Be("Casey Closer");
        saved.StatusNotes.Should().ContainSingle().Which.Text.Should().Be("Petition filed.");
        saved.Attorneys.Should().ContainSingle().Which.Firm.Should().Be("Law LLP");
        saved.Commissioners.Should().ContainSingle().Which.Name.Should().Be("C. Commissioner");
        saved.Notices.Should().ContainSingle().Which.Comments.Should().Be("Served.");

        var changed = await LifecycleSupport.Ok<CondemnationDto>(await ng.Http.PutAsJsonAsync(url, Body(new() { ["objectionsFiled"] = false }, new() { ["attorneys"] = Array.Empty<object>(), ["statusNotes"] = Array.Empty<object>() }), LifecycleSupport.Json));
        changed.Id.Should().Be(saved.Id);
        changed.Details.ObjectionsFiled.Should().BeFalse();
        changed.Attorneys.Should().BeEmpty();
        changed.StatusNotes.Should().BeEmpty("a save replaces the notes as sent");
        changed.Notices.Should().HaveCount(1);
        (await PgTestDatabase.ScalarAsync(_api.Database, "select count(*) from parcel_condemnations where parcel_id = @p", ("p", parcel.Id))).Should().Be(1);

        (await LifecycleSupport.Status(ng.Http.DeleteAsync(url))).Should().Be(HttpStatusCode.Forbidden, "the negotiator has create, read and update only");
        var admin = await _api.As("Admin");
        (await LifecycleSupport.Status(admin.Http.DeleteAsync(url))).Should().Be(HttpStatusCode.NoContent);
        (await LifecycleSupport.Ok<Envelope>(await admin.Http.GetAsync(url))).Record.Should().BeNull();
        (await PgTestDatabase.ScalarAsync(_api.Database, "select count(*) from record_status_notes where owner_id = @p", ("p", saved.Id))).Should().Be(0);
    }

    [PgFact]
    public async Task AT_NE_43_The_vested_owner_name_comes_from_the_ownerships_of_type_Vested_Owner()
    {
        var parcel = await LifecycleSupport.NewParcel(_api);
        var designer = await _api.As("Designer");
        await LifecycleSupport.Ok<PartyLinkDto>(await designer.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/parties", new { newParty = new { kind = "Person", name = "Vested One" }, role = "FeeOwner", ownerType = "Vested Owner", leaseOnFile = false }, LifecycleSupport.Json), HttpStatusCode.Created);
        var ng = await _api.As("Negotiator");

        var saved = await LifecycleSupport.Ok<CondemnationDto>(await ng.Http.PutAsJsonAsync($"/api/v1/parcels/{parcel.Id}/condemnation", Body(), LifecycleSupport.Json));

        saved.VestedOwnerName.Should().Be("Vested One");
    }

    [PgFact]
    public async Task AT_NE_43_Ten_first_saves_at_once_leave_exactly_one_record()
    {
        var parcel = await LifecycleSupport.NewParcel(_api);
        var ng = await _api.As("Negotiator");

        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(i => ng.Http.PutAsJsonAsync($"/api/v1/parcels/{parcel.Id}/condemnation", Body(new() { ["probateCauseNo"] = $"PC-{i}" }), LifecycleSupport.Json)));

        results.Select(r => r.StatusCode).Should().OnlyContain(s => s == HttpStatusCode.OK || s == HttpStatusCode.Conflict);
        results.Should().Contain(r => r.StatusCode == HttpStatusCode.OK);
        (await PgTestDatabase.ScalarAsync(_api.Database, "select count(*) from parcel_condemnations where parcel_id = @p", ("p", parcel.Id))).Should().Be(1);
    }

    [PgFact]
    public async Task AT_NE_43_Condemnation_input_is_checked()
    {
        var parcel = await LifecycleSupport.NewParcel(_api);
        var ng = await _api.As("Negotiator");
        var url = $"/api/v1/parcels/{parcel.Id}/condemnation";
        Task<HttpStatusCode> Put(Dictionary<string, object?> b) => LifecycleSupport.Status(ng.Http.PutAsJsonAsync(url, b, LifecycleSupport.Json));

        (await Put(new() { ["statusNotes"] = Array.Empty<object>() })).Should().Be(HttpStatusCode.BadRequest, "the details are required");
        (await Put(Body(new() { ["prelimPackageApprovedByAtp"] = "2026-02-01" }))).Should().Be(HttpStatusCode.BadRequest);
        (await Put(Body(new() { ["objectionDeadline"] = "2026-04-01" }))).Should().Be(HttpStatusCode.BadRequest);
        (await Put(Body(new() { ["probateCauseNo"] = new string('p', 101) }))).Should().Be(HttpStatusCode.BadRequest);
        (await Put(Body(null, new() { ["statusNotes"] = new[] { new { noteDate = "2026-05-01", text = "" } } }))).Should().Be(HttpStatusCode.BadRequest);
        (await Put(Body(null, new() { ["attorneys"] = new[] { new { partyRepresented = "" } } }))).Should().Be(HttpStatusCode.BadRequest);
        (await Put(Body(null, new() { ["commissioners"] = new[] { new { name = "" } } }))).Should().Be(HttpStatusCode.BadRequest);
        (await Put(Body(null, new() { ["notices"] = new[] { new { partyToServe = "O", serviceSubmitted = "2026-05-05", serviceCompleted = "2026-05-01" } } }))).Should().Be(HttpStatusCode.BadRequest);
        (await Put(Body(null, new() { ["attorneys"] = Enumerable.Range(0, 51).Select(_ => new { partyRepresented = "O" }).ToArray() }))).Should().Be(HttpStatusCode.BadRequest);
        (await LifecycleSupport.Status(ng.Http.GetAsync($"/api/v1/parcels/{Guid.NewGuid()}/condemnation"))).Should().Be(HttpStatusCode.NotFound);
        (await PgTestDatabase.ScalarAsync(_api.Database, "select count(*) from parcel_condemnations where parcel_id = @p", ("p", parcel.Id))).Should().Be(0);
    }

    [PgTheory]
    [MemberData(nameof(Roles))]
    public async Task AT_X_54_Each_role_can_do_exactly_what_the_Condemnation_page_allows(string role)
    {
        var parcel = await LifecycleSupport.NewParcel(_api);
        var other = await LifecycleSupport.NewParcel(_api);
        var admin = await _api.As("Admin");
        var url = $"/api/v1/parcels/{parcel.Id}/condemnation";
        await LifecycleSupport.Ok<CondemnationDto>(await admin.Http.PutAsJsonAsync(url, Body(), LifecycleSupport.Json));
        var user = await _api.As(role);
        bool Can(string action) => Spec.Can(role, "parcelcondemnation", action);

        (await LifecycleSupport.Status(user.Http.GetAsync(url))).Should().Be(LifecycleSupport.Expect(Can("read"), HttpStatusCode.OK));
        (await LifecycleSupport.Status(user.Http.PutAsJsonAsync($"/api/v1/parcels/{other.Id}/condemnation", Body(), LifecycleSupport.Json))).Should().Be(LifecycleSupport.Expect(Can("create"), HttpStatusCode.OK));
        (await LifecycleSupport.Status(user.Http.PutAsJsonAsync(url, Body(new() { ["probateCauseNo"] = $"by {role}" }), LifecycleSupport.Json))).Should().Be(LifecycleSupport.Expect(Can("update"), HttpStatusCode.OK));
        (await LifecycleSupport.Status(user.Http.DeleteAsync(url))).Should().Be(LifecycleSupport.Expect(Can("delete"), HttpStatusCode.NoContent));
    }

    public static IEnumerable<object[]> Roles() => LifecycleSupport.Roles();
}

/// <summary>Phase 1.5 slice 8b (ADR 0020): a parcel's Property Management and Disposition record. Matches AT-PM-43, AT-X-55.</summary>
public class PropertyManagementApiTests : IClassFixture<PgApi>
{
    private readonly PgApi _api;
    public PropertyManagementApiTests(PgApi api) => _api = api;
    private sealed record Envelope(PropertyDto? Record);

    private static Dictionary<string, object?> Body(Dictionary<string, object?>? details = null, Dictionary<string, object?>? extra = null)
    {
        var d = new Dictionary<string, object?>
        {
            ["transferMemoSubmittedToAtp"] = "2026-03-01", ["transferMemoApprovedByAtp"] = "2026-03-05", ["propertySecured"] = "2026-03-10", ["demoStatus"] = "Planned",
            ["dispositionType"] = "Sale", ["dispositionMethod"] = "Auction", ["dispositionStatus"] = "Open", ["buyerGrantee"] = "City", ["ftaConcurrenceRequired"] = true,
            ["salePrice"] = 125000.50m, ["closingTransferDate"] = "2026-06-01", ["recordingDate"] = "2026-06-05", ["documentRecordingNo"] = "2026-777",
        };
        if (details is not null) foreach (var kv in details) d[kv.Key] = kv.Value;
        var b = new Dictionary<string, object?>
        {
            ["details"] = d,
            ["statusNotes"] = new[] { new { noteDate = "2026-03-02", text = "Memo sent." } },
            ["funding"] = new[] { new { fundType = "Federal", fundSource = (string?)"FHWA-1", amount = 100000m, percentage = (decimal?)80m }, new { fundType = "Local", fundSource = (string?)null, amount = 25000m, percentage = (decimal?)20m } },
        };
        if (extra is not null) foreach (var kv in extra) b[kv.Key] = kv.Value;
        return b;
    }

    [PgFact]
    public async Task AT_PM_43_A_project_manager_saves_changes_and_removes_the_one_record()
    {
        var parcel = await LifecycleSupport.NewParcel(_api);
        var pm = await _api.As("ProjectManager");
        var url = $"/api/v1/parcels/{parcel.Id}/property-management";

        (await LifecycleSupport.Ok<Envelope>(await pm.Http.GetAsync(url))).Record.Should().BeNull();
        var saved = await LifecycleSupport.Ok<PropertyDto>(await pm.Http.PutAsJsonAsync(url, Body(), LifecycleSupport.Json));
        saved.Number.Should().Be($"PMD-{parcel.Number}");
        saved.Details.SalePrice.Should().Be(125000.50m);
        saved.Details.FtaConcurrenceRequired.Should().BeTrue();
        saved.Funding.Should().HaveCount(2);
        saved.StatusNotes.Should().ContainSingle().Which.Text.Should().Be("Memo sent.");

        var changed = await LifecycleSupport.Ok<PropertyDto>(await pm.Http.PutAsJsonAsync(url, Body(new() { ["dispositionStatus"] = "Closed" }, new() { ["funding"] = Array.Empty<object>() }), LifecycleSupport.Json));
        changed.Id.Should().Be(saved.Id);
        changed.Details.DispositionStatus.Should().Be("Closed");
        changed.Funding.Should().BeEmpty("a save replaces the grid as sent");
        (await PgTestDatabase.ScalarAsync(_api.Database, "select count(*) from parcel_property_records where parcel_id = @p", ("p", parcel.Id))).Should().Be(1);

        (await LifecycleSupport.Status(pm.Http.DeleteAsync(url))).Should().Be(HttpStatusCode.NoContent);
        (await LifecycleSupport.Ok<Envelope>(await pm.Http.GetAsync(url))).Record.Should().BeNull();
        (await LifecycleSupport.Status(pm.Http.DeleteAsync(url))).Should().Be(HttpStatusCode.NotFound);
    }

    [PgFact]
    public async Task AT_PM_43_Ten_first_saves_at_once_leave_exactly_one_record()
    {
        var parcel = await LifecycleSupport.NewParcel(_api);
        var pm = await _api.As("ProjectManager");

        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(i => pm.Http.PutAsJsonAsync($"/api/v1/parcels/{parcel.Id}/property-management", Body(new() { ["buyerGrantee"] = $"B{i}" }), LifecycleSupport.Json)));

        results.Select(r => r.StatusCode).Should().OnlyContain(s => s == HttpStatusCode.OK || s == HttpStatusCode.Conflict);
        (await PgTestDatabase.ScalarAsync(_api.Database, "select count(*) from parcel_property_records where parcel_id = @p", ("p", parcel.Id))).Should().Be(1);
    }

    [PgFact]
    public async Task AT_PM_43_Property_input_is_checked()
    {
        var parcel = await LifecycleSupport.NewParcel(_api);
        var pm = await _api.As("ProjectManager");
        var url = $"/api/v1/parcels/{parcel.Id}/property-management";
        Task<HttpStatusCode> Put(Dictionary<string, object?> b) => LifecycleSupport.Status(pm.Http.PutAsJsonAsync(url, b, LifecycleSupport.Json));

        (await Put(new() { ["statusNotes"] = Array.Empty<object>() })).Should().Be(HttpStatusCode.BadRequest);
        (await Put(Body(new() { ["salePrice"] = -1 }))).Should().Be(HttpStatusCode.BadRequest);
        (await Put(Body(new() { ["transferMemoApprovedByAtp"] = "2026-02-01" }))).Should().Be(HttpStatusCode.BadRequest);
        (await Put(Body(new() { ["recordingDate"] = "2026-05-01" }))).Should().Be(HttpStatusCode.BadRequest);
        (await Put(Body(new() { ["dispositionJustification"] = new string('j', 4001) }))).Should().Be(HttpStatusCode.BadRequest);
        (await Put(Body(null, new() { ["funding"] = new[] { new { fundType = "" } } }))).Should().Be(HttpStatusCode.BadRequest);
        (await Put(Body(null, new() { ["funding"] = new[] { new { fundType = "F", amount = -1m } } }))).Should().Be(HttpStatusCode.BadRequest);
        (await Put(Body(null, new() { ["funding"] = new[] { new { fundType = "F", percentage = 101m } } }))).Should().Be(HttpStatusCode.BadRequest);
        (await Put(Body(null, new() { ["funding"] = new[] { new { fundType = "F", percentage = 60m }, new { fundType = "G", percentage = 50m } } }))).Should().Be(HttpStatusCode.BadRequest);
        (await LifecycleSupport.Status(pm.Http.GetAsync($"/api/v1/parcels/{Guid.NewGuid()}/property-management"))).Should().Be(HttpStatusCode.NotFound);
        (await PgTestDatabase.ScalarAsync(_api.Database, "select count(*) from parcel_property_records where parcel_id = @p", ("p", parcel.Id))).Should().Be(0);
    }

    [PgTheory]
    [MemberData(nameof(Roles))]
    public async Task AT_X_55_Each_role_can_do_exactly_what_the_Property_Management_page_allows(string role)
    {
        var parcel = await LifecycleSupport.NewParcel(_api);
        var other = await LifecycleSupport.NewParcel(_api);
        var admin = await _api.As("Admin");
        var url = $"/api/v1/parcels/{parcel.Id}/property-management";
        await LifecycleSupport.Ok<PropertyDto>(await admin.Http.PutAsJsonAsync(url, Body(), LifecycleSupport.Json));
        var user = await _api.As(role);
        bool Can(string action) => Spec.Can(role, "parcelproperty", action);

        (await LifecycleSupport.Status(user.Http.GetAsync(url))).Should().Be(LifecycleSupport.Expect(Can("read"), HttpStatusCode.OK));
        (await LifecycleSupport.Status(user.Http.PutAsJsonAsync($"/api/v1/parcels/{other.Id}/property-management", Body(), LifecycleSupport.Json))).Should().Be(LifecycleSupport.Expect(Can("create"), HttpStatusCode.OK));
        (await LifecycleSupport.Status(user.Http.PutAsJsonAsync(url, Body(new() { ["buyerGrantee"] = $"by {role}" }), LifecycleSupport.Json))).Should().Be(LifecycleSupport.Expect(Can("update"), HttpStatusCode.OK));
        (await LifecycleSupport.Status(user.Http.DeleteAsync(url))).Should().Be(LifecycleSupport.Expect(Can("delete"), HttpStatusCode.NoContent));
    }

    public static IEnumerable<object[]> Roles() => LifecycleSupport.Roles();
}

/// <summary>Phase 1.5 slice 8c (ADR 0020): a parcel's relocations. Matches AT-RA-41, AT-X-56.</summary>
public class RelocationApiTests : IClassFixture<PgApi>
{
    private readonly PgApi _api;
    public RelocationApiTests(PgApi api) => _api = api;

    private static Dictionary<string, object?> Body(string number = "R-1", Dictionary<string, object?>? details = null, Dictionary<string, object?>? extra = null)
    {
        var d = new Dictionary<string, object?>
        {
            ["relocationNumber"] = number, ["displaceeName"] = "Kamali", ["relocationType"] = "Residential", ["assignedTo"] = "Casey Closer", ["limitedEnglishProficiency"] = true,
            ["ginMailedDate"] = "2026-03-01", ["ginDeliveredDate"] = "2026-03-03", ["initialInterview"] = "2026-03-10", ["targetVacateDate"] = "2026-09-01", ["demolitionRequired"] = true,
        };
        if (details is not null) foreach (var kv in details) d[kv.Key] = kv.Value;
        var b = new Dictionary<string, object?>
        {
            ["details"] = d,
            ["statusNotes"] = new[] { new { noteDate = "2026-03-02", text = "Interview booked." } },
            ["agents"] = new[] { new { agentName = "Pat Agent" } },
            ["contacts"] = new[] { new { name = "Alex", contactType = "Spouse", contact = "555", email = "a@x.com", address = "1 Oak", isPrimary = true } },
            ["eligibilities"] = new[] { new { eligibilityType = "Moving Expenses", claimLimit = 5000m, submittedForApproval = "2026-04-01", approved = "2026-04-05" } },
            ["contactLog"] = new[] { new { contactedAt = "2026-03-04T15:30:00Z", mode = "Phone", contactedBy = "Pat", contactPersons = "Alex", notes = "Left a message." } },
        };
        if (extra is not null) foreach (var kv in extra) b[kv.Key] = kv.Value;
        return b;
    }

    [PgFact]
    public async Task AT_RA_41_A_relocation_agent_adds_changes_and_removes_relocations_with_a_unique_number_per_parcel()
    {
        var parcel = await LifecycleSupport.NewParcel(_api);
        var ra = await _api.As("RelocationAgent");
        var url = $"/api/v1/parcels/{parcel.Id}/relocations";

        var first = await LifecycleSupport.Ok<RelocationDto>(await ra.Http.PostAsJsonAsync(url, Body("R-1"), LifecycleSupport.Json), HttpStatusCode.Created);
        await LifecycleSupport.Ok<RelocationDto>(await ra.Http.PostAsJsonAsync(url, Body("R-2"), LifecycleSupport.Json), HttpStatusCode.Created);
        first.Details.DisplaceeName.Should().Be("Kamali");
        first.Details.LimitedEnglishProficiency.Should().BeTrue();
        first.StatusNotes.Should().ContainSingle();
        first.Agents.Should().ContainSingle().Which.AgentName.Should().Be("Pat Agent");
        first.Contacts.Should().ContainSingle().Which.IsPrimary.Should().BeTrue();
        first.Eligibilities.Should().ContainSingle().Which.ClaimLimit.Should().Be(5000m);
        first.ContactLog.Should().ContainSingle().Which.Notes.Should().Be("Left a message.");

        (await LifecycleSupport.Status(ra.Http.PostAsJsonAsync(url, Body("R-1"), LifecycleSupport.Json))).Should().Be(HttpStatusCode.Conflict);

        var changed = await LifecycleSupport.Ok<RelocationDto>(await ra.Http.PutAsJsonAsync($"{url}/{first.Id}", Body("R-1", new() { ["relocationType"] = "Non-residential" }, new() { ["contacts"] = Array.Empty<object>() }), LifecycleSupport.Json));
        changed.Details.RelocationType.Should().Be("Non-residential");
        changed.Contacts.Should().BeEmpty("a save replaces the grids as sent");
        changed.Eligibilities.Should().HaveCount(1);
        (await LifecycleSupport.Status(ra.Http.PutAsJsonAsync($"{url}/{first.Id}", Body("R-2"), LifecycleSupport.Json))).Should().Be(HttpStatusCode.Conflict, "R-2 belongs to the other relocation");

        (await LifecycleSupport.Status(ra.Http.DeleteAsync($"{url}/{first.Id}"))).Should().Be(HttpStatusCode.NoContent);
        (await LifecycleSupport.Status(ra.Http.DeleteAsync($"{url}/{first.Id}"))).Should().Be(HttpStatusCode.NotFound);
        (await PgTestDatabase.ScalarAsync(_api.Database, "select count(*) from relocation_agents a join parcel_relocations r on r.id = a.relocation_id where r.parcel_id = @p and r.id = @i", ("p", parcel.Id), ("i", first.Id))).Should().Be(0);
    }

    [PgFact]
    public async Task AT_RA_41_An_agents_type_company_phone_and_email_are_read_from_the_Library_and_the_vested_owner_is_derived()
    {
        var parcel = await LifecycleSupport.NewParcel(_api);
        var admin = await _api.As("Admin");
        var name = $"Agent {Guid.NewGuid().ToString("N")[..6]}";
        (await LifecycleSupport.Status(admin.Http.PostAsJsonAsync("/api/v1/library/agents", new { name, agentType = "Relocation", company = "Move Co", phone = "555-0100", email = "m@example.com" }, LifecycleSupport.Json))).Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.OK);
        await LifecycleSupport.Ok<PartyLinkDto>(await admin.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/parties", new { newParty = new { kind = "Person", name = "Vested One" }, role = "FeeOwner", ownerType = "Vested Owner", leaseOnFile = false }, LifecycleSupport.Json), HttpStatusCode.Created);

        var made = await LifecycleSupport.Ok<RelocationDto>(await admin.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/relocations", Body("R-1", null, new() { ["agents"] = new[] { new { agentName = name } } }), LifecycleSupport.Json), HttpStatusCode.Created);

        made.Agents.Single().Should().BeEquivalentTo(new { AgentName = name, AgentType = "Relocation", Company = "Move Co", Phone = "555-0100", Email = "m@example.com" });
        made.VestedOwnerName.Should().Be("Vested One");
    }

    [PgFact]
    public async Task AT_RA_41_Relocation_input_is_checked()
    {
        var parcel = await LifecycleSupport.NewParcel(_api);
        var ra = await _api.As("RelocationAgent");
        var url = $"/api/v1/parcels/{parcel.Id}/relocations";
        Task<HttpStatusCode> Post(Dictionary<string, object?> b) => LifecycleSupport.Status(ra.Http.PostAsJsonAsync(url, b, LifecycleSupport.Json));

        (await Post(new() { ["statusNotes"] = Array.Empty<object>() })).Should().Be(HttpStatusCode.BadRequest);
        (await Post(Body(""))).Should().Be(HttpStatusCode.BadRequest);
        (await Post(Body("R", new() { ["displaceeName"] = "" }))).Should().Be(HttpStatusCode.BadRequest);
        (await Post(Body("R", new() { ["relocationType"] = "" }))).Should().Be(HttpStatusCode.BadRequest);
        (await Post(Body("R", new() { ["ginDeliveredDate"] = "2026-02-01" }))).Should().Be(HttpStatusCode.BadRequest);
        (await Post(Body("R", new() { ["actualVacateDate"] = "2026-01-01" }))).Should().Be(HttpStatusCode.BadRequest);
        (await Post(Body("R", null, new() { ["contacts"] = new[] { new { name = "A", isPrimary = true }, new { name = "B", isPrimary = true } } }))).Should().Be(HttpStatusCode.BadRequest);
        (await Post(Body("R", null, new() { ["eligibilities"] = new[] { new { eligibilityType = "" } } }))).Should().Be(HttpStatusCode.BadRequest);
        (await Post(Body("R", null, new() { ["eligibilities"] = new[] { new { eligibilityType = "T", claimLimit = -1m } } }))).Should().Be(HttpStatusCode.BadRequest);
        (await Post(Body("R", null, new() { ["agents"] = new[] { new { agentName = "" } } }))).Should().Be(HttpStatusCode.BadRequest);
        (await Post(Body("R", null, new() { ["contactLog"] = new[] { new { contactedAt = "0001-01-01T00:00:00Z" } } }))).Should().Be(HttpStatusCode.BadRequest);
        (await LifecycleSupport.Status(ra.Http.GetAsync($"/api/v1/parcels/{Guid.NewGuid()}/relocations"))).Should().Be(HttpStatusCode.NotFound);
        (await PgTestDatabase.ScalarAsync(_api.Database, "select count(*) from parcel_relocations where parcel_id = @p", ("p", parcel.Id))).Should().Be(0);
    }

    [PgTheory]
    [MemberData(nameof(Roles))]
    public async Task AT_X_56_Each_role_can_do_exactly_what_the_Relocation_page_allows(string role)
    {
        var parcel = await LifecycleSupport.NewParcel(_api);
        var admin = await _api.As("Admin");
        var url = $"/api/v1/parcels/{parcel.Id}/relocations";
        var seed = await LifecycleSupport.Ok<RelocationDto>(await admin.Http.PostAsJsonAsync(url, Body("SEED"), LifecycleSupport.Json), HttpStatusCode.Created);
        var doomed = await LifecycleSupport.Ok<RelocationDto>(await admin.Http.PostAsJsonAsync(url, Body("DOOMED"), LifecycleSupport.Json), HttpStatusCode.Created);
        var user = await _api.As(role);
        bool Can(string action) => Spec.Can(role, "parcelrelocation", action);

        (await LifecycleSupport.Status(user.Http.GetAsync(url))).Should().Be(LifecycleSupport.Expect(Can("read"), HttpStatusCode.OK));
        (await LifecycleSupport.Status(user.Http.PostAsJsonAsync(url, Body($"by-{role}"), LifecycleSupport.Json))).Should().Be(LifecycleSupport.Expect(Can("create"), HttpStatusCode.Created));
        (await LifecycleSupport.Status(user.Http.PutAsJsonAsync($"{url}/{seed.Id}", Body("SEED", new() { ["displaceeName"] = $"by {role}" }), LifecycleSupport.Json))).Should().Be(LifecycleSupport.Expect(Can("update"), HttpStatusCode.OK));
        (await LifecycleSupport.Status(user.Http.DeleteAsync($"{url}/{doomed.Id}"))).Should().Be(LifecycleSupport.Expect(Can("delete"), HttpStatusCode.NoContent));
    }

    public static IEnumerable<object[]> Roles() => LifecycleSupport.Roles();
}
