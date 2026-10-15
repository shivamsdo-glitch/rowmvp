using System.Text.Json;
using Aurigo.RowManagement.Application.Agency;
using Aurigo.RowManagement.Application.Auth;
using Aurigo.RowManagement.Application.Parcels;
using Aurigo.RowManagement.Application.Parties;
using Aurigo.RowManagement.Domain.Common;
using Aurigo.RowManagement.Domain.Entities;
using Aurigo.RowManagement.Domain.Enums;
using Aurigo.RowManagement.Domain.Errors;
using Aurigo.RowManagement.Infrastructure.Persistence;
using Aurigo.RowManagement.Infrastructure.Persistence.Seed;
using Aurigo.RowManagement.Infrastructure.Security;
using FluentAssertions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace Aurigo.RowManagement.UnitTests.Parties;

public class PartyTests
{
    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 3, 15, 12, 0, 0, TimeSpan.Zero);
    }

    private static readonly AesGcmTaxIdProtector Protector = new(new TaxIdOptions { Key = Enumerable.Repeat((byte)9, 32).ToArray() });

    private sealed class Ctx : IAsyncDisposable
    {
        public RowDbContext Db { get; }
        private readonly PagePermissionService _perms;
        private readonly ParcelService _parcels;
        private readonly PartyService _parties;
        private readonly AgencyClock _clock;

        public Ctx(string store, Guid tenant, string role)
        {
            var time = new FixedClock();
            Db = TestDb.Create(store, tenant, TestDb.UserA);
            var who = new FakeUser(TestDb.UserA, role);
            _perms = new PagePermissionService(Db, who);
            _parcels = new ParcelService(Db, who, time);
            _parties = new PartyService(Db);
            _clock = new AgencyClock(Db, time);
        }

        public ListPartiesQueryHandler List => new(Db, _perms, _parties);
        public GetPartyQueryHandler Get => new(_perms, _parties);
        public GetPartyUsageQueryHandler Usage => new(Db, _perms, _parties);
        public CreatePartyCommandHandler Create => new(Db, _perms);
        public UpdatePartyCommandHandler Update => new(Db, _perms, _parties);
        public DeletePartyCommandHandler Delete => new(Db, _perms, _parties);
        public SetPartyTaxInfoCommandHandler SetTax => new(Db, _perms, _parties, Protector, _clock);
        public ClearPartyTaxIdCommandHandler ClearTax => new(Db, _perms, _parties);
        public ListParcelPartiesQueryHandler ForParcel => new(_perms, _parcels, _parties);
        public ListTractPartiesQueryHandler ForTract => new(Db, _perms, _parties);
        public AddParcelPartyCommandHandler AddToParcel => new(Db, _perms, _parcels, _parties);
        public AddTractPartyCommandHandler AddToTract => new(Db, _perms, _parties);
        public UpdatePartyLinkCommandHandler UpdateLink => new(Db, _perms, _parcels, _parties);
        public DeletePartyLinkCommandHandler DeleteLink => new(Db, _perms, _parcels);
        public SplitParcelCommandHandler Split => new(Db, _perms, _parcels, _parties);
        public CombineParcelsCommandHandler Combine => new(Db, _perms, _parcels, _parties);
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed record Site(Guid ProjectId, Guid TractId, Guid ParcelId, Guid LooseParcelId);

    private static async Task<string> Provision()
    {
        var store = TestDb.NewStoreName();
        await using var db = TestDb.Create(store, tenant: null);
        await new TenantProvisioner(db).EnsureDefaultRolesAsync(TestDb.TenantA);
        await new TenantProvisioner(db).EnsureDefaultRolesAsync(TestDb.TenantB);
        return store;
    }

    /// <summary>A project with a tract, a parcel on the tract and a parcel on no tract.</summary>
    private static async Task<Site> AddSite(string store, Guid tenant)
    {
        await using var db = TestDb.Create(store, tenant, TestDb.UserA);
        var project = new Project { Number = "P-" + Guid.NewGuid().ToString("N")[..4], Name = "Site", OversightType = OversightType.Local };
        db.Projects.Add(project);
        var tract = new ParentTract { ProjectId = project.Id, Number = "T-1" };
        db.ParentTracts.Add(tract);
        await db.SaveChangesAsync();
        var status = await db.ParcelStatuses.OrderBy(s => s.SortOrder).FirstAsync();
        Parcel NewParcel(string number, Guid? tractId) => new()
        {
            ProjectId = project.Id, Number = number, ParentTractId = tractId, StatusId = status.Id, CurrentStepKey = "setup", AreaSqFt = 1000,
        };
        var onTract = NewParcel("001", tract.Id);
        var loose = NewParcel("002", null);
        db.Parcels.AddRange(onTract, loose);
        await db.SaveChangesAsync();
        return new Site(project.Id, tract.Id, onTract.Id, loose.Id);
    }

    private static NewPartyFields Person(string name = "Pat Owner") =>
        new(PartyKind.Person, name, null, "pat@example.com", "555-123-4567", "1 Elm St", null, "Springfield", "il", "62701", null);

    private static PartyLinkInput Owner(Guid partyId, decimal? share = null, PartyRole role = PartyRole.FeeOwner) =>
        new(partyId, null, role, share, null, false, null, null);

    private static PartyLinkInput NewOwner(string name, decimal? share = null, PartyRole role = PartyRole.FeeOwner) =>
        new(null, Person(name), role, share, null, false, null, null);

    private static CreatePartyCommand NewParty(string name = "Pat Owner", PartyKind kind = PartyKind.Person) =>
        new(kind, name, null, "pat@example.com", "555-123-4567", "1 Elm St", null, "Springfield", "il", "62701", null);

    private static async Task<Guid> MakeParty(Ctx c, string name = "Pat Owner") => (await c.Create.Handle(NewParty(name), default)).Id;

    private static async Task<T> Thrown<T>(Func<Task> act) where T : Exception => (await act.Should().ThrowAsync<T>()).Which;

    // ── parties (ROW-014) ──

    [Fact]
    public async Task ROW_014_A_party_is_created_trimmed_and_found_by_name_or_city()
    {
        var store = await Provision();
        await using var c = new Ctx(store, TestDb.TenantA, RoleNames.ProjectManager);

        var dto = await c.Create.Handle(NewParty("  Pat Owner  "), default);

        dto.Name.Should().Be("Pat Owner");
        dto.State.Should().Be("IL", "state codes are stored upper case");
        dto.HasTaxId.Should().BeFalse();
        (await c.List.Handle(new ListPartiesQuery("springfield", null), default)).Items.Should().ContainSingle(p => p.Id == dto.Id);
        (await c.List.Handle(new ListPartiesQuery("PAT", PartyKind.Organization), default)).Total.Should().Be(0);
    }

    [Fact]
    public async Task ROW_014_A_party_can_be_edited_and_removed_when_nothing_uses_it()
    {
        var store = await Provision();
        await using var c = new Ctx(store, TestDb.TenantA, RoleNames.ProjectManager);
        var id = await MakeParty(c);

        var updated = await c.Update.Handle(new UpdatePartyCommand(id, PartyKind.Organization, "Pat Holdings LLC", "Pat Holdings, L.L.C.", null, null, null, null, null, null, null, null), default);
        updated.Name.Should().Be("Pat Holdings LLC");
        updated.Kind.Should().Be(PartyKind.Organization);
        updated.Email.Should().BeNull();

        await c.Delete.Handle(new DeletePartyCommand(id), default);
        (await Thrown<NotFoundException>(() => c.Get.Handle(new GetPartyQuery(id), default))).Code.Should().Be("not_found");
    }

    [Fact]
    public async Task ROW_014_A_party_that_is_linked_somewhere_cannot_be_removed()
    {
        var store = await Provision();
        var site = await AddSite(store, TestDb.TenantA);
        await using var c = new Ctx(store, TestDb.TenantA, RoleNames.ProjectManager);
        var id = await MakeParty(c);
        await c.AddToTract.Handle(new AddTractPartyCommand(site.ProjectId, site.TractId, Owner(id, 100m)), default);

        (await Thrown<ConflictException>(() => c.Delete.Handle(new DeletePartyCommand(id), default))).Code.Should().Be("party_in_use");
    }

    [Theory]
    [InlineData("", "a@b.co", "555-1234", "IL", "62701", "Name is required")]
    [InlineData("Pat", "not-an-email", "555-1234", "IL", "62701", "valid email")]
    [InlineData("Pat", "a@b.co", "call me maybe", "IL", "62701", "valid phone")]
    [InlineData("Pat", "a@b.co", "555-1234", "Illinois", "62701", "two-letter")]
    [InlineData("Pat", "a@b.co", "555-1234", "IL", "<script>", "ZIP")]
    public void ROW_014_Bad_party_details_are_refused_at_the_boundary(string name, string email, string phone, string state, string zip, string expected)
    {
        var cmd = new CreatePartyCommand(PartyKind.Person, name, null, email, phone, null, null, null, state, zip, null);
        var result = new CreatePartyCommandValidator().Validate(cmd);
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains(expected));
    }

    [Fact]
    public async Task ROW_014_Parties_are_private_to_their_agency()
    {
        var store = await Provision();
        await using var a = new Ctx(store, TestDb.TenantA, RoleNames.ProjectManager);
        await using var b = new Ctx(store, TestDb.TenantB, RoleNames.ProjectManager);
        var id = await MakeParty(a);

        (await b.List.Handle(new ListPartiesQuery(null, null), default)).Total.Should().Be(0);
        await Thrown<NotFoundException>(() => b.Get.Handle(new GetPartyQuery(id), default));
        await Thrown<NotFoundException>(() => b.Delete.Handle(new DeletePartyCommand(id), default));
    }

    [Theory]
    [InlineData(RoleNames.ProjectManager, true)]
    [InlineData(RoleNames.Negotiator, true)]
    [InlineData(RoleNames.Admin, true)]
    [InlineData(RoleNames.TitleExaminer, false)]
    [InlineData(RoleNames.Appraiser, false)]
    [InlineData(RoleNames.Finance, false)]
    public async Task ROW_014_Only_roles_with_the_parties_page_can_change_parties_but_all_can_read(string role, bool canWrite)
    {
        var store = await Provision();
        await using var pm = new Ctx(store, TestDb.TenantA, RoleNames.ProjectManager);
        var existing = await MakeParty(pm);
        await using var c = new Ctx(store, TestDb.TenantA, role);

        (await c.List.Handle(new ListPartiesQuery(null, null), default)).Total.Should().Be(1);
        (await c.Get.Handle(new GetPartyQuery(existing), default)).Name.Should().Be("Pat Owner");

        var create = () => c.Create.Handle(NewParty("Second"), default);
        if (canWrite) await create.Should().NotThrowAsync();
        else await create.Should().ThrowAsync<ForbiddenException>();
        var tax = () => c.SetTax.Handle(new SetPartyTaxInfoCommand(existing, TaxIdType.Ssn, "123-45-6789", null, false), default);
        if (canWrite) await tax.Should().NotThrowAsync();
        else await tax.Should().ThrowAsync<ForbiddenException>();
    }

    // ── tax information (ROW-018) ──

    [Fact]
    public async Task ROW_018_A_tax_number_is_stored_protected_and_only_the_last_four_are_ever_shown()
    {
        var store = await Provision();
        await using var c = new Ctx(store, TestDb.TenantA, RoleNames.ProjectManager);
        var id = await MakeParty(c);

        var dto = await c.SetTax.Handle(new SetPartyTaxInfoCommand(id, TaxIdType.Ssn, "123-45-6789", new DateOnly(2026, 3, 1), true), default);

        dto.TaxIdMasked.Should().Be("***-**-6789");
        dto.HasTaxId.Should().BeTrue();
        dto.Reportable1099.Should().BeTrue();
        dto.W9ReceivedOn.Should().Be(new DateOnly(2026, 3, 1));
        JsonSerializer.Serialize(dto).Should().NotContain("123456789").And.NotContain("123-45-6789");

        var row = await c.Db.Parties.SingleAsync(p => p.Id == id);
        row.TaxIdProtected.Should().NotBeNull().And.NotContain("123456789");
        Protector.Unprotect(row.TaxIdProtected!, id).Should().Be("123456789");
        (await c.Get.Handle(new GetPartyQuery(id), default)).TaxIdMasked.Should().Be("***-**-6789");
        (await c.List.Handle(new ListPartiesQuery(null, null), default)).Items.Single().HasTaxId.Should().BeTrue();
    }

    [Fact]
    public async Task ROW_018_The_audit_trail_records_that_the_number_changed_but_never_the_number()
    {
        var store = await Provision();
        await using var c = new Ctx(store, TestDb.TenantA, RoleNames.ProjectManager);
        var id = await MakeParty(c);
        await c.SetTax.Handle(new SetPartyTaxInfoCommand(id, TaxIdType.Ssn, "123-45-6789", null, false), default);
        await c.SetTax.Handle(new SetPartyTaxInfoCommand(id, TaxIdType.Ssn, "987-65-4321", null, false), default);
        await c.ClearTax.Handle(new ClearPartyTaxIdCommand(id), default);

        var rows = await c.Db.AuditLogs.IgnoreQueryFilters().Where(a => a.EntityId == id).ToListAsync();
        rows.Should().Contain(a => a.Field == nameof(Party.TaxIdProtected), "the change itself is recorded");
        var everything = string.Join("\n", rows.Select(a => $"{a.OldValue}|{a.NewValue}"));
        everything.Should().NotContain("123456789").And.NotContain("987654321").And.NotContain("6789").And.NotContain("4321");
        var protectedChanges = rows.Where(a => a.Field is nameof(Party.TaxIdProtected) or nameof(Party.TaxIdLast4)).ToList();
        protectedChanges.Should().OnlyContain(a => a.OldValue == null || a.OldValue == "[redacted]" || a.OldValue.Length > 0);
        everything.Should().NotContain("v1.", "not even the encrypted form is copied into the log");
    }

    [Fact]
    public void ROW_018_Printing_the_command_never_shows_the_number()
    {
        var cmd = new SetPartyTaxInfoCommand(Guid.NewGuid(), TaxIdType.Ssn, "123-45-6789", null, false);
        cmd.ToString().Should().NotContain("123").And.Contain("[redacted]");
    }

    [Theory]
    [InlineData(TaxIdType.Ssn, "123-45-678")]
    [InlineData(TaxIdType.Ssn, "000-12-3456")]
    [InlineData(TaxIdType.Ein, "00-1234567")]
    [InlineData(TaxIdType.Ssn, "12345678a")]
    public void ROW_018_A_bad_number_is_refused_without_repeating_it(TaxIdType type, string number)
    {
        var result = new SetPartyTaxInfoCommandValidator().Validate(new SetPartyTaxInfoCommand(Guid.NewGuid(), type, number, null, false));
        result.IsValid.Should().BeFalse();
        result.Errors.Should().OnlyContain(e => !e.ErrorMessage.Contains(number));
    }

    [Fact]
    public void ROW_018_A_number_needs_a_type()
    {
        var result = new SetPartyTaxInfoCommandValidator().Validate(new SetPartyTaxInfoCommand(Guid.NewGuid(), null, "123-45-6789", null, false));
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("type"));
    }

    [Fact]
    public async Task ROW_018_A_W9_cannot_be_received_in_the_future_but_today_in_the_agency_zone_is_fine()
    {
        var store = await Provision();
        await using var c = new Ctx(store, TestDb.TenantA, RoleNames.ProjectManager);
        var id = await MakeParty(c);

        (await Thrown<ValidationException>(() => c.SetTax.Handle(new SetPartyTaxInfoCommand(id, null, null, new DateOnly(2026, 3, 16), false), default)))
            .Errors.Should().Contain(e => e.PropertyName == "W9ReceivedOn");
        (await c.SetTax.Handle(new SetPartyTaxInfoCommand(id, null, null, new DateOnly(2026, 3, 15), false), default)).W9ReceivedOn.Should().Be(new DateOnly(2026, 3, 15));
    }

    [Fact]
    public async Task ROW_018_Saving_the_W9_date_without_a_number_keeps_the_stored_number_and_changing_the_type_needs_it_again()
    {
        var store = await Provision();
        await using var c = new Ctx(store, TestDb.TenantA, RoleNames.ProjectManager);
        var id = await MakeParty(c);
        await c.SetTax.Handle(new SetPartyTaxInfoCommand(id, TaxIdType.Ssn, "123-45-6789", null, false), default);

        var kept = await c.SetTax.Handle(new SetPartyTaxInfoCommand(id, TaxIdType.Ssn, null, new DateOnly(2026, 1, 5), true), default);
        kept.TaxIdMasked.Should().Be("***-**-6789");
        kept.W9ReceivedOn.Should().Be(new DateOnly(2026, 1, 5));

        (await Thrown<InvalidRequestException>(() => c.SetTax.Handle(new SetPartyTaxInfoCommand(id, TaxIdType.Ein, null, null, false), default)))
            .Code.Should().Be("tax_id_type_change");
    }

    [Fact]
    public async Task ROW_018_Clearing_the_number_removes_it_and_its_mask()
    {
        var store = await Provision();
        await using var c = new Ctx(store, TestDb.TenantA, RoleNames.ProjectManager);
        var id = await MakeParty(c);
        await c.SetTax.Handle(new SetPartyTaxInfoCommand(id, TaxIdType.Ein, "12-3456789", null, true), default);

        var cleared = await c.ClearTax.Handle(new ClearPartyTaxIdCommand(id), default);

        cleared.HasTaxId.Should().BeFalse();
        cleared.TaxIdMasked.Should().BeNull();
        cleared.TaxIdType.Should().BeNull();
        (await c.Db.Parties.SingleAsync(p => p.Id == id)).TaxIdProtected.Should().BeNull();
    }

    // ── links (ROW-014..017) ──

    [Fact]
    public async Task ROW_014_Owners_are_recorded_on_the_tract_and_a_parcel_on_it_sees_them()
    {
        var store = await Provision();
        var site = await AddSite(store, TestDb.TenantA);
        await using var c = new Ctx(store, TestDb.TenantA, RoleNames.ProjectManager);

        var link = await c.AddToTract.Handle(new AddTractPartyCommand(site.ProjectId, site.TractId, NewOwner("Pat Owner", 60m)), default);
        await c.AddToTract.Handle(new AddTractPartyCommand(site.ProjectId, site.TractId, NewOwner("Sam Owner", 40m)), default);

        link.Source.Should().Be("Tract");
        link.TractNumber.Should().Be("T-1");
        var seen = await c.ForParcel.Handle(new ListParcelPartiesQuery(site.ParcelId), default);
        seen.Ownership.Select(l => l.Party.Name).Should().Equal("Pat Owner", "Sam Owner");
        seen.Ownership.Should().OnlyContain(l => l.Source == "Tract");
        seen.TractNumber.Should().Be("T-1");
        (await c.ForParcel.Handle(new ListParcelPartiesQuery(site.LooseParcelId), default)).Ownership.Should().BeEmpty();
    }

    [Fact]
    public async Task ROW_014_Owners_are_not_entered_twice_on_a_parcel_that_has_a_tract()
    {
        var store = await Provision();
        var site = await AddSite(store, TestDb.TenantA);
        await using var c = new Ctx(store, TestDb.TenantA, RoleNames.ProjectManager);

        (await Thrown<InvalidRequestException>(() => c.AddToParcel.Handle(new AddParcelPartyCommand(site.ParcelId, NewOwner("Pat Owner")), default)))
            .Code.Should().Be("owners_on_tract");
        (await c.AddToParcel.Handle(new AddParcelPartyCommand(site.LooseParcelId, NewOwner("Pat Owner", 100m)), default)).Source.Should().Be("Parcel");
    }

    [Fact]
    public async Task ROW_015_016_017_Contacts_tenants_and_abutters_go_on_the_parcel_with_their_own_details()
    {
        var store = await Provision();
        var site = await AddSite(store, TestDb.TenantA);
        await using var c = new Ctx(store, TestDb.TenantA, RoleNames.ProjectManager);

        await c.AddToParcel.Handle(new AddParcelPartyCommand(site.ParcelId, new PartyLinkInput(null, Person("Lee Lawyer"), PartyRole.Attorney, null, "Handles closing", false, null, null)), default);
        await c.AddToParcel.Handle(new AddParcelPartyCommand(site.ParcelId, new PartyLinkInput(null, Person("Tina Tenant"), PartyRole.Tenant, null, null, true, "Month to month", "Renter policy 55")), default);
        await c.AddToParcel.Handle(new AddParcelPartyCommand(site.ParcelId, new PartyLinkInput(null, Person("Abe Abutter"), PartyRole.Abutter, null, null, false, null, null)), default);

        var seen = await c.ForParcel.Handle(new ListParcelPartiesQuery(site.ParcelId), default);
        seen.Contacts.Should().ContainSingle().Which.Notes.Should().Be("Handles closing");
        var tenant = seen.Occupants.Should().ContainSingle().Subject;
        tenant.LeaseOnFile.Should().BeTrue();
        tenant.LeaseDetails.Should().Be("Month to month");
        tenant.InsuranceDetails.Should().Be("Renter policy 55");
        seen.Abutters.Should().ContainSingle();
    }

    [Fact]
    public async Task ROW_014_The_same_party_in_the_same_role_twice_is_refused_but_another_role_is_fine()
    {
        var store = await Provision();
        var site = await AddSite(store, TestDb.TenantA);
        await using var c = new Ctx(store, TestDb.TenantA, RoleNames.ProjectManager);
        var id = await MakeParty(c);
        await c.AddToTract.Handle(new AddTractPartyCommand(site.ProjectId, site.TractId, Owner(id, 50m)), default);

        (await Thrown<ConflictException>(() => c.AddToTract.Handle(new AddTractPartyCommand(site.ProjectId, site.TractId, Owner(id, 10m)), default)))
            .Code.Should().Be("party_link_exists");
        await c.AddToTract.Handle(new AddTractPartyCommand(site.ProjectId, site.TractId, Owner(id, null, PartyRole.Trustee)), default);
    }

    [Fact]
    public async Task ROW_014_Shares_of_one_kind_of_interest_cannot_add_up_to_more_than_the_whole()
    {
        var store = await Provision();
        var site = await AddSite(store, TestDb.TenantA);
        await using var c = new Ctx(store, TestDb.TenantA, RoleNames.ProjectManager);
        await c.AddToTract.Handle(new AddTractPartyCommand(site.ProjectId, site.TractId, NewOwner("A", 60m)), default);

        var refused = await Thrown<ConflictException>(() => c.AddToTract.Handle(new AddTractPartyCommand(site.ProjectId, site.TractId, NewOwner("B", 40.5m)), default));
        refused.Code.Should().Be("share_exceeds_whole");
        refused.Message.Should().Contain("40");
        (await c.Db.Parties.CountAsync(p => p.Name == "B")).Should().Be(0, "a refused link must not leave a new party behind");

        await c.AddToTract.Handle(new AddTractPartyCommand(site.ProjectId, site.TractId, NewOwner("B", 40m)), default);
        await c.AddToTract.Handle(new AddTractPartyCommand(site.ProjectId, site.TractId, NewOwner("Easement Co", 100m, PartyRole.EasementHolder)), default);
    }

    [Fact]
    public async Task ROW_014_A_share_can_be_changed_within_the_whole_and_not_beyond_it()
    {
        var store = await Provision();
        var site = await AddSite(store, TestDb.TenantA);
        await using var c = new Ctx(store, TestDb.TenantA, RoleNames.ProjectManager);
        var a = await c.AddToTract.Handle(new AddTractPartyCommand(site.ProjectId, site.TractId, NewOwner("A", 50m)), default);
        await c.AddToTract.Handle(new AddTractPartyCommand(site.ProjectId, site.TractId, NewOwner("B", 30m)), default);

        (await c.UpdateLink.Handle(new UpdatePartyLinkCommand(a.Id, 70m, "Revised", false, null, null), default)).SharePercent.Should().Be(70m);
        (await Thrown<ConflictException>(() => c.UpdateLink.Handle(new UpdatePartyLinkCommand(a.Id, 70.01m, null, false, null, null), default)))
            .Code.Should().Be("share_exceeds_whole");
    }

    [Fact]
    public async Task ROW_014_Roles_without_shares_or_leases_refuse_them()
    {
        var store = await Provision();
        var site = await AddSite(store, TestDb.TenantA);
        await using var c = new Ctx(store, TestDb.TenantA, RoleNames.ProjectManager);

        var lien = new PartyLinkInput(null, Person("Bank"), PartyRole.Lienholder, 10m, null, false, null, null);
        new PartyLinkInputValidator().Validate(lien).Errors.Should().Contain(e => e.ErrorMessage.Contains("share"));
        var lease = new PartyLinkInput(null, Person("Bank"), PartyRole.Lienholder, null, null, true, "x", null);
        new PartyLinkInputValidator().Validate(lease).Errors.Should().Contain(e => e.ErrorMessage.Contains("tenants only"));

        var a = await c.AddToTract.Handle(new AddTractPartyCommand(site.ProjectId, site.TractId, Owner(await MakeParty(c), null, PartyRole.Lienholder)), default);
        await Thrown<ValidationException>(() => c.UpdateLink.Handle(new UpdatePartyLinkCommand(a.Id, 10m, null, false, null, null), default));
        await Thrown<ValidationException>(() => c.UpdateLink.Handle(new UpdatePartyLinkCommand(a.Id, null, null, true, "lease", null), default));
    }

    [Fact]
    public void ROW_014_A_link_names_an_existing_party_or_gives_a_new_one_not_both_and_not_neither()
    {
        var v = new PartyLinkInputValidator();
        v.Validate(new PartyLinkInput(null, null, PartyRole.FeeOwner, null, null, false, null, null)).IsValid.Should().BeFalse();
        v.Validate(new PartyLinkInput(Guid.NewGuid(), Person(), PartyRole.FeeOwner, null, null, false, null, null)).IsValid.Should().BeFalse();
        v.Validate(new PartyLinkInput(Guid.NewGuid(), null, PartyRole.FeeOwner, 50m, null, false, null, null)).IsValid.Should().BeTrue();
        v.Validate(new PartyLinkInput(Guid.NewGuid(), null, (PartyRole)999, null, null, false, null, null)).IsValid.Should().BeFalse();
        v.Validate(new PartyLinkInput(null, Person(""), PartyRole.FeeOwner, null, null, false, null, null)).IsValid.Should().BeFalse("a new party is validated like any other");
    }

    [Fact]
    public async Task ROW_014_A_link_can_be_removed_and_the_party_then_can_be_too()
    {
        var store = await Provision();
        var site = await AddSite(store, TestDb.TenantA);
        await using var c = new Ctx(store, TestDb.TenantA, RoleNames.ProjectManager);
        var link = await c.AddToTract.Handle(new AddTractPartyCommand(site.ProjectId, site.TractId, NewOwner("A", 100m)), default);

        await c.DeleteLink.Handle(new DeletePartyLinkCommand(link.Id), default);

        (await c.ForTract.Handle(new ListTractPartiesQuery(site.ProjectId, site.TractId), default)).Ownership.Should().BeEmpty();
        await c.Delete.Handle(new DeletePartyCommand(link.Party.Id), default);
        await Thrown<NotFoundException>(() => c.DeleteLink.Handle(new DeletePartyLinkCommand(link.Id), default));
    }

    [Fact]
    public async Task ROW_014_A_tract_from_another_project_or_agency_is_not_found()
    {
        var store = await Provision();
        var mine = await AddSite(store, TestDb.TenantA);
        var theirs = await AddSite(store, TestDb.TenantB);
        await using var a = new Ctx(store, TestDb.TenantA, RoleNames.ProjectManager);

        await Thrown<NotFoundException>(() => a.AddToTract.Handle(new AddTractPartyCommand(mine.ProjectId, theirs.TractId, NewOwner("X")), default));
        await Thrown<NotFoundException>(() => a.AddToTract.Handle(new AddTractPartyCommand(theirs.ProjectId, theirs.TractId, NewOwner("X")), default));
        await Thrown<NotFoundException>(() => a.ForParcel.Handle(new ListParcelPartiesQuery(theirs.ParcelId), default));
        await Thrown<NotFoundException>(() => a.AddToParcel.Handle(new AddParcelPartyCommand(theirs.LooseParcelId, NewOwner("X", 100m)), default));
    }

    [Fact]
    public async Task ROW_014_Another_agencys_party_cannot_be_linked()
    {
        var store = await Provision();
        var site = await AddSite(store, TestDb.TenantA);
        await using var other = new Ctx(store, TestDb.TenantB, RoleNames.ProjectManager);
        var foreign = await MakeParty(other);
        await using var c = new Ctx(store, TestDb.TenantA, RoleNames.ProjectManager);

        await Thrown<NotFoundException>(() => c.AddToTract.Handle(new AddTractPartyCommand(site.ProjectId, site.TractId, Owner(foreign, 100m)), default));
    }

    [Theory]
    [InlineData(RoleNames.ProjectManager, true)]
    [InlineData(RoleNames.Negotiator, true)]
    [InlineData(RoleNames.Admin, true)]
    [InlineData(RoleNames.TitleExaminer, false)]
    [InlineData(RoleNames.Appraiser, false)]
    [InlineData(RoleNames.Finance, false)]
    public async Task ROW_014_Only_roles_that_may_edit_parties_can_link_change_or_remove_them(string role, bool canWrite)
    {
        var store = await Provision();
        var site = await AddSite(store, TestDb.TenantA);
        await using var pm = new Ctx(store, TestDb.TenantA, RoleNames.ProjectManager);
        var existing = await pm.AddToTract.Handle(new AddTractPartyCommand(site.ProjectId, site.TractId, NewOwner("Pat", 50m)), default);
        await using var c = new Ctx(store, TestDb.TenantA, role);

        (await c.ForParcel.Handle(new ListParcelPartiesQuery(site.ParcelId), default)).Ownership.Should().ContainSingle();

        var add = () => c.AddToParcel.Handle(new AddParcelPartyCommand(site.ParcelId, new PartyLinkInput(null, Person("N"), PartyRole.Attorney, null, null, false, null, null)), default);
        var update = () => c.UpdateLink.Handle(new UpdatePartyLinkCommand(existing.Id, 40m, null, false, null, null), default);
        var remove = () => c.DeleteLink.Handle(new DeletePartyLinkCommand(existing.Id), default);
        foreach (var act in new Func<Task>[] { add, update, remove })
        {
            if (canWrite) await act.Should().NotThrowAsync();
            else await act.Should().ThrowAsync<ForbiddenException>();
        }
    }

    [Fact]
    public async Task ROW_014_Usage_lists_every_tract_and_parcel_a_party_appears_on()
    {
        var store = await Provision();
        var site = await AddSite(store, TestDb.TenantA);
        await using var c = new Ctx(store, TestDb.TenantA, RoleNames.ProjectManager);
        var id = await MakeParty(c, "Lee Lawyer");
        await c.AddToTract.Handle(new AddTractPartyCommand(site.ProjectId, site.TractId, Owner(id, null, PartyRole.Attorney)), default);
        await c.AddToParcel.Handle(new AddParcelPartyCommand(site.LooseParcelId, Owner(id, null, PartyRole.Attorney)), default);

        var usage = await c.Usage.Handle(new GetPartyUsageQuery(id), default);

        usage.Select(u => u.TargetLabel).Should().Equal("Parcel 002", "Tract T-1");
        usage.Should().OnlyContain(u => u.Role == PartyRole.Attorney && u.ProjectId == site.ProjectId);
        (await c.Get.Handle(new GetPartyQuery(id), default)).LinkCount.Should().Be(2);
    }

    // ── split and combine keep the people (ROW-007 with ROW-014..017) ──

    [Fact]
    public async Task ROW_007_014_Splitting_a_parcel_gives_every_piece_the_same_people()
    {
        var store = await Provision();
        var site = await AddSite(store, TestDb.TenantA);
        await using var c = new Ctx(store, TestDb.TenantA, RoleNames.ProjectManager);
        await c.AddToParcel.Handle(new AddParcelPartyCommand(site.LooseParcelId, NewOwner("Pat Owner", 100m)), default);
        await c.AddToParcel.Handle(new AddParcelPartyCommand(site.LooseParcelId, new PartyLinkInput(null, Person("Tina"), PartyRole.Tenant, null, null, true, "Lease A", null)), default);

        var result = await c.Split.Handle(new SplitParcelCommand(site.LooseParcelId, new[] { new SplitPart(400m), new SplitPart(300m) }), default);

        var pieces = result.Parts.Concat(result.Remnant is null ? Array.Empty<ParcelSummaryDto>() : new[] { result.Remnant }).ToList();
        pieces.Should().HaveCount(3);
        foreach (var piece in pieces)
        {
            var seen = await c.ForParcel.Handle(new ListParcelPartiesQuery(piece.Id), default);
            seen.Ownership.Should().ContainSingle().Which.SharePercent.Should().Be(100m);
            seen.Occupants.Should().ContainSingle().Which.LeaseDetails.Should().Be("Lease A");
        }
        (await c.Db.Parties.CountAsync()).Should().Be(2, "people are shared, not copied");
    }

    [Fact]
    public async Task ROW_007_014_Combining_parcels_keeps_each_person_once()
    {
        var store = await Provision();
        var site = await AddSite(store, TestDb.TenantA);
        await using var c = new Ctx(store, TestDb.TenantA, RoleNames.ProjectManager);
        var second = await AddLooseParcel(store, TestDb.TenantA, site.ProjectId, "003");
        var shared = await MakeParty(c, "Shared Attorney");
        await c.AddToParcel.Handle(new AddParcelPartyCommand(site.LooseParcelId, Owner(shared, null, PartyRole.Attorney)), default);
        await c.AddToParcel.Handle(new AddParcelPartyCommand(second, Owner(shared, null, PartyRole.Attorney)), default);
        await c.AddToParcel.Handle(new AddParcelPartyCommand(second, NewOwner("Only On Second", 100m)), default);

        var combined = await c.Combine.Handle(new CombineParcelsCommand(new[] { site.LooseParcelId, second }, null), default);

        var seen = await c.ForParcel.Handle(new ListParcelPartiesQuery(combined.Id), default);
        seen.Contacts.Should().ContainSingle(l => l.Party.Id == shared);
        seen.Ownership.Should().ContainSingle(l => l.Party.Name == "Only On Second");
    }

    [Fact]
    public async Task ROW_007_014_A_piece_of_a_tracted_parcel_still_sees_the_tracts_owners()
    {
        var store = await Provision();
        var site = await AddSite(store, TestDb.TenantA);
        await using var c = new Ctx(store, TestDb.TenantA, RoleNames.ProjectManager);
        await c.AddToTract.Handle(new AddTractPartyCommand(site.ProjectId, site.TractId, NewOwner("Pat Owner", 100m)), default);

        var result = await c.Split.Handle(new SplitParcelCommand(site.ParcelId, new[] { new SplitPart(400m) }), default);

        var seen = await c.ForParcel.Handle(new ListParcelPartiesQuery(result.Parts.Single().Id), default);
        seen.Ownership.Should().ContainSingle().Which.Source.Should().Be("Tract");
        (await c.Db.PartyLinks.CountAsync(l => l.ParcelId != null)).Should().Be(0, "tract links are inherited, never copied onto parcels");
    }

    private static async Task<Guid> AddLooseParcel(string store, Guid tenant, Guid projectId, string number)
    {
        await using var db = TestDb.Create(store, tenant, TestDb.UserA);
        var status = await db.ParcelStatuses.OrderBy(s => s.SortOrder).FirstAsync();
        var parcel = new Parcel { ProjectId = projectId, Number = number, StatusId = status.Id, CurrentStepKey = "setup", AreaSqFt = 500 };
        db.Parcels.Add(parcel);
        await db.SaveChangesAsync();
        return parcel.Id;
    }
}
