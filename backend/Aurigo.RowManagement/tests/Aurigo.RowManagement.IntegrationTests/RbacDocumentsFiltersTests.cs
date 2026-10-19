using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Aurigo.RowManagement.Application.Auth;
using Aurigo.RowManagement.Application.Documents;
using Aurigo.RowManagement.Application.SavedFilters;
using Aurigo.RowManagement.Domain.Common;
using Aurigo.RowManagement.Domain.Entities;
using Aurigo.RowManagement.Infrastructure.Persistence;
using Aurigo.RowManagement.Infrastructure.Persistence.Seed;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Aurigo.RowManagement.IntegrationTests;

public class RbacDocumentsFiltersTests : IClassFixture<RowApiFactory>
{
    private static readonly Guid OtherTenant = Guid.Parse("00000000-0000-0000-0000-0000000000b2");
    private static readonly Guid Parcel = Guid.NewGuid();
    private static readonly JsonSerializerOptions Json = RowApiFactory.JsonOptions;
    private readonly RowApiFactory _factory;

    public RbacDocumentsFiltersTests(RowApiFactory factory) => _factory = factory;

    private async Task<HttpClient> As(string role, Guid? tenant = null, Guid? user = null)
    {
        var token = await _factory.DevTokenAsync(_factory.CreateClient(), role, tenant, user);
        return _factory.ClientWithToken(token);
    }

    private async Task EnsureOtherTenantAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RowDbContext>();
        if (!await db.Tenants.AnyAsync(t => t.Id == OtherTenant))
        {
            db.Tenants.Add(new Tenant { Id = OtherTenant, Name = "Other Agency", Slug = "other", IsActive = true });
            await db.SaveChangesAsync();
        }
        await scope.ServiceProvider.GetRequiredService<TenantProvisioner>().EnsureDefaultRolesAsync(OtherTenant);
    }

    private static MultipartFormDataContent Form(string entityType, Guid entityId, string fileName, byte[] bytes, string? docType = null)
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent(entityType), "entityType" },
            { new StringContent(entityId.ToString()), "entityId" },
        };
        if (docType is not null) form.Add(new StringContent(docType), "docType");
        form.Add(new ByteArrayContent(bytes), "file", fileName);
        return form;
    }

    // ---- permissions & seeding -------------------------------------------------------------

    [Fact]
    public async Task The_demo_tenant_is_seeded_with_every_role_on_every_page()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RowDbContext>();

        (await db.Roles.IgnoreQueryFilters().CountAsync(r => r.TenantId == WellKnownIds.DevTenant)).Should().Be(RoleNames.All.Count);
        (await db.RolePagePermissions.IgnoreQueryFilters().CountAsync(p => p.TenantId == WellKnownIds.DevTenant))
            .Should().Be(RoleNames.All.Count * PageKeys.All.Count);
    }

    [Theory]
    [InlineData(RoleNames.Admin, true)]
    [InlineData(RoleNames.Finance, false)]
    public async Task Only_admin_gets_the_administration_page(string role, bool expected)
    {
        var client = await As(role);

        var perms = await client.GetFromJsonAsync<List<PagePermissionDto>>("/api/v1/auth/permissions", Json);

        perms!.Single(p => p.PageKey == PageKeys.Administration).CanRead.Should().Be(expected);
    }

    [Fact]
    public async Task Permissions_reflect_the_default_matrix_for_a_role()
    {
        var client = await As(RoleNames.TitleResearcher);

        var perms = await client.GetFromJsonAsync<List<PagePermissionDto>>("/api/v1/auth/permissions", Json);

        var title = perms!.Single(p => p.PageKey == PageKeys.Title);
        (title.CanCreate, title.CanRead, title.CanUpdate, title.CanDelete).Should().Be((true, true, true, true));
        perms!.Where(p => p.PageKey == PageKeys.Payments).Should().OnlyContain(p => !p.CanRead);
    }

    [Fact]
    public async Task Permissions_require_authentication()
    {
        (await _factory.CreateClient().GetAsync("/api/v1/auth/permissions")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ---- documents -------------------------------------------------------------------------

    [Fact]
    public async Task A_document_can_be_uploaded_listed_downloaded_and_deleted()
    {
        var client = await As(RoleNames.TitleResearcher);
        var bytes = Encoding.UTF8.GetBytes("deed body");

        var upload = await client.PostAsync("/api/v1/documents", Form("TitleRecord", Parcel, "Deed.pdf", bytes, "Deed"));
        upload.StatusCode.Should().Be(HttpStatusCode.Created);
        var dto = (await upload.Content.ReadFromJsonAsync<DocumentDto>(Json))!;
        dto.ContentType.Should().Be("application/pdf");

        var list = await client.GetFromJsonAsync<List<DocumentDto>>($"/api/v1/documents?entityType=TitleRecord&entityId={Parcel}", Json);
        list.Should().ContainSingle(d => d.Id == dto.Id);

        var download = await client.GetAsync($"/api/v1/documents/{dto.Id}/content");
        download.StatusCode.Should().Be(HttpStatusCode.OK);
        download.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");
        download.Headers.GetValues("X-Content-Type-Options").Should().Contain("nosniff");
        (await download.Content.ReadAsByteArrayAsync()).Should().Equal(bytes);

        (await client.DeleteAsync($"/api/v1/documents/{dto.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetAsync($"/api/v1/documents/{dto.Id}/content")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetFromJsonAsync<List<DocumentDto>>($"/api/v1/documents?entityType=TitleRecord&entityId={Parcel}", Json))
            .Should().BeEmpty();
    }

    [Fact]
    public async Task A_role_without_update_on_the_owning_page_cannot_attach()
    {
        var appraiser = await As(RoleNames.Appraiser);

        var response = await appraiser.PostAsync("/api/v1/documents", Form("TitleRecord", Guid.NewGuid(), "x.pdf", new byte[] { 1 }));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Documents_require_authentication()
    {
        var anon = _factory.CreateClient();

        (await anon.GetAsync($"/api/v1/documents?entityType=Parcel&entityId={Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anon.PostAsync("/api/v1/documents", Form("Parcel", Guid.NewGuid(), "x.pdf", new byte[] { 1 }))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("Parcel", "malware.exe")]
    [InlineData("Planet", "deed.pdf")]
    public async Task Bad_extension_or_unknown_entity_type_is_a_400(string entityType, string fileName)
    {
        var client = await As(RoleNames.Admin);

        var response = await client.PostAsync("/api/v1/documents", Form(entityType, Guid.NewGuid(), fileName, new byte[] { 1 }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_missing_or_empty_file_is_a_400()
    {
        var client = await As(RoleNames.Admin);
        var noFile = new MultipartFormDataContent
        {
            { new StringContent("Parcel"), "entityType" },
            { new StringContent(Guid.NewGuid().ToString()), "entityId" },
        };

        (await client.PostAsync("/api/v1/documents", noFile)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.PostAsync("/api/v1/documents", Form("Parcel", Guid.NewGuid(), "empty.pdf", Array.Empty<byte>()))).StatusCode
            .Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_oversize_file_is_a_400_and_nothing_is_stored()
    {
        await using var small = RowApiFactory.WithSettings(new() { ["Documents:MaxBytes"] = "100" });
        var token = await small.DevTokenAsync(small.CreateClient(), RoleNames.Admin);
        var client = small.ClientWithToken(token);

        var response = await client.PostAsync("/api/v1/documents", Form("Parcel", Guid.NewGuid(), "big.pdf", new byte[5000]));

        response.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.RequestEntityTooLarge);
        using var scope = small.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<RowDbContext>().Documents.IgnoreQueryFilters().AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Another_tenant_cannot_see_or_fetch_a_document()
    {
        await EnsureOtherTenantAsync();
        var owner = await As(RoleNames.Admin);
        var dto = (await (await owner.PostAsync("/api/v1/documents", Form("Parcel", Parcel, "secret.pdf", new byte[] { 9 })))
            .Content.ReadFromJsonAsync<DocumentDto>(Json))!;

        var outsider = await As(RoleNames.Admin, tenant: OtherTenant);

        (await outsider.GetFromJsonAsync<List<DocumentDto>>($"/api/v1/documents?entityType=Parcel&entityId={Parcel}", Json)).Should().BeEmpty();
        (await outsider.GetAsync($"/api/v1/documents/{dto.Id}/content")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await outsider.DeleteAsync($"/api/v1/documents/{dto.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Uploads_and_deletes_are_audited()
    {
        var client = await As(RoleNames.Admin);
        var dto = (await (await client.PostAsync("/api/v1/documents", Form("Parcel", Guid.NewGuid(), "a.pdf", new byte[] { 1 })))
            .Content.ReadFromJsonAsync<DocumentDto>(Json))!;
        await client.DeleteAsync($"/api/v1/documents/{dto.Id}");

        using var scope = _factory.Services.CreateScope();
        var audit = await scope.ServiceProvider.GetRequiredService<RowDbContext>().AuditLogs.IgnoreQueryFilters()
            .Where(a => a.EntityId == dto.Id).ToListAsync();

        audit.Should().Contain(a => a.Action == Aurigo.RowManagement.Domain.Enums.AuditAction.Create);
        audit.Should().Contain(a => a.Field == "IsDeleted");
    }

    // ---- saved filters ---------------------------------------------------------------------

    [Fact]
    public async Task Saved_filters_support_the_full_lifecycle_and_stay_private_to_the_user()
    {
        var ann = await As(RoleNames.ProjectManager, user: Guid.NewGuid());
        var ben = await As(RoleNames.ProjectManager, user: Guid.NewGuid());

        var created = await ann.PostAsJsonAsync("/api/v1/saved-filters",
            new { pageKey = PageKeys.Parcels, name = "Open", filterJson = "{\"status\":\"Open\"}" });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var dto = (await created.Content.ReadFromJsonAsync<SavedFilterDto>(Json))!;

        (await ann.GetFromJsonAsync<List<SavedFilterDto>>($"/api/v1/saved-filters?pageKey={PageKeys.Parcels}", Json))
            .Should().ContainSingle(f => f.Id == dto.Id);
        (await ben.GetFromJsonAsync<List<SavedFilterDto>>($"/api/v1/saved-filters?pageKey={PageKeys.Parcels}", Json)).Should().BeEmpty();
        (await ben.DeleteAsync($"/api/v1/saved-filters/{dto.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var put = await ann.PutAsJsonAsync($"/api/v1/saved-filters/{dto.Id}", new { name = "Open v2", filterJson = "{}" });
        put.StatusCode.Should().Be(HttpStatusCode.OK);
        (await put.Content.ReadFromJsonAsync<SavedFilterDto>(Json))!.Name.Should().Be("Open v2");

        (await ann.DeleteAsync($"/api/v1/saved-filters/{dto.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ann.GetFromJsonAsync<List<SavedFilterDto>>($"/api/v1/saved-filters?pageKey={PageKeys.Parcels}", Json)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_duplicate_saved_filter_name_is_409_and_invalid_json_is_400()
    {
        var client = await As(RoleNames.ProjectManager, user: Guid.NewGuid());
        var body = new { pageKey = PageKeys.Projects, name = "Mine", filterJson = "{}" };

        (await client.PostAsJsonAsync("/api/v1/saved-filters", body)).StatusCode.Should().Be(HttpStatusCode.Created);
        (await client.PostAsJsonAsync("/api/v1/saved-filters", body)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await client.PostAsJsonAsync("/api/v1/saved-filters", new { pageKey = PageKeys.Projects, name = "Bad", filterJson = "{nope" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Saved_filters_require_authentication_and_page_access()
    {
        (await _factory.CreateClient().GetAsync($"/api/v1/saved-filters?pageKey={PageKeys.Parcels}")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);

        var finance = await As(RoleNames.Finance);
        (await finance.PostAsJsonAsync("/api/v1/saved-filters",
            new { pageKey = PageKeys.Administration, name = "x", filterJson = "{}" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ---- production safety -----------------------------------------------------------------

    [Fact]
    public async Task Production_seeds_no_demo_tenant_users_or_data()
    {
        await using var production = new RowApiFactory("Production", new()
        {
            ["Jwt:SigningKey"] = new string('p', 48), ["Security:TaxIdKey"] = Support.TestKeys.TaxIdKey,
            ["ConnectionStrings:Postgres"] = "Host=unused",
        });
        using var scope = production.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RowDbContext>();

        (await db.Tenants.AnyAsync()).Should().BeFalse();
        (await db.AppUsers.IgnoreQueryFilters().AnyAsync()).Should().BeFalse();
    }
}
