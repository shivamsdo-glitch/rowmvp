using System.Text;
using Aurigo.RowManagement.Infrastructure.ExternalClients;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace Aurigo.RowManagement.UnitTests.Persistence;

public sealed class LocalDiskFileStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "row-storage-" + Guid.NewGuid().ToString("N"));
    private readonly LocalDiskFileStorage _storage;
    private static readonly string Tenant = Guid.NewGuid().ToString();

    public LocalDiskFileStorageTests() =>
        _storage = new LocalDiskFileStorage(
            Options.Create(new FileStorageOptions { RootPath = _root }), new FakeEnv("Development"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private static MemoryStream Bytes(string s) => new(Encoding.UTF8.GetBytes(s));

    [Fact]
    public async Task Saved_bytes_come_back_exactly()
    {
        var key = await _storage.SaveAsync(Bytes("hello deed"), Tenant, default);

        await using var read = await _storage.OpenAsync(key, default);
        using var reader = new StreamReader(read);
        (await reader.ReadToEndAsync()).Should().Be("hello deed");
        (await _storage.ExistsAsync(key, default)).Should().BeTrue();
    }

    [Fact]
    public async Task Each_save_gets_its_own_key_under_the_tenant()
    {
        var a = await _storage.SaveAsync(Bytes("a"), Tenant, default);
        var b = await _storage.SaveAsync(Bytes("a"), Tenant, default);

        a.Should().NotBe(b);
        a.Should().StartWith(Guid.Parse(Tenant).ToString("N") + "/");
    }

    [Fact]
    public async Task Delete_removes_the_file_and_deleting_again_is_fine()
    {
        var key = await _storage.SaveAsync(Bytes("x"), Tenant, default);

        await _storage.DeleteAsync(key, default);
        await _storage.DeleteAsync(key, default);

        (await _storage.ExistsAsync(key, default)).Should().BeFalse();
    }

    [Fact]
    public async Task Opening_an_unknown_key_throws_file_not_found()
    {
        var act = () => _storage.OpenAsync(Guid.NewGuid().ToString("N") + "/2026/none", default);

        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("..\\..\\windows\\win.ini")]
    [InlineData("a/../../escape")]
    public async Task Keys_cannot_escape_the_storage_root(string key)
    {
        await FluentActions.Awaiting(() => _storage.OpenAsync(key, default)).Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => _storage.DeleteAsync(key, default)).Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task A_tenant_key_that_is_not_a_guid_is_refused()
    {
        var act = () => _storage.SaveAsync(Bytes("x"), "../evil", default);

        await act.Should().ThrowAsync<ArgumentException>();
    }
}
