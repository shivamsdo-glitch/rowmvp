using Aurigo.RowManagement.Application.Auth;
using Aurigo.RowManagement.Application.Library;
using Aurigo.RowManagement.Application.Parcels;
using Aurigo.RowManagement.Domain.Common;
using Aurigo.RowManagement.Domain.Enums;
using Aurigo.RowManagement.Domain.Errors;
using Aurigo.RowManagement.Infrastructure.Persistence;
using Aurigo.RowManagement.Infrastructure.Persistence.Seed;
using FluentAssertions;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Aurigo.RowManagement.UnitTests.Library;

public class LibraryTests
{
    /// <summary>Hands the one request the Library delegates (a parcel status change) to the real handler.</summary>
    private sealed class DirectSender : ISender
    {
        private readonly SaveParcelStatusCommandHandler _status;
        public DirectSender(SaveParcelStatusCommandHandler status) => _status = status;

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            request is SaveParcelStatusCommand c ? (Task<TResponse>)(object)_status.Handle(c, cancellationToken) : throw new NotSupportedException();
        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class Ctx : IAsyncDisposable
    {
        public RowDbContext Db { get; }
        private readonly PagePermissionService _perms;
        private readonly DirectSender _sender;

        public Ctx(string store, Guid tenant, string role)
        {
            Db = TestDb.Create(store, tenant, TestDb.UserA);
            _perms = new PagePermissionService(Db, new FakeUser(TestDb.UserA, role));
            _sender = new DirectSender(new SaveParcelStatusCommandHandler(Db, _perms));
        }

        public ListLibraryFieldsQueryHandler List => new(Db, _perms);
        public GetLibraryFieldQueryHandler Get => new(Db, _perms);
        public CreateLibraryFieldCommandHandler Create => new(Db, _perms);
        public UpdateLibraryFieldCommandHandler Update => new(Db, _perms);
        public AddLibraryValueCommandHandler Add => new(Db, _perms, _sender);
        public UpdateLibraryValueCommandHandler UpdateValue => new(Db, _perms, _sender);
        public ImportLibraryValuesCommandHandler Import => new(Db, _perms, _sender);

        public async Task<LibraryFieldDto> Field(string key)
        {
            var summary = (await List.Handle(new ListLibraryFieldsQuery(), default)).Single(f => f.Key == key);
            return await Get.Handle(new GetLibraryFieldQuery(summary.Id), default);
        }

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

    private static Ctx Admin(string store, Guid? tenant = null) => new(store, tenant ?? TestDb.TenantA, RoleNames.Admin);

    private static async Task<T> Thrown<T>(Func<Task> act) where T : Exception => (await act.Should().ThrowAsync<T>()).Which;

    // ── defaults ──

    [Fact]
    public async Task Library_An_agency_starts_with_the_default_fields_and_values()
    {
        var store = await Provision();
        await using var c = Admin(store);

        var fields = await c.List.Handle(new ListLibraryFieldsQuery(), default);

        fields.Select(f => f.Key).Should().Equal(DefaultLibrary.Fields.Select(f => f.Key));
        fields.Should().OnlyContain(f => f.IsSystem);
        (await c.Field(LibraryKeys.PossessionType)).Values.Select(v => v.Value).Should().Equal("PUA", "Closing", "Notice of Deposit", "Judgement");
        (await c.Field(LibraryKeys.InterestType)).Values.Select(v => v.Value).Should().Contain(new[] { "Partial Fee", "Full Fee", "Permanent Easement", "Sidewalk Easement" }).And.HaveCount(10);
        (await c.Field(LibraryKeys.CadNumber)).FieldType.Should().Be(CustomFieldType.Text);
        (await c.Field(LibraryKeys.CadNumber)).Values.Should().BeEmpty();
    }

    [Fact]
    public async Task Library_Every_default_list_that_forms_offer_starts_with_values_so_no_dropdown_opens_empty()
    {
        var store = await Provision();
        await using var c = Admin(store);

        foreach (var d in DefaultLibrary.Fields.Where(f => f.Type == CustomFieldType.List && f.Key != LibraryKeys.ParcelStatus))
        {
            d.Values.Should().NotBeEmpty($"{d.Key} is a dropdown");
            (await c.Field(d.Key)).Values.Select(v => v.Value).Should().Equal(d.Values);
        }
    }

    [Fact]
    public async Task Library_A_default_list_created_empty_by_an_earlier_version_gets_its_starter_values_but_an_edited_one_is_left_alone()
    {
        var store = await Provision();
        await using var c = Admin(store);
        var empty = await c.Db.LibraryFields.IgnoreQueryFilters().Include(f => f.Values).SingleAsync(f => f.TenantId == TestDb.TenantA && f.Key == "cure_status");
        c.Db.LibraryValues.RemoveRange(empty.Values);
        var edited = await c.Field(LibraryKeys.PossessionType);
        await c.UpdateValue.Handle(new UpdateLibraryValueCommand(edited.Id, edited.Values[0].Id, "PUA", false), default); // switched off, not deleted
        await c.Db.SaveChangesAsync();

        await using var db = TestDb.Create(store, null);
        await new TenantProvisioner(db).EnsureDefaultRolesAsync(TestDb.TenantA);

        (await c.Field("cure_status")).Values.Select(v => v.Value).Should().Equal(DefaultLibrary.Fields.Single(f => f.Key == "cure_status").Values);
        (await c.Field(LibraryKeys.PossessionType)).Values[0].IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Library_The_parcel_status_default_list_is_the_fifteen_values_with_no_colours_and_Inactive_first()
    {
        var store = await Provision();
        await using var c = Admin(store);

        var status = await c.Field(LibraryKeys.ParcelStatus);

        status.Values.Select(v => v.Value).Should().Equal(
            "Inactive", "In Design", "Pre-Acquisition", "Appraisal", "Initial Offer", "Final Offer", "In Possession - PUA", "In Possession - CLOSED", "Acquired",
            "In Possession - NOD", "Pending Condemnation", "Petition Filed", "Hearing Scheduled", "Objections Filed", "Deleted",
            "Disposition In Progress", "Disposition Complete");
        status.Values.Single(v => v.IsDefault).Value.Should().Be("Inactive");
        (await c.Db.ParcelStatuses.Select(s => s.Color).Distinct().ToListAsync()).Should().ContainSingle().Which.Should().Be(DefaultParcelRules.NeutralColor);
    }

    [Fact]
    public async Task Library_Provisioning_again_adds_what_is_missing_and_never_touches_an_edited_library()
    {
        var store = await Provision();
        await using var c = Admin(store);
        var possession = await c.Field(LibraryKeys.PossessionType);
        await c.UpdateValue.Handle(new UpdateLibraryValueCommand(possession.Id, possession.Values[0].Id, "PUA (early)", false), default);
        await c.Add.Handle(new AddLibraryValueCommand(possession.Id, "Easement recorded"), default);
        var field = await c.Db.LibraryFields.IgnoreQueryFilters().SingleAsync(f => f.TenantId == TestDb.TenantA && f.Key == LibraryKeys.Segment);
        c.Db.LibraryFields.Remove(field);
        await c.Db.SaveChangesAsync();

        await using var db = TestDb.Create(store, null);
        await new TenantProvisioner(db).EnsureDefaultRolesAsync(TestDb.TenantA);

        var after = await c.Field(LibraryKeys.PossessionType);
        after.Values.Select(v => v.Value).Should().Equal("PUA (early)", "Closing", "Notice of Deposit", "Judgement", "Easement recorded");
        after.Values[0].IsActive.Should().BeFalse();
        (await c.List.Handle(new ListLibraryFieldsQuery(), default)).Should().Contain(f => f.Key == LibraryKeys.Segment, "a missing default is added back");
    }

    // ── values ──

    [Fact]
    public async Task Library_A_value_can_be_added_renamed_and_switched_off_and_on()
    {
        var store = await Provision();
        await using var c = Admin(store);
        var field = await c.Field(LibraryKeys.AgentType);

        var added = await c.Add.Handle(new AddLibraryValueCommand(field.Id, "  Wizard  "), default);
        var surveyor = added.Values.Single(v => v.Value == "Wizard");
        added.Values.Should().HaveCount(DefaultLibrary.Fields.Single(f => f.Key == LibraryKeys.AgentType).Values.Count + 1, "the defaults and the new one");

        var renamed = await c.UpdateValue.Handle(new UpdateLibraryValueCommand(field.Id, surveyor.Id, "Land surveyor", true), default);
        renamed.Values.Select(v => v.Value).Should().Contain("Appraiser").And.Contain("Land surveyor").And.NotContain("Wizard");
        var off = await c.UpdateValue.Handle(new UpdateLibraryValueCommand(field.Id, surveyor.Id, "Land surveyor", false), default);
        off.Values.Single(v => v.Id == surveyor.Id).IsActive.Should().BeFalse();
        var defaults = DefaultLibrary.Fields.Single(f => f.Key == LibraryKeys.AgentType).Values.Count;
        (await c.List.Handle(new ListLibraryFieldsQuery(), default)).Single(f => f.Key == LibraryKeys.AgentType).Should().Match<LibraryFieldSummaryDto>(f => f.ValueCount == defaults + 1 && f.ActiveCount == defaults);
    }

    [Fact]
    public async Task Library_No_two_values_of_a_field_can_be_alike_ignoring_case_but_another_field_may_repeat_one()
    {
        var store = await Provision();
        await using var c = Admin(store);
        var possession = await c.Field(LibraryKeys.PossessionType);
        var agent = await c.Field(LibraryKeys.AgentType);

        (await Thrown<ConflictException>(() => c.Add.Handle(new AddLibraryValueCommand(possession.Id, "closing"), default))).Code.Should().Be("library_value_exists");
        (await Thrown<ConflictException>(() => c.UpdateValue.Handle(new UpdateLibraryValueCommand(possession.Id, possession.Values[0].Id, " CLOSING ", true), default))).Code.Should().Be("library_value_exists");
        await c.Add.Handle(new AddLibraryValueCommand(agent.Id, "Closing"), default);
    }

    [Fact]
    public async Task Library_Values_belong_only_to_lists_and_only_to_their_own_field()
    {
        var store = await Provision();
        await using var c = Admin(store);
        var text = await c.Field(LibraryKeys.CadNumber);
        var possession = await c.Field(LibraryKeys.PossessionType);
        var agent = await c.Field(LibraryKeys.AgentType);

        (await Thrown<InvalidRequestException>(() => c.Add.Handle(new AddLibraryValueCommand(text.Id, "x"), default))).Code.Should().Be("not_a_list");
        (await Thrown<InvalidRequestException>(() => c.Import.Handle(new ImportLibraryValuesCommand(text.Id, new[] { "x" }), default))).Code.Should().Be("not_a_list");
        await Thrown<NotFoundException>(() => c.UpdateValue.Handle(new UpdateLibraryValueCommand(agent.Id, possession.Values[0].Id, "Hijack", true), default));
        await Thrown<NotFoundException>(() => c.Add.Handle(new AddLibraryValueCommand(Guid.NewGuid(), "x"), default));
    }

    [Fact]
    public async Task Library_Bad_values_are_refused_at_the_boundary()
    {
        var id = Guid.NewGuid();
        new AddLibraryValueCommandValidator().Validate(new AddLibraryValueCommand(id, "  ")).IsValid.Should().BeFalse();
        new AddLibraryValueCommandValidator().Validate(new AddLibraryValueCommand(id, new string('x', 101))).IsValid.Should().BeFalse();
        new AddLibraryValueCommandValidator().Validate(new AddLibraryValueCommand(id, new string('x', 100))).IsValid.Should().BeTrue();
        new ImportLibraryValuesCommandValidator().Validate(new ImportLibraryValuesCommand(id, Array.Empty<string>())).IsValid.Should().BeFalse();
        new ImportLibraryValuesCommandValidator().Validate(new ImportLibraryValuesCommand(id, Enumerable.Repeat("v", 501).ToList())).IsValid.Should().BeFalse();
        new ImportLibraryValuesCommandValidator().Validate(new ImportLibraryValuesCommand(id, new[] { "ok", "" })).IsValid.Should().BeFalse();
        new CreateLibraryFieldCommandValidator().Validate(new CreateLibraryFieldCommand("", CustomFieldType.Text)).IsValid.Should().BeFalse();
        new CreateLibraryFieldCommandValidator().Validate(new CreateLibraryFieldCommand("Ok", (CustomFieldType)99)).IsValid.Should().BeFalse();
        await Task.CompletedTask;
    }

    // ── import ──

    [Fact]
    public async Task Library_Import_adds_new_values_and_skips_ones_already_there_or_repeated()
    {
        var store = await Provision();
        await using var c = Admin(store);
        var possession = await c.Field(LibraryKeys.PossessionType);

        var result = await c.Import.Handle(new ImportLibraryValuesCommand(possession.Id, new[] { "pua", "Deed", "deed", " Lease ", "Judgement" }), default);

        result.Added.Should().Be(2);
        result.AlreadyThere.Should().Be(3);
        result.Field.Values.Select(v => v.Value).Should().Equal("PUA", "Closing", "Notice of Deposit", "Judgement", "Deed", "Lease");
    }

    [Fact]
    public async Task Library_Importing_parcel_statuses_adds_real_statuses_with_the_neutral_colour()
    {
        var store = await Provision();
        await using var c = Admin(store);
        var status = await c.Field(LibraryKeys.ParcelStatus);

        var result = await c.Import.Handle(new ImportLibraryValuesCommand(status.Id, new[] { "Staked", "inactive", "Surveyed" }), default);

        result.Added.Should().Be(2);
        result.AlreadyThere.Should().Be(1);
        var rows = await c.Db.ParcelStatuses.Where(s => s.Name == "Staked" || s.Name == "Surveyed").ToListAsync();
        rows.Should().HaveCount(2).And.OnlyContain(s => s.Color == DefaultParcelRules.NeutralColor && s.IsActive && !s.IsInitial);
    }

    // ── parcel status is the real status list ──

    [Fact]
    public async Task Library_Parcel_status_edits_are_the_parcel_statuses_and_keep_the_status_rules()
    {
        var store = await Provision();
        await using var c = Admin(store);
        var status = await c.Field(LibraryKeys.ParcelStatus);

        var added = await c.Add.Handle(new AddLibraryValueCommand(status.Id, "Staked"), default);
        (await c.Db.ParcelStatuses.AnyAsync(s => s.Name == "Staked")).Should().BeTrue();
        added.Values.Last().Value.Should().Be("Staked");

        var inactive = added.Values.Single(v => v.IsDefault);
        (await Thrown<BusinessRuleException>(() => c.UpdateValue.Handle(new UpdateLibraryValueCommand(status.Id, inactive.Id, "Inactive", false), default)))
            .Message.Should().Contain("initial status must be active");
        (await Thrown<ConflictException>(() => c.Add.Handle(new AddLibraryValueCommand(status.Id, "in design"), default))).Code.Should().Be("status_exists");

        var renamed = await c.UpdateValue.Handle(new UpdateLibraryValueCommand(status.Id, added.Values.Last().Id, "Staked out", false), default);
        renamed.Values.Last().Should().Match<LibraryValueDto>(v => v.Value == "Staked out" && !v.IsActive);
        (await c.Db.ParcelStatuses.SingleAsync(s => s.Name == "Staked out")).IsActive.Should().BeFalse();
    }

    // ── fields ──

    [Fact]
    public async Task Library_A_new_field_gets_a_key_from_its_name_and_a_list_starts_empty()
    {
        var store = await Provision();
        await using var c = Admin(store);

        var one = await c.Create.Handle(new CreateLibraryFieldCommand("  Utility owner  ", CustomFieldType.List), default);
        var two = await c.Create.Handle(new CreateLibraryFieldCommand("Utility-Owner!", CustomFieldType.Text), default);
        var third = await c.Create.Handle(new CreateLibraryFieldCommand("42 things", CustomFieldType.Number), default);

        one.Key.Should().Be("utility_owner");
        one.IsSystem.Should().BeFalse();
        one.Values.Should().BeEmpty();
        two.Key.Should().Be("utility_owner_2");
        third.Key.Should().Be("f_42_things");
        (await Thrown<ConflictException>(() => c.Create.Handle(new CreateLibraryFieldCommand("UTILITY OWNER", CustomFieldType.List), default))).Code.Should().Be("library_field_exists");
    }

    [Fact]
    public async Task Library_A_field_can_be_renamed_and_its_type_changed_until_it_has_values_but_a_system_fields_type_is_fixed()
    {
        var store = await Provision();
        await using var c = Admin(store);
        var mine = await c.Create.Handle(new CreateLibraryFieldCommand("Zone", CustomFieldType.List), default);

        var asText = await c.Update.Handle(new UpdateLibraryFieldCommand(mine.Id, "Zoning", CustomFieldType.Text), default);
        asText.Should().Match<LibraryFieldDto>(f => f.Label == "Zoning" && f.FieldType == CustomFieldType.Text && f.Key == "zone");
        var backToList = await c.Update.Handle(new UpdateLibraryFieldCommand(mine.Id, "Zoning", CustomFieldType.List), default);
        await c.Add.Handle(new AddLibraryValueCommand(mine.Id, "R1"), default);
        (await Thrown<ConflictException>(() => c.Update.Handle(new UpdateLibraryFieldCommand(mine.Id, "Zoning", CustomFieldType.Number), default))).Code.Should().Be("library_field_has_values");
        backToList.FieldType.Should().Be(CustomFieldType.List);

        var possession = await c.Field(LibraryKeys.PossessionType);
        (await Thrown<BusinessRuleException>(() => c.Update.Handle(new UpdateLibraryFieldCommand(possession.Id, "Possession type", CustomFieldType.Text), default))).Message.Should().Contain("fixed");
        (await c.Update.Handle(new UpdateLibraryFieldCommand(possession.Id, "Possession kind", CustomFieldType.List), default)).Label.Should().Be("Possession kind");
        (await Thrown<ConflictException>(() => c.Update.Handle(new UpdateLibraryFieldCommand(mine.Id, "possession KIND", CustomFieldType.List), default))).Code.Should().Be("library_field_exists");
    }

    // ── access and tenancy ──

    [Theory]
    [InlineData(RoleNames.Admin, true)]
    [InlineData(RoleNames.ProjectManager, false)]
    [InlineData(RoleNames.TitleResearcher, false)]
    [InlineData(RoleNames.Appraiser, false)]
    [InlineData(RoleNames.Negotiator, false)]
    [InlineData(RoleNames.Finance, false)]
    public async Task Library_Everyone_can_read_it_and_only_the_administrator_can_change_it(string role, bool allowed)
    {
        var store = await Provision();
        await using var admin = Admin(store);
        var possession = await admin.Field(LibraryKeys.PossessionType);
        await using var c = new Ctx(store, TestDb.TenantA, role);

        var attempts = new Func<Task>[]
        {
            () => c.List.Handle(new ListLibraryFieldsQuery(), default),
            () => c.Get.Handle(new GetLibraryFieldQuery(possession.Id), default),
            () => c.Create.Handle(new CreateLibraryFieldCommand($"Field {role}", CustomFieldType.Text), default),
            () => c.Update.Handle(new UpdateLibraryFieldCommand(possession.Id, "Possession type", CustomFieldType.List), default),
            () => c.Add.Handle(new AddLibraryValueCommand(possession.Id, $"By {role}"), default),
            () => c.UpdateValue.Handle(new UpdateLibraryValueCommand(possession.Id, possession.Values[0].Id, "PUA", true), default),
            () => c.Import.Handle(new ImportLibraryValuesCommand(possession.Id, new[] { $"Imp {role}" }), default),
        };
        // Reading the Library is open to every role (forms need its dropdown values); changing it is the Library permission's.
        foreach (var (attempt, index) in attempts.Select((a, i) => (a, i)))
        {
            var isRead = index < 2;
            if (allowed || isRead) await attempt.Should().NotThrowAsync();
            else await attempt.Should().ThrowAsync<ForbiddenException>();
        }
        if (!allowed) (await admin.Field(LibraryKeys.PossessionType)).Values.Should().HaveCount(4, "a refused request changes nothing");
    }

    [Fact]
    public async Task Library_One_agencys_library_is_invisible_and_untouchable_to_another()
    {
        var store = await Provision();
        await using var a = Admin(store, TestDb.TenantA);
        await using var b = Admin(store, TestDb.TenantB);
        var mine = await a.Field(LibraryKeys.PossessionType);
        await a.Add.Handle(new AddLibraryValueCommand(mine.Id, "Only for A"), default);

        (await b.Field(LibraryKeys.PossessionType)).Values.Select(v => v.Value).Should().NotContain("Only for A");
        await Thrown<NotFoundException>(() => b.Get.Handle(new GetLibraryFieldQuery(mine.Id), default));
        await Thrown<NotFoundException>(() => b.Add.Handle(new AddLibraryValueCommand(mine.Id, "Intruder"), default));
        await Thrown<NotFoundException>(() => b.Update.Handle(new UpdateLibraryFieldCommand(mine.Id, "Hijacked", CustomFieldType.List), default));
        await Thrown<NotFoundException>(() => b.Import.Handle(new ImportLibraryValuesCommand(mine.Id, new[] { "x" }), default));
        (await a.Field(LibraryKeys.PossessionType)).Values.Select(v => v.Value).Should().NotContain("Intruder");
    }
}
