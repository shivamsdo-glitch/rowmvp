using Aurigo.RowManagement.Application.Auth;
using Aurigo.RowManagement.Application.Projects;
using Aurigo.RowManagement.Domain.Common;
using Aurigo.RowManagement.Domain.Enums;
using Aurigo.RowManagement.Domain.Errors;
using Aurigo.RowManagement.Infrastructure.Persistence;
using Aurigo.RowManagement.Infrastructure.Persistence.Seed;
using FluentAssertions;
using FluentValidation;
using MediatR;

namespace Aurigo.RowManagement.UnitTests.Projects;

public class ProjectTests
{
    private sealed class FixedClock : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public FixedClock(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
    }

    private static readonly DateTimeOffset Now = new(2026, 3, 15, 12, 0, 0, TimeSpan.Zero);

    /// <summary>One caller in one tenant, wired with the real handlers over the shared in-memory store.</summary>
    private sealed class Ctx : IAsyncDisposable
    {
        public RowDbContext Db { get; }
        private readonly PagePermissionService _perms;
        private readonly ProjectNumberGenerator _numbers;
        private readonly FakeTenant _tenant;

        public Ctx(string store, Guid tenant, Guid user, string role, DateTimeOffset? now = null)
        {
            Db = TestDb.Create(store, tenant, user);
            _tenant = new FakeTenant(tenant);
            _perms = new PagePermissionService(Db, new FakeUser(user, role));
            _numbers = new ProjectNumberGenerator(Db, new FixedClock(now ?? Now));
        }

        public CreateProjectCommandHandler Create => new(Db, _perms, _numbers, _tenant);
        public UpdateProjectCommandHandler Update => new(Db, _perms);
        public GetProjectQueryHandler Get => new(Db, _perms);
        public ListProjectsQueryHandler List => new(Db, _perms);
        public RecordNepaDecisionCommandHandler Nepa => new(Db, _perms);
        public SetAcquisitionExceptionCommandHandler Exception => new(Db, _perms);
        public AddCostEstimateCommandHandler AddEstimate => new(Db, _perms);
        public ListCostEstimatesQueryHandler ListEstimates => new(Db, _perms);
        public SetProjectBudgetCommandHandler SetBudget => new(Db, _perms);
        public ListProjectBudgetsQueryHandler ListBudgets => new(Db, _perms);
        public AddRowAuthorizationCommandHandler AddAuth => new(Db, _perms);
        public GetAcquisitionGateQueryHandler Gate => new(Db, _perms);
        public SetProjectNumberSettingsCommandHandler SetNumberSettings => new(Db, _numbers, _perms);
        public GetProjectNumberSettingsQueryHandler GetNumberSettings => new(_numbers, _perms);
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

    private static CreateProjectCommand NewProject(string? number = null, string name = "US-95 Widening",
        OversightType oversight = OversightType.Federal, string? region = "North", string? district = "D1") =>
        new(number, name, null, region, district, oversight, "FHWA STBG", 5_000_000m, 4_500_000m, null, null, null, null);

    // ── numbering (ROW-001) ──

    [Fact]
    public async Task ROW_001_A_blank_number_is_generated_from_the_default_format_and_counts_up()
    {
        var store = await Provision();
        await using var c = new Ctx(store, TestDb.TenantA, TestDb.UserA, RoleNames.ProjectManager);

        var first = await c.Create.Handle(NewProject(), default);
        var second = await c.Create.Handle(NewProject(name: "Second"), default);

        first.Number.Should().Be("2026-0001");
        second.Number.Should().Be("2026-0002");
    }

    [Fact]
    public async Task ROW_001_A_typed_number_is_kept_and_generation_skips_over_it()
    {
        var store = await Provision();
        await using var c = new Ctx(store, TestDb.TenantA, TestDb.UserA, RoleNames.ProjectManager);

        var typed = await c.Create.Handle(NewProject("2026-0001"), default);
        var generated = await c.Create.Handle(NewProject(name: "Next"), default);

        typed.Number.Should().Be("2026-0001");
        generated.Number.Should().Be("2026-0002", "0001 was taken by hand");
    }

    [Fact]
    public async Task ROW_001_A_duplicate_number_is_a_conflict_ignoring_case_but_the_same_number_is_fine_in_another_tenant()
    {
        var store = await Provision();
        await using var a = new Ctx(store, TestDb.TenantA, TestDb.UserA, RoleNames.ProjectManager);
        await using var b = new Ctx(store, TestDb.TenantB, TestDb.UserB, RoleNames.ProjectManager);
        await a.Create.Handle(NewProject("EXT-100"), default);

        var dup = () => a.Create.Handle(NewProject("ext-100"), default);
        (await dup.Should().ThrowAsync<ConflictException>()).Which.Code.Should().Be("project_number_exists");

        var other = () => b.Create.Handle(NewProject("EXT-100"), default);
        await other.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ROW_001_The_tenant_can_change_the_format_and_the_fiscal_year_start()
    {
        var store = await Provision();
        await using var admin = new Ctx(store, TestDb.TenantA, TestDb.UserA, RoleNames.Admin);

        var saved = await admin.SetNumberSettings.Handle(new SetProjectNumberSettingsCommand("DOT-{YY}-{SEQ:3}", 10), default);
        var created = await admin.Create.Handle(NewProject(), default);

        saved.Example.Should().Be("DOT-26-001", "March 2026 is before the October start, so it is still FY2026");
        created.Number.Should().Be("DOT-26-001");
    }

    [Fact]
    public async Task ROW_001_Only_administrators_can_change_the_format()
    {
        var store = await Provision();
        await using var pm = new Ctx(store, TestDb.TenantA, TestDb.UserA, RoleNames.ProjectManager);

        var act = () => pm.SetNumberSettings.Handle(new SetProjectNumberSettingsCommand("{FY}-{SEQ:4}", 1), default);

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Theory]
    [InlineData("{FY}-")]
    [InlineData("{SEQ:4}{SEQ:4}")]
    public void ROW_001_The_format_validator_rejects_bad_patterns(string pattern)
    {
        new SetProjectNumberSettingsCommandValidator().Validate(new SetProjectNumberSettingsCommand(pattern, 1))
            .IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task ROW_001_Editing_a_number_by_hand_must_stay_unique()
    {
        var store = await Provision();
        await using var c = new Ctx(store, TestDb.TenantA, TestDb.UserA, RoleNames.ProjectManager);
        var one = await c.Create.Handle(NewProject("A-1"), default);
        await c.Create.Handle(NewProject("A-2"), default);

        UpdateProjectCommand Edit(string number) => new(
            one.Id, number, one.Name, null, "North", "D1", OversightType.Federal, null, null, null, null, null, null, null);

        var clash = () => c.Update.Handle(Edit("a-2"), default);
        await clash.Should().ThrowAsync<ConflictException>();

        var ok = await c.Update.Handle(Edit("A-9"), default);
        ok.Number.Should().Be("A-9");
        (await c.Update.Handle(Edit(""), default)).Number.Should().Be("A-9", "a blank number keeps the current one");
    }

    // ── project record (ROW-003, 004) ──

    [Fact]
    public async Task ROW_003_Projects_can_be_filtered_by_region_district_and_text()
    {
        var store = await Provision();
        await using var c = new Ctx(store, TestDb.TenantA, TestDb.UserA, RoleNames.ProjectManager);
        await c.Create.Handle(NewProject("P-1", "Main St Bridge", region: "North", district: "D1"), default);
        await c.Create.Handle(NewProject("P-2", "Harbor Rd", region: "South", district: "D2"), default);
        await c.Create.Handle(NewProject("P-3", "Main St Overpass", region: "South", district: "D3"), default);

        var south = await c.List.Handle(new ListProjectsQuery(null, "South", null, null), default);
        var main = await c.List.Handle(new ListProjectsQuery("main", null, null, null), default);
        var d3 = await c.List.Handle(new ListProjectsQuery(null, "South", "D3", null), default);

        south.Items.Select(i => i.Number).Should().Equal("P-2", "P-3");
        main.Items.Select(i => i.Number).Should().Equal("P-1", "P-3");
        d3.Items.Should().ContainSingle().Which.Number.Should().Be("P-3");
        south.Total.Should().Be(2);
    }

    [Fact]
    public void ROW_004_Construction_end_before_start_is_rejected()
    {
        var cmd = NewProject() with { ConstructionStartDate = new DateOnly(2027, 5, 1), ConstructionEndDate = new DateOnly(2027, 4, 30) };
        new CreateProjectCommandValidator().Validate(cmd).IsValid.Should().BeFalse();
        (cmd with { ConstructionEndDate = new DateOnly(2027, 5, 1) }).Should().Match<CreateProjectCommand>(c =>
            new CreateProjectCommandValidator().Validate(c).IsValid);
    }

    [Fact]
    public void ROW_004_Name_is_required_and_amounts_cannot_be_negative()
    {
        var v = new CreateProjectCommandValidator();
        v.Validate(NewProject(name: " ")).IsValid.Should().BeFalse();
        v.Validate(NewProject() with { FundingAmount = -1m }).IsValid.Should().BeFalse();
        v.Validate(NewProject() with { DesignEstimate = -0.01m }).IsValid.Should().BeFalse();
    }

    // ── tenancy and permissions ──

    [Fact]
    public async Task Projects_are_invisible_to_other_tenants()
    {
        var store = await Provision();
        await using var a = new Ctx(store, TestDb.TenantA, TestDb.UserA, RoleNames.ProjectManager);
        await using var b = new Ctx(store, TestDb.TenantB, TestDb.UserB, RoleNames.ProjectManager);
        var created = await a.Create.Handle(NewProject("SECRET-1"), default);

        (await b.List.Handle(new ListProjectsQuery(null, null, null, null), default)).Items.Should().BeEmpty();
        var get = () => b.Get.Handle(new GetProjectQuery(created.Id), default);
        await get.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Finance_can_read_projects_but_not_create_or_edit_them()
    {
        var store = await Provision();
        await using var pm = new Ctx(store, TestDb.TenantA, TestDb.UserA, RoleNames.ProjectManager);
        await using var fin = new Ctx(store, TestDb.TenantA, TestDb.UserA2, RoleNames.Finance);
        var p = await pm.Create.Handle(NewProject(), default);

        (await fin.Get.Handle(new GetProjectQuery(p.Id), default)).Id.Should().Be(p.Id);
        var create = () => fin.Create.Handle(NewProject("F-1"), default);
        await create.Should().ThrowAsync<ForbiddenException>();
        var estimate = () => fin.AddEstimate.Handle(new AddCostEstimateCommand(p.Id, 1, 0, 0, 0, null), default);
        await estimate.Should().ThrowAsync<ForbiddenException>();
    }

    // ── versioned estimates (ROW-012) ──

    [Fact]
    public async Task ROW_012_Each_estimate_is_a_new_version_and_earlier_ones_are_kept()
    {
        var store = await Provision();
        await using var c = new Ctx(store, TestDb.TenantA, TestDb.UserA, RoleNames.ProjectManager);
        var p = await c.Create.Handle(NewProject(), default);

        var v1 = await c.AddEstimate.Handle(new AddCostEstimateCommand(p.Id, 1_000m, 200m, 300m, 50m, "first"), default);
        var v2 = await c.AddEstimate.Handle(new AddCostEstimateCommand(p.Id, 1_500m, 200m, 300m, 75m, "revised"), default);
        var all = await c.ListEstimates.Handle(new ListCostEstimatesQuery(p.Id), default);

        v1.Version.Should().Be(1);
        v1.Total.Should().Be(1_550m);
        v2.Version.Should().Be(2);
        v2.Total.Should().Be(2_075m);
        all.Select(e => e.Version).Should().Equal(2, 1);
        all.Single(e => e.Version == 1).Total.Should().Be(1_550m, "the original is untouched");
        (await c.Get.Handle(new GetProjectQuery(p.Id), default)).CurrentEstimate.Should().Be(2_075m);
    }

    [Fact]
    public void ROW_012_Negative_estimate_lines_are_rejected()
    {
        new AddCostEstimateCommandValidator()
            .Validate(new AddCostEstimateCommand(Guid.NewGuid(), -1m, 0, 0, 0, null)).IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task ROW_012_An_estimate_for_a_missing_project_is_not_found()
    {
        var store = await Provision();
        await using var c = new Ctx(store, TestDb.TenantA, TestDb.UserA, RoleNames.ProjectManager);

        var act = () => c.AddEstimate.Handle(new AddCostEstimateCommand(Guid.NewGuid(), 1, 0, 0, 0, null), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    // ── annual budget (ROW-013) ──

    [Fact]
    public async Task ROW_013_A_fiscal_year_has_one_budget_and_setting_it_again_adjusts_it()
    {
        var store = await Provision();
        await using var c = new Ctx(store, TestDb.TenantA, TestDb.UserA, RoleNames.ProjectManager);
        var p = await c.Create.Handle(NewProject(), default);

        await c.SetBudget.Handle(new SetProjectBudgetCommand(p.Id, 2027, 1_000_000m, null), default);
        await c.SetBudget.Handle(new SetProjectBudgetCommand(p.Id, 2026, 400_000m, null), default);
        await c.SetBudget.Handle(new SetProjectBudgetCommand(p.Id, 2027, 1_250_000m, "adjusted"), default);
        var budgets = await c.ListBudgets.Handle(new ListProjectBudgetsQuery(p.Id), default);

        budgets.Select(b => (b.FiscalYear, b.Amount)).Should().Equal((2026, 400_000m), (2027, 1_250_000m));
    }

    // ── authorizations (ROW-238) ──

    [Fact]
    public async Task ROW_238_Federal_share_is_derived_from_the_authorized_amount()
    {
        var store = await Provision();
        await using var c = new Ctx(store, TestDb.TenantA, TestDb.UserA, RoleNames.ProjectManager);
        var p = await c.Create.Handle(NewProject(), default);

        var auth = await c.AddAuth.Handle(new AddRowAuthorizationCommand(
            p.Id, AuthorizationType.Acquisition, 1_000_000m, 80m, new DateOnly(2026, 2, 1), "FMIS-1", true, null), default);

        auth.FederalShareAmount.Should().Be(800_000m);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(100.01)]
    public void ROW_238_Federal_share_must_be_a_percentage(double pct)
    {
        new AddRowAuthorizationCommandValidator().Validate(new AddRowAuthorizationCommand(
            Guid.NewGuid(), AuthorizationType.Acquisition, 1m, (decimal)pct, new DateOnly(2026, 1, 1), null, false, null))
            .IsValid.Should().BeFalse();
    }

    // ── acquisition gate (ROW-076, 238) ──

    [Fact]
    public async Task ROW_076_A_federal_project_stays_locked_until_nepa_and_an_acquisition_authorization_are_on_file()
    {
        var store = await Provision();
        await using var c = new Ctx(store, TestDb.TenantA, TestDb.UserA, RoleNames.ProjectManager);
        var p = await c.Create.Handle(NewProject(), default);
        (await c.Gate.Handle(new GetAcquisitionGateQuery(p.Id), default)).Unlocked.Should().BeFalse();

        await c.Nepa.Handle(new RecordNepaDecisionCommand(p.Id, NepaDecisionType.CategoricalExclusion, new DateOnly(2026, 1, 10)), default);
        (await c.Gate.Handle(new GetAcquisitionGateQuery(p.Id), default)).Missing.Should().Equal("Acquisition authorization");

        // A preliminary authorization does not unlock negotiations.
        await c.AddAuth.Handle(new AddRowAuthorizationCommand(
            p.Id, AuthorizationType.Preliminary, 100m, null, new DateOnly(2026, 1, 15), null, false, null), default);
        (await c.Gate.Handle(new GetAcquisitionGateQuery(p.Id), default)).Unlocked.Should().BeFalse();

        await c.AddAuth.Handle(new AddRowAuthorizationCommand(
            p.Id, AuthorizationType.Acquisition, 900m, 80m, new DateOnly(2026, 2, 1), null, true, null), default);
        var gate = await c.Gate.Handle(new GetAcquisitionGateQuery(p.Id), default);
        gate.Unlocked.Should().BeTrue();
        gate.Reason.Should().Be("ClearanceComplete");
    }

    [Fact]
    public async Task ROW_076_Local_funds_are_never_held()
    {
        var store = await Provision();
        await using var c = new Ctx(store, TestDb.TenantA, TestDb.UserA, RoleNames.ProjectManager);
        var p = await c.Create.Handle(NewProject(oversight: OversightType.Local), default);

        (await c.Gate.Handle(new GetAcquisitionGateQuery(p.Id), default)).Unlocked.Should().BeTrue();
        p.AcquisitionGate.Reason.Should().Be("UngatedFunding");
    }

    [Fact]
    public async Task ROW_076_An_approved_exception_unlocks_and_withdrawing_it_locks_again()
    {
        var store = await Provision();
        await using var c = new Ctx(store, TestDb.TenantA, TestDb.UserA, RoleNames.ProjectManager);
        var p = await c.Create.Handle(NewProject(), default);

        var unlocked = await c.Exception.Handle(new SetAcquisitionExceptionCommand(
            p.Id, AcquisitionExceptionType.Hardship, new DateOnly(2026, 3, 1), "Owner medical hardship"), default);
        unlocked.AcquisitionGate.Unlocked.Should().BeTrue();

        var locked = await c.Exception.Handle(new SetAcquisitionExceptionCommand(p.Id, null, null, null), default);
        locked.AcquisitionGate.Unlocked.Should().BeFalse();
        locked.ExceptionReason.Should().BeNull();
    }

    [Fact]
    public void ROW_076_An_exception_needs_a_date_and_a_reason_and_a_nepa_decision_needs_a_date()
    {
        var ex = new SetAcquisitionExceptionCommandValidator();
        ex.Validate(new SetAcquisitionExceptionCommand(Guid.NewGuid(), AcquisitionExceptionType.Early, null, "board approved")).IsValid.Should().BeFalse();
        ex.Validate(new SetAcquisitionExceptionCommand(Guid.NewGuid(), AcquisitionExceptionType.Early, new DateOnly(2026, 1, 1), " ")).IsValid.Should().BeFalse();

        var nepa = new RecordNepaDecisionCommandValidator();
        nepa.Validate(new RecordNepaDecisionCommand(Guid.NewGuid(), NepaDecisionType.EnvironmentalAssessment, null)).IsValid.Should().BeFalse();
        nepa.Validate(new RecordNepaDecisionCommand(Guid.NewGuid(), null, null)).IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ROW_076_The_list_shows_each_projects_gate_state()
    {
        var store = await Provision();
        await using var c = new Ctx(store, TestDb.TenantA, TestDb.UserA, RoleNames.ProjectManager);
        await c.Create.Handle(NewProject("G-1", oversight: OversightType.Federal), default);
        await c.Create.Handle(NewProject("G-2", oversight: OversightType.Local), default);

        var list = await c.List.Handle(new ListProjectsQuery(null, null, null, null), default);

        list.Items.Single(i => i.Number == "G-1").AcquisitionGate.Unlocked.Should().BeFalse();
        list.Items.Single(i => i.Number == "G-2").AcquisitionGate.Unlocked.Should().BeTrue();
    }

    // ── import (ROW-001) ──

    private sealed class DirectSender : ISender
    {
        private readonly Ctx _ctx;
        public DirectSender(Ctx ctx) => _ctx = ctx;

        // Mimics the pipeline: validate, then handle. Enough for the commands the import sends.
        public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default)
        {
            if (request is not CreateProjectCommand create) throw new NotSupportedException();
            var result = new CreateProjectCommandValidator().Validate(create);
            if (!result.IsValid) throw new ValidationException(result.Errors);
            return (TResponse)(object)await _ctx.Create.Handle(create, ct);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken ct = default) where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken ct = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken ct = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken ct = default) => throw new NotSupportedException();
    }

    [Fact]
    public async Task ROW_001_Import_creates_good_rows_and_reports_each_bad_one_without_stopping()
    {
        var store = await Provision();
        await using var c = new Ctx(store, TestDb.TenantA, TestDb.UserA, RoleNames.ProjectManager);
        await c.Create.Handle(NewProject("EXISTING-1"), default);
        var handler = new ImportProjectsCommandHandler(new DirectSender(c), c.Db);

        ProjectImportRow Row(string? number, string name) =>
            new(number, name, null, "North", "D1", OversightType.State, null, null, null, null, null, null, null);

        var summary = await handler.Handle(new ImportProjectsCommand(new[]
        {
            Row("IMP-1", "Imported one"),
            Row("EXISTING-1", "Clashes with an existing project"),
            Row("IMP-1", "Repeats a number from this file"),
            Row(null, "Gets a generated number"),
            Row("IMP-2", " "),                       // invalid: name required
            Row("IMP-3", "Imported three"),
        }), default);

        summary.Created.Should().Be(3);
        summary.Failed.Should().Be(3);
        summary.Results.Select(r => r.Outcome).Should().Equal("Created", "Failed", "Failed", "Created", "Failed", "Created");
        summary.Results[3].Number.Should().Be("2026-0001");
        summary.Results[1].Error.Should().Contain("already exists");
        summary.Results[2].Error.Should().Contain("more than once");
        (await c.List.Handle(new ListProjectsQuery(null, null, null, null), default)).Total.Should().Be(4);
    }

    [Fact]
    public void ROW_001_Import_is_capped_per_request()
    {
        var row = new ProjectImportRow(null, "x", null, null, null, OversightType.Local, null, null, null, null, null, null, null);
        var v = new ImportProjectsCommandValidator();

        v.Validate(new ImportProjectsCommand(Array.Empty<ProjectImportRow>())).IsValid.Should().BeFalse();
        v.Validate(new ImportProjectsCommand(Enumerable.Repeat(row, ImportProjectsCommandValidator.MaxRows).ToList())).IsValid.Should().BeTrue();
        v.Validate(new ImportProjectsCommand(Enumerable.Repeat(row, ImportProjectsCommandValidator.MaxRows + 1).ToList())).IsValid.Should().BeFalse();
    }
}
