using Aurigo.RowManagement.Application.Auth;
using Aurigo.RowManagement.Infrastructure.Persistence;
using Aurigo.RowManagement.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Aurigo.RowManagement.UnitTests;

internal sealed class FakeTenant : ICurrentTenant
{
    public FakeTenant(Guid? value) => Value = value ?? Guid.Empty;
    public Guid Value { get; }
    public bool IsAvailable => Value != Guid.Empty;
}

internal sealed class FakeUser : ICurrentUser
{
    public FakeUser(Guid? id, params string[] roles)
    {
        Id = id ?? Guid.Empty;
        Roles = roles;
    }

    public Guid Id { get; }
    public string Email => "test@row.local";
    public IReadOnlyList<string> Roles { get; }
    public bool IsAuthenticated => Id != Guid.Empty;
}

internal sealed class FakeEnv : IHostEnvironment
{
    public FakeEnv(string name) => EnvironmentName = name;
    public string EnvironmentName { get; set; }
    public string ApplicationName { get; set; } = "test";
    public string ContentRootPath { get; set; } = ".";
    public IFileProvider ContentRootFileProvider { get; set; } = null!;
}

internal static class TestDb
{
    public static readonly Guid TenantA = new("aaaaaaaa-0000-0000-0000-000000000001");
    public static readonly Guid TenantB = new("bbbbbbbb-0000-0000-0000-000000000002");
    public static readonly Guid UserA = new("aaaaaaaa-0000-0000-0000-0000000000a1");
    public static readonly Guid UserA2 = new("aaaaaaaa-0000-0000-0000-0000000000a2");
    public static readonly Guid UserB = new("bbbbbbbb-0000-0000-0000-0000000000b1");

    /// <summary>
    /// A context over a named in-memory store. Contexts created with the same name share data, so a test
    /// can write as one tenant and read as another. The auditing interceptor is wired exactly as in production.
    /// </summary>
    public static RowDbContext Create(string storeName, Guid? tenant, Guid? user = null)
    {
        var currentTenant = new FakeTenant(tenant);
        var options = new DbContextOptionsBuilder<RowDbContext>()
            .UseInMemoryDatabase(storeName)
            .AddInterceptors(new AuditingSaveChangesInterceptor(new FakeUser(user), currentTenant))
            .Options;
        return new RowDbContext(options, currentTenant);
    }

    public static string NewStoreName() => Guid.NewGuid().ToString("N");
}
