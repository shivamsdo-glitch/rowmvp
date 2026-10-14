using Aurigo.RowManagement.Application.Auth;
using Aurigo.RowManagement.Application.Common;
using Aurigo.RowManagement.Application.Parcels;
using Aurigo.RowManagement.Domain.Common;
using Aurigo.RowManagement.Domain.Entities;
using Aurigo.RowManagement.Domain.Enums;
using Aurigo.RowManagement.Domain.Errors;
using Aurigo.RowManagement.Infrastructure.Persistence;
using Aurigo.RowManagement.Infrastructure.Persistence.Seed;
using FluentAssertions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Aurigo.RowManagement.UnitTests.Parcels;

public class ParcelTests
{
    private sealed class FixedClock : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public FixedClock(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
    }

    private static readonly DateTimeOffset Now = new(2026, 3, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 3, 15);

    /// <summary>One caller in one agency with the real handlers over a shared in-memory store.</summary>
    private sealed class Ctx : IAsyncDisposable
    {
        public RowDbContext Db { get; }
        private readonly PagePermissionService _perms;
        private readonly ParcelService _parcels;

        public Ctx(string store, Guid tenant, Guid user, string role)
        {
            Db = TestDb.Create(store, tenant, user);
            var who = new FakeUser(user, role);
            _perms = new PagePermissionService(Db, who);
            _parcels = new ParcelService(Db, who, new FixedClock(Now));
        }

        public CreateParcelCommandHandler Create => new(Db, _perms, _parcels);
        public UpdateParcelCommandHandler Update => new(Db, _perms, _parcels);
        public GetParcelQueryHandler Get => new(_perms, _parcels);
        public ListParcelsQueryHandler List => new(Db, _perms, _parcels);
        public BatchUpdateParcelsCommandHandler Batch => new(Db, _perms, _parcels);
        public SplitParcelCommandHandler Split => new(Db, _perms, _parcels);
        public CombineParcelsCommandHandler Combine => new(Db, _perms, _parcels);
        public SaveParcelGroupCommandHandler SaveGroup => new(Db, _perms);
        public ListParcelGroupsQueryHandler Groups => new(Db, _perms);
        public DeleteParcelGroupCommandHandler DeleteGroup => new(Db, _perms);
        public SaveParentTractCommandHandler SaveTract => new(Db, _perms);
        public ListParentTractsQueryHandler Tracts => new(Db, _perms);
        public GetParcelRollupQueryHandler Rollup => new(Db, _perms);
        public SaveParcelInterestCommandHandler SaveInterest => new(Db, _perms, _parcels);
        public DeleteParcelInterestCommandHandler DeleteInterest => new(Db, _perms, _parcels);
        public AddParcelMilestoneCommandHandler AddMilestone => new(Db, _perms, _parcels);
        public UpdateParcelMilestoneCommandHandler UpdateMilestone => new(Db, _perms, _parcels);
        public DeleteParcelMilestoneCommandHandler DeleteMilestone => new(Db, _perms, _parcels);
        public AddParcelSiteVisitCommandHandler AddVisit => new(Db, _perms, _parcels);
        public DeleteParcelSiteVisitCommandHandler DeleteVisit => new(Db, _perms, _parcels);
        public RecordNoticeToOwnerCommandHandler Notice => new(Db, _perms, _parcels);
        public AdvanceParcelStepCommandHandler Advance => new(Db, _perms, _parcels);
        public ListParcelStatusesQueryHandler Statuses => new(Db, _perms);
        public SaveParcelStatusCommandHandler SaveStatus => new(Db, _perms);
        public ListCustomFieldsQueryHandler CustomFields => new(Db, _perms);
        public SaveCustomFieldCommandHandler SaveCustomField => new(Db, _perms);
        public ListDeadlineRulesQueryHandler Rules => new(Db, _perms);
        public SaveDeadlineRuleCommandHandler SaveRule => new(Db, _perms);
        public ListWorkflowsQueryHandler Workflows => new(Db, _perms);
        public GetStepGateSettingsQueryHandler StepGateSettings => new(Db, _perms);
        public SetStepGateSettingsCommandHandler SetStepGateSettings => new(Db, _perms);
        public ListRemindersQueryHandler Reminders => new(Db, _perms, _parcels);
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private static async Task<string> Provision()
    {
        var store = TestDb.NewStoreName();
        await using var db = TestDb.Create(store, tenant: null);
        await new TenantProvisioner(db).EnsureDefaultRolesAsync(TestDb.TenantA);
        await new TenantProvisioner(db).EnsureDefaultRolesAsync(TestDb.TenantB);
        return store;
    }

    private static async Task<Guid> AddProject(string store, Guid tenant, string number = "P-1", OversightType oversight = OversightType.Federal, bool cleared = false)
    {
        await using var db = TestDb.Create(store, tenant, TestDb.UserA);
        var project = new Project { Number = number, Name = $"Project {number}", OversightType = oversight };
        if (cleared)
        {
            project.NepaDecisionType = NepaDecisionType.CategoricalExclusion;
            project.NepaDecisionDate = Today;
            project.Authorizations.Add(new RowAuthorization { Type = AuthorizationType.Acquisition, AuthorizedAmount = 1m, AuthorizedOn = Today });
        }
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        return project.Id;
    }

    private static CreateParcelCommand NewParcel(Guid project, string? number = null, decimal area = 10_000m, AcquisitionType type = AcquisitionType.Standard,
        Guid? group = null, Guid? tract = null, Guid? status = null, Dictionary<string, string?>? custom = null) =>
        new(project, number, tract, group, "Lot 7, Block 3", "12 Main St", area, "Commercial", status, type, custom);

    private static UpdateParcelCommand Edit(ParcelDto p, string? number = null, decimal? area = null, AcquisitionType? type = null, Guid? status = null,
        Guid? group = null, Guid? tract = null, Dictionary<string, string?>? custom = null) =>
        new(p.Id, number, tract ?? p.ParentTractId, group ?? p.GroupId, p.LegalDescription, p.Address, area ?? p.AreaSqFt, p.PropertyType, status ?? p.StatusId,
            type ?? p.AcquisitionType, custom);

    private static Task<Ctx> Pm(string store, Guid? tenant = null) => Task.FromResult(new Ctx(store, tenant ?? TestDb.TenantA, TestDb.UserA, RoleNames.ProjectManager));

    // ── create (ROW-005) ──

    [Fact]
    public async Task ROW_005_A_new_parcel_gets_the_next_number_the_initial_status_the_first_step_and_the_standard_milestones()
    {
        var store = await Provision();
        var project = await AddProject(store, TestDb.TenantA);
        await using var c = await Pm(store);

        var first = await c.Create.Handle(NewParcel(project), default);
        var second = await c.Create.Handle(NewParcel(project), default);

        first.Number.Should().Be("001");
        second.Number.Should().Be("002");
        first.StatusName.Should().Be("Identified");
        first.State.Should().Be(ParcelState.Active);
        first.Workflow.CurrentKey.Should().Be("setup");
        first.Workflow.NextKey.Should().Be("title_survey");
        first.Milestones.Select(m => m.Name).Should().Equal(MilestoneNames.Standard);
        first.Milestones.Single(m => m.Name == MilestoneNames.ParcelSetUp).CompletedOn.Should().Be(Today);
        first.Milestones.Where(m => m.Name != MilestoneNames.ParcelSetUp).Should().OnlyContain(m => m.CompletedOn == null);
        first.Milestones.Should().OnlyContain(m => m.IsStandard);
    }

    [Fact]
    public async Task ROW_005_Numbers_are_per_project_and_a_typed_number_is_kept_and_skipped_over()
    {
        var store = await Provision();
        var p1 = await AddProject(store, TestDb.TenantA, "P-1");
        var p2 = await AddProject(store, TestDb.TenantA, "P-2");
        await using var c = await Pm(store);

        (await c.Create.Handle(NewParcel(p1, "010"), default)).Number.Should().Be("010");
        (await c.Create.Handle(NewParcel(p1), default)).Number.Should().Be("011", "numbering follows the highest plain number");
        (await c.Create.Handle(NewParcel(p2), default)).Number.Should().Be("001", "another project starts again");
        (await c.Create.Handle(NewParcel(p2, "010"), default)).Number.Should().Be("010", "the same number is fine in another project");
    }

    [Fact]
    public async Task ROW_005_A_duplicate_number_in_the_same_project_is_refused_ignoring_case()
    {
        var store = await Provision();
        var project = await AddProject(store, TestDb.TenantA);
        await using var c = await Pm(store);
        await c.Create.Handle(NewParcel(project, "A-1"), default);

        var act = () => c.Create.Handle(NewParcel(project, "a-1"), default);

        (await act.Should().ThrowAsync<ConflictException>()).Which.Code.Should().Be("parcel_number_exists");
    }

    [Fact]
    public async Task ROW_005_A_parcel_for_a_missing_project_group_tract_or_status_is_refused_clearly()
    {
        var store = await Provision();
        var p1 = await AddProject(store, TestDb.TenantA, "P-1");
        var p2 = await AddProject(store, TestDb.TenantA, "P-2");
        await using var c = await Pm(store);
        var groupOfP2 = (await c.SaveGroup.Handle(new SaveParcelGroupCommand(p2, null, "Other", null), default)).Id;
        var tractOfP2 = (await c.SaveTract.Handle(new SaveParentTractCommand(p2, null, "T-9", null, null), default)).Id;

        (await ((Func<Task>)(() => c.Create.Handle(NewParcel(Guid.NewGuid()), default))).Should().ThrowAsync<NotFoundException>()).Which.Code.Should().Be("not_found");
        (await ((Func<Task>)(() => c.Create.Handle(NewParcel(p1, group: groupOfP2), default))).Should().ThrowAsync<InvalidRequestException>()).Which.Code.Should().Be("invalid_group");
        (await ((Func<Task>)(() => c.Create.Handle(NewParcel(p1, tract: tractOfP2), default))).Should().ThrowAsync<InvalidRequestException>()).Which.Code.Should().Be("invalid_tract");
        (await ((Func<Task>)(() => c.Create.Handle(NewParcel(p1, status: Guid.NewGuid()), default))).Should().ThrowAsync<InvalidRequestException>()).Which.Code.Should().Be("invalid_status");
    }

    [Fact]
    public async Task ROW_005_A_retired_status_cannot_be_chosen_for_a_new_parcel()
    {
        var store = await Provision();
        var project = await AddProject(store, TestDb.TenantA);
        await using var admin = new Ctx(store, TestDb.TenantA, TestDb.UserA, RoleNames.Admin);
        var statuses = await admin.Statuses.Handle(new ListParcelStatusesQuery(), default);
        var impasse = statuses.Single(s => s.Name == "Impasse");
        await admin.SaveStatus.Handle(new SaveParcelStatusCommand(impasse.Id, "Impasse", impasse.Color, impasse.SortOrder, false, false), default);

        var act = () => admin.Create.Handle(NewParcel(project, status: impasse.Id), default);

        (await act.Should().ThrowAsync<InvalidRequestException>()).Which.Message.Should().Contain("no longer in use");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(10_000_000_001)]
    public void ROW_005_Area_must_be_zero_or_more_and_sane(long area)
    {
        new CreateParcelCommandValidator().Validate(NewParcel(Guid.NewGuid(), area: area)).IsValid.Should().BeFalse();
        new CreateParcelCommandValidator().Validate(NewParcel(Guid.NewGuid(), area: 0)).IsValid.Should().BeTrue();
    }

    [Fact]
    public void ROW_005_Parcel_numbers_cannot_contain_odd_characters()
    {
        var v = new CreateParcelCommandValidator();
        v.Validate(NewParcel(Guid.NewGuid(), "has space")).IsValid.Should().BeFalse();
        v.Validate(NewParcel(Guid.NewGuid(), "'; drop table")).IsValid.Should().BeFalse();
        v.Validate(NewParcel(Guid.NewGuid(), new string('9', 51))).IsValid.Should().BeFalse();
        v.Validate(NewParcel(Guid.NewGuid(), "12-A/3.b_x")).IsValid.Should().BeTrue();
    }

    // ── update ──

    [Fact]
    public async Task ROW_005_An_update_changes_the_record_and_a_new_number_must_stay_unique()
    {
        var store = await Provision();
        var project = await AddProject(store, TestDb.TenantA);
        await using var c = await Pm(store);
        var one = await c.Create.Handle(NewParcel(project, "A-1"), default);
        await c.Create.Handle(NewParcel(project, "A-2"), default);

        var clash = () => c.Update.Handle(Edit(one, number: "a-2"), default);
        await clash.Should().ThrowAsync<ConflictException>();

        var updated = await c.Update.Handle(Edit(one, number: "A-9", area: 4_321.5m), default);
        updated.Number.Should().Be("A-9");
        updated.AreaSqFt.Should().Be(4_321.5m);
        (await c.Update.Handle(Edit(updated, number: ""), default)).Number.Should().Be("A-9", "a blank number keeps the current one");
    }

    [Fact]
    public async Task ROW_071_The_acquisition_type_can_change_on_the_first_step_but_is_locked_once_the_parcel_has_moved_on()
    {
        var store = await Provision();
        var project = await AddProject(store, TestDb.TenantA);
        await using var c = await Pm(store);
        var parcel = await c.Create.Handle(NewParcel(project), default);

        var donation = await c.Update.Handle(Edit(parcel, type: AcquisitionType.Donation), default);
        donation.AcquisitionType.Should().Be(AcquisitionType.Donation);
        donation.Workflow.Steps.Select(s => s.Key).Should().Equal("setup", "title_survey", "donation_review", "agreement", "closing");

        var moved = await c.Advance.Handle(new AdvanceParcelStepCommand(parcel.Id, "title_survey", null), default);
        var locked = () => c.Update.Handle(Edit(moved, type: AcquisitionType.Standard), default);
        (await locked.Should().ThrowAsync<ConflictException>()).Which.Code.Should().Be("acquisition_type_locked");
    }

    [Fact]
    public async Task ROW_005_A_parcel_keeps_a_status_that_was_later_retired_but_cannot_newly_take_one()
    {
        var store = await Provision();
        var project = await AddProject(store, TestDb.TenantA);
        await using var admin = new Ctx(store, TestDb.TenantA, TestDb.UserA, RoleNames.Admin);
        var statuses = await admin.Statuses.Handle(new ListParcelStatusesQuery(), default);
        var appraisal = statuses.Single(s => s.Name == "Appraisal");
        var parcel = await admin.Create.Handle(NewParcel(project, status: appraisal.Id), default);
        await admin.SaveStatus.Handle(new SaveParcelStatusCommand(appraisal.Id, "Appraisal", appraisal.Color, appraisal.SortOrder, false, false), default);

        (await admin.Update.Handle(Edit(parcel, area: 5_000m), default)).StatusName.Should().Be("Appraisal");
        var other = await admin.Create.Handle(NewParcel(project), default);
        var act = () => admin.Update.Handle(Edit(other, status: appraisal.Id), default);
        await act.Should().ThrowAsync<InvalidRequestException>();
    }

    [Fact]
    public async Task ROW_011_Custom_values_are_validated_normalised_and_kept_when_not_mentioned()
    {
        var store = await Provision();
        var project = await AddProject(store, TestDb.TenantA);
        await using var admin = new Ctx(store, TestDb.TenantA, TestDb.UserA, RoleNames.Admin);
        await admin.SaveCustomField.Handle(new SaveCustomFieldCommand(null, "frontage", "Frontage (ft)", CustomFieldType.Number, null, false, true, 1), default);
        await admin.SaveCustomField.Handle(new SaveCustomFieldCommand(null, "soil", "Soil", CustomFieldType.List, new[] { "Clay", "Sand" }, false, true, 2), default);

        var bad = () => admin.Create.Handle(NewParcel(project, custom: new() { ["frontage"] = "wide" }), default);
        (await bad.Should().ThrowAsync<BusinessRuleException>()).Which.Message.Should().Contain("Frontage (ft)");
        var unknown = () => admin.Create.Handle(NewParcel(project, custom: new() { ["nope"] = "x" }), default);
        await unknown.Should().ThrowAsync<BusinessRuleException>();

        var parcel = await admin.Create.Handle(NewParcel(project, custom: new() { ["frontage"] = " 120.50 ", ["soil"] = "sand" }), default);
        parcel.CustomValues.Should().Contain(new KeyValuePair<string, string>("frontage", "120.50")).And.Contain(new KeyValuePair<string, string>("soil", "Sand"));

        // An update that sends no custom values leaves them alone; one that sends a blank clears it.
        (await admin.Update.Handle(Edit(parcel, area: 1m), default)).CustomValues.Should().HaveCount(2);
        var cleared = await admin.Update.Handle(Edit(parcel, custom: new() { ["frontage"] = "" }), default);
        cleared.CustomValues.Should().NotContainKey("frontage").And.ContainKey("soil");
    }

    [Fact]
    public async Task ROW_011_A_required_custom_field_must_have_a_value_and_a_deactivated_one_no_longer_applies()
    {
        var store = await Provision();
        var project = await AddProject(store, TestDb.TenantA);
        await using var admin = new Ctx(store, TestDb.TenantA, TestDb.UserA, RoleNames.Admin);
        var field = await admin.SaveCustomField.Handle(new SaveCustomFieldCommand(null, "county", "County", CustomFieldType.Text, null, true, true, 1), default);

        var missing = () => admin.Create.Handle(NewParcel(project), default);
        (await missing.Should().ThrowAsync<BusinessRuleException>()).Which.Message.Should().Contain("County is required.");

        await admin.SaveCustomField.Handle(new SaveCustomFieldCommand(field.Id, "county", "County", CustomFieldType.Text, null, true, false, 1), default);
        (await admin.Create.Handle(NewParcel(project), default)).Number.Should().Be("001");
    }

    [Fact]
    public async Task A_superseded_parcel_cannot_be_changed_but_can_still_be_read()
    {
        var store = await Provision();
        var project = await AddProject(store, TestDb.TenantA);
        await using var c = await Pm(store);
        var parcel = await c.Create.Handle(NewParcel(project), default);
        await c.Split.Handle(new SplitParcelCommand(parcel.Id, new[] { new SplitPart(4_000m) }), default);

        var update = () => c.Update.Handle(Edit(parcel, area: 1m), default);
        (await update.Should().ThrowAsync<ConflictException>()).Which.Code.Should().Be("parcel_not_active");
        var advance = () => c.Advance.Handle(new AdvanceParcelStepCommand(parcel.Id, "title_survey", null), default);
        await advance.Should().ThrowAsync<ConflictException>();
        var read = await c.Get.Handle(new GetParcelQuery(parcel.Id), default);
        read.State.Should().Be(ParcelState.Split);
        read.SupersededOn.Should().NotBeNull();
    }

    // ── split and combine (ROW-007) ──

    [Fact]
    public async Task ROW_007_Splitting_creates_numbered_parts_and_a_remnant_and_supersedes_the_original()
    {
        var store = await Provision();
        var project = await AddProject(store, TestDb.TenantA);
        await using var c = await Pm(store);
        var group = (await c.SaveGroup.Handle(new SaveParcelGroupCommand(project, null, "Corridor A", null), default)).Id;
        var tract = (await c.SaveTract.Handle(new SaveParentTractCommand(project, null, "T-1", null, null), default)).Id;
        var original = await c.Create.Handle(NewParcel(project, "012", 10_000m, group: group, tract: tract, custom: null), default);
        await c.Advance.Handle(new AdvanceParcelStepCommand(original.Id, "title_survey", null), default);

        var result = await c.Split.Handle(new SplitParcelCommand(original.Id, new[] { new SplitPart(3_000m), new SplitPart(2_500m) }), default);

        result.Source.State.Should().Be(ParcelState.Split);
        result.Parts.Select(p => (p.Number, p.AreaSqFt)).Should().Equal(("012-A", 3_000m), ("012-B", 2_500m));
        result.Remnant.Should().NotBeNull();
        result.Remnant!.Number.Should().Be("012-R");
        result.Remnant.AreaSqFt.Should().Be(4_500m);
        result.Remnant.IsRemnant.Should().BeTrue();
        result.Parts.Concat(new[] { result.Remnant }).Should().OnlyContain(p => p.GroupId == group && p.ParentTractId == tract && p.StatusName == "Identified" && p.CurrentStepKey == "title_survey");

        var detail = await c.Get.Handle(new GetParcelQuery(result.Parts[0].Id), default);
        detail.SourceParcelIds.Should().Equal(original.Id);
        detail.Milestones.Should().HaveCount(MilestoneNames.Standard.Count);

        var live = await c.List.Handle(new ListParcelsQuery(project, null, null, null, null), default);
        live.Items.Select(p => p.Number).Should().Equal("012-A", "012-B", "012-R");
        (await c.List.Handle(new ListParcelsQuery(project, null, null, null, null, IncludeSuperseded: true), default)).Total.Should().Be(4);
    }

    [Fact]
    public async Task ROW_007_Parts_that_use_the_whole_parcel_leave_no_remnant()
    {
        var store = await Provision();
        var project = await AddProject(store, TestDb.TenantA);
        await using var c = await Pm(store);
        var original = await c.Create.Handle(NewParcel(project, "5", 1_000m), default);

        var result = await c.Split.Handle(new SplitParcelCommand(original.Id, new[] { new SplitPart(600m), new SplitPart(400m) }), default);

        result.Remnant.Should().BeNull();
        result.Parts.Should().HaveCount(2);
    }

    [Fact]
    public async Task ROW_007_A_split_that_uses_more_than_the_parcel_or_collides_with_a_number_is_refused_and_changes_nothing()
    {
        var store = await Provision();
        var project = await AddProject(store, TestDb.TenantA);
        await using var c = await Pm(store);
        var original = await c.Create.Handle(NewParcel(project, "7", 1_000m), default);
        await c.Create.Handle(NewParcel(project, "7-A", 10m), default);

        var tooBig = () => c.Split.Handle(new SplitParcelCommand(original.Id, new[] { new SplitPart(600m), new SplitPart(400.01m) }), default);
        (await tooBig.Should().ThrowAsync<BusinessRuleException>()).Which.Message.Should().Contain("more than the parcel");
        var collides = () => c.Split.Handle(new SplitParcelCommand(original.Id, new[] { new SplitPart(100m) }), default);
        (await collides.Should().ThrowAsync<ConflictException>()).Which.Code.Should().Be("parcel_number_exists");

        (await c.Get.Handle(new GetParcelQuery(original.Id), default)).State.Should().Be(ParcelState.Active);
        (await c.List.Handle(new ListParcelsQuery(project, null, null, null, null), default)).Total.Should().Be(2);
    }

    [Fact]
    public void ROW_007_The_split_request_needs_between_one_and_twenty_five_parts_each_above_zero()
    {
        var v = new SplitParcelCommandValidator();
        v.Validate(new SplitParcelCommand(Guid.NewGuid(), Array.Empty<SplitPart>())).IsValid.Should().BeFalse();
        v.Validate(new SplitParcelCommand(Guid.NewGuid(), new[] { new SplitPart(0m) })).IsValid.Should().BeFalse();
        v.Validate(new SplitParcelCommand(Guid.NewGuid(), Enumerable.Repeat(new SplitPart(1m), 26).ToList())).IsValid.Should().BeFalse();
        v.Validate(new SplitParcelCommand(Guid.NewGuid(), Enumerable.Repeat(new SplitPart(1m), 25).ToList())).IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ROW_007_Combining_adds_the_areas_merges_interests_by_type_and_supersedes_the_sources()
    {
        var store = await Provision();
        var project = await AddProject(store, TestDb.TenantA);
        await using var c = await Pm(store);
        var a = await c.Create.Handle(NewParcel(project, "1", 1_000m), default);
        var b = await c.Create.Handle(NewParcel(project, "2", 2_500m), default);
        await c.SaveInterest.Handle(new SaveParcelInterestCommand(a.Id, null, InterestType.FeeTaking, 400m, null), default);
        await c.SaveInterest.Handle(new SaveParcelInterestCommand(b.Id, null, InterestType.FeeTaking, 600m, null), default);
        await c.SaveInterest.Handle(new SaveParcelInterestCommand(b.Id, null, InterestType.TemporaryEasement, 100m, null), default);
        await c.Advance.Handle(new AdvanceParcelStepCommand(b.Id, "title_survey", null), default);

        var combined = await c.Combine.Handle(new CombineParcelsCommand(new[] { a.Id, b.Id }, null), default);

        combined.AreaSqFt.Should().Be(3_500m);
        combined.Number.Should().Be("003");
        combined.SourceParcelIds.Should().BeEquivalentTo(new[] { a.Id, b.Id });
        combined.Interests.Select(i => (i.Type, i.AreaSqFt)).Should().BeEquivalentTo(new[] { (InterestType.FeeTaking, 1_000m), (InterestType.TemporaryEasement, 100m) });
        combined.Workflow.CurrentKey.Should().Be("setup", "the combined parcel is only as far along as its least advanced source");
        (await c.Get.Handle(new GetParcelQuery(a.Id), default)).State.Should().Be(ParcelState.Combined);
        (await c.Get.Handle(new GetParcelQuery(b.Id), default)).State.Should().Be(ParcelState.Combined);
        (await c.List.Handle(new ListParcelsQuery(project, null, null, null, null), default)).Items.Should().ContainSingle().Which.Number.Should().Be("003");
    }

    [Fact]
    public async Task ROW_007_Combined_parcels_keep_a_shared_status_and_fall_back_to_the_initial_one_when_they_differ()
    {
        var store = await Provision();
        var project = await AddProject(store, TestDb.TenantA);
        await using var c = await Pm(store);
        var statuses = await c.Create.Handle(NewParcel(project), default); // to learn ids via the list below
        var all = await new ListParcelStatusesQueryHandler(c.Db, new PagePermissionService(c.Db, new FakeUser(TestDb.UserA, RoleNames.ProjectManager))).Handle(new ListParcelStatusesQuery(), default);
        var negotiation = all.Single(s => s.Name == "Negotiation").Id;
        var a = await c.Create.Handle(NewParcel(project, status: negotiation), default);
        var b = await c.Create.Handle(NewParcel(project, status: negotiation), default);
        var d = await c.Create.Handle(NewParcel(project), default);

        (await c.Combine.Handle(new CombineParcelsCommand(new[] { a.Id, b.Id }, "NEG-1"), default)).StatusName.Should().Be("Negotiation");
        (await c.Combine.Handle(new CombineParcelsCommand(new[] { statuses.Id, d.Id }, null), default)).StatusName.Should().Be("Identified");
    }

    [Fact]
    public async Task ROW_007_Combining_across_projects_or_acquisition_types_or_with_a_used_up_parcel_is_refused()
    {
        var store = await Provision();
        var p1 = await AddProject(store, TestDb.TenantA, "P-1");
        var p2 = await AddProject(store, TestDb.TenantA, "P-2");
        await using var c = await Pm(store);
        var a = await c.Create.Handle(NewParcel(p1), default);
        var other = await c.Create.Handle(NewParcel(p2), default);
        var donation = await c.Create.Handle(NewParcel(p1, type: AcquisitionType.Donation), default);
        var gone = await c.Create.Handle(NewParcel(p1), default);
        await c.Split.Handle(new SplitParcelCommand(gone.Id, new[] { new SplitPart(1m) }), default);

        (await ((Func<Task>)(() => c.Combine.Handle(new CombineParcelsCommand(new[] { a.Id, other.Id }, null), default))).Should().ThrowAsync<InvalidRequestException>()).Which.Message.Should().Contain("different projects");
        (await ((Func<Task>)(() => c.Combine.Handle(new CombineParcelsCommand(new[] { a.Id, donation.Id }, null), default))).Should().ThrowAsync<InvalidRequestException>()).Which.Message.Should().Contain("acquisition types");
        (await ((Func<Task>)(() => c.Combine.Handle(new CombineParcelsCommand(new[] { a.Id, gone.Id }, null), default))).Should().ThrowAsync<ConflictException>()).Which.Code.Should().Be("parcel_not_active");
        await ((Func<Task>)(() => c.Combine.Handle(new CombineParcelsCommand(new[] { a.Id, Guid.NewGuid() }, null), default))).Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public void ROW_007_Combining_needs_two_to_twenty_five_distinct_parcels()
    {
        var v = new CombineParcelsCommandValidator();
        var id = Guid.NewGuid();
        v.Validate(new CombineParcelsCommand(new[] { id }, null)).IsValid.Should().BeFalse();
        v.Validate(new CombineParcelsCommand(new[] { id, id }, null)).IsValid.Should().BeFalse();
        v.Validate(new CombineParcelsCommand(new[] { Guid.NewGuid(), Guid.NewGuid() }, null)).IsValid.Should().BeTrue();
        v.Validate(new CombineParcelsCommand(Enumerable.Range(0, 26).Select(_ => Guid.NewGuid()).ToList(), null)).IsValid.Should().BeFalse();
    }

    // ── batch update (ROW-008) ──

    [Fact]
    public async Task ROW_008_A_batch_update_changes_only_the_chosen_parcels()
    {
        var store = await Provision();
        var project = await AddProject(store, TestDb.TenantA);
        await using var c = await Pm(store);
        var group = (await c.SaveGroup.Handle(new SaveParcelGroupCommand(project, null, "Corridor A", null), default)).Id;
        var a = await c.Create.Handle(NewParcel(project), default);
        var b = await c.Create.Handle(NewParcel(project), default);
        var untouched = await c.Create.Handle(NewParcel(project), default);
        var statuses = await c.Statuses.Handle(new ListParcelStatusesQuery(), default);
        var appraisal = statuses.Single(s => s.Name == "Appraisal").Id;

        var result = await c.Batch.Handle(new BatchUpdateParcelsCommand(new[] { a.Id, b.Id }, appraisal, group, false), default);

        result.Updated.Should().Be(2);
        (await c.Get.Handle(new GetParcelQuery(a.Id), default)).Should().Match<ParcelDto>(p => p.StatusName == "Appraisal" && p.GroupId == group);
        (await c.Get.Handle(new GetParcelQuery(b.Id), default)).StatusName.Should().Be("Appraisal");
        (await c.Get.Handle(new GetParcelQuery(untouched.Id), default)).Should().Match<ParcelDto>(p => p.StatusName == "Identified" && p.GroupId == null);

        await c.Batch.Handle(new BatchUpdateParcelsCommand(new[] { a.Id }, null, null, true), default);
        (await c.Get.Handle(new GetParcelQuery(a.Id), default)).GroupId.Should().BeNull();
    }

    [Fact]
    public async Task ROW_008_A_batch_with_one_bad_parcel_changes_none()
    {
        var store = await Provision();
        var project = await AddProject(store, TestDb.TenantA);
        await using var c = await Pm(store);
        var a = await c.Create.Handle(NewParcel(project), default);
        var appraisal = (await c.Statuses.Handle(new ListParcelStatusesQuery(), default)).Single(s => s.Name == "Appraisal").Id;

        var act = () => c.Batch.Handle(new BatchUpdateParcelsCommand(new[] { a.Id, Guid.NewGuid() }, appraisal, null, false), default);

        await act.Should().ThrowAsync<NotFoundException>();
        (await c.Get.Handle(new GetParcelQuery(a.Id), default)).StatusName.Should().Be("Identified");
    }

    [Fact]
    public async Task ROW_008_A_group_can_only_be_assigned_to_parcels_of_its_own_project()
    {
        var store = await Provision();
        var p1 = await AddProject(store, TestDb.TenantA, "P-1");
        var p2 = await AddProject(store, TestDb.TenantA, "P-2");
        await using var c = await Pm(store);
        var group = (await c.SaveGroup.Handle(new SaveParcelGroupCommand(p1, null, "G", null), default)).Id;
        var a = await c.Create.Handle(NewParcel(p1), default);
        var b = await c.Create.Handle(NewParcel(p2), default);

        var mixed = () => c.Batch.Handle(new BatchUpdateParcelsCommand(new[] { a.Id, b.Id }, null, group, false), default);
        (await mixed.Should().ThrowAsync<InvalidRequestException>()).Which.Message.Should().Contain("single project");
        var wrong = () => c.Batch.Handle(new BatchUpdateParcelsCommand(new[] { b.Id }, null, group, false), default);
        (await wrong.Should().ThrowAsync<InvalidRequestException>()).Which.Message.Should().Contain("different project");
    }

    [Fact]
    public void ROW_008_The_batch_request_needs_parcels_and_a_change_and_is_capped()
    {
        var v = new BatchUpdateParcelsCommandValidator();
        var id = Guid.NewGuid();
        v.Validate(new BatchUpdateParcelsCommand(Array.Empty<Guid>(), Guid.NewGuid(), null, false)).IsValid.Should().BeFalse();
        v.Validate(new BatchUpdateParcelsCommand(new[] { id }, null, null, false)).IsValid.Should().BeFalse("nothing to apply");
        v.Validate(new BatchUpdateParcelsCommand(new[] { id }, null, Guid.NewGuid(), true)).IsValid.Should().BeFalse("a group and clear-group together");
        v.Validate(new BatchUpdateParcelsCommand(new[] { id, id }, Guid.NewGuid(), null, false)).IsValid.Should().BeFalse();
        v.Validate(new BatchUpdateParcelsCommand(Enumerable.Range(0, 200).Select(_ => Guid.NewGuid()).ToList(), Guid.NewGuid(), null, false)).IsValid.Should().BeTrue();
        v.Validate(new BatchUpdateParcelsCommand(Enumerable.Range(0, 201).Select(_ => Guid.NewGuid()).ToList(), Guid.NewGuid(), null, false)).IsValid.Should().BeFalse();
    }

    // ── groups, tracts, roll-up (ROW-002, 006, 233) ──

    [Fact]
    public async Task ROW_002_Groups_are_unique_per_project_counted_renamed_and_only_removable_when_empty()
    {
        var store = await Provision();
        var p1 = await AddProject(store, TestDb.TenantA, "P-1");
        var p2 = await AddProject(store, TestDb.TenantA, "P-2");
        await using var c = await Pm(store);
        var g = await c.SaveGroup.Handle(new SaveParcelGroupCommand(p1, null, "North", "Segment 1"), default);
        await c.SaveGroup.Handle(new SaveParcelGroupCommand(p2, null, "North", null), default); // same name, another project

        var dup = () => c.SaveGroup.Handle(new SaveParcelGroupCommand(p1, null, "north", null), default);
        (await dup.Should().ThrowAsync<ConflictException>()).Which.Code.Should().Be("group_exists");

        await c.Create.Handle(NewParcel(p1, group: g.Id), default);
        (await c.Groups.Handle(new ListParcelGroupsQuery(p1), default)).Should().ContainSingle().Which.ParcelCount.Should().Be(1);

        var renamed = await c.SaveGroup.Handle(new SaveParcelGroupCommand(p1, g.Id, "North corridor", null), default);
        renamed.Name.Should().Be("North corridor");

        var notEmpty = () => c.DeleteGroup.Handle(new DeleteParcelGroupCommand(p1, g.Id), default);
        (await notEmpty.Should().ThrowAsync<ConflictException>()).Which.Code.Should().Be("group_not_empty");

        var empty = await c.SaveGroup.Handle(new SaveParcelGroupCommand(p1, null, "Spare", null), default);
        await c.DeleteGroup.Handle(new DeleteParcelGroupCommand(p1, empty.Id), default);
        (await c.Groups.Handle(new ListParcelGroupsQuery(p1), default)).Select(x => x.Name).Should().Equal("North corridor");
    }

    [Fact]
    public async Task ROW_233_A_parent_tract_keeps_its_tax_parcel_numbers_cleaned_and_unique_per_project()
    {
        var store = await Provision();
        var project = await AddProject(store, TestDb.TenantA);
        await using var c = await Pm(store);

        var tract = await c.SaveTract.Handle(new SaveParentTractCommand(project, null, "T-1", "Harbor Holdings", new[] { " APN 044-1 ", "APN 044-2", "apn 044-1", "" }), default);

        tract.TaxParcelNumbers.Should().Equal("APN 044-1", "APN 044-2");
        var dup = () => c.SaveTract.Handle(new SaveParentTractCommand(project, null, "t-1", null, null), default);
        (await dup.Should().ThrowAsync<ConflictException>()).Which.Code.Should().Be("tract_exists");

        var edited = await c.SaveTract.Handle(new SaveParentTractCommand(project, tract.Id, "T-1", "Harbor Holdings LLC", new[] { "APN 9" }), default);
        edited.TaxParcelNumbers.Should().Equal("APN 9");
        await c.Create.Handle(NewParcel(project, tract: tract.Id), default);
        (await c.Tracts.Handle(new ListParentTractsQuery(project), default)).Single().ParcelCount.Should().Be(1);
        (await c.Get.Handle(new GetParcelQuery((await c.List.Handle(new ListParcelsQuery(project, null, null, null, null), default)).Items[0].Id), default))
            .TaxParcelNumbers.Should().Equal("APN 9");
    }

    [Fact]
    public async Task ROW_006_The_rollup_counts_live_parcels_by_status_and_group_with_acres()
    {
        var store = await Provision();
        var project = await AddProject(store, TestDb.TenantA);
        await using var c = await Pm(store);
        var group = (await c.SaveGroup.Handle(new SaveParcelGroupCommand(project, null, "North", null), default)).Id;
        var appraisal = (await c.Statuses.Handle(new ListParcelStatusesQuery(), default)).Single(s => s.Name == "Appraisal").Id;
        await c.Create.Handle(NewParcel(project, area: 43_560m, group: group), default);
        await c.Create.Handle(NewParcel(project, area: 21_780m, status: appraisal), default);
        var gone = await c.Create.Handle(NewParcel(project, area: 999_999m), default);
        await c.Split.Handle(new SplitParcelCommand(gone.Id, new[] { new SplitPart(100m) }), default);

        var all = await c.Rollup.Handle(new GetParcelRollupQuery(project, null), default);
        var north = await c.Rollup.Handle(new GetParcelRollupQuery(project, group), default);

        all.TotalCount.Should().Be(4, "two live parcels plus the split's part and remnant");
        all.ByStatus.Single(b => b.Name == "Appraisal").Should().Match<RollupBucketDto>(b => b.Count == 1 && b.AreaSqFt == 21_780m && b.Acres == 0.5m);
        all.ByStatus.Select(b => b.Name).Should().Contain("Identified").And.Contain("Closed", "empty statuses still appear");
        all.ByGroup.Single(b => b.Name == "North").Count.Should().Be(1);
        all.ByGroup.Single(b => b.Name == "No group").Count.Should().Be(3);
        north.TotalCount.Should().Be(1);
        north.TotalAcres.Should().Be(1m);
    }

    // ── interests, milestones, visits, notice ──

    [Fact]
    public async Task ROW_234_Interests_are_added_changed_and_removed_and_cannot_exceed_the_parcel()
    {
        var store = await Provision();
        var project = await AddProject(store, TestDb.TenantA);
        await using var c = await Pm(store);
        var parcel = await c.Create.Handle(NewParcel(project, area: 1_000m), default);

        var added = await c.SaveInterest.Handle(new SaveParcelInterestCommand(parcel.Id, null, InterestType.FeeTaking, 1_000m, "whole parcel"), default);
        var id = added.Interests.Single().Id;
        var tooBig = () => c.SaveInterest.Handle(new SaveParcelInterestCommand(parcel.Id, null, InterestType.PermanentEasement, 1_000.01m, null), default);
        (await tooBig.Should().ThrowAsync<BusinessRuleException>()).Which.Message.Should().Contain("larger than the parcel");

        var changed = await c.SaveInterest.Handle(new SaveParcelInterestCommand(parcel.Id, id, InterestType.Remainder, 250m, null), default);
        changed.Interests.Single().Should().Match<ParcelInterestDto>(i => i.Type == InterestType.Remainder && i.AreaSqFt == 250m);
        (await c.DeleteInterest.Handle(new DeleteParcelInterestCommand(parcel.Id, id), default)).Interests.Should().BeEmpty();
        var missing = () => c.DeleteInterest.Handle(new DeleteParcelInterestCommand(parcel.Id, Guid.NewGuid()), default);
        await missing.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task ROW_234_A_parcel_whose_area_is_not_yet_known_accepts_any_interest_area()
    {
        var store = await Provision();
        var project = await AddProject(store, TestDb.TenantA);
        await using var c = await Pm(store);
        var parcel = await c.Create.Handle(NewParcel(project, area: 0m), default);

        var added = await c.SaveInterest.Handle(new SaveParcelInterestCommand(parcel.Id, null, InterestType.FeeTaking, 5_000m, null), default);

        added.Interests.Should().ContainSingle();
    }

    [Fact]
    public async Task ROW_010_Custom_milestones_can_be_added_dated_flagged_overdue_and_removed_but_standard_ones_stay()
    {
        var store = await Provision();
        var project = await AddProject(store, TestDb.TenantA);
        await using var c = await Pm(store);
        var parcel = await c.Create.Handle(NewParcel(project), default);

        var added = await c.AddMilestone.Handle(new AddParcelMilestoneCommand(parcel.Id, "Appraiser walkthrough", Today.AddDays(-3), null, "owner away"), default);
        var custom = added.Milestones.Single(m => m.Name == "Appraiser walkthrough");
        custom.IsStandard.Should().BeFalse();
        custom.IsOverdue.Should().BeTrue("the due date has passed and it is not complete");

        var dup = () => c.AddMilestone.Handle(new AddParcelMilestoneCommand(parcel.Id, "appraiser WALKTHROUGH", null, null, null), default);
        (await dup.Should().ThrowAsync<ConflictException>()).Which.Code.Should().Be("milestone_exists");

        var done = await c.UpdateMilestone.Handle(new UpdateParcelMilestoneCommand(parcel.Id, custom.Id, Today.AddDays(-3), Today, null), default);
        done.Milestones.Single(m => m.Id == custom.Id).Should().Match<ParcelMilestoneDto>(m => m.CompletedOn == Today && !m.IsOverdue);

        var standard = done.Milestones.First(m => m.IsStandard);
        var cannot = () => c.DeleteMilestone.Handle(new DeleteParcelMilestoneCommand(parcel.Id, standard.Id), default);
        (await cannot.Should().ThrowAsync<ConflictException>()).Which.Code.Should().Be("milestone_standard");
        (await c.DeleteMilestone.Handle(new DeleteParcelMilestoneCommand(parcel.Id, custom.Id), default)).Milestones.Should().NotContain(m => m.Id == custom.Id);
    }

    [Fact]
    public async Task ROW_010_The_notice_milestone_date_is_set_by_recording_the_notice_not_by_editing_the_milestone()
    {
        var store = await Provision();
        var project = await AddProject(store, TestDb.TenantA);
        await using var c = await Pm(store);
        var parcel = await c.Create.Handle(NewParcel(project), default);
        var noticed = await c.Notice.Handle(new RecordNoticeToOwnerCommand(parcel.Id, Today.AddDays(-10), NoticeMethod.Email), default);
        var milestone = noticed.Milestones.Single(m => m.Name == MilestoneNames.NoticeToOwner);

        var drift = () => c.UpdateMilestone.Handle(new UpdateParcelMilestoneCommand(parcel.Id, milestone.Id, null, Today.AddDays(-3), null), default);
        var clear = () => c.UpdateMilestone.Handle(new UpdateParcelMilestoneCommand(parcel.Id, milestone.Id, null, null, null), default);

        (await drift.Should().ThrowAsync<InvalidRequestException>()).Which.Code.Should().Be("milestone_notice_managed");
        (await clear.Should().ThrowAsync<InvalidRequestException>()).Which.Code.Should().Be("milestone_notice_managed");
        // Its due date and notes are still the user's to set, with the completion date left as recorded.
        var kept = await c.UpdateMilestone.Handle(new UpdateParcelMilestoneCommand(parcel.Id, milestone.Id, Today.AddDays(5), Today.AddDays(-10), "sent by courier"), default);
        kept.Milestones.Single(m => m.Id == milestone.Id).Should().Match<ParcelMilestoneDto>(m => m.CompletedOn == Today.AddDays(-10) && m.DueOn == Today.AddDays(5) && m.Notes == "sent by courier");
        kept.NoticeIssuedOn.Should().Be(Today.AddDays(-10), "the notice and its milestone never disagree");
    }

    [Fact]
    public async Task ROW_010_The_set_up_date_can_be_corrected_but_not_cleared()
    {
        var store = await Provision();
        var project = await AddProject(store, TestDb.TenantA);
        await using var c = await Pm(store);
        var parcel = await c.Create.Handle(NewParcel(project), default);
        var setUp = parcel.Milestones.Single(m => m.Name == MilestoneNames.ParcelSetUp);

        var clear = () => c.UpdateMilestone.Handle(new UpdateParcelMilestoneCommand(parcel.Id, setUp.Id, null, null, null), default);
        var corrected = await c.UpdateMilestone.Handle(new UpdateParcelMilestoneCommand(parcel.Id, setUp.Id, null, Today.AddDays(-30), null), default);

        (await clear.Should().ThrowAsync<ValidationException>()).Which.Errors.Should().ContainSingle(e => e.PropertyName == "CompletedOn" && e.ErrorMessage == "The set-up date cannot be cleared.");
        corrected.Milestones.Single(m => m.Id == setUp.Id).CompletedOn.Should().Be(Today.AddDays(-30));
    }

    [Fact]
    public async Task ROW_010_A_milestone_cannot_be_completed_in_the_future_when_added_or_edited_but_may_be_due_then()
    {
        var store = await Provision();
        var project = await AddProject(store, TestDb.TenantA);
        await using var c = await Pm(store);
        var parcel = await c.Create.Handle(NewParcel(project), default);

        var addFuture = () => c.AddMilestone.Handle(new AddParcelMilestoneCommand(parcel.Id, "Survey stakes", null, Today.AddDays(1), null), default);
        (await addFuture.Should().ThrowAsync<ValidationException>()).Which.Errors.Should().ContainSingle(e => e.PropertyName == "CompletedOn" && e.ErrorMessage.Contains("future"));

        var added = await c.AddMilestone.Handle(new AddParcelMilestoneCommand(parcel.Id, "Survey stakes", Today.AddDays(30), Today, null), default);
        var m = added.Milestones.Single(x => x.Name == "Survey stakes");
        m.CompletedOn.Should().Be(Today, "completing today is fine");
        m.DueOn.Should().Be(Today.AddDays(30), "a due date can be in the future");

        var editFuture = () => c.UpdateMilestone.Handle(new UpdateParcelMilestoneCommand(parcel.Id, m.Id, null, Today.AddDays(2), null), default);
        (await editFuture.Should().ThrowAsync<ValidationException>()).Which.Errors.Should().ContainSingle(e => e.PropertyName == "CompletedOn");
        (await c.Get.Handle(new GetParcelQuery(parcel.Id), default)).Milestones.Single(x => x.Id == m.Id).CompletedOn.Should().Be(Today, "the refused edit changed nothing");
    }

    [Theory]
    [InlineData(1900, 1, 1)]
    [InlineData(1989, 12, 31)]
    [InlineData(2101, 1, 1)]
    [InlineData(1, 1, 1)]
    public void ROW_010_Implausible_milestone_dates_are_refused_and_ordinary_ones_accepted(int year, int month, int day)
    {
        var odd = new DateOnly(year, month, day);

        new AddParcelMilestoneCommandValidator().Validate(new AddParcelMilestoneCommand(Guid.NewGuid(), "X", odd, null, null)).Errors.Should().ContainSingle(e => e.PropertyName == "DueOn");
        new AddParcelMilestoneCommandValidator().Validate(new AddParcelMilestoneCommand(Guid.NewGuid(), "X", null, odd, null)).Errors.Should().ContainSingle(e => e.PropertyName == "CompletedOn");
        new UpdateParcelMilestoneCommandValidator().Validate(new UpdateParcelMilestoneCommand(Guid.NewGuid(), Guid.NewGuid(), odd, odd, null)).Errors.Should().HaveCount(2);
        new AddParcelMilestoneCommandValidator().Validate(new AddParcelMilestoneCommand(Guid.NewGuid(), "X", new DateOnly(1990, 1, 1), new DateOnly(2100, 12, 31), null)).IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ROW_236_Site_visits_are_recorded_newest_first_and_can_be_removed()
    {
        var store = await Provision();
        var project = await AddProject(store, TestDb.TenantA);
        await using var c = await Pm(store);
        var parcel = await c.Create.Handle(NewParcel(project), default);

        await c.AddVisit.Handle(new AddParcelSiteVisitCommand(parcel.Id, new DateOnly(2026, 1, 5), "Walked the frontage", true), default);
        var two = await c.AddVisit.Handle(new AddParcelSiteVisitCommand(parcel.Id, new DateOnly(2026, 2, 9), null, false), default);

        two.SiteVisits.Select(v => v.VisitedOn).Should().Equal(new DateOnly(2026, 2, 9), new DateOnly(2026, 1, 5));
        two.SiteVisits[1].OwnerContactConfirmed.Should().BeTrue();
        var left = await c.DeleteVisit.Handle(new DeleteParcelSiteVisitCommand(parcel.Id, two.SiteVisits[0].Id), default);
        left.SiteVisits.Should().ContainSingle();
    }

    [Fact]
    public async Task ROW_237_The_notice_to_owner_records_date_and_method_and_completes_its_milestone_and_can_be_cleared()
    {
        var store = await Provision();
        var project = await AddProject(store, TestDb.TenantA);
        await using var c = await Pm(store);
        var parcel = await c.Create.Handle(NewParcel(project), default);

        var sent = await c.Notice.Handle(new RecordNoticeToOwnerCommand(parcel.Id, new DateOnly(2026, 3, 1), NoticeMethod.CertifiedMail), default);

        sent.NoticeIssuedOn.Should().Be(new DateOnly(2026, 3, 1));
        sent.NoticeMethod.Should().Be(NoticeMethod.CertifiedMail);
        sent.Milestones.Single(m => m.Name == MilestoneNames.NoticeToOwner).CompletedOn.Should().Be(new DateOnly(2026, 3, 1));

        var cleared = await c.Notice.Handle(new RecordNoticeToOwnerCommand(parcel.Id, null, null), default);
        cleared.NoticeIssuedOn.Should().BeNull();
        cleared.NoticeMethod.Should().BeNull();
        cleared.Milestones.Single(m => m.Name == MilestoneNames.NoticeToOwner).CompletedOn.Should().BeNull();
    }

    [Fact]
    public void ROW_237_The_notice_needs_a_method_with_its_date_and_none_without_it()
    {
        var v = new RecordNoticeToOwnerCommandValidator();
        v.Validate(new RecordNoticeToOwnerCommand(Guid.NewGuid(), Today, null)).IsValid.Should().BeFalse();
        v.Validate(new RecordNoticeToOwnerCommand(Guid.NewGuid(), null, NoticeMethod.Email)).IsValid.Should().BeFalse();
        v.Validate(new RecordNoticeToOwnerCommand(Guid.NewGuid(), Today, NoticeMethod.Email)).IsValid.Should().BeTrue();
        v.Validate(new RecordNoticeToOwnerCommand(Guid.NewGuid(), null, null)).IsValid.Should().BeTrue();
    }

    // ── deadlines and reminders (ROW-073, 074) ──

    [Fact]
    public async Task ROW_073_The_offer_deadline_runs_from_the_notice_and_flags_a_delay_in_the_parcel_view()
    {
        var store = await Provision();
        var project = await AddProject(store, TestDb.TenantA);
        await using var c = await Pm(store);
        var late = await c.Create.Handle(NewParcel(project), default);
        var fresh = await c.Create.Handle(NewParcel(project), default);

        var lateView = await c.Notice.Handle(new RecordNoticeToOwnerCommand(late.Id, Today.AddDays(-100), NoticeMethod.HandDelivery), default);
        var freshView = await c.Notice.Handle(new RecordNoticeToOwnerCommand(fresh.Id, Today.AddDays(-10), NoticeMethod.HandDelivery), default);

        var offerLate = lateView.Deadlines.Single(d => d.TriggerMilestone == MilestoneNames.NoticeToOwner);
        offerLate.Status.Should().Be("Overdue");
        offerLate.DueOn.Should().Be(Today.AddDays(-100 + 90));
        offerLate.DaysRemaining.Should().Be(-10);
        freshView.Deadlines.Single(d => d.TriggerMilestone == MilestoneNames.NoticeToOwner).Status.Should().Be("Pending");
        lateView.Deadlines.Single(d => d.TriggerMilestone == MilestoneNames.ParcelSetUp).Status.Should().Be("Pending", "the 24-month clock started today");
    }

    [Fact]
    public async Task ROW_074_Reminders_list_overdue_then_due_soon_across_parcels_and_leave_out_used_up_parcels_and_inactive_rules()
    {
        var store = await Provision();
        var project = await AddProject(store, TestDb.TenantA);
        await using var admin = new Ctx(store, TestDb.TenantA, TestDb.UserA, RoleNames.Admin);
        var overdue = await admin.Create.Handle(NewParcel(project, "OVERDUE"), default);
        var soon = await admin.Create.Handle(NewParcel(project, "SOON"), default);
        var fine = await admin.Create.Handle(NewParcel(project, "FINE"), default);
        var gone = await admin.Create.Handle(NewParcel(project, "GONE"), default);
        await admin.Notice.Handle(new RecordNoticeToOwnerCommand(overdue.Id, Today.AddDays(-95), NoticeMethod.Email), default);
        await admin.Notice.Handle(new RecordNoticeToOwnerCommand(soon.Id, Today.AddDays(-85), NoticeMethod.Email), default);
        await admin.Notice.Handle(new RecordNoticeToOwnerCommand(fine.Id, Today.AddDays(-5), NoticeMethod.Email), default);
        await admin.Notice.Handle(new RecordNoticeToOwnerCommand(gone.Id, Today.AddDays(-200), NoticeMethod.Email), default);
        await admin.Split.Handle(new SplitParcelCommand(gone.Id, new[] { new SplitPart(1m) }), default);

        var reminders = await admin.Reminders.Handle(new ListRemindersQuery(), default);

        reminders.Select(r => (r.ParcelNumber, r.Status)).Should().Equal(("OVERDUE", "Overdue"), ("SOON", "DueSoon"));
        reminders[0].DaysRemaining.Should().Be(-5);
        reminders[1].DaysRemaining.Should().Be(5);
        reminders[0].RuleName.Should().Contain("Offer within");

        (await admin.Reminders.Handle(new ListRemindersQuery(2), default)).Select(r => r.ParcelNumber).Should().Equal("OVERDUE");

        var rules = await admin.Rules.Handle(new ListDeadlineRulesQuery(), default);
        foreach (var r in rules) await admin.SaveRule.Handle(new SaveDeadlineRuleCommand(r.Id, r.Name, r.TriggerMilestone, r.TargetMilestone, r.Days, false), default);
        (await admin.Reminders.Handle(new ListRemindersQuery(), default)).Should().BeEmpty();
    }

    [Fact]
    public async Task ROW_074_Reminders_find_a_late_parcel_however_many_parcels_the_agency_has()
    {
        // A cap on how many parcels were looked at used to drop whatever sorted after the first 5,000 parcel numbers.
        var store = await Provision();
        var project = await AddProject(store, TestDb.TenantA);
        await using var c = await Pm(store);
        var parcels = Enumerable.Range(0, 5_200).Select(i => new Parcel
        {
            ProjectId = project, Number = $"N{i:D5}", StatusId = Guid.NewGuid(), CurrentStepKey = "setup",
            Milestones =
            {
                new ParcelMilestone { Name = MilestoneNames.ParcelSetUp, CompletedOn = Today },
                new ParcelMilestone { Name = MilestoneNames.NoticeToOwner, CompletedOn = Today.AddDays(-5) }, // 85 days left: nothing to do
            },
        }).ToList();
        parcels.Add(new Parcel
        {
            ProjectId = project, Number = "ZZ-LAST", StatusId = Guid.NewGuid(), CurrentStepKey = "setup",
            Milestones = { new ParcelMilestone { Name = MilestoneNames.NoticeToOwner, CompletedOn = Today.AddDays(-150) } },
        });
        c.Db.Parcels.AddRange(parcels);
        await c.Db.SaveChangesAsync();

        var reminders = await c.Reminders.Handle(new ListRemindersQuery(), default);

        reminders.Select(r => r.ParcelNumber).Should().Equal("ZZ-LAST");
        reminders[0].Status.Should().Be("Overdue");
    }

    [Fact]
    public async Task ROW_074_Reminders_match_rule_milestones_ignoring_case_and_leave_out_met_deadlines_and_the_window_edge_is_exact()
    {
        var store = await Provision();
        var project = await AddProject(store, TestDb.TenantA);
        await using var admin = new Ctx(store, TestDb.TenantA, TestDb.UserA, RoleNames.Admin);
        // Only this rule is active, written in different letters from the milestone names.
        foreach (var r in await admin.Rules.Handle(new ListDeadlineRulesQuery(), default))
            await admin.SaveRule.Handle(new SaveDeadlineRuleCommand(r.Id, r.Name, r.TriggerMilestone, r.TargetMilestone, r.Days, false), default);
        await admin.SaveRule.Handle(new SaveDeadlineRuleCommand(null, "Agreement in 30", "NOTICE TO owner", "agreement SIGNED", 30, true), default);

        async Task<ParcelDto> ParcelNoticed(string number, int daysAgo) =>
            await admin.Notice.Handle(new RecordNoticeToOwnerCommand((await admin.Create.Handle(NewParcel(project, number), default)).Id, Today.AddDays(-daysAgo), NoticeMethod.Email), default);

        await ParcelNoticed("LATE", 40);        // due 10 days ago
        await ParcelNoticed("EDGE-IN", 23);     // due in exactly 7 days: inside the window
        await ParcelNoticed("EDGE-OUT", 22);    // due in 8 days: outside it
        await ParcelNoticed("JUST-LATE", 31);   // due yesterday
        var signed = await ParcelNoticed("SIGNED", 40);
        await admin.UpdateMilestone.Handle(new UpdateParcelMilestoneCommand(signed.Id, signed.Milestones.Single(m => m.Name == MilestoneNames.AgreementSigned).Id, null, Today.AddDays(-12), null), default);

        var reminders = await admin.Reminders.Handle(new ListRemindersQuery(7), default);

        reminders.Select(r => (r.ParcelNumber, r.Status)).Should().Equal(("LATE", "Overdue"), ("JUST-LATE", "Overdue"), ("EDGE-IN", "DueSoon"));
        (await admin.Reminders.Handle(new ListRemindersQuery(8), default)).Select(r => r.ParcelNumber).Should().Equal("LATE", "JUST-LATE", "EDGE-IN", "EDGE-OUT");
        (await admin.Reminders.Handle(new ListRemindersQuery(0), default)).Select(r => r.ParcelNumber).Should().Equal("LATE", "JUST-LATE");
    }

    [Fact]
    public async Task ROW_074_Reminders_never_cross_agencies()
    {
        var store = await Provision();
        var project = await AddProject(store, TestDb.TenantA);
        await using var a = await Pm(store);
        var parcel = await a.Create.Handle(NewParcel(project), default);
        await a.Notice.Handle(new RecordNoticeToOwnerCommand(parcel.Id, Today.AddDays(-150), NoticeMethod.Email), default);

        await using var b = new Ctx(store, TestDb.TenantB, TestDb.UserB, RoleNames.ProjectManager);

        (await a.Reminders.Handle(new ListRemindersQuery(), default)).Should().ContainSingle();
        (await b.Reminders.Handle(new ListRemindersQuery(), default)).Should().BeEmpty();
    }

    // ── step gating (ROW-072) ──

    [Fact]
    public async Task ROW_072_The_next_step_is_taken_in_order_and_recorded_in_the_history()
    {
        var store = await Provision();
        var project = await AddProject(store, TestDb.TenantA);
        await using var c = await Pm(store);
        var parcel = await c.Create.Handle(NewParcel(project), default);

        var moved = await c.Advance.Handle(new AdvanceParcelStepCommand(parcel.Id, "title_survey", null), default);

        moved.Workflow.CurrentKey.Should().Be("title_survey");
        moved.Workflow.NextKey.Should().Be("appraisal");
        moved.StepHistory.Should().ContainSingle().Which.Should().Match<ParcelStepEventDto>(e =>
            e.FromStepKey == "setup" && e.ToStepKey == "title_survey" && !e.IsOverride && e.Reason == null && e.By == TestDb.UserA);
    }

    [Fact]
    public async Task ROW_072_Skipping_ahead_needs_a_reason_and_is_recorded_as_an_override_with_who_and_why()
    {
        var store = await Provision();
        var project = await AddProject(store, TestDb.TenantA);
        await using var c = await Pm(store);
        var parcel = await c.Create.Handle(NewParcel(project), default);

        var noReason = () => c.Advance.Handle(new AdvanceParcelStepCommand(parcel.Id, "appraisal", "  "), default);
        (await noReason.Should().ThrowAsync<ValidationException>()).Which.Errors.Should().Contain(e => e.ErrorMessage.Contains("reason is required"));
        (await c.Get.Handle(new GetParcelQuery(parcel.Id), default)).Workflow.CurrentKey.Should().Be("setup", "a refused move changes nothing");

        var skipped = await c.Advance.Handle(new AdvanceParcelStepCommand(parcel.Id, "appraisal", " Title was cleared under the 2024 project "), default);

        skipped.Workflow.CurrentKey.Should().Be("appraisal");
        skipped.StepHistory.Should().ContainSingle().Which.Should().Match<ParcelStepEventDto>(e => e.IsOverride && e.Reason == "Title was cleared under the 2024 project");

        var back = await c.Advance.Handle(new AdvanceParcelStepCommand(parcel.Id, "setup", "Re-opened after boundary dispute"), default);
        back.StepHistory.Should().HaveCount(2);
        back.StepHistory[0].ToStepKey.Should().Be("setup", "newest first");
        back.StepHistory[0].IsOverride.Should().BeTrue();
    }

    [Fact]
    public async Task ROW_072_A_role_that_may_update_parcels_but_not_override_can_only_take_the_next_step_until_an_administrator_adds_it()
    {
        var store = await Provision();
        var project = await AddProject(store, TestDb.TenantA);
        await using (var db = TestDb.Create(store, tenant: null))
        {
            var row = await db.RolePagePermissions.IgnoreQueryFilters().Include(p => p.Role)
                .SingleAsync(p => p.TenantId == TestDb.TenantA && p.Role!.Name == RoleNames.Negotiator && p.PageKey == PageKeys.Parcels);
            row.CanUpdate = true;
            await db.SaveChangesAsync();
        }
        await using var pm = await Pm(store);
        await using var negotiator = new Ctx(store, TestDb.TenantA, TestDb.UserA2, RoleNames.Negotiator);
        await using var admin = new Ctx(store, TestDb.TenantA, TestDb.UserA, RoleNames.Admin);
        var parcel = await pm.Create.Handle(NewParcel(project), default);

        (await negotiator.Advance.Handle(new AdvanceParcelStepCommand(parcel.Id, "title_survey", null), default)).Workflow.CurrentKey.Should().Be("title_survey");
        var back = () => negotiator.Advance.Handle(new AdvanceParcelStepCommand(parcel.Id, "setup", "I know best"), default);
        await back.Should().ThrowAsync<ForbiddenException>();
        (await negotiator.Get.Handle(new GetParcelQuery(parcel.Id), default)).Workflow.CanOverride.Should().BeFalse();

        await admin.SetStepGateSettings.Handle(new SetStepGateSettingsCommand(new[] { RoleNames.Negotiator }), default);

        // The next request (a fresh context, as in production) sees the new setting at once.
        await using var next = new Ctx(store, TestDb.TenantA, TestDb.UserA2, RoleNames.Negotiator);
        (await next.Get.Handle(new GetParcelQuery(parcel.Id), default)).Workflow.CanOverride.Should().BeTrue();
        // Skipping across offer steps still needs the project's gate, which is closed for this federal project.
        var gated = () => next.Advance.Handle(new AdvanceParcelStepCommand(parcel.Id, "closing", "approved by the district"), default);
        (await gated.Should().ThrowAsync<ConflictException>()).Which.Code.Should().Be("acquisition_gate_closed");
        (await next.Advance.Handle(new AdvanceParcelStepCommand(parcel.Id, "setup", "approved by the district"), default)).Workflow.CurrentKey.Should().Be("setup");
    }

    [Fact]
    public async Task ROW_076_Offer_steps_stay_held_until_the_project_is_cleared_to_acquire_and_no_override_gets_past_it()
    {
        var store = await Provision();
        var held = await AddProject(store, TestDb.TenantA, "HELD");
        var open = await AddProject(store, TestDb.TenantA, "OPEN", cleared: true);
        var local = await AddProject(store, TestDb.TenantA, "LOCAL", OversightType.Local);
        await using var c = await Pm(store);

        async Task<ParcelDto> AtAppraisal(Guid project)
        {
            var p = await c.Create.Handle(NewParcel(project), default);
            foreach (var step in new[] { "title_survey", "appraisal" }) p = await c.Advance.Handle(new AdvanceParcelStepCommand(p.Id, step, null), default);
            return p;
        }

        var heldParcel = await AtAppraisal(held);
        heldParcel.Workflow.AcquisitionGateOpen.Should().BeFalse();
        var blocked = () => c.Advance.Handle(new AdvanceParcelStepCommand(heldParcel.Id, "offer", null), default);
        (await blocked.Should().ThrowAsync<ConflictException>()).Which.Code.Should().Be("acquisition_gate_closed");
        var overridden = () => c.Advance.Handle(new AdvanceParcelStepCommand(heldParcel.Id, "agreement", "rush"), default);
        (await overridden.Should().ThrowAsync<ConflictException>()).Which.Code.Should().Be("acquisition_gate_closed");

        (await c.Advance.Handle(new AdvanceParcelStepCommand((await AtAppraisal(open)).Id, "offer", null), default)).Workflow.CurrentKey.Should().Be("offer");
        (await c.Advance.Handle(new AdvanceParcelStepCommand((await AtAppraisal(local)).Id, "offer", null), default)).Workflow.CurrentKey.Should().Be("offer");
    }

    [Fact]
    public async Task ROW_072_An_unknown_step_or_the_same_step_is_refused()
    {
        var store = await Provision();
        var project = await AddProject(store, TestDb.TenantA);
        await using var c = await Pm(store);
        var parcel = await c.Create.Handle(NewParcel(project), default);

        (await ((Func<Task>)(() => c.Advance.Handle(new AdvanceParcelStepCommand(parcel.Id, "teleport", "x"), default))).Should().ThrowAsync<InvalidRequestException>()).Which.Code.Should().Be("invalid_step");
        (await ((Func<Task>)(() => c.Advance.Handle(new AdvanceParcelStepCommand(parcel.Id, "setup", null), default))).Should().ThrowAsync<InvalidRequestException>()).Which.Message.Should().Contain("already on");
    }

    [Fact]
    public async Task ROW_071_Each_acquisition_type_has_its_own_workflow()
    {
        var store = await Provision();
        await using var c = await Pm(store);

        var workflows = await c.Workflows.Handle(new ListWorkflowsQuery(), default);

        workflows.Select(w => w.AcquisitionType).Should().Equal(AcquisitionType.Standard, AcquisitionType.Donation, AcquisitionType.DeveloperConveyance, AcquisitionType.Early);
        workflows.Single(w => w.AcquisitionType == AcquisitionType.Standard).Steps.Select(s => s.Key)
            .Should().Equal("setup", "title_survey", "appraisal", "offer", "negotiation", "agreement", "closing");
        workflows.Single(w => w.AcquisitionType == AcquisitionType.Standard).Steps.Where(s => s.RequiresAcquisitionGate).Select(s => s.Key)
            .Should().Equal("offer", "negotiation", "agreement", "closing");
    }

    // ── agency rules: who can change them, and how ──

    [Fact]
    public async Task ROW_005_Only_administrators_change_the_agencys_rules_but_everyone_with_parcel_access_can_read_them()
    {
        var store = await Provision();
        await using var pm = await Pm(store);
        var statuses = await pm.Statuses.Handle(new ListParcelStatusesQuery(), default);
        statuses.Should().HaveCount(DefaultParcelRules.Statuses.Count);

        await ((Func<Task>)(() => pm.SaveStatus.Handle(new SaveParcelStatusCommand(null, "New", "#112233", 9, true, false), default))).Should().ThrowAsync<ForbiddenException>();
        await ((Func<Task>)(() => pm.SaveCustomField.Handle(new SaveCustomFieldCommand(null, "x", "X", CustomFieldType.Text, null, false, true, 0), default))).Should().ThrowAsync<ForbiddenException>();
        await ((Func<Task>)(() => pm.SaveRule.Handle(new SaveDeadlineRuleCommand(null, "R", "A", "B", 5, true), default))).Should().ThrowAsync<ForbiddenException>();
        await ((Func<Task>)(() => pm.SetStepGateSettings.Handle(new SetStepGateSettingsCommand(new[] { RoleNames.Finance }), default))).Should().ThrowAsync<ForbiddenException>();
        (await pm.CustomFields.Handle(new ListCustomFieldsQuery(), default)).Should().BeEmpty();
        (await pm.Rules.Handle(new ListDeadlineRulesQuery(), default)).Should().HaveCount(DefaultParcelRules.DeadlineRules.Count);
        (await pm.StepGateSettings.Handle(new GetStepGateSettingsQuery(), default)).AlwaysRoles.Should().BeEquivalentTo(RoleNames.ProjectManager, RoleNames.Admin);
    }

    [Fact]
    public async Task ROW_005_Statuses_can_be_added_recoloured_reordered_and_the_initial_one_moves_without_leaving_two()
    {
        var store = await Provision();
        await using var admin = new Ctx(store, TestDb.TenantA, TestDb.UserA, RoleNames.Admin);

        var added = await admin.SaveStatus.Handle(new SaveParcelStatusCommand(null, "Pending review", "#AABBCC", 3, true, false), default);
        added.Color.Should().Be("#aabbcc");

        var dup = () => admin.SaveStatus.Handle(new SaveParcelStatusCommand(null, "pending REVIEW", "#112233", 1, true, false), default);
        (await dup.Should().ThrowAsync<ConflictException>()).Which.Code.Should().Be("status_exists");

        await admin.SaveStatus.Handle(new SaveParcelStatusCommand(added.Id, "Pending review", "#AABBCC", 3, true, true), default);
        var list = await admin.Statuses.Handle(new ListParcelStatusesQuery(), default);
        list.Where(s => s.IsInitial).Should().ContainSingle().Which.Name.Should().Be("Pending review");

        var unset = () => admin.SaveStatus.Handle(new SaveParcelStatusCommand(added.Id, "Pending review", "#AABBCC", 3, true, false), default);
        await unset.Should().ThrowAsync<BusinessRuleException>();
        var retireInitial = () => admin.SaveStatus.Handle(new SaveParcelStatusCommand(added.Id, "Pending review", "#AABBCC", 3, false, true), default);
        await retireInitial.Should().ThrowAsync<BusinessRuleException>();
    }

    [Theory]
    [InlineData("red")]
    [InlineData("#12345")]
    [InlineData("#1234567")]
    [InlineData("#gggggg")]
    [InlineData("")]
    public void ROW_005_A_status_colour_must_be_a_six_digit_hex_value(string color)
    {
        new SaveParcelStatusCommandValidator().Validate(new SaveParcelStatusCommand(null, "S", color, 0, true, false)).IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task ROW_011_Custom_field_keys_and_types_are_fixed_once_created_and_keys_are_unique()
    {
        var store = await Provision();
        await using var admin = new Ctx(store, TestDb.TenantA, TestDb.UserA, RoleNames.Admin);
        var field = await admin.SaveCustomField.Handle(new SaveCustomFieldCommand(null, "zoning", "Zoning", CustomFieldType.Text, null, false, true, 1), default);

        var dup = () => admin.SaveCustomField.Handle(new SaveCustomFieldCommand(null, "zoning", "Zoning again", CustomFieldType.Text, null, false, true, 2), default);
        (await dup.Should().ThrowAsync<ConflictException>()).Which.Code.Should().Be("custom_field_exists");
        var retype = () => admin.SaveCustomField.Handle(new SaveCustomFieldCommand(field.Id, "zoning", "Zoning", CustomFieldType.Number, null, false, true, 1), default);
        await retype.Should().ThrowAsync<BusinessRuleException>();
        var rekey = () => admin.SaveCustomField.Handle(new SaveCustomFieldCommand(field.Id, "zone", "Zoning", CustomFieldType.Text, null, false, true, 1), default);
        await rekey.Should().ThrowAsync<BusinessRuleException>();

        var relabelled = await admin.SaveCustomField.Handle(new SaveCustomFieldCommand(field.Id, "zoning", "Zoning district", CustomFieldType.Text, null, true, true, 5), default);
        relabelled.Should().Match<CustomFieldDto>(f => f.Label == "Zoning district" && f.IsRequired && f.SortOrder == 5);
    }

    [Fact]
    public void ROW_011_Custom_field_definitions_are_validated()
    {
        var v = new SaveCustomFieldCommandValidator();
        SaveCustomFieldCommand Cmd(string key = "ok_key", CustomFieldType t = CustomFieldType.Text, string[]? options = null, string label = "L") =>
            new(null, key, label, t, options, false, true, 0);

        v.Validate(Cmd()).IsValid.Should().BeTrue();
        foreach (var bad in new[] { "Upper", "1abc", "has space", "", new string('a', 41), "dash-ed" })
            v.Validate(Cmd(key: bad)).IsValid.Should().BeFalse($"key '{bad}'");
        v.Validate(Cmd(label: " ")).IsValid.Should().BeFalse();
        v.Validate(Cmd(t: CustomFieldType.List)).IsValid.Should().BeFalse("a list needs options");
        v.Validate(Cmd(t: CustomFieldType.List, options: new[] { "A", "a" })).IsValid.Should().BeFalse("duplicate options");
        v.Validate(Cmd(t: CustomFieldType.List, options: new[] { "A", "" })).IsValid.Should().BeFalse("blank option");
        v.Validate(Cmd(t: CustomFieldType.List, options: new[] { "A", "B" })).IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ROW_073_Saving_a_seeded_deadline_rule_confirms_it_and_the_rule_data_drives_the_clock()
    {
        var store = await Provision();
        await using var admin = new Ctx(store, TestDb.TenantA, TestDb.UserA, RoleNames.Admin);
        var rules = await admin.Rules.Handle(new ListDeadlineRulesQuery(), default);
        rules.Should().OnlyContain(r => r.IsPlaceholder);
        var offer = rules.Single(r => r.TriggerMilestone == MilestoneNames.NoticeToOwner);

        var confirmed = await admin.SaveRule.Handle(new SaveDeadlineRuleCommand(offer.Id, offer.Name, offer.TriggerMilestone, offer.TargetMilestone, 60, true), default);
        confirmed.IsPlaceholder.Should().BeFalse();
        confirmed.Days.Should().Be(60);

        var added = await admin.SaveRule.Handle(new SaveDeadlineRuleCommand(null, "Agreement within 30 days of offer", MilestoneNames.OfferDelivered, MilestoneNames.AgreementSigned, 30, true), default);
        added.IsPlaceholder.Should().BeFalse();
        var dup = () => admin.SaveRule.Handle(new SaveDeadlineRuleCommand(null, "agreement WITHIN 30 days of offer", "A", "B", 1, true), default);
        (await dup.Should().ThrowAsync<ConflictException>()).Which.Code.Should().Be("deadline_rule_exists");

        var project = await AddProject(store, TestDb.TenantA);
        var parcel = await admin.Create.Handle(NewParcel(project), default);
        var view = await admin.Notice.Handle(new RecordNoticeToOwnerCommand(parcel.Id, Today.AddDays(-50), NoticeMethod.Email), default);
        view.Deadlines.Single(d => d.Name == offer.Name).DueOn.Should().Be(Today.AddDays(10));
        view.Deadlines.Single(d => d.Name == offer.Name).Status.Should().Be("Pending");
    }

    [Theory]
    [InlineData("", "B", 10)]
    [InlineData("A", "", 10)]
    [InlineData("A", "a", 10)]
    [InlineData("A", "B", 0)]
    [InlineData("A", "B", 3651)]
    public void ROW_073_A_deadline_rule_needs_two_different_milestones_and_a_sane_number_of_days(string trigger, string target, int days)
    {
        new SaveDeadlineRuleCommandValidator().Validate(new SaveDeadlineRuleCommand(null, "R", trigger, target, days, true)).IsValid.Should().BeFalse();
        new SaveDeadlineRuleCommandValidator().Validate(new SaveDeadlineRuleCommand(null, "R", "A", "B", 3650, true)).IsValid.Should().BeTrue();
        new SaveDeadlineRuleCommandValidator().Validate(new SaveDeadlineRuleCommand(null, "R", "A", "B", 1, true)).IsValid.Should().BeTrue();
    }

    [Fact]
    public void ROW_072_Override_roles_must_be_real_roles_listed_once_and_not_the_always_allowed_ones()
    {
        var v = new SetStepGateSettingsCommandValidator();
        v.Validate(new SetStepGateSettingsCommand(new[] { RoleNames.Negotiator, RoleNames.Finance })).IsValid.Should().BeTrue();
        v.Validate(new SetStepGateSettingsCommand(Array.Empty<string>())).IsValid.Should().BeTrue();
        v.Validate(new SetStepGateSettingsCommand(new[] { "Wizard" })).IsValid.Should().BeFalse();
        v.Validate(new SetStepGateSettingsCommand(new[] { RoleNames.Admin })).IsValid.Should().BeFalse();
        v.Validate(new SetStepGateSettingsCommand(new[] { RoleNames.ProjectManager })).IsValid.Should().BeFalse();
        v.Validate(new SetStepGateSettingsCommand(new[] { RoleNames.Finance, RoleNames.Finance })).IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task ROW_072_The_override_roles_are_saved_and_listed_with_the_roles_that_can_still_be_added()
    {
        var store = await Provision();
        await using var admin = new Ctx(store, TestDb.TenantA, TestDb.UserA, RoleNames.Admin);

        var saved = await admin.SetStepGateSettings.Handle(new SetStepGateSettingsCommand(new[] { RoleNames.Negotiator }), default);

        saved.ExtraOverrideRoles.Should().Equal(RoleNames.Negotiator);
        saved.AlwaysRoles.Should().BeEquivalentTo(RoleNames.ProjectManager, RoleNames.Admin);
        saved.AssignableRoles.Should().BeEquivalentTo(RoleNames.TitleExaminer, RoleNames.Appraiser, RoleNames.Negotiator, RoleNames.Finance);
        (await admin.StepGateSettings.Handle(new GetStepGateSettingsQuery(), default)).ExtraOverrideRoles.Should().Equal(RoleNames.Negotiator);
    }

    // ── access: roles and agencies ──

    [Fact]
    public async Task Read_only_roles_see_parcels_but_cannot_create_change_split_combine_or_move_them()
    {
        var store = await Provision();
        var project = await AddProject(store, TestDb.TenantA);
        await using var pm = await Pm(store);
        var parcel = await pm.Create.Handle(NewParcel(project), default);
        var other = await pm.Create.Handle(NewParcel(project), default);

        foreach (var role in new[] { RoleNames.TitleExaminer, RoleNames.Appraiser, RoleNames.Negotiator, RoleNames.Finance })
        {
            await using var c = new Ctx(store, TestDb.TenantA, TestDb.UserA2, role);
            (await c.Get.Handle(new GetParcelQuery(parcel.Id), default)).Id.Should().Be(parcel.Id);
            (await c.List.Handle(new ListParcelsQuery(project, null, null, null, null), default)).Total.Should().Be(2);
            (await c.Rollup.Handle(new GetParcelRollupQuery(project, null), default)).TotalCount.Should().Be(2);

            var attempts = new Func<Task>[]
            {
                () => c.Create.Handle(NewParcel(project), default),
                () => c.Update.Handle(Edit(parcel, area: 1m), default),
                () => c.Split.Handle(new SplitParcelCommand(parcel.Id, new[] { new SplitPart(1m) }), default),
                () => c.Combine.Handle(new CombineParcelsCommand(new[] { parcel.Id, other.Id }, null), default),
                () => c.Batch.Handle(new BatchUpdateParcelsCommand(new[] { parcel.Id }, null, null, true), default),
                () => c.Advance.Handle(new AdvanceParcelStepCommand(parcel.Id, "title_survey", null), default),
                () => c.SaveInterest.Handle(new SaveParcelInterestCommand(parcel.Id, null, InterestType.FeeTaking, 1m, null), default),
                () => c.AddMilestone.Handle(new AddParcelMilestoneCommand(parcel.Id, "X", null, null, null), default),
                () => c.AddVisit.Handle(new AddParcelSiteVisitCommand(parcel.Id, Today, null, false), default),
                () => c.Notice.Handle(new RecordNoticeToOwnerCommand(parcel.Id, Today, NoticeMethod.Email), default),
                () => c.SaveGroup.Handle(new SaveParcelGroupCommand(project, null, "G", null), default),
                () => c.SaveTract.Handle(new SaveParentTractCommand(project, null, "T", null, null), default),
            };
            foreach (var attempt in attempts) await attempt.Should().ThrowAsync<ForbiddenException>($"{role} must not change parcels");
        }
    }

    [Fact]
    public async Task Only_roles_with_delete_rights_remove_interests_milestones_visits_and_groups()
    {
        var store = await Provision();
        var project = await AddProject(store, TestDb.TenantA);
        await using var pm = await Pm(store);
        var parcel = await pm.Create.Handle(NewParcel(project), default);
        var withInterest = await pm.SaveInterest.Handle(new SaveParcelInterestCommand(parcel.Id, null, InterestType.FeeTaking, 1m, null), default);
        var group = await pm.SaveGroup.Handle(new SaveParcelGroupCommand(project, null, "G", null), default);
        await using (var db = TestDb.Create(store, tenant: null))
        {
            var row = await db.RolePagePermissions.IgnoreQueryFilters().Include(p => p.Role)
                .SingleAsync(p => p.TenantId == TestDb.TenantA && p.Role!.Name == RoleNames.Negotiator && p.PageKey == PageKeys.Parcels);
            row.CanCreate = row.CanUpdate = true; // Standard: create, read, update, but no delete
            await db.SaveChangesAsync();
        }
        await using var standard = new Ctx(store, TestDb.TenantA, TestDb.UserA2, RoleNames.Negotiator);

        await standard.AddVisit.Handle(new AddParcelSiteVisitCommand(parcel.Id, Today, null, false), default);
        await ((Func<Task>)(() => standard.DeleteInterest.Handle(new DeleteParcelInterestCommand(parcel.Id, withInterest.Interests[0].Id), default))).Should().ThrowAsync<ForbiddenException>();
        await ((Func<Task>)(() => standard.DeleteGroup.Handle(new DeleteParcelGroupCommand(project, group.Id), default))).Should().ThrowAsync<ForbiddenException>();
        await ((Func<Task>)(() => standard.DeleteMilestone.Handle(new DeleteParcelMilestoneCommand(parcel.Id, Guid.NewGuid()), default))).Should().ThrowAsync<ForbiddenException>();
        await ((Func<Task>)(() => standard.DeleteVisit.Handle(new DeleteParcelSiteVisitCommand(parcel.Id, Guid.NewGuid()), default))).Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task One_agency_never_sees_or_changes_anothers_parcels_groups_or_rules()
    {
        var store = await Provision();
        var project = await AddProject(store, TestDb.TenantA);
        await using var a = await Pm(store);
        var parcel = await a.Create.Handle(NewParcel(project), default);
        var group = await a.SaveGroup.Handle(new SaveParcelGroupCommand(project, null, "G", null), default);
        await using var b = new Ctx(store, TestDb.TenantB, TestDb.UserB, RoleNames.ProjectManager);

        await ((Func<Task>)(() => b.Get.Handle(new GetParcelQuery(parcel.Id), default))).Should().ThrowAsync<NotFoundException>();
        await ((Func<Task>)(() => b.Update.Handle(Edit(parcel, area: 1m), default))).Should().ThrowAsync<NotFoundException>();
        await ((Func<Task>)(() => b.Advance.Handle(new AdvanceParcelStepCommand(parcel.Id, "title_survey", null), default))).Should().ThrowAsync<NotFoundException>();
        await ((Func<Task>)(() => b.Groups.Handle(new ListParcelGroupsQuery(project), default))).Should().ThrowAsync<NotFoundException>();
        await ((Func<Task>)(() => b.Create.Handle(NewParcel(project), default))).Should().ThrowAsync<NotFoundException>();
        await ((Func<Task>)(() => b.DeleteGroup.Handle(new DeleteParcelGroupCommand(project, group.Id), default))).Should().ThrowAsync<NotFoundException>();
        (await b.List.Handle(new ListParcelsQuery(null, null, null, null, null), default)).Total.Should().Be(0);
        (await b.Statuses.Handle(new ListParcelStatusesQuery(), default)).Should().OnlyContain(s => true).And.HaveCount(DefaultParcelRules.Statuses.Count);
    }

    // ── hostile input reaches the validation pipeline (nested null characters) ──

    [Fact]
    public async Task A_null_character_anywhere_in_a_request_is_rejected_including_inside_custom_values_and_lists()
    {
        var validators = new IValidator<CreateParcelCommand>[] { new CreateParcelCommandValidator() };
        var pipeline = new ValidationBehavior<CreateParcelCommand, ParcelDto>(validators);
        Task<ParcelDto> Run(CreateParcelCommand cmd) => pipeline.Handle(cmd, () => Task.FromResult<ParcelDto>(null!), default);

        var topLevel = NewParcel(Guid.NewGuid()) with { Address = "12 Main\0St" };
        var inValue = NewParcel(Guid.NewGuid(), custom: new() { ["zoning"] = "R\02" });
        var inKey = NewParcel(Guid.NewGuid(), custom: new() { ["zon\0ing"] = "R2" });

        foreach (var cmd in new[] { topLevel, inValue, inKey })
            (await ((Func<Task>)(() => Run(cmd))).Should().ThrowAsync<ValidationException>()).Which.Errors.Should().Contain(e => e.ErrorMessage.Contains("null characters"));

        var tractValidators = new IValidator<SaveParentTractCommand>[] { new SaveParentTractCommandValidator() };
        var tract = new ValidationBehavior<SaveParentTractCommand, ParentTractDto>(tractValidators);
        var inList = new SaveParentTractCommand(Guid.NewGuid(), null, "T-1", null, new[] { "APN 1", "APN\02" });
        (await ((Func<Task>)(() => tract.Handle(inList, () => Task.FromResult<ParentTractDto>(null!), default))).Should().ThrowAsync<ValidationException>())
            .Which.Errors.Should().Contain(e => e.PropertyName.Contains("TaxParcelNumbers[1]"));
    }
}
