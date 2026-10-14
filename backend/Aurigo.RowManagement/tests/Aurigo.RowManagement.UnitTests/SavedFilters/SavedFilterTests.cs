using Aurigo.RowManagement.Application.SavedFilters;
using Aurigo.RowManagement.Application.Auth;
using Aurigo.RowManagement.Domain.Common;
using Aurigo.RowManagement.Domain.Errors;
using Aurigo.RowManagement.Infrastructure.Persistence;
using Aurigo.RowManagement.Infrastructure.Persistence.Seed;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Aurigo.RowManagement.UnitTests.SavedFilters;

public class SavedFilterTests
{
    private const string Json = "{\"status\":[\"Open\"],\"search\":\"Main St\"}";

    private sealed class Ctx : IAsyncDisposable
    {
        public RowDbContext Db { get; }
        public FakeUser User { get; }
        public Ctx(string store, Guid tenant, Guid user, params string[] roles)
        {
            Db = TestDb.Create(store, tenant, user);
            User = new FakeUser(user, roles);
        }

        public CreateSavedFilterCommandHandler Create => new(Db, User, new PagePermissionService(Db, User));
        public ListSavedFiltersQueryHandler List => new(Db, User);
        public UpdateSavedFilterCommandHandler Update => new(Db, User);
        public DeleteSavedFilterCommandHandler Delete => new(Db, User);
        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private static async Task<string> Provision()
    {
        var store = TestDb.NewStoreName();
        await using var db = TestDb.Create(store, tenant: null);
        await new TenantProvisioner(db).EnsureDefaultRolesAsync(TestDb.TenantA);
        return store;
    }

    [Fact]
    public async Task A_saved_filter_can_be_created_and_listed_back_exactly()
    {
        var store = await Provision();
        await using var c = new Ctx(store, TestDb.TenantA, TestDb.UserA, RoleNames.ProjectManager);

        var created = await c.Create.Handle(new CreateSavedFilterCommand(PageKeys.Parcels, "  Open parcels  ", Json), default);
        var list = await c.List.Handle(new ListSavedFiltersQuery(PageKeys.Parcels), default);

        created.Name.Should().Be("Open parcels", "names are trimmed");
        list.Should().ContainSingle().Which.Should().Match<SavedFilterDto>(f =>
            f.Id == created.Id && f.FilterJson == Json && f.PageKey == PageKeys.Parcels);
    }

    [Fact]
    public async Task Filters_are_listed_per_page_and_by_name()
    {
        var store = await Provision();
        await using var c = new Ctx(store, TestDb.TenantA, TestDb.UserA, RoleNames.ProjectManager);
        await c.Create.Handle(new CreateSavedFilterCommand(PageKeys.Parcels, "Zebra", Json), default);
        await c.Create.Handle(new CreateSavedFilterCommand(PageKeys.Parcels, "Alpha", Json), default);
        await c.Create.Handle(new CreateSavedFilterCommand(PageKeys.Projects, "Elsewhere", Json), default);

        var parcels = await c.List.Handle(new ListSavedFiltersQuery(PageKeys.Parcels), default);

        parcels.Select(f => f.Name).Should().Equal("Alpha", "Zebra");
    }

    [Fact]
    public async Task Users_never_see_each_others_filters()
    {
        var store = await Provision();
        await using var ann = new Ctx(store, TestDb.TenantA, TestDb.UserA, RoleNames.ProjectManager);
        await using var ben = new Ctx(store, TestDb.TenantA, TestDb.UserA2, RoleNames.ProjectManager);
        var anns = await ann.Create.Handle(new CreateSavedFilterCommand(PageKeys.Parcels, "Mine", Json), default);

        (await ben.List.Handle(new ListSavedFiltersQuery(PageKeys.Parcels), default)).Should().BeEmpty();

        // Ben cannot update or delete Ann's filter, and is told it does not exist rather than that it is forbidden.
        var update = () => ben.Update.Handle(new UpdateSavedFilterCommand(anns.Id, "Stolen", Json), default);
        await update.Should().ThrowAsync<NotFoundException>();
        var delete = () => ben.Delete.Handle(new DeleteSavedFilterCommand(anns.Id), default);
        await delete.Should().ThrowAsync<NotFoundException>();

        (await ann.List.Handle(new ListSavedFiltersQuery(PageKeys.Parcels), default)).Should().ContainSingle().Which.Name.Should().Be("Mine");
    }

    [Fact]
    public async Task Two_users_may_use_the_same_filter_name()
    {
        var store = await Provision();
        await using var ann = new Ctx(store, TestDb.TenantA, TestDb.UserA, RoleNames.ProjectManager);
        await using var ben = new Ctx(store, TestDb.TenantA, TestDb.UserA2, RoleNames.ProjectManager);

        await ann.Create.Handle(new CreateSavedFilterCommand(PageKeys.Parcels, "My queue", Json), default);
        var act = () => ben.Create.Handle(new CreateSavedFilterCommand(PageKeys.Parcels, "My queue", Json), default);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task A_duplicate_name_on_the_same_page_is_a_conflict_ignoring_case()
    {
        var store = await Provision();
        await using var c = new Ctx(store, TestDb.TenantA, TestDb.UserA, RoleNames.ProjectManager);
        await c.Create.Handle(new CreateSavedFilterCommand(PageKeys.Parcels, "Open", Json), default);

        var act = () => c.Create.Handle(new CreateSavedFilterCommand(PageKeys.Parcels, "OPEN", Json), default);

        (await act.Should().ThrowAsync<ConflictException>()).Which.Code.Should().Be("saved_filter_exists");
        // The same name on a different page is fine.
        await c.Create.Handle(new CreateSavedFilterCommand(PageKeys.Projects, "Open", Json), default);
    }

    [Fact]
    public async Task Update_changes_name_and_filter_and_renaming_onto_an_existing_name_conflicts()
    {
        var store = await Provision();
        await using var c = new Ctx(store, TestDb.TenantA, TestDb.UserA, RoleNames.ProjectManager);
        var a = await c.Create.Handle(new CreateSavedFilterCommand(PageKeys.Parcels, "A", Json), default);
        await c.Create.Handle(new CreateSavedFilterCommand(PageKeys.Parcels, "B", Json), default);

        var updated = await c.Update.Handle(new UpdateSavedFilterCommand(a.Id, "A renamed", "{\"x\":1}"), default);
        updated.Name.Should().Be("A renamed");
        updated.FilterJson.Should().Be("{\"x\":1}");

        var clash = () => c.Update.Handle(new UpdateSavedFilterCommand(a.Id, "b", Json), default);
        await clash.Should().ThrowAsync<ConflictException>();

        // Saving a filter under its own name is not a conflict with itself.
        await c.Update.Handle(new UpdateSavedFilterCommand(a.Id, "A renamed", Json), default);
    }

    [Fact]
    public async Task Delete_removes_the_filter_and_the_audit_log_keeps_its_snapshot()
    {
        var store = await Provision();
        await using var c = new Ctx(store, TestDb.TenantA, TestDb.UserA, RoleNames.ProjectManager);
        var created = await c.Create.Handle(new CreateSavedFilterCommand(PageKeys.Parcels, "Temp", Json), default);

        await c.Delete.Handle(new DeleteSavedFilterCommand(created.Id), default);

        (await c.List.Handle(new ListSavedFiltersQuery(PageKeys.Parcels), default)).Should().BeEmpty();
        (await c.Db.AuditLogs.AnyAsync(a => a.EntityId == created.Id && a.OldValue!.Contains("Temp"))).Should().BeTrue();
    }

    [Fact]
    public async Task Saving_a_filter_for_a_page_the_user_cannot_read_is_forbidden()
    {
        var store = await Provision();
        await using var c = new Ctx(store, TestDb.TenantA, TestDb.UserA, RoleNames.Finance);

        var act = () => c.Create.Handle(new CreateSavedFilterCommand(PageKeys.Administration, "Nope", Json), default);

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Theory]
    [InlineData("{}", true)]
    [InlineData("{\"a\":{\"b\":[1,2,3]}}", true)]
    [InlineData("[]", false)]            // must be an object
    [InlineData("\"text\"", false)]
    [InlineData("42", false)]
    [InlineData("null", false)]
    [InlineData("{not json", false)]
    [InlineData("", false)]
    public void The_filter_must_be_a_json_object(string json, bool valid) =>
        new CreateSavedFilterCommandValidator().Validate(new CreateSavedFilterCommand(PageKeys.Parcels, "n", json)).IsValid.Should().Be(valid);

    [Fact]
    public void Oversized_filters_names_and_unknown_pages_are_refused()
    {
        var v = new CreateSavedFilterCommandValidator();
        v.Validate(new CreateSavedFilterCommand(PageKeys.Parcels, "n", "{\"x\":\"" + new string('a', 20_000) + "\"}")).IsValid.Should().BeFalse();
        v.Validate(new CreateSavedFilterCommand(PageKeys.Parcels, new string('n', 101), Json)).IsValid.Should().BeFalse();
        v.Validate(new CreateSavedFilterCommand(PageKeys.Parcels, "   ", Json)).IsValid.Should().BeFalse();
        v.Validate(new CreateSavedFilterCommand("not-a-page", "n", Json)).IsValid.Should().BeFalse();
    }
}
