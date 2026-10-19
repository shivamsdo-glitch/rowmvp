using Aurigo.RowManagement.Application.Auth;
using Aurigo.RowManagement.Application.Calculations;
using Aurigo.RowManagement.Application.ParcelRecords;
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

namespace Aurigo.RowManagement.UnitTests.ParcelRecords;

public class ParcelRecordTests
{
    private sealed class FixedClock : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public FixedClock(DateTimeOffset? now = null) => _now = now ?? new DateTimeOffset(2026, 3, 15, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
    }

    private static readonly DateOnly Today = new(2026, 3, 15);

    private sealed class Ctx : IAsyncDisposable
    {
        public RowDbContext Db { get; }
        private readonly PagePermissionService _perms;
        private readonly ParcelService _parcels;
        private readonly FakeUser _user;

        public Ctx(string store, Guid tenant, string role, DateTimeOffset? at = null)
        {
            Db = TestDb.Create(store, tenant, TestDb.UserA);
            _user = new FakeUser(TestDb.UserA, role);
            _perms = new PagePermissionService(Db, _user);
            _parcels = new ParcelService(Db, _user, new FixedClock(at));
        }

        public ListParcelTeamQueryHandler Team => new(Db, _perms, _parcels);
        public AddTeamMemberCommandHandler AddTeam => new(Db, _perms, _parcels);
        public UpdateTeamMemberCommandHandler UpdateTeam => new(Db, _perms, _parcels);
        public RemoveTeamMemberCommandHandler RemoveTeam => new(Db, _perms, _parcels);
        public ListParcelDiaryQueryHandler Diary => new(Db, _perms, _parcels);
        public AddDiaryEntryCommandHandler AddDiary => new(Db, _perms, _parcels, _user);
        public RemoveDiaryEntryCommandHandler RemoveDiary => new(Db, _perms, _parcels);
        public ListParcelNotesQueryHandler Notes => new(Db, _perms, _parcels);
        public AddParcelNoteCommandHandler AddNote => new(Db, _perms, _parcels, _user);
        public RemoveParcelNoteCommandHandler RemoveNote => new(Db, _perms, _parcels);
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

    private static async Task<(Guid Parcel, Guid Project)> AddParcel(string store, Guid tenant, ParcelState state = ParcelState.Active)
    {
        await using var db = TestDb.Create(store, tenant, TestDb.UserA);
        var project = new Project { Number = "P-" + Guid.NewGuid().ToString("N")[..4], Name = "Records", OversightType = OversightType.Local };
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        var status = await db.ParcelStatuses.OrderBy(s => s.SortOrder).FirstAsync();
        var parcel = new Parcel { ProjectId = project.Id, Number = "001", StatusId = status.Id, CurrentStepKey = "setup", AreaSqFt = 10, State = state };
        db.Parcels.Add(parcel);
        await db.SaveChangesAsync();
        return (parcel.Id, project.Id);
    }

    private static async Task<T> Thrown<T>(Func<Task> act) where T : Exception => (await act.Should().ThrowAsync<T>()).Which;

    private static AddTeamMemberCommand Member(Guid parcel, TeamRole role = TeamRole.Negotiator, string name = "Riley Okafor") =>
        new(parcel, role, name, "Okafor Land Services", true, "riley@example.com", "555-123-4567", null);

    // ── pure rules ──

    [Fact]
    public void ROW_019_Names_are_compared_ignoring_case_and_spacing()
    {
        ParcelRecordRules.NameKey("  riley   OKAFOR ").Should().Be(ParcelRecordRules.NameKey("Riley Okafor"));
        ParcelRecordRules.CleanName("  Riley   Okafor ").Should().Be("Riley Okafor");
        ParcelRecordRules.NameKey("Riley Okafor").Should().NotBe(ParcelRecordRules.NameKey("Riley Okafors"));
    }

    [Fact]
    public void ROW_020_A_diary_entry_cannot_be_dated_after_today_but_today_and_the_past_are_fine()
    {
        ParcelRecordRules.DiaryDateProblem(Today, Today).Should().BeNull();
        ParcelRecordRules.DiaryDateProblem(Today.AddDays(-400), Today).Should().BeNull();
        ParcelRecordRules.DiaryDateProblem(Today.AddDays(1), Today).Should().Contain("future");
        ParcelRecordRules.DiaryDateProblem(new DateOnly(1980, 1, 1), Today).Should().Contain("valid date");
    }

    [Fact]
    public void ROW_019_Every_role_has_a_plain_name()
    {
        foreach (var role in Enum.GetValues<TeamRole>()) ParcelRecordRules.DescribeRole(role).Should().NotBeNullOrWhiteSpace();
    }

    // ── team (ROW-019) ──

    [Fact]
    public async Task ROW_019_People_are_assigned_roles_on_a_parcel_and_listed_by_role()
    {
        var store = await Provision();
        var (parcel, _) = await AddParcel(store, TestDb.TenantA);
        await using var c = new Ctx(store, TestDb.TenantA, RoleNames.ProjectManager);

        await c.AddTeam.Handle(Member(parcel, TeamRole.Negotiator, "  Riley   Okafor "), default);
        await c.AddTeam.Handle(Member(parcel, TeamRole.ProjectLead, "Dana Whitfield") with { IsConsultant = false, Organization = null }, default);

        var team = await c.Team.Handle(new ListParcelTeamQuery(parcel), default);
        team.Select(m => (m.Role, m.Name)).Should().Equal((TeamRole.ProjectLead, "Dana Whitfield"), (TeamRole.Negotiator, "Riley Okafor"));
        team.Single(m => m.Name == "Riley Okafor").IsConsultant.Should().BeTrue();
    }

    [Fact]
    public async Task ROW_019_The_same_person_in_the_same_role_twice_is_refused_but_another_role_or_parcel_is_fine()
    {
        var store = await Provision();
        var (parcel, _) = await AddParcel(store, TestDb.TenantA);
        var (other, _) = await AddParcel(store, TestDb.TenantA);
        await using var c = new Ctx(store, TestDb.TenantA, RoleNames.ProjectManager);
        await c.AddTeam.Handle(Member(parcel), default);

        (await Thrown<ConflictException>(() => c.AddTeam.Handle(Member(parcel, name: "riley okafor"), default))).Code.Should().Be("team_member_exists");
        await c.AddTeam.Handle(Member(parcel, TeamRole.Counsel), default);
        await c.AddTeam.Handle(Member(other), default);
    }

    [Fact]
    public async Task ROW_019_A_team_member_can_be_changed_and_removed_and_changes_cannot_collide()
    {
        var store = await Provision();
        var (parcel, _) = await AddParcel(store, TestDb.TenantA);
        await using var c = new Ctx(store, TestDb.TenantA, RoleNames.ProjectManager);
        var a = await c.AddTeam.Handle(Member(parcel), default);
        await c.AddTeam.Handle(Member(parcel, TeamRole.Reviewer, "Sam Reviewer"), default);

        var changed = await c.UpdateTeam.Handle(new UpdateTeamMemberCommand(parcel, a.Id, TeamRole.Appraiser, "Riley Okafor", null, false, null, null, "Moved over"), default);
        changed.Should().Match<TeamMemberDto>(m => m.Role == TeamRole.Appraiser && !m.IsConsultant && m.Notes == "Moved over");
        (await Thrown<ConflictException>(() => c.UpdateTeam.Handle(new UpdateTeamMemberCommand(parcel, a.Id, TeamRole.Reviewer, "SAM reviewer", null, false, null, null, null), default)))
            .Code.Should().Be("team_member_exists");

        await c.RemoveTeam.Handle(new RemoveTeamMemberCommand(parcel, a.Id), default);
        (await c.Team.Handle(new ListParcelTeamQuery(parcel), default)).Should().ContainSingle();
        await Thrown<NotFoundException>(() => c.RemoveTeam.Handle(new RemoveTeamMemberCommand(parcel, a.Id), default));
    }

    [Theory]
    [InlineData("", "a@b.co", "555-1234", "Name is required")]
    [InlineData("Riley", "nope", "555-1234", "valid email")]
    [InlineData("Riley", "a@b.co", "call me", "valid phone")]
    public void ROW_019_Bad_team_details_are_refused_at_the_boundary(string name, string email, string phone, string expected) =>
        new AddTeamMemberCommandValidator().Validate(new AddTeamMemberCommand(Guid.NewGuid(), TeamRole.Other, name, null, false, email, phone, null))
            .Errors.Should().Contain(e => e.ErrorMessage.Contains(expected));

    // ── diary (ROW-020) ──

    [Fact]
    public async Task ROW_020_A_contact_is_logged_with_who_when_how_and_what_was_promised_newest_first()
    {
        var store = await Provision();
        var (parcel, _) = await AddParcel(store, TestDb.TenantA);
        await using var c = new Ctx(store, TestDb.TenantA, RoleNames.Negotiator);

        await c.AddDiary.Handle(new AddDiaryEntryCommand(parcel, Today.AddDays(-7), DiaryMethod.Call, DiaryDirection.Outgoing, "  Alex   Marlowe ", null, "Explained the offer.", "Will call back Friday"), default);
        var latest = await c.AddDiary.Handle(new AddDiaryEntryCommand(parcel, Today, DiaryMethod.Email, DiaryDirection.Incoming, "Alex Marlowe", null, "Sent plans.", null), default);

        latest.ContactName.Should().Be("Alex Marlowe");
        var diary = await c.Diary.Handle(new ListParcelDiaryQuery(parcel), default);
        diary.Select(e => e.OccurredOn).Should().Equal(Today, Today.AddDays(-7));
        diary[1].Outcome.Should().Be("Will call back Friday");
    }

    [Fact]
    public async Task ROW_020_A_diary_entry_in_the_future_or_with_no_one_or_nothing_said_is_refused()
    {
        var store = await Provision();
        var (parcel, _) = await AddParcel(store, TestDb.TenantA);
        await using var c = new Ctx(store, TestDb.TenantA, RoleNames.Negotiator);

        (await Thrown<ValidationException>(() => c.AddDiary.Handle(new AddDiaryEntryCommand(parcel, Today.AddDays(1), DiaryMethod.Call, DiaryDirection.Outgoing, "A", null, "x", null), default)))
            .Errors.Should().Contain(e => e.PropertyName == "OccurredOn");
        new AddDiaryEntryCommandValidator().Validate(new AddDiaryEntryCommand(parcel, Today, DiaryMethod.Call, DiaryDirection.Outgoing, " ", null, "x", null)).Errors
            .Should().Contain(e => e.PropertyName == "ContactName");
        new AddDiaryEntryCommandValidator().Validate(new AddDiaryEntryCommand(parcel, Today, DiaryMethod.Call, DiaryDirection.Outgoing, "A", null, " ", null)).Errors
            .Should().Contain(e => e.ErrorMessage == "Summary is required.");
        new AddDiaryEntryCommandValidator().Validate(new AddDiaryEntryCommand(parcel, Today, DiaryMethod.Call, DiaryDirection.Outgoing, "A", null, new string('x', 2001), null)).IsValid.Should().BeFalse();
        new AddDiaryEntryCommandValidator().Validate(new AddDiaryEntryCommand(parcel, Today, (DiaryMethod)99, DiaryDirection.Outgoing, "A", null, "x", null)).IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task ROW_020_The_day_is_counted_where_the_project_is()
    {
        var store = await Provision();
        var (parcel, projectId) = await AddParcel(store, TestDb.TenantA);
        await using (var db = TestDb.Create(store, TestDb.TenantA, TestDb.UserA))
        {
            (await db.Projects.SingleAsync(p => p.Id == projectId)).TimeZoneId = "Pacific/Guam"; // 16 March 06:00 there at the instant used below
            await db.SaveChangesAsync();
        }
        await using var c = new Ctx(store, TestDb.TenantA, RoleNames.Negotiator, new DateTimeOffset(2026, 3, 15, 20, 0, 0, TimeSpan.Zero));

        await c.AddDiary.Handle(new AddDiaryEntryCommand(parcel, Today.AddDays(1), DiaryMethod.Call, DiaryDirection.Outgoing, "A", null, "x", null), default);
    }

    [Fact]
    public async Task ROW_020_A_party_can_be_the_contact_and_must_belong_to_the_agency()
    {
        var store = await Provision();
        var (parcel, _) = await AddParcel(store, TestDb.TenantA);
        Guid mine, theirs;
        await using (var db = TestDb.Create(store, TestDb.TenantA, TestDb.UserA)) { var p = new Party { Name = "Pat Owner" }; db.Parties.Add(p); await db.SaveChangesAsync(); mine = p.Id; }
        await using (var db = TestDb.Create(store, TestDb.TenantB, TestDb.UserB)) { var p = new Party { Name = "Their Owner" }; db.Parties.Add(p); await db.SaveChangesAsync(); theirs = p.Id; }
        await using var c = new Ctx(store, TestDb.TenantA, RoleNames.Negotiator);

        (await c.AddDiary.Handle(new AddDiaryEntryCommand(parcel, Today, DiaryMethod.Meeting, DiaryDirection.Incoming, null, mine, "Met", null), default)).ContactName.Should().Be("Pat Owner");
        await Thrown<NotFoundException>(() => c.AddDiary.Handle(new AddDiaryEntryCommand(parcel, Today, DiaryMethod.Meeting, DiaryDirection.Incoming, null, theirs, "Met", null), default));
    }

    [Fact]
    public async Task ROW_020_An_entry_can_be_removed_and_removal_is_audited()
    {
        var store = await Provision();
        var (parcel, _) = await AddParcel(store, TestDb.TenantA);
        await using var c = new Ctx(store, TestDb.TenantA, RoleNames.Negotiator);
        var entry = await c.AddDiary.Handle(new AddDiaryEntryCommand(parcel, Today, DiaryMethod.Call, DiaryDirection.Outgoing, "A", null, "x", null), default);

        await c.RemoveDiary.Handle(new RemoveDiaryEntryCommand(parcel, entry.Id), default);

        (await c.Diary.Handle(new ListParcelDiaryQuery(parcel), default)).Should().BeEmpty();
        (await c.Db.AuditLogs.IgnoreQueryFilters().CountAsync(a => a.EntityType == nameof(ParcelDiaryEntry) && a.EntityId == entry.Id && a.Action == AuditAction.Delete)).Should().Be(1);
    }

    // ── notes (ROW-021) ──

    [Fact]
    public async Task ROW_021_A_note_shows_who_made_it_when_the_account_is_known_and_can_be_deleted()
    {
        var store = await Provision();
        var (parcel, _) = await AddParcel(store, TestDb.TenantA);
        await using (var db = TestDb.Create(store, TestDb.TenantA, TestDb.UserA))
        {
            db.AppUsers.Add(new AppUser { Id = TestDb.UserA, FirstName = "Dana", LastName = "Whitfield", Email = "dana@example.test" });
            await db.SaveChangesAsync();
        }
        await using var c = new Ctx(store, TestDb.TenantA, RoleNames.ProjectManager);

        var note = await c.AddNote.Handle(new AddParcelNoteCommand(parcel, "  Prefers email.  "), default);
        note.Text.Should().Be("Prefers email.");
        note.AuthorName.Should().Be("Dana Whitfield");
        (await c.Notes.Handle(new ListParcelNotesQuery(parcel), default)).Should().ContainSingle().Which.CreatedBy.Should().Be(TestDb.UserA);

        await c.RemoveNote.Handle(new RemoveParcelNoteCommand(parcel, note.Id), default);
        (await c.Notes.Handle(new ListParcelNotesQuery(parcel), default)).Should().BeEmpty();
    }

    [Fact]
    public void ROW_021_An_empty_or_enormous_note_is_refused()
    {
        new AddParcelNoteCommandValidator().Validate(new AddParcelNoteCommand(Guid.NewGuid(), "  ")).IsValid.Should().BeFalse();
        new AddParcelNoteCommandValidator().Validate(new AddParcelNoteCommand(Guid.NewGuid(), new string('x', 4001))).IsValid.Should().BeFalse();
        new AddParcelNoteCommandValidator().Validate(new AddParcelNoteCommand(Guid.NewGuid(), new string('x', 4000))).IsValid.Should().BeTrue();
    }

    // ── shared rules ──

    [Fact]
    public async Task ROW_019_020_021_A_split_or_combined_parcel_takes_no_new_records_but_can_still_be_read()
    {
        var store = await Provision();
        var (parcel, _) = await AddParcel(store, TestDb.TenantA, ParcelState.Split);
        await using var c = new Ctx(store, TestDb.TenantA, RoleNames.Admin);

        (await Thrown<ConflictException>(() => c.AddTeam.Handle(Member(parcel), default))).Code.Should().Be("parcel_not_active");
        (await Thrown<ConflictException>(() => c.AddDiary.Handle(new AddDiaryEntryCommand(parcel, Today, DiaryMethod.Call, DiaryDirection.Outgoing, "A", null, "x", null), default))).Code.Should().Be("parcel_not_active");
        (await Thrown<ConflictException>(() => c.AddNote.Handle(new AddParcelNoteCommand(parcel, "x"), default))).Code.Should().Be("parcel_not_active");
        (await c.Team.Handle(new ListParcelTeamQuery(parcel), default)).Should().BeEmpty();
        (await c.Diary.Handle(new ListParcelDiaryQuery(parcel), default)).Should().BeEmpty();
        (await c.Notes.Handle(new ListParcelNotesQuery(parcel), default)).Should().BeEmpty();
    }

    [Fact]
    public async Task ROW_019_020_021_Another_agencys_parcel_is_not_found_for_every_operation()
    {
        var store = await Provision();
        var (theirs, _) = await AddParcel(store, TestDb.TenantB);
        await using var c = new Ctx(store, TestDb.TenantA, RoleNames.Admin);

        await Thrown<NotFoundException>(() => c.Team.Handle(new ListParcelTeamQuery(theirs), default));
        await Thrown<NotFoundException>(() => c.AddTeam.Handle(Member(theirs), default));
        await Thrown<NotFoundException>(() => c.Diary.Handle(new ListParcelDiaryQuery(theirs), default));
        await Thrown<NotFoundException>(() => c.AddDiary.Handle(new AddDiaryEntryCommand(theirs, Today, DiaryMethod.Call, DiaryDirection.Outgoing, "A", null, "x", null), default));
        await Thrown<NotFoundException>(() => c.Notes.Handle(new ListParcelNotesQuery(theirs), default));
        await Thrown<NotFoundException>(() => c.AddNote.Handle(new AddParcelNoteCommand(theirs, "x"), default));
    }

    [Fact]
    public async Task ROW_019_020_021_A_record_of_another_parcel_cannot_be_reached_through_this_one()
    {
        var store = await Provision();
        var (a, _) = await AddParcel(store, TestDb.TenantA);
        var (b, _) = await AddParcel(store, TestDb.TenantA);
        await using var c = new Ctx(store, TestDb.TenantA, RoleNames.Admin);
        var member = await c.AddTeam.Handle(Member(a), default);
        var entry = await c.AddDiary.Handle(new AddDiaryEntryCommand(a, Today, DiaryMethod.Call, DiaryDirection.Outgoing, "A", null, "x", null), default);
        var note = await c.AddNote.Handle(new AddParcelNoteCommand(a, "x"), default);

        await Thrown<NotFoundException>(() => c.RemoveTeam.Handle(new RemoveTeamMemberCommand(b, member.Id), default));
        await Thrown<NotFoundException>(() => c.RemoveDiary.Handle(new RemoveDiaryEntryCommand(b, entry.Id), default));
        await Thrown<NotFoundException>(() => c.RemoveNote.Handle(new RemoveParcelNoteCommand(b, note.Id), default));
        await Thrown<NotFoundException>(() => c.UpdateTeam.Handle(new UpdateTeamMemberCommand(b, member.Id, TeamRole.Other, "X", null, false, null, null, null), default));
    }

    [Theory]
    [InlineData(RoleNames.ProjectManager, true, false, true)]
    [InlineData(RoleNames.Admin, true, true, true)]
    [InlineData(RoleNames.Negotiator, false, true, false)]
    [InlineData(RoleNames.TitleResearcher, false, false, false)]
    [InlineData(RoleNames.Appraiser, false, false, false)]
    [InlineData(RoleNames.Finance, false, false, false)]
    public async Task ROW_019_020_021_Each_role_can_do_exactly_what_its_pages_allow(string role, bool teamAndNotes, bool diary, bool notesToo)
    {
        var store = await Provision();
        var (parcel, _) = await AddParcel(store, TestDb.TenantA);
        await using var admin = new Ctx(store, TestDb.TenantA, RoleNames.Admin);
        var member = await admin.AddTeam.Handle(Member(parcel, TeamRole.Counsel, "Seed Counsel"), default);
        var entry = await admin.AddDiary.Handle(new AddDiaryEntryCommand(parcel, Today, DiaryMethod.Call, DiaryDirection.Outgoing, "Seed", null, "x", null), default);
        var note = await admin.AddNote.Handle(new AddParcelNoteCommand(parcel, "seed"), default);
        await using var c = new Ctx(store, TestDb.TenantA, role);

        // Everyone can read the team and notes (parcels) and the diary (negotiations: all six roles but two have no access).
        (await c.Team.Handle(new ListParcelTeamQuery(parcel), default)).Should().NotBeEmpty();
        (await c.Notes.Handle(new ListParcelNotesQuery(parcel), default)).Should().NotBeEmpty();
        var readDiary = () => c.Diary.Handle(new ListParcelDiaryQuery(parcel), default);
        if (role is RoleNames.TitleResearcher or RoleNames.Appraiser) await readDiary.Should().ThrowAsync<ForbiddenException>();
        else await readDiary.Should().NotThrowAsync();

        async Task Check(Func<Task> act, bool allowed)
        {
            if (allowed) await act.Should().NotThrowAsync();
            else await act.Should().ThrowAsync<ForbiddenException>();
        }
        await Check(() => c.AddTeam.Handle(Member(parcel, TeamRole.Other, $"By {role}"), default), teamAndNotes);
        await Check(() => c.UpdateTeam.Handle(new UpdateTeamMemberCommand(parcel, member.Id, TeamRole.Counsel, "Seed Counsel", null, false, null, null, $"by {role}"), default), teamAndNotes);
        await Check(() => c.AddNote.Handle(new AddParcelNoteCommand(parcel, $"by {role}"), default), notesToo);
        await Check(() => c.AddDiary.Handle(new AddDiaryEntryCommand(parcel, Today, DiaryMethod.Call, DiaryDirection.Outgoing, $"By {role}", null, "x", null), default), diary);
        await Check(() => c.RemoveDiary.Handle(new RemoveDiaryEntryCommand(parcel, entry.Id), default), diary);
        await Check(() => c.RemoveNote.Handle(new RemoveParcelNoteCommand(parcel, note.Id), default), notesToo);
        await Check(() => c.RemoveTeam.Handle(new RemoveTeamMemberCommand(parcel, member.Id), default), teamAndNotes);
    }
}
