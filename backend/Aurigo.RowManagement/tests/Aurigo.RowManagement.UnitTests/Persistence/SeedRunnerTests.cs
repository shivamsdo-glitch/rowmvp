using Aurigo.RowManagement.Domain.Common;
using Aurigo.RowManagement.Domain.Entities;
using Aurigo.RowManagement.Infrastructure.Persistence.Seed;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aurigo.RowManagement.UnitTests.Persistence;

public class SeedRunnerTests
{
    private sealed class RecordingStep : ISeedStep
    {
        private readonly List<string> _log;
        private readonly bool _fail;
        public RecordingStep(string name, int order, bool devOnly, List<string> log, bool fail = false)
        {
            Name = name; Order = order; DevelopmentOnly = devOnly; _log = log; _fail = fail;
        }
        public string Name { get; }
        public int Order { get; }
        public bool DevelopmentOnly { get; }
        public Task RunAsync(CancellationToken ct)
        {
            _log.Add(Name);
            return _fail ? throw new InvalidOperationException("boom") : Task.CompletedTask;
        }
    }

    private static SeedRunner Runner(string env, IEnumerable<ISeedStep> steps) =>
        new(steps, new FakeEnv(env), NullLogger<SeedRunner>.Instance);

    [Fact]
    public async Task Steps_run_in_order_regardless_of_registration_order()
    {
        var log = new List<string>();
        await Runner(Environments.Development, new ISeedStep[]
        {
            new RecordingStep("third", 30, false, log),
            new RecordingStep("first", 10, false, log),
            new RecordingStep("second", 20, false, log),
        }).RunAsync();

        log.Should().Equal("first", "second", "third");
    }

    [Fact]
    public async Task Development_only_steps_are_skipped_in_production_but_platform_steps_still_run()
    {
        var log = new List<string>();
        await Runner(Environments.Production, new ISeedStep[]
        {
            new RecordingStep("demo tenant", 10, devOnly: true, log),
            new RecordingStep("rbac", 20, devOnly: false, log),
            new RecordingStep("demo data", 40, devOnly: true, log),
        }).RunAsync();

        log.Should().Equal("rbac");
    }

    [Theory]
    [InlineData("Staging")]
    [InlineData("Production")]
    [InlineData("Test")]
    public async Task Only_the_Development_environment_runs_demo_steps(string env)
    {
        var log = new List<string>();
        await Runner(env, new ISeedStep[] { new RecordingStep("demo", 10, devOnly: true, log) }).RunAsync();

        log.Should().BeEmpty();
    }

    [Fact]
    public async Task A_failing_step_stops_the_run_and_the_error_surfaces()
    {
        var log = new List<string>();
        var runner = Runner(Environments.Development, new ISeedStep[]
        {
            new RecordingStep("ok", 10, false, log),
            new RecordingStep("bad", 20, false, log, fail: true),
            new RecordingStep("never", 30, false, log),
        });

        var act = () => runner.RunAsync();

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
        log.Should().Equal("ok", "bad");
    }

    [Fact]
    public void The_real_steps_run_tenant_then_roles_then_users_then_data()
    {
        // Dependencies: users need roles, roles need a tenant. Guard the numbering.
        var db = TestDb.Create(TestDb.NewStoreName(), tenant: null);
        var logger = NullLogger<DefaultRbacSeedStep>.Instance;
        var steps = new ISeedStep[]
        {
            new DemoTenantSeedStep(db),
            new DefaultRbacSeedStep(db, new TenantProvisioner(db), logger),
            new DemoUsersSeedStep(db, new Aurigo.RowManagement.Infrastructure.Auth.Pbkdf2PasswordHasher()),
            new DemoDataSeedStep(db, NullLogger<DemoDataSeedStep>.Instance),
        };

        steps.Select(s => s.Order).Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();
        steps.Single(s => s is DefaultRbacSeedStep).DevelopmentOnly.Should().BeFalse("platform roles are needed in every environment");
        steps.Where(s => s is not DefaultRbacSeedStep).Should().OnlyContain(s => s.DevelopmentOnly);
    }

    [Fact]
    public async Task The_real_steps_seed_a_working_demo_tenant_idempotently()
    {
        var store = TestDb.NewStoreName();
        async Task RunAll()
        {
            await using var db = TestDb.Create(store, tenant: null);
            await Runner(Environments.Development, new ISeedStep[]
            {
                new DemoTenantSeedStep(db),
                new DefaultRbacSeedStep(db, new TenantProvisioner(db), NullLogger<DefaultRbacSeedStep>.Instance),
                new DemoUsersSeedStep(db, new Aurigo.RowManagement.Infrastructure.Auth.Pbkdf2PasswordHasher()),
                new DemoDataSeedStep(db, NullLogger<DemoDataSeedStep>.Instance),
            }).RunAsync();
        }

        await RunAll();
        await RunAll(); // second boot must change nothing

        await using var read = TestDb.Create(store, tenant: null);
        (await read.Tenants.CountAsync()).Should().Be(1);
        (await read.Roles.IgnoreQueryFilters().CountAsync()).Should().Be(RoleNames.All.Count);
        (await read.RolePagePermissions.IgnoreQueryFilters().CountAsync()).Should().Be(RoleNames.All.Count * PageKeys.All.Count);
        (await read.AppUsers.IgnoreQueryFilters().CountAsync()).Should().Be(RoleNames.All.Count);
        (await read.UserRoles.IgnoreQueryFilters().CountAsync()).Should().Be(RoleNames.All.Count);
        (await read.Tenants.SingleAsync()).Id.Should().Be(WellKnownIds.DevTenant);
        (await read.Projects.IgnoreQueryFilters().CountAsync()).Should().Be(3, "demo projects are seeded once");
        (await read.Projects.IgnoreQueryFilters().AllAsync(p => p.TenantId == WellKnownIds.DevTenant)).Should().BeTrue();
        (await read.ProjectCostEstimates.IgnoreQueryFilters().CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Production_style_run_with_an_existing_tenant_still_gets_default_roles_but_no_demo_users()
    {
        var store = TestDb.NewStoreName();
        await using (var seed = TestDb.Create(store, tenant: null))
        {
            seed.Tenants.Add(new Tenant { Id = TestDb.TenantA, Name = "Real Agency", Slug = "real", IsActive = true });
            seed.Tenants.Add(new Tenant { Id = TestDb.TenantB, Name = "Closed Agency", Slug = "closed", IsActive = false });
            await seed.SaveChangesAsync();
        }

        await using var db = TestDb.Create(store, tenant: null);
        await Runner(Environments.Production, new ISeedStep[]
        {
            new DemoTenantSeedStep(db),
            new DefaultRbacSeedStep(db, new TenantProvisioner(db), NullLogger<DefaultRbacSeedStep>.Instance),
            new DemoUsersSeedStep(db, new Aurigo.RowManagement.Infrastructure.Auth.Pbkdf2PasswordHasher()),
        }).RunAsync();

        (await db.Roles.IgnoreQueryFilters().AllAsync(r => r.TenantId == TestDb.TenantA)).Should().BeTrue("inactive tenants are not provisioned");
        (await db.Roles.IgnoreQueryFilters().CountAsync()).Should().Be(RoleNames.All.Count);
        (await db.AppUsers.IgnoreQueryFilters().AnyAsync()).Should().BeFalse("demo users never exist outside Development");
        (await db.Tenants.CountAsync()).Should().Be(2, "the demo tenant is not added in production");
    }
}
