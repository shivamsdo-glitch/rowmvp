using System.Net;
using System.Net.Http.Json;
using Aurigo.RowManagement.Application.Dashboard;
using Aurigo.RowManagement.Application.Parcels;
using Aurigo.RowManagement.Application.ParcelTabs;
using Aurigo.RowManagement.Application.Parties;
using Aurigo.RowManagement.IntegrationTests.Support;
using FluentAssertions;

namespace Aurigo.RowManagement.IntegrationTests;

/// <summary>ADR 0026: the relocation case workflow on the real API and Postgres. Matches AT-RA-42, AT-RA-43, AT-RA-44, AT-X-64.</summary>
public class RelocationWorkflowApiTests : IClassFixture<PgApi>
{
    private readonly PgApi _api;
    public RelocationWorkflowApiTests(PgApi api) => _api = api;

    private static Task<T> Ok<T>(HttpResponseMessage r, HttpStatusCode e = HttpStatusCode.OK) => LifecycleSupport.Ok<T>(r, e);

    private async Task<PartyLinkDto> AddOwner(ParcelDto parcel, string name, bool displaced)
    {
        var admin = await _api.As("Admin");
        return await Ok<PartyLinkDto>(await admin.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/parties",
            new { newParty = new { kind = "Person", name }, role = "FeeOwner", ownerType = "Fee Owner", leaseOnFile = false, displacementRequired = displaced }, LifecycleSupport.Json), HttpStatusCode.Created);
    }

    private static object Details(string number, Guid? linkId, string? complete = null, Dictionary<string, object?>? more = null)
    {
        var d = new Dictionary<string, object?>
        {
            ["relocationNumber"] = number, ["displaceeName"] = "Kamali", ["relocationType"] = "Residential", ["partyLinkId"] = linkId,
            ["actualVacateDate"] = complete is null ? null : "2026-05-01", ["relocationComplete"] = complete,
        };
        if (more is not null) foreach (var kv in more) d[kv.Key] = kv.Value;
        return new { details = d };
    }

    private async Task<string> StatusOf(ParcelDto parcel)
    {
        var admin = await _api.As("Admin");
        return (await Ok<ParcelDto>(await admin.Http.GetAsync($"/api/v1/parcels/{parcel.Id}"))).StatusName;
    }

    [PgFact]
    public async Task AT_RA_42_The_parcel_becomes_Acquired_once_every_displaced_interest_has_finished_relocating()
    {
        var parcel = await LifecycleSupport.NewParcel(_api);
        var a = await AddOwner(parcel, "Owner A", true);
        var b = await AddOwner(parcel, "Owner B", true);
        await AddOwner(parcel, "Owner C", false);
        var ra = await _api.As("RelocationAgent");
        var url = $"/api/v1/parcels/{parcel.Id}/relocations";

        var first = await Ok<RelocationDto>(await ra.Http.PostAsJsonAsync(url, Details("R-A", a.Id, "2026-06-01"), LifecycleSupport.Json), HttpStatusCode.Created);
        first.InterestOwnerName.Should().Be("Owner A");
        (await StatusOf(parcel)).Should().NotBe("Acquired");
        var progress = await Ok<ParcelRelocationProgressDto>(await ra.Http.GetAsync($"/api/v1/parcels/{parcel.Id}/relocation-progress"));
        progress.Should().BeEquivalentTo(new { DisplacedInterests = 2, CompletedInterests = 1, ReadyToAcquire = false, AcquiredStatusAvailable = true });

        var second = await Ok<RelocationDto>(await ra.Http.PostAsJsonAsync(url, Details("R-B", b.Id), LifecycleSupport.Json), HttpStatusCode.Created);
        (await StatusOf(parcel)).Should().NotBe("Acquired");
        await Ok<RelocationDto>(await ra.Http.PutAsJsonAsync($"{url}/{second.Id}", Details("R-B", b.Id, "2026-06-15"), LifecycleSupport.Json));
        (await StatusOf(parcel)).Should().Be("Acquired");
    }

    [PgFact]
    public async Task AT_RA_42_A_parcel_with_no_displaced_interests_is_not_changed_and_a_foreign_interest_is_refused()
    {
        var parcel = await LifecycleSupport.NewParcel(_api);
        var other = await LifecycleSupport.NewParcel(_api);
        var plain = await AddOwner(parcel, "Stays", false);
        var foreign = await AddOwner(other, "Elsewhere", true);
        var ra = await _api.As("RelocationAgent");
        var url = $"/api/v1/parcels/{parcel.Id}/relocations";

        var before = await StatusOf(parcel);
        await Ok<RelocationDto>(await ra.Http.PostAsJsonAsync(url, Details("R-1", plain.Id, "2026-06-01"), LifecycleSupport.Json), HttpStatusCode.Created);
        (await StatusOf(parcel)).Should().Be(before);
        (await LifecycleSupport.Status(ra.Http.PostAsJsonAsync(url, Details("R-2", foreign.Id), LifecycleSupport.Json))).Should().Be(HttpStatusCode.BadRequest);
        (await LifecycleSupport.Status(ra.Http.PostAsJsonAsync(url, Details("R-3", null, null, new() { ["eligibilityException"] = "Because" }), LifecycleSupport.Json))).Should().Be(HttpStatusCode.BadRequest);
    }

    [PgFact]
    public async Task AT_RA_43_A_payment_goes_through_the_approval_chain_and_Finance_marks_it_paid()
    {
        var admin = await _api.As("Admin");
        // Other tests save bands; start from the placeholder chain.
        await Ok<RelocationBandsDto>(await admin.Http.PutAsJsonAsync("/api/v1/relocation-payment-bands", new
        {
            bands = new[] { new { minAmount = 0m, maxAmount = (decimal?)25000m, roles = new[] { "ProjectManager" } }, new { minAmount = 25000m, maxAmount = (decimal?)null, roles = new[] { "ProjectManager", "Finance" } } },
        }, LifecycleSupport.Json));

        var parcel = await LifecycleSupport.NewParcel(_api);
        var ra = await _api.As("RelocationAgent");
        var url = $"/api/v1/parcels/{parcel.Id}/relocations";
        var reloc = await Ok<RelocationDto>(await ra.Http.PostAsJsonAsync(url, Details("R-1", null), LifecycleSupport.Json), HttpStatusCode.Created);
        var pay = $"{url}/{reloc.Id}/payments";

        (await LifecycleSupport.Status(ra.Http.PostAsJsonAsync(pay, new { paymentType = "Moving expense", amount = 0m }, LifecycleSupport.Json))).Should().Be(HttpStatusCode.BadRequest);
        var small = (await Ok<RelocationDto>(await ra.Http.PostAsJsonAsync(pay, new { paymentType = "Moving expense", amount = 1500m, taxable = false }, LifecycleSupport.Json))).Payments.Single();
        small.Status.Should().Be("Requested");
        small.RequiredRoles.Should().Equal("ProjectManager");

        // The requester cannot sign, and neither can a role that is not next.
        (await LifecycleSupport.Status(ra.Http.PostAsJsonAsync($"{pay}/{small.Id}/decision", new { approve = true }, LifecycleSupport.Json))).Should().Be(HttpStatusCode.Forbidden);
        var fin = await _api.As("Finance");
        (await LifecycleSupport.Status(fin.Http.PostAsJsonAsync($"{pay}/{small.Id}/decision", new { approve = true }, LifecycleSupport.Json))).Should().Be(HttpStatusCode.Forbidden);
        // Not approved yet, so it cannot be paid.
        (await LifecycleSupport.Status(fin.Http.PostAsJsonAsync($"{pay}/{small.Id}/paid", new { reference = "X" }, LifecycleSupport.Json))).Should().Be(HttpStatusCode.Conflict);

        var pm = await _api.As("ProjectManager");
        var approved = (await Ok<RelocationDto>(await pm.Http.PostAsJsonAsync($"{pay}/{small.Id}/decision", new { approve = true }, LifecycleSupport.Json))).Payments.Single();
        approved.Status.Should().Be("Approved");
        (await LifecycleSupport.Status(pm.Http.PostAsJsonAsync($"{pay}/{small.Id}/paid", new { reference = "X" }, LifecycleSupport.Json))).Should().Be(HttpStatusCode.Forbidden);
        var paid = (await Ok<RelocationDto>(await fin.Http.PostAsJsonAsync($"{pay}/{small.Id}/paid", new { reference = "EFT-1" }, LifecycleSupport.Json))).Payments.Single();
        paid.Status.Should().Be("Paid");
        paid.PaymentReference.Should().Be("EFT-1");

        // A larger payment needs the Project Manager and then Finance, in that order.
        var big = (await Ok<RelocationDto>(await ra.Http.PostAsJsonAsync(pay, new { paymentType = "Replacement housing - purchase supplement", amount = 40000m, taxable = true }, LifecycleSupport.Json))).Payments.Last();
        big.RequiredRoles.Should().Equal("ProjectManager", "Finance");
        (await LifecycleSupport.Status(fin.Http.PostAsJsonAsync($"{pay}/{big.Id}/decision", new { approve = true }, LifecycleSupport.Json))).Should().Be(HttpStatusCode.Forbidden);
        var step1 = (await Ok<RelocationDto>(await pm.Http.PostAsJsonAsync($"{pay}/{big.Id}/decision", new { approve = true }, LifecycleSupport.Json))).Payments.Last();
        step1.Status.Should().Be("Requested");
        step1.NextApprover.Should().Be("Finance");
        (await Ok<RelocationDto>(await fin.Http.PostAsJsonAsync($"{pay}/{big.Id}/decision", new { approve = true }, LifecycleSupport.Json))).Payments.Last().Status.Should().Be("Approved");

        // A rejection needs a reason, and an approved or paid payment blocks removing the relocation.
        var third = (await Ok<RelocationDto>(await ra.Http.PostAsJsonAsync(pay, new { paymentType = "Moving expense", amount = 10m }, LifecycleSupport.Json))).Payments.Last();
        (await LifecycleSupport.Status(pm.Http.PostAsJsonAsync($"{pay}/{third.Id}/decision", new { approve = false }, LifecycleSupport.Json))).Should().Be(HttpStatusCode.BadRequest);
        (await Ok<RelocationDto>(await pm.Http.PostAsJsonAsync($"{pay}/{third.Id}/decision", new { approve = false, note = "No receipt" }, LifecycleSupport.Json))).Payments.Last().Status.Should().Be("Rejected");
        (await LifecycleSupport.Status(ra.Http.DeleteAsync($"{url}/{reloc.Id}"))).Should().Be(HttpStatusCode.Conflict);
    }

    [PgFact]
    public async Task AT_RA_44_Only_an_administrator_changes_the_approval_bands_and_a_broken_chain_is_refused()
    {
        var admin = await _api.As("Admin");
        var ra = await _api.As("RelocationAgent");
        var good = new { bands = new[] { new { minAmount = 0m, maxAmount = (decimal?)5000m, roles = new[] { "RelocationAgent" } }, new { minAmount = 5000m, maxAmount = (decimal?)null, roles = new[] { "ProjectManager", "Finance" } } } };
        (await LifecycleSupport.Status(ra.Http.PutAsJsonAsync("/api/v1/relocation-payment-bands", good, LifecycleSupport.Json))).Should().Be(HttpStatusCode.Forbidden);
        var gap = new { bands = new[] { new { minAmount = 0m, maxAmount = (decimal?)5000m, roles = new[] { "ProjectManager" } }, new { minAmount = 6000m, maxAmount = (decimal?)null, roles = new[] { "Finance" } } } };
        (await LifecycleSupport.Status(admin.Http.PutAsJsonAsync("/api/v1/relocation-payment-bands", gap, LifecycleSupport.Json))).Should().Be(HttpStatusCode.BadRequest);
        var saved = await Ok<RelocationBandsDto>(await admin.Http.PutAsJsonAsync("/api/v1/relocation-payment-bands", good, LifecycleSupport.Json));
        saved.IsDefault.Should().BeFalse();
        (await Ok<RelocationBandsDto>(await ra.Http.GetAsync("/api/v1/relocation-payment-bands"))).Bands.Should().HaveCount(2);
    }

    [PgFact]
    public async Task AT_X_64_The_Relocations_work_screen_finds_a_case_by_owner_number_or_parcel()
    {
        var parcel = await LifecycleSupport.NewParcel(_api);
        var owner = await AddOwner(parcel, "Quillfeather Holdings", true);
        var ra = await _api.As("RelocationAgent");
        await Ok<RelocationDto>(await ra.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/relocations", Details("RW-77", owner.Id), LifecycleSupport.Json), HttpStatusCode.Created);

        foreach (var term in new[] { "quillfeather", "rw-77", parcel.Number })
        {
            var rows = await Ok<List<RelocationOverviewRow>>(await ra.Http.GetAsync($"/api/v1/overview/relocations?search={Uri.EscapeDataString(term)}"));
            rows.Should().Contain(r => r.ParcelId == parcel.Id && r.Number == "RW-77");
        }
        (await Ok<List<RelocationOverviewRow>>(await ra.Http.GetAsync("/api/v1/overview/relocations?search=zzzz-none"))).Should().BeEmpty();
    }

    [PgFact]
    public async Task AT_RA_42_The_dashboards_show_open_relocations_payments_to_sign_and_the_program_row()
    {
        var admin = await _api.As("Admin");
        await Ok<RelocationBandsDto>(await admin.Http.PutAsJsonAsync("/api/v1/relocation-payment-bands", new
        {
            bands = new[] { new { minAmount = 0m, maxAmount = (decimal?)null, roles = new[] { "ProjectManager" } } },
        }, LifecycleSupport.Json));
        var parcel = await LifecycleSupport.NewParcel(_api);
        var owner = await AddOwner(parcel, "Dash Owner", true);
        var ra = await _api.As("RelocationAgent");
        var pm = await _api.As("ProjectManager");
        var before = await Ok<MyDashboardDto>(await ra.Http.GetAsync("/api/v1/dashboard/me"));
        var pmBefore = await Ok<MyDashboardDto>(await pm.Http.GetAsync("/api/v1/dashboard/me"));
        var reloc = await Ok<RelocationDto>(await ra.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/relocations", Details("D-1", owner.Id), LifecycleSupport.Json), HttpStatusCode.Created);
        await Ok<RelocationDto>(await ra.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/relocations/{reloc.Id}/payments", new { paymentType = "Moving expense", amount = 900m }, LifecycleSupport.Json));

        var after = await Ok<MyDashboardDto>(await ra.Http.GetAsync("/api/v1/dashboard/me"));
        after.Pending.Single(p => p.Key == "relocations-open").Count.Should().Be(before.Pending.Single(p => p.Key == "relocations-open").Count + 1);
        after.Pending.Should().NotContain(p => p.Key == "relocation-payments-to-sign", "nobody signs their own request");
        var pmAfter = await Ok<MyDashboardDto>(await pm.Http.GetAsync("/api/v1/dashboard/me"));
        var signBefore = pmBefore.Pending.FirstOrDefault(p => p.Key == "relocation-payments-to-sign")?.Count ?? 0;
        pmAfter.Pending.Single(p => p.Key == "relocation-payments-to-sign").Count.Should().Be(signBefore + 1);

        var team = await Ok<TeamDashboardDto>(await pm.Http.GetAsync("/api/v1/dashboard/team"));
        team.Tiles.Select(t => t.Key).Should().Contain(new[] { "relocations-open", "relocation-payments" });
        var program = await Ok<ProgramDashboardDto>(await pm.Http.GetAsync("/api/v1/dashboard/program"));
        program.Tiles.Select(t => t.Key).Should().Contain("relocating");
        var project = await Ok<ProjectDashboardDto>(await pm.Http.GetAsync($"/api/v1/dashboard/projects/{parcel.ProjectId}"));
        project.Project.Displaced.Should().Be(1);
        project.Project.Relocated.Should().Be(0);
        project.Parcels.Single().Relocation.Should().Be("0 of 1 relocated");
    }
}
