using System.Security.Cryptography;
using System.Text;
using Aurigo.RowManagement.Application.Auth;
using Aurigo.RowManagement.Application.Documents;
using Aurigo.RowManagement.Domain.Common;
using Aurigo.RowManagement.Domain.Enums;
using Aurigo.RowManagement.Domain.Errors;
using Aurigo.RowManagement.Infrastructure.ExternalClients;
using Aurigo.RowManagement.Infrastructure.Persistence;
using Aurigo.RowManagement.Infrastructure.Persistence.Seed;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Aurigo.RowManagement.UnitTests.Documents;

public class DocumentHandlerTests
{
    private static readonly Guid Parcel = Guid.NewGuid();

    /// <summary>Everything a handler needs, for one user in one tenant, over a shared store.</summary>
    private sealed class Harness : IAsyncDisposable
    {
        public RowDbContext Db { get; }
        public StubDocMgmtClient Store { get; }
        public IOptions<DocumentOptions> Options { get; }
        private readonly FakeUser _user;

        public Harness(string store, Guid tenant, Guid user, StubDocMgmtClient? sharedStore = null, long maxBytes = 1024 * 1024, params string[] roles)
        {
            Db = TestDb.Create(store, tenant, user);
            Store = sharedStore ?? new StubDocMgmtClient(NullLogger<StubDocMgmtClient>.Instance);
            Options = Microsoft.Extensions.Options.Options.Create(new DocumentOptions { MaxBytes = maxBytes });
            _user = new FakeUser(user, roles);
        }

        private PagePermissionService Permissions => new(Db, _user);

        public UploadDocumentCommandHandler Uploader => new(Db, Store, Permissions, Options);
        public ListDocumentsQueryHandler Lister => new(Db, Permissions);
        public DownloadDocumentQueryHandler Downloader => new(Db, Store, Permissions);
        public DeleteDocumentCommandHandler Deleter => new(Db, Permissions, _user);

        public UploadDocumentCommand Upload(string type, string name, byte[] content, long? declared = null, string? docType = null, Guid? entityId = null) =>
            new(type, entityId ?? Parcel, name, declared ?? content.Length, new MemoryStream(content), docType, null);

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private static async Task<string> Provision()
    {
        var store = TestDb.NewStoreName();
        await using var db = TestDb.Create(store, tenant: null);
        var provisioner = new TenantProvisioner(db);
        await provisioner.EnsureDefaultRolesAsync(TestDb.TenantA);
        await provisioner.EnsureDefaultRolesAsync(TestDb.TenantB);
        return store;
    }

    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    [Fact]
    public async Task Upload_stores_the_bytes_and_records_metadata_with_a_hash_and_a_server_chosen_content_type()
    {
        var store = await Provision();
        await using var h = new Harness(store, TestDb.TenantA, TestDb.UserA, roles: RoleNames.TitleExaminer);
        var content = Bytes("title commitment body");

        var dto = await h.Uploader.Handle(h.Upload("TitleRecord", "Commitment.PDF", content, docType: "TitleCommitment"), default);

        dto.FileName.Should().Be("Commitment.PDF");
        dto.ContentType.Should().Be("application/pdf");
        dto.SizeBytes.Should().Be(content.Length);
        dto.DocType.Should().Be("TitleCommitment");
        dto.UploadedBy.Should().Be(TestDb.UserA);

        var row = await h.Db.Documents.SingleAsync();
        row.ContentHash.Should().Be(Convert.ToBase64String(SHA256.HashData(content)));
        row.TenantId.Should().Be(TestDb.TenantA);
        row.ExternalDocId.Should().NotBeNullOrWhiteSpace();
        (await h.Store.DownloadAsync(row.ExternalDocId, default)).Should().NotBeSameAs(Stream.Null);
    }

    [Fact]
    public async Task Upload_does_not_expose_the_external_store_id_in_the_dto()
    {
        typeof(DocumentDto).GetProperties().Select(p => p.Name).Should().NotContain("ExternalDocId");
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Upload_cleans_a_hostile_file_name()
    {
        var store = await Provision();
        await using var h = new Harness(store, TestDb.TenantA, TestDb.UserA, roles: RoleNames.TitleExaminer);

        var dto = await h.Uploader.Handle(h.Upload("TitleRecord", @"..\..\boot\evil.pdf", Bytes("x")), default);

        dto.FileName.Should().Be("evil.pdf");
    }

    [Fact]
    public async Task Upload_measures_the_real_size_and_rejects_a_stream_larger_than_it_claimed()
    {
        var store = await Provision();
        await using var h = new Harness(store, TestDb.TenantA, TestDb.UserA, maxBytes: 100, roles: RoleNames.TitleExaminer);

        // The client claims 10 bytes but sends 5,000.
        var act = () => h.Uploader.Handle(h.Upload("TitleRecord", "big.pdf", new byte[5000], declared: 10), default);

        (await act.Should().ThrowAsync<InvalidRequestException>()).Which.Code.Should().Be("file_too_large");
        (await h.Db.Documents.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Upload_rejects_an_empty_stream_even_if_it_claimed_a_size()
    {
        var store = await Provision();
        await using var h = new Harness(store, TestDb.TenantA, TestDb.UserA, roles: RoleNames.TitleExaminer);

        var act = () => h.Uploader.Handle(h.Upload("TitleRecord", "empty.pdf", Array.Empty<byte>(), declared: 50), default);

        (await act.Should().ThrowAsync<InvalidRequestException>()).Which.Code.Should().Be("file_empty");
    }

    [Fact]
    public async Task Attaching_requires_update_on_the_owning_page()
    {
        var store = await Provision();

        // Appraiser can only read the title page, so cannot attach to a title record...
        await using var appraiser = new Harness(store, TestDb.TenantA, TestDb.UserA, roles: RoleNames.Appraiser);
        var denied = () => appraiser.Uploader.Handle(appraiser.Upload("TitleRecord", "t.pdf", Bytes("x")), default);
        await denied.Should().ThrowAsync<ForbiddenException>();

        // ...but can attach to an appraisal.
        var ok = await appraiser.Uploader.Handle(appraiser.Upload("Appraisal", "report.pdf", Bytes("x")), default);
        ok.EntityType.Should().Be("Appraisal");
    }

    [Fact]
    public async Task A_refused_upload_stores_nothing()
    {
        var store = await Provision();
        await using var h = new Harness(store, TestDb.TenantA, TestDb.UserA, roles: RoleNames.Finance);

        var act = () => h.Uploader.Handle(h.Upload("TitleRecord", "t.pdf", Bytes("x")), default);

        await act.Should().ThrowAsync<ForbiddenException>();
        (await h.Db.Documents.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task List_returns_only_that_entitys_live_documents_newest_first()
    {
        var store = await Provision();
        await using var h = new Harness(store, TestDb.TenantA, TestDb.UserA, roles: RoleNames.TitleExaminer);
        var other = Guid.NewGuid();

        await h.Uploader.Handle(h.Upload("TitleRecord", "first.pdf", Bytes("1"), entityId: Parcel), default);
        await Task.Delay(5);
        var second = await h.Uploader.Handle(h.Upload("TitleRecord", "second.pdf", Bytes("2"), entityId: Parcel), default);
        await h.Uploader.Handle(h.Upload("TitleRecord", "elsewhere.pdf", Bytes("3"), entityId: other), default);

        var list = await h.Lister.Handle(new ListDocumentsQuery("TitleRecord", Parcel), default);

        list.Select(d => d.FileName).Should().Equal("second.pdf", "first.pdf");
        list.First().Id.Should().Be(second.Id);
    }

    [Fact]
    public async Task List_can_filter_by_document_type()
    {
        var store = await Provision();
        await using var h = new Harness(store, TestDb.TenantA, TestDb.UserA, roles: RoleNames.TitleExaminer);
        await h.Uploader.Handle(h.Upload("TitleRecord", "a.pdf", Bytes("1"), docType: "Deed"), default);
        await h.Uploader.Handle(h.Upload("TitleRecord", "b.pdf", Bytes("2"), docType: "Plat"), default);

        var list = await h.Lister.Handle(new ListDocumentsQuery("TitleRecord", Parcel, "Plat"), default);

        list.Should().ContainSingle().Which.FileName.Should().Be("b.pdf");
    }

    [Fact]
    public async Task Listing_requires_read_on_the_owning_page()
    {
        var store = await Provision();
        await using var titleExaminer = new Harness(store, TestDb.TenantA, TestDb.UserA, roles: RoleNames.TitleExaminer);
        await titleExaminer.Uploader.Handle(titleExaminer.Upload("TitleRecord", "t.pdf", Bytes("x")), default);

        // TitleExaminer has no access to the negotiations page.
        var denied = () => titleExaminer.Lister.Handle(new ListDocumentsQuery("Offer", Parcel), default);
        await denied.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task Download_returns_the_exact_bytes_with_the_stored_name_and_type()
    {
        var store = await Provision();
        await using var h = new Harness(store, TestDb.TenantA, TestDb.UserA, roles: RoleNames.TitleExaminer);
        var content = Bytes("the real deed contents");
        var dto = await h.Uploader.Handle(h.Upload("TitleRecord", "deed.pdf", content), default);

        var file = await h.Downloader.Handle(new DownloadDocumentQuery(dto.Id), default);

        file.FileName.Should().Be("deed.pdf");
        file.ContentType.Should().Be("application/pdf");
        using var ms = new MemoryStream();
        await file.Content.CopyToAsync(ms);
        ms.ToArray().Should().Equal(content);
    }

    [Fact]
    public async Task Download_of_an_unknown_id_is_not_found()
    {
        var store = await Provision();
        await using var h = new Harness(store, TestDb.TenantA, TestDb.UserA, roles: RoleNames.Admin);

        var act = () => h.Downloader.Handle(new DownloadDocumentQuery(Guid.NewGuid()), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Download_where_the_store_has_lost_the_file_is_not_found_not_an_empty_success()
    {
        var store = await Provision();
        await using var uploader = new Harness(store, TestDb.TenantA, TestDb.UserA, roles: RoleNames.TitleExaminer);
        var dto = await uploader.Uploader.Handle(uploader.Upload("TitleRecord", "deed.pdf", Bytes("x")), default);

        // A different (empty) store instance stands in for a store that no longer has the bytes.
        await using var reader = new Harness(store, TestDb.TenantA, TestDb.UserA, roles: RoleNames.TitleExaminer);
        var act = () => reader.Downloader.Handle(new DownloadDocumentQuery(dto.Id), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Delete_hides_the_document_but_keeps_the_row_and_the_stored_bytes()
    {
        var store = await Provision();
        await using var h = new Harness(store, TestDb.TenantA, TestDb.UserA, roles: RoleNames.TitleExaminer);
        var dto = await h.Uploader.Handle(h.Upload("TitleRecord", "deed.pdf", Bytes("x")), default);
        var externalId = (await h.Db.Documents.SingleAsync()).ExternalDocId;

        await h.Deleter.Handle(new DeleteDocumentCommand(dto.Id), default);

        (await h.Lister.Handle(new ListDocumentsQuery("TitleRecord", Parcel), default)).Should().BeEmpty();
        var download = () => h.Downloader.Handle(new DownloadDocumentQuery(dto.Id), default);
        await download.Should().ThrowAsync<NotFoundException>();

        var row = await h.Db.Documents.SingleAsync();
        row.IsDeleted.Should().BeTrue();
        row.DeletedAt.Should().NotBeNull();
        row.DeletedBy.Should().Be(TestDb.UserA);
        (await h.Store.DownloadAsync(externalId, default)).Should().NotBeSameAs(Stream.Null, "the bytes are kept for audit");
    }

    [Fact]
    public async Task Deleting_twice_is_not_found_the_second_time()
    {
        var store = await Provision();
        await using var h = new Harness(store, TestDb.TenantA, TestDb.UserA, roles: RoleNames.TitleExaminer);
        var dto = await h.Uploader.Handle(h.Upload("TitleRecord", "deed.pdf", Bytes("x")), default);
        await h.Deleter.Handle(new DeleteDocumentCommand(dto.Id), default);

        var again = () => h.Deleter.Handle(new DeleteDocumentCommand(dto.Id), default);

        await again.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Deleting_is_audited_as_an_update_of_the_deleted_flag()
    {
        var store = await Provision();
        await using var h = new Harness(store, TestDb.TenantA, TestDb.UserA, roles: RoleNames.TitleExaminer);
        var dto = await h.Uploader.Handle(h.Upload("TitleRecord", "deed.pdf", Bytes("x")), default);
        await h.Deleter.Handle(new DeleteDocumentCommand(dto.Id), default);

        var audit = await h.Db.AuditLogs.Where(a => a.EntityId == dto.Id).ToListAsync();

        audit.Should().Contain(a => a.Action == AuditAction.Create);
        audit.Should().Contain(a => a.Action == AuditAction.Update && a.Field == "IsDeleted" && a.NewValue == "True");
    }

    [Fact]
    public async Task Update_without_delete_permission_cannot_remove_a_document()
    {
        // Negotiator has Standard (no delete) on closing.
        var store = await Provision();
        await using var h = new Harness(store, TestDb.TenantA, TestDb.UserA, roles: RoleNames.Negotiator);
        var dto = await h.Uploader.Handle(h.Upload("ClosingRecord", "closing.pdf", Bytes("x")), default);

        var act = () => h.Deleter.Handle(new DeleteDocumentCommand(dto.Id), default);

        await act.Should().ThrowAsync<ForbiddenException>().WithMessage("*delete*closing*");
        (await h.Db.Documents.SingleAsync()).IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task Another_tenant_cannot_list_download_or_delete_a_document_even_with_its_id()
    {
        var store = await Provision();
        var sharedStore = new StubDocMgmtClient(NullLogger<StubDocMgmtClient>.Instance);
        await using var a = new Harness(store, TestDb.TenantA, TestDb.UserA, sharedStore, roles: RoleNames.Admin);
        var dto = await a.Uploader.Handle(a.Upload("TitleRecord", "secret.pdf", Bytes("A's secret")), default);

        await using var b = new Harness(store, TestDb.TenantB, TestDb.UserB, sharedStore, roles: RoleNames.Admin);

        (await b.Lister.Handle(new ListDocumentsQuery("TitleRecord", Parcel), default)).Should().BeEmpty();
        var download = () => b.Downloader.Handle(new DownloadDocumentQuery(dto.Id), default);
        await download.Should().ThrowAsync<NotFoundException>();
        var delete = () => b.Deleter.Handle(new DeleteDocumentCommand(dto.Id), default);
        await delete.Should().ThrowAsync<NotFoundException>();

        (await a.Db.Documents.SingleAsync()).IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task Identical_files_get_identical_hashes_and_different_files_do_not()
    {
        var store = await Provision();
        await using var h = new Harness(store, TestDb.TenantA, TestDb.UserA, roles: RoleNames.TitleExaminer);

        await h.Uploader.Handle(h.Upload("TitleRecord", "a.pdf", Bytes("same")), default);
        await h.Uploader.Handle(h.Upload("TitleRecord", "b.pdf", Bytes("same")), default);
        await h.Uploader.Handle(h.Upload("TitleRecord", "c.pdf", Bytes("different")), default);

        var hashes = await h.Db.Documents.OrderBy(d => d.FileName).Select(d => d.ContentHash).ToListAsync();
        hashes[0].Should().Be(hashes[1]);
        hashes[2].Should().NotBe(hashes[0]);
    }
}

public class DocumentValidatorTests
{
    private static UploadDocumentCommandValidator Validator(long max = 1024) =>
        new(Microsoft.Extensions.Options.Options.Create(new DocumentOptions { MaxBytes = max }));

    private static UploadDocumentCommand Command(string type = "Parcel", string name = "deed.pdf", long length = 100, Guid? id = null) =>
        new(type, id ?? Guid.NewGuid(), name, length, Stream.Null, null, null);

    [Fact]
    public void A_normal_upload_is_valid() => Validator().Validate(Command()).IsValid.Should().BeTrue();

    [Theory]
    [InlineData("Planet")]
    [InlineData("")]
    [InlineData("parcel")] // case-sensitive
    public void Only_registered_entity_types_are_accepted(string type) =>
        Validator().Validate(Command(type: type)).IsValid.Should().BeFalse();

    [Fact]
    public void Every_registered_type_is_governed_by_a_real_page()
    {
        foreach (var type in AttachmentTargets.EntityTypes)
            PageKeys.IsKnown(AttachmentTargets.PageFor(type)).Should().BeTrue(type);
    }

    [Theory]
    [InlineData("malware.exe")]
    [InlineData("page.html")]
    [InlineData("noextension")]
    public void Disallowed_file_types_are_refused(string name) =>
        Validator().Validate(Command(name: name)).IsValid.Should().BeFalse();

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(1024, true)]
    [InlineData(1025, false)]
    public void The_declared_size_must_be_within_the_limit(long length, bool valid) =>
        Validator(max: 1024).Validate(Command(length: length)).IsValid.Should().Be(valid);

    [Fact]
    public void An_empty_entity_id_is_refused() =>
        Validator().Validate(Command(id: Guid.Empty)).IsValid.Should().BeFalse();
}
