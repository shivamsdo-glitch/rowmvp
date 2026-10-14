using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aurigo.RowManagement.Application.Common;
using Aurigo.RowManagement.Application.Parcels;
using Aurigo.RowManagement.Application.Projects;
using Aurigo.RowManagement.Domain.Entities;
using Aurigo.RowManagement.Domain.Enums;
using Aurigo.RowManagement.Infrastructure.Persistence;
using Aurigo.RowManagement.Infrastructure.Persistence.Seed;
using Aurigo.RowManagement.IntegrationTests.Support;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Aurigo.RowManagement.IntegrationTests;

/// <summary>
/// Parcel core on the real API and real Postgres (ROW-002, 005..011, 071..074, 233..237): the job, every role in both
/// directions, races, tenancy, audit, schema, seeded data and hostile input. Matches acceptance cases AT-PM-21..25, AT-AD-22.
/// </summary>
public class ParcelApiTests : IClassFixture<PgApi>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly Guid OtherTenant = Guid.Parse("00000000-0000-0000-0000-0000000000d4");
    private readonly PgApi _api;
    public ParcelApiTests(PgApi api) => _api = api;

    // ── helpers ──

    private static async Task<T> Read<T>(HttpResponseMessage r) => (await r.Content.ReadFromJsonAsync<T>(Json))!;

    private static async Task<T> Ok<T>(HttpResponseMessage r, HttpStatusCode expected = HttpStatusCode.OK)
    {
        r.StatusCode.Should().Be(expected, await r.Content.ReadAsStringAsync());
        return await Read<T>(r);
    }

    private async Task<ProjectDto> NewProject(TestUser user, string name = "Parcel test project", string oversight = "Local")
    {
        var res = await user.Http.PostAsJsonAsync("/api/v1/projects", new { name = $"{name} {Guid.NewGuid().ToString("N")[..6]}", oversightType = oversight }, Json);
        return await Ok<ProjectDto>(res, HttpStatusCode.Created);
    }

    /// <summary>A federal project that is cleared to acquire (NEPA decision plus an acquisition authorization).</summary>
    private async Task<ProjectDto> ClearedProject(TestUser user)
    {
        var project = await NewProject(user, "Cleared", "Federal");
        (await user.Http.PutAsJsonAsync($"/api/v1/projects/{project.Id}/nepa", new { decisionType = "CategoricalExclusion", decisionDate = "2026-01-10" }, Json)).EnsureSuccessStatusCode();
        (await user.Http.PostAsJsonAsync($"/api/v1/projects/{project.Id}/authorizations",
            new { type = "Acquisition", authorizedAmount = 1000m, authorizedOn = "2026-02-01" }, Json)).EnsureSuccessStatusCode();
        return project;
    }

    private async Task<ParcelDto> NewParcel(TestUser user, Guid projectId, string? number = null, decimal area = 10_000m, object? extra = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["projectId"] = projectId, ["number"] = number, ["areaSqFt"] = area, ["address"] = "12 Main St", ["acquisitionType"] = "Standard",
        };
        if (extra is not null)
            foreach (var p in extra.GetType().GetProperties()) body[char.ToLowerInvariant(p.Name[0]) + p.Name[1..]] = p.GetValue(extra);
        return await Ok<ParcelDto>(await user.Http.PostAsJsonAsync("/api/v1/parcels", body, Json), HttpStatusCode.Created);
    }

    private static async Task<ParcelDto> GetParcel(TestUser user, Guid id) => await Ok<ParcelDto>(await user.Http.GetAsync($"/api/v1/parcels/{id}"));

    private async Task EnsureOtherTenantAsync()
    {
        using var scope = _api.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RowDbContext>();
        if (!await db.Tenants.AnyAsync(t => t.Id == OtherTenant))
        {
            db.Tenants.Add(new Tenant { Id = OtherTenant, Name = "Other Agency D4", Slug = "other-d4", IsActive = true });
            await db.SaveChangesAsync();
        }
        await scope.ServiceProvider.GetRequiredService<TenantProvisioner>().EnsureDefaultRolesAsync(OtherTenant);
    }

    private static async Task<IReadOnlyList<ParcelStatusDto>> Statuses(TestUser user) =>
        await Ok<List<ParcelStatusDto>>(await user.Http.GetAsync("/api/v1/parcel-rules/statuses"));

    // ── the PM's job (AT-PM-21, 23) ──

    [PgFact]
    public async Task AT_PM_21_A_parent_tract_is_split_into_parcels_with_a_remnant_and_the_areas_add_up()
    {
        var pm = await _api.As("ProjectManager");
        var project = await NewProject(pm);
        var group = await Ok<ParcelGroupDto>(await pm.Http.PostAsJsonAsync($"/api/v1/projects/{project.Id}/groups", new { name = "North corridor" }, Json), HttpStatusCode.Created);
        var tract = await Ok<ParentTractDto>(await pm.Http.PostAsJsonAsync($"/api/v1/projects/{project.Id}/tracts",
            new { number = "T-100", description = "Harbor Holdings", taxParcelNumbers = new[] { "APN 044-120-07", "APN 044-120-08" } }, Json), HttpStatusCode.Created);

        var parcel = await NewParcel(pm, project.Id, "100", 10_000m, new { ParentTractId = tract.Id, GroupId = group.Id, LegalDescription = "Lot 7" });
        parcel.TaxParcelNumbers.Should().Equal("APN 044-120-07", "APN 044-120-08");
        parcel.GroupName.Should().Be("North corridor");

        var split = await Ok<ParcelSplitResultDto>(await pm.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/split",
            new { parts = new[] { new { areaSqFt = 3_000m }, new { areaSqFt = 2_500m } } }, Json), HttpStatusCode.Created);

        split.Parts.Select(p => (p.Number, p.AreaSqFt)).Should().Equal(("100-A", 3_000m), ("100-B", 2_500m));
        split.Remnant!.Number.Should().Be("100-R");
        split.Remnant.AreaSqFt.Should().Be(4_500m);
        (split.Parts.Sum(p => p.AreaSqFt) + split.Remnant.AreaSqFt).Should().Be(10_000m);
        split.Parts.Concat(new[] { split.Remnant }).Should().OnlyContain(p => p.GroupId == group.Id && p.ParentTractId == tract.Id);
        split.Source.State.Should().Be(ParcelState.Split);

        // The database holds the lineage and the uuid / text arrays are real arrays.
        (await PgTestDatabase.ScalarAsync(_api.Database, "select count(*) from parcels where @src = any(source_parcel_ids)", ("src", parcel.Id))).Should().Be(3);
        (await PgTestDatabase.ScalarAsync(_api.Database, "select cardinality(tax_parcel_numbers) from parent_tracts where id = @id", ("id", tract.Id))).Should().Be(2);

        var live = await Ok<PagedResult<ParcelSummaryDto>>(await pm.Http.GetAsync($"/api/v1/parcels?projectId={project.Id}"));
        live.Items.Select(p => p.Number).Should().Equal("100-A", "100-B", "100-R");
        (await Ok<PagedResult<ParcelSummaryDto>>(await pm.Http.GetAsync($"/api/v1/parcels?projectId={project.Id}&includeSuperseded=true"))).Total.Should().Be(4);

        var rollup = await Ok<ParcelRollupDto>(await pm.Http.GetAsync($"/api/v1/projects/{project.Id}/parcel-rollup"));
        rollup.TotalCount.Should().Be(3);
        rollup.TotalAreaSqFt.Should().Be(10_000m);
    }

    [PgFact]
    public async Task AT_PM_21_Combining_parcels_adds_their_areas_and_merges_their_interests()
    {
        var pm = await _api.As("ProjectManager");
        var project = await NewProject(pm);
        var a = await NewParcel(pm, project.Id, area: 1_000m);
        var b = await NewParcel(pm, project.Id, area: 2_000m);
        await pm.Http.PostAsJsonAsync($"/api/v1/parcels/{a.Id}/interests", new { type = "FeeTaking", areaSqFt = 300m }, Json);
        await pm.Http.PostAsJsonAsync($"/api/v1/parcels/{b.Id}/interests", new { type = "FeeTaking", areaSqFt = 500m }, Json);

        var combined = await Ok<ParcelDto>(await pm.Http.PostAsJsonAsync("/api/v1/parcels/combine", new { parcelIds = new[] { a.Id, b.Id } }, Json), HttpStatusCode.Created);

        combined.AreaSqFt.Should().Be(3_000m);
        combined.Interests.Should().ContainSingle().Which.AreaSqFt.Should().Be(800m);
        (await GetParcel(pm, a.Id)).State.Should().Be(ParcelState.Combined);
        (await PgTestDatabase.ScalarAsync(_api.Database, "select count(*) from parcels where state = 'Combined' and project_id = @p", ("p", project.Id))).Should().Be(2);
    }

    [PgFact]
    public async Task AT_PM_23_Milestones_and_the_notice_drive_the_deadline_and_the_project_rollup_counts_match()
    {
        var pm = await _api.As("ProjectManager");
        var project = await NewProject(pm);
        var late = await NewParcel(pm, project.Id, "LATE");
        var fresh = await NewParcel(pm, project.Id, "FRESH");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var lateView = await Ok<ParcelDto>(await pm.Http.PutAsJsonAsync($"/api/v1/parcels/{late.Id}/notice", new { issuedOn = today.AddDays(-100), method = "CertifiedMail" }, Json));
        lateView.Milestones.Single(m => m.Name == "Notice to owner").CompletedOn.Should().Be(today.AddDays(-100));
        lateView.Deadlines.Single(d => d.TriggerMilestone == "Notice to owner").Status.Should().Be("Overdue");
        await pm.Http.PutAsJsonAsync($"/api/v1/parcels/{fresh.Id}/notice", new { issuedOn = today.AddDays(-5), method = "HandDelivery" }, Json);

        var custom = await Ok<ParcelDto>(await pm.Http.PostAsJsonAsync($"/api/v1/parcels/{fresh.Id}/milestones",
            new { name = "Appraiser walkthrough", dueOn = today.AddDays(-2), notes = "owner away" }, Json), HttpStatusCode.Created);
        custom.Milestones.Single(m => m.Name == "Appraiser walkthrough").IsOverdue.Should().BeTrue();
        var duplicate = await pm.Http.PostAsJsonAsync($"/api/v1/parcels/{fresh.Id}/milestones", new { name = "appraiser WALKTHROUGH" }, Json);
        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var reminders = await Ok<List<ReminderDto>>(await pm.Http.GetAsync("/api/v1/reminders?withinDays=7"));
        reminders.Where(r => r.ProjectId == project.Id).Select(r => (r.ParcelNumber, r.Status)).Should().Equal(("LATE", "Overdue"));

        var rollup = await Ok<ParcelRollupDto>(await pm.Http.GetAsync($"/api/v1/projects/{project.Id}/parcel-rollup"));
        rollup.TotalCount.Should().Be(2);
        rollup.ByStatus.Single(b => b.Name == "Identified").Count.Should().Be(2);
    }

    [PgFact]
    public async Task AT_PM_24_The_step_gate_blocks_out_of_order_moves_and_an_override_needs_a_reason_and_is_audited()
    {
        var pm = await _api.As("ProjectManager", user: Guid.NewGuid());
        var project = await NewProject(pm);
        var parcel = await NewParcel(pm, project.Id);

        var skip = await pm.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/advance", new { targetStepKey = "appraisal" }, Json);
        skip.StatusCode.Should().Be(HttpStatusCode.BadRequest, "an out-of-order move without a reason is a validation problem");
        (await skip.Content.ReadAsStringAsync()).Should().Contain("reason is required");

        var inOrder = await Ok<ParcelDto>(await pm.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/advance", new { targetStepKey = "title_survey" }, Json));
        inOrder.Workflow.CurrentKey.Should().Be("title_survey");

        var overridden = await Ok<ParcelDto>(await pm.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/advance",
            new { targetStepKey = "closing", overrideReason = "Closing under the 2024 agreement" }, Json));
        overridden.Workflow.CurrentKey.Should().Be("closing");
        overridden.StepHistory.Should().HaveCount(2);
        overridden.StepHistory[0].Should().Match<ParcelStepEventDto>(e => e.IsOverride && e.Reason == "Closing under the 2024 agreement" && e.By == pm.UserId);

        (await PgTestDatabase.ScalarAsync(_api.Database,
            "select count(*) from parcel_step_events where parcel_id = @p and is_override and reason = @r and created_by = @u",
            ("p", parcel.Id), ("r", "Closing under the 2024 agreement"), ("u", pm.UserId))).Should().Be(1);
        (await PgTestDatabase.ScalarAsync(_api.Database,
            "select count(*) from audit_log where entity_type = 'ParcelStepEvent' and changed_by = @u", ("u", pm.UserId))).Should().BeGreaterThan(0);
    }

    [PgFact]
    public async Task AT_PM_24_Offer_steps_are_held_for_a_federal_project_until_it_is_cleared_and_overrides_cannot_get_past_the_gate()
    {
        var pm = await _api.As("ProjectManager");
        var held = await NewProject(pm, "Held", "Federal");
        var cleared = await ClearedProject(pm);
        var heldParcel = await NewParcel(pm, held.Id);
        var openParcel = await NewParcel(pm, cleared.Id);

        foreach (var parcel in new[] { heldParcel, openParcel })
            foreach (var step in new[] { "title_survey", "appraisal" })
                (await pm.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/advance", new { targetStepKey = step }, Json)).EnsureSuccessStatusCode();

        var blocked = await pm.Http.PostAsJsonAsync($"/api/v1/parcels/{heldParcel.Id}/advance", new { targetStepKey = "offer" }, Json);
        blocked.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await blocked.Content.ReadAsStringAsync()).Should().Contain("acquisition_gate_closed");
        var overridden = await pm.Http.PostAsJsonAsync($"/api/v1/parcels/{heldParcel.Id}/advance", new { targetStepKey = "closing", overrideReason = "please" }, Json);
        overridden.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await GetParcel(pm, heldParcel.Id)).Workflow.AcquisitionGateOpen.Should().BeFalse();

        (await Ok<ParcelDto>(await pm.Http.PostAsJsonAsync($"/api/v1/parcels/{openParcel.Id}/advance", new { targetStepKey = "offer" }, Json))).Workflow.CurrentKey.Should().Be("offer");

        // An approved exception on the held project opens it.
        (await pm.Http.PutAsJsonAsync($"/api/v1/projects/{held.Id}/acquisition-exception",
            new { exceptionType = "Hardship", approvedOn = "2026-03-01", reason = "Owner hardship" }, Json)).EnsureSuccessStatusCode();
        (await Ok<ParcelDto>(await pm.Http.PostAsJsonAsync($"/api/v1/parcels/{heldParcel.Id}/advance", new { targetStepKey = "offer" }, Json))).Workflow.CurrentKey.Should().Be("offer");
    }

    [PgFact]
    public async Task AT_PM_25_A_batch_update_changes_the_chosen_parcels_only_and_is_all_or_nothing()
    {
        var pm = await _api.As("ProjectManager");
        var project = await NewProject(pm);
        var group = await Ok<ParcelGroupDto>(await pm.Http.PostAsJsonAsync($"/api/v1/projects/{project.Id}/groups", new { name = "South" }, Json), HttpStatusCode.Created);
        var parcels = new List<ParcelDto>();
        for (var i = 0; i < 4; i++) parcels.Add(await NewParcel(pm, project.Id));
        var appraisal = (await Statuses(pm)).Single(s => s.Name == "Appraisal");
        var chosen = parcels.Take(3).Select(p => p.Id).ToArray();

        var result = await Ok<BatchUpdateResultDto>(await pm.Http.PostAsJsonAsync("/api/v1/parcels/batch-update",
            new { parcelIds = chosen, statusId = appraisal.Id, groupId = group.Id }, Json));
        result.Updated.Should().Be(3);
        foreach (var id in chosen) (await GetParcel(pm, id)).Should().Match<ParcelDto>(p => p.StatusName == "Appraisal" && p.GroupId == group.Id);
        (await GetParcel(pm, parcels[3].Id)).StatusName.Should().Be("Identified");

        var bad = await pm.Http.PostAsJsonAsync("/api/v1/parcels/batch-update",
            new { parcelIds = new[] { parcels[3].Id, Guid.NewGuid() }, statusId = appraisal.Id }, Json);
        bad.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await GetParcel(pm, parcels[3].Id)).StatusName.Should().Be("Identified");

        (await PgTestDatabase.ScalarAsync(_api.Database,
            "select count(*) from audit_log where entity_type = 'Parcel' and field = 'StatusId' and entity_id = any(@ids)", ("ids", chosen))).Should().BeGreaterThanOrEqualTo(3);
    }

    [PgFact]
    public async Task AT_PM_21_Interests_site_visits_and_custom_values_round_trip()
    {
        var pm = await _api.As("ProjectManager");
        var admin = await _api.As("Admin");
        await Ok<CustomFieldDto>(await admin.Http.PostAsJsonAsync("/api/v1/parcel-rules/custom-fields",
            new { key = $"frontage_{Guid.NewGuid():N}"[..20], label = "Frontage (ft)", fieldType = "Number", isRequired = false, isActive = true, sortOrder = 1 }, Json), HttpStatusCode.Created);
        var fields = await Ok<List<CustomFieldDto>>(await pm.Http.GetAsync("/api/v1/parcel-rules/custom-fields"));
        var key = fields.First().Key;
        var project = await NewProject(pm);

        var parcel = await NewParcel(pm, project.Id, area: 1_000m, extra: new { CustomValues = new Dictionary<string, string?> { [key] = " 120.5 " } });
        parcel.CustomValues[key].Should().Be("120.5");
        (await PgTestDatabase.ScalarAsync(_api.Database, "select count(*) from parcels where id = @id and custom_values_json ->> @k = '120.5'", ("id", parcel.Id), ("k", key))).Should().Be(1);

        var bad = await pm.Http.PutAsJsonAsync($"/api/v1/parcels/{parcel.Id}", new { areaSqFt = 1_000m, acquisitionType = "Standard", customValues = new Dictionary<string, string?> { [key] = "wide" } }, Json);
        bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var withInterest = await Ok<ParcelDto>(await pm.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/interests", new { type = "TemporaryEasement", areaSqFt = 250m, notes = "staging" }, Json), HttpStatusCode.Created);
        var tooBig = await pm.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/interests", new { type = "FeeTaking", areaSqFt = 1_000.01m }, Json);
        tooBig.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var interestId = withInterest.Interests.Single().Id;
        (await Ok<ParcelDto>(await pm.Http.DeleteAsync($"/api/v1/parcels/{parcel.Id}/interests/{interestId}"))).Interests.Should().BeEmpty();

        var visit = await Ok<ParcelDto>(await pm.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/site-visits", new { visitedOn = "2026-02-09", notes = "Walked the frontage", ownerContactConfirmed = true }, Json), HttpStatusCode.Created);
        visit.SiteVisits.Should().ContainSingle().Which.OwnerContactConfirmed.Should().BeTrue();
        (await Ok<ParcelDto>(await pm.Http.DeleteAsync($"/api/v1/parcels/{parcel.Id}/site-visits/{visit.SiteVisits[0].Id}"))).SiteVisits.Should().BeEmpty();
    }

    // ── agency rules (AT-AD-22) ──

    [PgFact]
    public async Task AT_AD_22_An_administrator_configures_statuses_custom_fields_and_deadline_rules_and_they_apply_at_once()
    {
        var admin = await _api.As("Admin");
        var pm = await _api.As("ProjectManager");
        var stamp = Guid.NewGuid().ToString("N")[..6];

        var status = await Ok<ParcelStatusDto>(await admin.Http.PostAsJsonAsync("/api/v1/parcel-rules/statuses",
            new { name = $"Awaiting owner {stamp}", color = "#AA33CC", sortOrder = 10, isActive = true, isInitial = false }, Json), HttpStatusCode.Created);
        status.Color.Should().Be("#aa33cc");
        (await Statuses(pm)).Should().Contain(s => s.Id == status.Id);
        var dupStatus = await admin.Http.PostAsJsonAsync("/api/v1/parcel-rules/statuses", new { name = status.Name.ToUpperInvariant(), color = "#000000", sortOrder = 1, isActive = true, isInitial = false }, Json);
        dupStatus.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var project = await NewProject(pm);
        (await NewParcel(pm, project.Id, extra: new { StatusId = status.Id })).StatusName.Should().Be(status.Name);

        var rule = await Ok<DeadlineRuleDto>(await admin.Http.PostAsJsonAsync("/api/v1/parcel-rules/deadline-rules",
            new { name = $"Agreement within 45 days of offer {stamp}", triggerMilestone = "Offer delivered", targetMilestone = "Agreement signed", days = 45, isActive = true }, Json), HttpStatusCode.Created);
        rule.IsPlaceholder.Should().BeFalse();
        var seeded = (await Ok<List<DeadlineRuleDto>>(await pm.Http.GetAsync("/api/v1/parcel-rules/deadline-rules"))).Where(r => r.IsPlaceholder).ToList();
        seeded.Should().NotBeEmpty("seeded rules are flagged until an administrator confirms them");
        var confirmed = await Ok<DeadlineRuleDto>(await admin.Http.PutAsJsonAsync($"/api/v1/parcel-rules/deadline-rules/{seeded[0].Id}",
            new { seeded[0].Name, seeded[0].TriggerMilestone, seeded[0].TargetMilestone, days = seeded[0].Days + 1, isActive = true }, Json));
        confirmed.IsPlaceholder.Should().BeFalse();
        confirmed.Days.Should().Be(seeded[0].Days + 1);

        var parcel = await NewParcel(pm, project.Id);
        (await Ok<ParcelDto>(await pm.Http.PutAsJsonAsync($"/api/v1/parcels/{parcel.Id}/milestones/{parcel.Milestones.Single(m => m.Name == "Offer delivered").Id}",
            new { completedOn = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-60) }, Json)))
            .Deadlines.Single(d => d.RuleId == rule.Id).Should().Match<DeadlineDto>(d => d.Status == "Overdue" && d.Days == 45 && d.DaysRemaining == -15);

        var badRule = await admin.Http.PostAsJsonAsync("/api/v1/parcel-rules/deadline-rules", new { name = "x", triggerMilestone = "A", targetMilestone = "a", days = 5, isActive = true }, Json);
        badRule.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var badColor = await admin.Http.PostAsJsonAsync("/api/v1/parcel-rules/statuses", new { name = "Bad", color = "red", sortOrder = 1, isActive = true, isInitial = false }, Json);
        badColor.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [PgFact]
    public async Task AT_AD_22_The_roles_allowed_to_override_step_order_are_configurable_and_the_screen_data_reflects_it()
    {
        var admin = await _api.As("Admin");
        var negotiator = await _api.As("Negotiator");
        var project = await NewProject(admin);
        var parcel = await NewParcel(admin, project.Id);

        (await GetParcel(negotiator, parcel.Id)).Workflow.CanOverride.Should().BeFalse();
        var settings = await Ok<StepGateSettingsDto>(await admin.Http.GetAsync("/api/v1/parcel-rules/step-gate"));
        settings.AlwaysRoles.Should().BeEquivalentTo("ProjectManager", "Admin");
        settings.AssignableRoles.Should().Contain("Negotiator").And.NotContain("Admin");

        try
        {
            var saved = await Ok<StepGateSettingsDto>(await admin.Http.PutAsJsonAsync("/api/v1/parcel-rules/step-gate", new { extraOverrideRoles = new[] { "Negotiator" } }, Json));
            saved.ExtraOverrideRoles.Should().Equal("Negotiator");
            (await GetParcel(negotiator, parcel.Id)).Workflow.CanOverride.Should().BeTrue();
            (await GetParcel(await _api.As("Finance"), parcel.Id)).Workflow.CanOverride.Should().BeFalse();

            (await admin.Http.PutAsJsonAsync("/api/v1/parcel-rules/step-gate", new { extraOverrideRoles = new[] { "Admin" } }, Json)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await admin.Http.PutAsJsonAsync("/api/v1/parcel-rules/step-gate", new { extraOverrideRoles = new[] { "Wizard" } }, Json)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
        finally
        {
            await admin.Http.PutAsJsonAsync("/api/v1/parcel-rules/step-gate", new { extraOverrideRoles = Array.Empty<string>() }, Json);
        }
    }

    [PgFact]
    public async Task ROW_071_Every_acquisition_type_has_a_workflow_and_the_default_statuses_and_rules_are_provisioned()
    {
        var pm = await _api.As("ProjectManager");

        var workflows = await Ok<List<WorkflowDto>>(await pm.Http.GetAsync("/api/v1/parcel-rules/workflows"));
        workflows.Select(w => w.AcquisitionType.ToString()).Should().Equal("Standard", "Donation", "DeveloperConveyance", "Early");
        (await Statuses(pm)).Select(s => s.Name).Should().Contain(new[] { "Identified", "Negotiation", "Closed", "Impasse" });
        (await Statuses(pm)).Where(s => s.IsInitial).Should().ContainSingle();

        var project = await NewProject(pm);
        var donation = await NewParcel(pm, project.Id, extra: new { AcquisitionType = "Donation" });
        donation.Workflow.Steps.Select(s => s.Key).Should().Equal("setup", "title_survey", "donation_review", "agreement", "closing");
    }

    // ── every role, both directions ──

    [PgTheory]
    [MemberData(nameof(Spec.EachRole), MemberType = typeof(Spec))]
    public async Task Every_role_can_do_exactly_what_the_spec_allows_on_parcels(string role)
    {
        var admin = await _api.As("Admin");
        var project = await NewProject(admin);
        var parcel = await NewParcel(admin, project.Id, area: 5_000m);
        var spare = await NewParcel(admin, project.Id, area: 700m);
        var splitTarget = await NewParcel(admin, project.Id, area: 900m);
        var group = await Ok<ParcelGroupDto>(await admin.Http.PostAsJsonAsync($"/api/v1/projects/{project.Id}/groups", new { name = $"G {role}" }, Json), HttpStatusCode.Created);
        var tract = await Ok<ParentTractDto>(await admin.Http.PostAsJsonAsync($"/api/v1/projects/{project.Id}/tracts", new { number = $"T-{role}" }, Json), HttpStatusCode.Created);
        var withExtras = await Ok<ParcelDto>(await admin.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/interests", new { type = "FeeTaking", areaSqFt = 100m }, Json), HttpStatusCode.Created);
        withExtras = await Ok<ParcelDto>(await admin.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/milestones", new { name = "Custom check" }, Json), HttpStatusCode.Created);
        withExtras = await Ok<ParcelDto>(await admin.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/site-visits", new { visitedOn = "2026-01-02", ownerContactConfirmed = false }, Json), HttpStatusCode.Created);
        var emptyGroup = await Ok<ParcelGroupDto>(await admin.Http.PostAsJsonAsync($"/api/v1/projects/{project.Id}/groups", new { name = $"Empty {role}" }, Json), HttpStatusCode.Created);
        var user = await _api.As(role);

        HttpStatusCode Expect(bool allowed, HttpStatusCode ok) => allowed ? ok : HttpStatusCode.Forbidden;
        bool Can(string action) => Spec.Can(role, "parcels", action);
        var canAdmin = Spec.Can(role, "administration", "update");
        async Task<HttpStatusCode> Status(Task<HttpResponseMessage> t) => (await t).StatusCode;

        // Reading
        foreach (var url in new[]
        {
            "/api/v1/parcels", $"/api/v1/parcels/{parcel.Id}", $"/api/v1/projects/{project.Id}/groups", $"/api/v1/projects/{project.Id}/tracts",
            $"/api/v1/projects/{project.Id}/parcel-rollup", "/api/v1/parcel-rules/statuses", "/api/v1/parcel-rules/custom-fields", "/api/v1/parcel-rules/deadline-rules",
            "/api/v1/parcel-rules/workflows", "/api/v1/parcel-rules/step-gate", "/api/v1/reminders",
        })
            (await user.Http.GetAsync(url)).StatusCode.Should().Be(Expect(Can("read"), HttpStatusCode.OK), $"{role} GET {url}");

        // Creating
        (await Status(user.Http.PostAsJsonAsync("/api/v1/parcels", new { projectId = project.Id, areaSqFt = 10m, acquisitionType = "Standard" }, Json)))
            .Should().Be(Expect(Can("create"), HttpStatusCode.Created), $"{role} create parcel");
        (await Status(user.Http.PostAsJsonAsync($"/api/v1/parcels/{splitTarget.Id}/split", new { parts = new[] { new { areaSqFt = 100m } } }, Json)))
            .Should().Be(Expect(Can("create"), HttpStatusCode.Created), $"{role} split");
        (await Status(user.Http.PostAsJsonAsync("/api/v1/parcels/combine", new { parcelIds = new[] { spare.Id, (await NewParcel(admin, project.Id)).Id } }, Json)))
            .Should().Be(Expect(Can("create"), HttpStatusCode.Created), $"{role} combine");
        (await Status(user.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/interests", new { type = "Remainder", areaSqFt = 5m }, Json)))
            .Should().Be(Expect(Can("create"), HttpStatusCode.Created), $"{role} add interest");
        (await Status(user.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/milestones", new { name = $"M {role}" }, Json)))
            .Should().Be(Expect(Can("create"), HttpStatusCode.Created), $"{role} add milestone");
        (await Status(user.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/site-visits", new { visitedOn = "2026-02-02", ownerContactConfirmed = true }, Json)))
            .Should().Be(Expect(Can("create"), HttpStatusCode.Created), $"{role} add visit");
        (await Status(user.Http.PostAsJsonAsync($"/api/v1/projects/{project.Id}/groups", new { name = $"Made by {role}" }, Json)))
            .Should().Be(Expect(Can("create"), HttpStatusCode.Created), $"{role} add group");
        (await Status(user.Http.PostAsJsonAsync($"/api/v1/projects/{project.Id}/tracts", new { number = $"TN-{role}" }, Json)))
            .Should().Be(Expect(Can("create"), HttpStatusCode.Created), $"{role} add tract");

        // Updating
        (await Status(user.Http.PutAsJsonAsync($"/api/v1/parcels/{parcel.Id}", new { areaSqFt = 5_000m, acquisitionType = "Standard", address = $"By {role}" }, Json)))
            .Should().Be(Expect(Can("update"), HttpStatusCode.OK), $"{role} update parcel");
        (await Status(user.Http.PostAsJsonAsync("/api/v1/parcels/batch-update", new { parcelIds = new[] { parcel.Id }, groupId = group.Id }, Json)))
            .Should().Be(Expect(Can("update"), HttpStatusCode.OK), $"{role} batch update");
        (await Status(user.Http.PutAsJsonAsync($"/api/v1/parcels/{parcel.Id}/notice", new { issuedOn = "2026-02-01", method = "Email" }, Json)))
            .Should().Be(Expect(Can("update"), HttpStatusCode.OK), $"{role} notice");
        (await Status(user.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/advance", new { targetStepKey = "title_survey" }, Json)))
            .Should().Be(Expect(Can("update"), HttpStatusCode.OK), $"{role} advance");
        (await Status(user.Http.PutAsJsonAsync($"/api/v1/parcels/{parcel.Id}/milestones/{withExtras.Milestones.First(m => m.Name == "Custom check").Id}", new { dueOn = "2026-05-05" }, Json)))
            .Should().Be(Expect(Can("update"), HttpStatusCode.OK), $"{role} update milestone");
        (await Status(user.Http.PutAsJsonAsync($"/api/v1/parcels/{parcel.Id}/interests/{withExtras.Interests[0].Id}", new { type = "FeeTaking", areaSqFt = 90m }, Json)))
            .Should().Be(Expect(Can("update"), HttpStatusCode.OK), $"{role} update interest");
        (await Status(user.Http.PutAsJsonAsync($"/api/v1/projects/{project.Id}/groups/{group.Id}", new { name = $"Renamed by {role}" }, Json)))
            .Should().Be(Expect(Can("update"), HttpStatusCode.OK), $"{role} rename group");
        (await Status(user.Http.PutAsJsonAsync($"/api/v1/projects/{project.Id}/tracts/{tract.Id}", new { number = $"T-{role}", description = "edited" }, Json)))
            .Should().Be(Expect(Can("update"), HttpStatusCode.OK), $"{role} edit tract");

        // Deleting
        var after = await GetParcel(admin, parcel.Id);
        (await Status(user.Http.DeleteAsync($"/api/v1/parcels/{parcel.Id}/interests/{after.Interests[0].Id}")))
            .Should().Be(Expect(Can("delete"), HttpStatusCode.OK), $"{role} delete interest");
        (await Status(user.Http.DeleteAsync($"/api/v1/parcels/{parcel.Id}/milestones/{after.Milestones.First(m => m.Name == "Custom check").Id}")))
            .Should().Be(Expect(Can("delete"), HttpStatusCode.OK), $"{role} delete milestone");
        (await Status(user.Http.DeleteAsync($"/api/v1/parcels/{parcel.Id}/site-visits/{after.SiteVisits[0].Id}")))
            .Should().Be(Expect(Can("delete"), HttpStatusCode.OK), $"{role} delete visit");
        (await Status(user.Http.DeleteAsync($"/api/v1/projects/{project.Id}/groups/{emptyGroup.Id}")))
            .Should().Be(Expect(Can("delete"), HttpStatusCode.NoContent), $"{role} delete group");

        // Agency rules: administrators only
        var stamp = Guid.NewGuid().ToString("N")[..6];
        (await Status(user.Http.PostAsJsonAsync("/api/v1/parcel-rules/statuses", new { name = $"S {role} {stamp}", color = "#112233", sortOrder = 50, isActive = true, isInitial = false }, Json)))
            .Should().Be(Expect(canAdmin, HttpStatusCode.Created), $"{role} add status");
        (await Status(user.Http.PostAsJsonAsync("/api/v1/parcel-rules/custom-fields", new { key = $"f_{role.ToLowerInvariant()}_{stamp}", label = "F", fieldType = "Text", isRequired = false, isActive = true, sortOrder = 9 }, Json)))
            .Should().Be(Expect(canAdmin, HttpStatusCode.Created), $"{role} add custom field");
        (await Status(user.Http.PostAsJsonAsync("/api/v1/parcel-rules/deadline-rules", new { name = $"R {role} {stamp}", triggerMilestone = "Parcel set up", targetMilestone = "Closed", days = 400, isActive = true }, Json)))
            .Should().Be(Expect(canAdmin, HttpStatusCode.Created), $"{role} add deadline rule");
        (await Status(user.Http.PutAsJsonAsync("/api/v1/parcel-rules/step-gate", new { extraOverrideRoles = Array.Empty<string>() }, Json)))
            .Should().Be(Expect(canAdmin, HttpStatusCode.OK), $"{role} set override roles");
    }

    [PgFact]
    public async Task Anonymous_callers_are_refused_everywhere_on_parcels()
    {
        var anon = _api.Factory.CreateClient();
        foreach (var url in new[] { "/api/v1/parcels", $"/api/v1/parcels/{Guid.NewGuid()}", "/api/v1/parcel-rules/statuses", "/api/v1/reminders", $"/api/v1/projects/{Guid.NewGuid()}/parcel-rollup" })
            (await anon.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.Unauthorized, url);
        (await anon.PostAsJsonAsync("/api/v1/parcels", new { areaSqFt = 1 }, Json)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anon.PostAsJsonAsync("/api/v1/parcels/combine", new { parcelIds = Array.Empty<Guid>() }, Json)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── races ──

    [PgFact]
    public async Task Twenty_simultaneous_parcel_creates_get_twenty_different_numbers_without_errors()
    {
        var pm = await _api.As("ProjectManager");
        var project = await NewProject(pm);

        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ =>
            pm.Http.PostAsJsonAsync("/api/v1/parcels", new { projectId = project.Id, areaSqFt = 100m, acquisitionType = "Standard" }, Json)));

        results.Select(r => r.StatusCode).Should().OnlyContain(s => s == HttpStatusCode.Created);
        var numbers = new List<string>();
        foreach (var r in results) numbers.Add((await Read<ParcelDto>(r)).Number);
        numbers.Distinct().Should().HaveCount(20);
        (await PgTestDatabase.ScalarAsync(_api.Database, "select count(distinct number) from parcels where project_id = @p", ("p", project.Id))).Should().Be(20);
    }

    [PgFact]
    public async Task Ten_people_typing_the_same_parcel_number_produce_exactly_one_parcel()
    {
        var pm = await _api.As("ProjectManager");
        var project = await NewProject(pm);

        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ =>
            pm.Http.PostAsJsonAsync("/api/v1/parcels", new { projectId = project.Id, number = "RACE-1", areaSqFt = 1m, acquisitionType = "Standard" }, Json)));

        results.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1);
        results.Count(r => r.StatusCode == HttpStatusCode.Conflict).Should().Be(9);
        (await PgTestDatabase.ScalarAsync(_api.Database, "select count(*) from parcels where project_id = @p and number = 'RACE-1'", ("p", project.Id))).Should().Be(1);
    }

    [PgFact]
    public async Task Splitting_the_same_parcel_at_once_succeeds_once_and_never_duplicates_the_pieces()
    {
        var pm = await _api.As("ProjectManager");
        var project = await NewProject(pm);
        var parcel = await NewParcel(pm, project.Id, "SPLITRACE", 1_000m);

        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ =>
            pm.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/split", new { parts = new[] { new { areaSqFt = 400m } } }, Json)));

        results.Select(r => r.StatusCode).Should().OnlyContain(s => s == HttpStatusCode.Created || s == HttpStatusCode.Conflict);
        results.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1);
        (await PgTestDatabase.ScalarAsync(_api.Database, "select count(*) from parcels where project_id = @p and number like 'SPLITRACE-%'", ("p", project.Id))).Should().Be(2, "one part and one remnant");
    }

    [PgFact]
    public async Task Two_people_combining_overlapping_parcels_at_once_never_use_a_parcel_twice()
    {
        var pm = await _api.As("ProjectManager");
        var project = await NewProject(pm);
        var shared = await NewParcel(pm, project.Id, area: 100m);
        var left = await NewParcel(pm, project.Id, area: 100m);
        var right = await NewParcel(pm, project.Id, area: 100m);

        var results = await Task.WhenAll(
            pm.Http.PostAsJsonAsync("/api/v1/parcels/combine", new { parcelIds = new[] { shared.Id, left.Id } }, Json),
            pm.Http.PostAsJsonAsync("/api/v1/parcels/combine", new { parcelIds = new[] { shared.Id, right.Id } }, Json));

        results.Select(r => r.StatusCode).Should().OnlyContain(s => s == HttpStatusCode.Created || s == HttpStatusCode.Conflict);
        (await PgTestDatabase.ScalarAsync(_api.Database,
            "select count(*) from parcels where project_id = @p and @s = any(source_parcel_ids)", ("p", project.Id), ("s", shared.Id))).Should().BeLessThanOrEqualTo(1);
    }

    // ── tenancy ──

    [PgFact]
    public async Task One_agency_never_sees_or_changes_another_agencys_parcels_or_rules()
    {
        await EnsureOtherTenantAsync();
        var mine = await _api.As("ProjectManager");
        var theirs = await _api.As("ProjectManager", OtherTenant);
        var theirAdmin = await _api.As("Admin", OtherTenant);
        var project = await NewProject(mine);
        var parcel = await NewParcel(mine, project.Id, "SECRET");

        (await theirs.Http.GetAsync($"/api/v1/parcels/{parcel.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await theirs.Http.PutAsJsonAsync($"/api/v1/parcels/{parcel.Id}", new { areaSqFt = 1m, acquisitionType = "Standard" }, Json)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await theirs.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/advance", new { targetStepKey = "title_survey" }, Json)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await theirs.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/split", new { parts = new[] { new { areaSqFt = 1m } } }, Json)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await theirs.Http.PostAsJsonAsync("/api/v1/parcels", new { projectId = project.Id, areaSqFt = 1m, acquisitionType = "Standard" }, Json)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await theirs.Http.PostAsJsonAsync("/api/v1/parcels/batch-update", new { parcelIds = new[] { parcel.Id }, clearGroup = true }, Json)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await theirs.Http.GetAsync($"/api/v1/projects/{project.Id}/groups")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Read<PagedResult<ParcelSummaryDto>>(await theirs.Http.GetAsync("/api/v1/parcels?search=SECRET"))).Items.Should().BeEmpty();

        // Each agency has its own rules; one agency's edits never show in the other.
        var theirStatus = await Ok<ParcelStatusDto>(await theirAdmin.Http.PostAsJsonAsync("/api/v1/parcel-rules/statuses",
            new { name = "Theirs only", color = "#123456", sortOrder = 40, isActive = true, isInitial = false }, Json), HttpStatusCode.Created);
        (await Statuses(mine)).Should().NotContain(s => s.Id == theirStatus.Id);
        (await Statuses(theirs)).Should().Contain(s => s.Id == theirStatus.Id);
        (await theirs.Http.PostAsJsonAsync("/api/v1/parcels", new { projectId = Guid.NewGuid(), areaSqFt = 1m, statusId = (await Statuses(mine))[0].Id, acquisitionType = "Standard" }, Json))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await PgTestDatabase.ScalarAsync(_api.Database, "select count(*) from parcel_statuses where tenant_id = @t", ("t", OtherTenant))).Should().BeGreaterThanOrEqualTo(8);
    }

    [PgFact]
    public async Task Reminders_only_cover_the_callers_own_agency()
    {
        await EnsureOtherTenantAsync();
        var mine = await _api.As("ProjectManager");
        var theirs = await _api.As("ProjectManager", OtherTenant);
        var project = await NewProject(mine);
        var parcel = await NewParcel(mine, project.Id, "ONLYMINE");
        await mine.Http.PutAsJsonAsync($"/api/v1/parcels/{parcel.Id}/notice", new { issuedOn = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-200), method = "Email" }, Json);

        (await Ok<List<ReminderDto>>(await mine.Http.GetAsync("/api/v1/reminders"))).Should().Contain(r => r.ParcelNumber == "ONLYMINE");
        (await Ok<List<ReminderDto>>(await theirs.Http.GetAsync("/api/v1/reminders"))).Should().NotContain(r => r.ParcelNumber == "ONLYMINE");
    }

    // ── schema, seeded data, hostile input ──

    [PgFact]
    public async Task The_schema_enforces_unique_parcel_numbers_status_names_and_one_workflow_per_type()
    {
        var indexes = (await PgTestDatabase.QueryAsync(_api.Database,
            "select indexname from pg_indexes where tablename in ('parcels','parcel_statuses','parcel_groups','parent_tracts','parcel_milestones','workflow_templates','workflow_steps','custom_field_definitions','step_gate_settings')"))
            .Select(r => (string)r[0]!).ToList();

        indexes.Should().Contain(i => i.Contains("parcels") && i.Contains("project_id") && i.Contains("number"));
        indexes.Should().Contain(i => i.Contains("parcel_statuses") && i.Contains("name"));
        indexes.Should().Contain(i => i.Contains("parcel_groups") && i.Contains("name"));
        indexes.Should().Contain(i => i.Contains("parent_tracts") && i.Contains("number"));
        indexes.Should().Contain(i => i.Contains("parcel_milestones") && i.Contains("name"));
        indexes.Should().Contain(i => i.Contains("workflow_templates") && i.Contains("acquisition_type"));
        indexes.Should().Contain(i => i.Contains("custom_field_definitions") && i.Contains("key"));
        indexes.Should().Contain(i => i.Contains("step_gate_settings") && i.Contains("tenant_id"));

        (await PgTestDatabase.QueryAsync(_api.Database,
            "select data_type from information_schema.columns where table_name='parcels' and column_name='custom_values_json'"))[0][0].Should().Be("jsonb");
        (await PgTestDatabase.QueryAsync(_api.Database,
            "select data_type from information_schema.columns where table_name='parent_tracts' and column_name='tax_parcel_numbers'"))[0][0].Should().Be("ARRAY");
    }

    [PgFact]
    public async Task The_demo_agency_opens_with_parcels_a_split_with_remnant_and_an_overdue_reminder()
    {
        var pm = await _api.As("ProjectManager");
        var projects = await Ok<PagedResult<ProjectSummaryDto>>(await pm.Http.GetAsync("/api/v1/projects?search=DEMO-002"));
        var bridge = projects.Items.Single();

        var parcels = await Ok<PagedResult<ParcelSummaryDto>>(await pm.Http.GetAsync($"/api/v1/parcels?projectId={bridge.Id}"));
        parcels.Items.Select(p => p.Number).Should().Equal("001", "002", "003", "004-A", "004-R");
        parcels.Items.Single(p => p.Number == "004-R").IsRemnant.Should().BeTrue();
        parcels.Items.Single(p => p.Number == "001").GroupName.Should().Be("Main span");
        parcels.Items.Single(p => p.Number == "001").ParentTractNumber.Should().Be("T-1001");
        (await Ok<PagedResult<ParcelSummaryDto>>(await pm.Http.GetAsync($"/api/v1/parcels?projectId={bridge.Id}&includeSuperseded=true"))).Total.Should().Be(6);

        var offer = await GetParcel(pm, parcels.Items.Single(p => p.Number == "001").Id);
        offer.Interests.Select(i => i.Type.ToString()).Should().BeEquivalentTo("FeeTaking", "TemporaryEasement");
        offer.SiteVisits.Should().ContainSingle();
        offer.Workflow.CurrentKey.Should().Be("offer");
        offer.Workflow.AcquisitionGateOpen.Should().BeTrue("the bridge project is cleared to acquire");

        var reminders = await Ok<List<ReminderDto>>(await pm.Http.GetAsync("/api/v1/reminders"));
        reminders.Should().Contain(r => r.ProjectNumber == "DEMO-002" && r.ParcelNumber == "002" && r.Status == "Overdue");
    }

    [PgFact]
    public async Task Hostile_and_oversized_parcel_input_is_a_clear_4xx_never_a_500()
    {
        var pm = await _api.As("ProjectManager");
        var project = await NewProject(pm);
        var parcel = await NewParcel(pm, project.Id);
        async Task<HttpStatusCode> Create(object body) => (await pm.Http.PostAsJsonAsync("/api/v1/parcels", body, Json)).StatusCode;
        object Body(Dictionary<string, object?> extra)
        {
            var b = new Dictionary<string, object?> { ["projectId"] = project.Id, ["areaSqFt"] = 10m, ["acquisitionType"] = "Standard" };
            foreach (var kv in extra) b[kv.Key] = kv.Value;
            return b;
        }

        (await Create(Body(new() { ["address"] = "12 Main\u0000St" }))).Should().Be(HttpStatusCode.BadRequest);
        (await Create(Body(new() { ["customValues"] = new Dictionary<string, string?> { ["zoning"] = "R\u00002" } }))).Should().Be(HttpStatusCode.BadRequest);
        (await Create(Body(new() { ["customValues"] = new Dictionary<string, string?> { ["zon\u0000ing"] = "R2" } }))).Should().Be(HttpStatusCode.BadRequest);
        (await Create(Body(new() { ["number"] = "'; drop table parcels;--" }))).Should().Be(HttpStatusCode.BadRequest);
        (await Create(Body(new() { ["number"] = new string('9', 51) }))).Should().Be(HttpStatusCode.BadRequest);
        (await Create(Body(new() { ["areaSqFt"] = -1m }))).Should().Be(HttpStatusCode.BadRequest);
        (await Create(Body(new() { ["areaSqFt"] = 99_999_999_999m }))).Should().Be(HttpStatusCode.BadRequest);
        (await Create(Body(new() { ["legalDescription"] = new string('x', 4_001) }))).Should().Be(HttpStatusCode.BadRequest);
        (await Create(Body(new() { ["acquisitionType"] = "Nonsense" }))).Should().Be(HttpStatusCode.BadRequest);
        (await Create(Body(new() { ["customValues"] = Enumerable.Range(0, 60).ToDictionary(i => $"k{i}", i => (string?)"v") }))).Should().Be(HttpStatusCode.BadRequest);
        (await Create(Body(new() { ["address"] = "<script>alert(1)</script>" }))).Should().Be(HttpStatusCode.Created, "text is data, escaped on display");
        (await pm.Http.PostAsync("/api/v1/parcels", new StringContent("{ not json", System.Text.Encoding.UTF8, "application/json"))).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        (await pm.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/split", new { parts = new[] { new { areaSqFt = 0m } } }, Json)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await pm.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/split", new { parts = new[] { new { areaSqFt = 10_000.01m } } }, Json)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await pm.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/split", new { parts = Array.Empty<object>() }, Json)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await pm.Http.PostAsJsonAsync("/api/v1/parcels/combine", new { parcelIds = new[] { parcel.Id } }, Json)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await pm.Http.PostAsJsonAsync("/api/v1/parcels/batch-update", new { parcelIds = new[] { parcel.Id } }, Json)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await pm.Http.PostAsJsonAsync($"/api/v1/projects/{project.Id}/tracts", new { number = "T-9", taxParcelNumbers = new[] { "APN\u00001" } }, Json)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await pm.Http.PostAsJsonAsync($"/api/v1/projects/{project.Id}/groups", new { name = "G\u0000" }, Json)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await pm.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/milestones", new { name = "M\u0000" }, Json)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await pm.Http.PostAsJsonAsync($"/api/v1/parcels/{parcel.Id}/advance", new { targetStepKey = "x\u0000", overrideReason = "r" }, Json)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await pm.Http.PutAsJsonAsync($"/api/v1/parcels/{parcel.Id}/notice", new { issuedOn = "2026-01-01" }, Json)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await pm.Http.GetAsync("/api/v1/parcels?pageSize=1000")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await pm.Http.GetAsync("/api/v1/parcels?page=0")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await pm.Http.GetAsync("/api/v1/reminders?withinDays=-1")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await pm.Http.GetAsync("/api/v1/parcels?search=%27%3B%20drop%20table%20parcels%3B--")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await pm.Http.GetAsync($"/api/v1/parcels/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await pm.Http.GetAsync("/api/v1/parcels/not-a-guid")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Nothing hostile was stored (Postgres cannot hold a null character at all, so only the injection text needs checking).
        (await PgTestDatabase.ScalarAsync(_api.Database, "select count(*) from parcels where number like '%drop table%'")).Should().Be(0);
    }

    [PgFact]
    public async Task Every_parcel_change_is_audited_with_who_and_when_and_attachments_work_on_parcels()
    {
        var pm = await _api.As("ProjectManager", user: Guid.NewGuid());
        var project = await NewProject(pm);
        var parcel = await NewParcel(pm, project.Id);
        await pm.Http.PutAsJsonAsync($"/api/v1/parcels/{parcel.Id}", new { areaSqFt = 321m, acquisitionType = "Standard", address = "Changed Rd" }, Json);

        (await PgTestDatabase.ScalarAsync(_api.Database,
            "select count(*) from audit_log where entity_type = 'Parcel' and entity_id = @id and changed_by = @u", ("id", parcel.Id), ("u", pm.UserId))).Should().BeGreaterThan(2);
        (await PgTestDatabase.ScalarAsync(_api.Database,
            "select count(*) from audit_log where entity_type = 'Parcel' and entity_id = @id and field = 'AreaSqFt' and new_value like '321%'", ("id", parcel.Id))).Should().Be(1);

        var upload = await pm.Upload("Parcel", parcel.Id, "sketch.pdf", System.Text.Encoding.UTF8.GetBytes("parcel sketch"));
        upload.StatusCode.Should().Be(HttpStatusCode.Created);
        var docs = await pm.Http.GetAsync($"/api/v1/documents?entityType=Parcel&entityId={parcel.Id}");
        (await docs.Content.ReadAsStringAsync()).Should().Contain("sketch.pdf");
    }
}
