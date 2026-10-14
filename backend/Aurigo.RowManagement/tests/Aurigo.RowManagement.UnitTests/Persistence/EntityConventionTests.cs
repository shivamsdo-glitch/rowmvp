using Aurigo.RowManagement.Domain.Common;
using Aurigo.RowManagement.Domain.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Aurigo.RowManagement.UnitTests.Persistence;

/// <summary>
/// Guards the platform rules for every present and future entity, so a new module cannot quietly opt out
/// of tenancy or auditing.
/// </summary>
public class EntityConventionTests
{
    /// <summary>The only entities allowed not to be an EntityBase, and why.</summary>
    private static readonly IReadOnlyDictionary<Type, string> Exempt = new Dictionary<Type, string>
    {
        [typeof(Tenant)] = "it is the tenant itself",
        [typeof(AuditLog)] = "it is written by the audit interceptor and must not audit itself",
    };

    private static IEnumerable<Type> DomainEntities() =>
        typeof(EntityBase).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && t.Namespace == typeof(Tenant).Namespace);

    [Fact]
    public void Every_domain_entity_inherits_EntityBase_unless_explicitly_exempt()
    {
        foreach (var type in DomainEntities().Where(t => !Exempt.ContainsKey(t) && t != typeof(WellKnownIds)))
            type.IsSubclassOf(typeof(EntityBase)).Should().BeTrue($"{type.Name} must inherit EntityBase");
    }

    [Fact]
    public void Every_tenant_owned_entity_in_the_model_has_the_tenant_query_filter()
    {
        using var db = TestDb.Create(TestDb.NewStoreName(), tenant: null);

        var tenantOwned = db.Model.GetEntityTypes().Where(e => typeof(ITenantOwned).IsAssignableFrom(e.ClrType)).ToList();

        tenantOwned.Should().NotBeEmpty();
        tenantOwned.Should().OnlyContain(e => e.GetQueryFilter() != null, "every tenant-owned entity needs the global filter");
    }

    [Fact]
    public void Every_entity_type_in_the_model_is_either_tenant_owned_or_explicitly_exempt()
    {
        using var db = TestDb.Create(TestDb.NewStoreName(), tenant: null);

        var unfiltered = db.Model.GetEntityTypes()
            .Where(e => !typeof(ITenantOwned).IsAssignableFrom(e.ClrType))
            .Select(e => e.ClrType)
            .ToList();

        unfiltered.Should().BeEquivalentTo(new[] { typeof(Tenant) });
    }

    [Fact]
    public void Every_entity_set_on_the_context_is_in_the_handler_facing_interface()
    {
        // A new DbSet that is not exposed on IRowDbContext cannot be used by handlers.
        var onContext = typeof(Aurigo.RowManagement.Infrastructure.Persistence.RowDbContext).GetProperties()
            .Where(p => p.PropertyType.IsGenericType && p.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>))
            .Select(p => p.Name).ToList();
        var onInterface = typeof(Aurigo.RowManagement.Application.Persistence.IRowDbContext).GetProperties()
            .Select(p => p.Name).ToList();

        onInterface.Should().Contain(onContext);
    }
}
