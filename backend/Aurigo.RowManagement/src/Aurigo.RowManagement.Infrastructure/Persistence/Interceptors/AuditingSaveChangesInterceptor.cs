using System.Text.Json;
using Aurigo.RowManagement.Application.Auth;
using Aurigo.RowManagement.Domain.Common;
using Aurigo.RowManagement.Domain.Entities;
using Aurigo.RowManagement.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Aurigo.RowManagement.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Runs on every SaveChanges. Stamps tenant and audit columns on new/changed entities and writes
/// <see cref="AuditLog"/> rows for every create, update and delete, so handlers never contain audit code.
/// Also refuses to save a tenant-owned entity that has no tenant, or to move one between tenants.
/// </summary>
public sealed class AuditingSaveChangesInterceptor : SaveChangesInterceptor
{
    private const string Redacted = "[redacted]";

    /// <summary>Maintained by this interceptor itself, so changes to them are noise.</summary>
    private static readonly HashSet<string> StampFields =
        new(StringComparer.Ordinal) { nameof(IAuditable.CreatedAt), nameof(IAuditable.CreatedBy), nameof(IAuditable.UpdatedAt), nameof(IAuditable.UpdatedBy) };

    /// <summary>Secrets: the fact of a change is recorded, never the value.</summary>
    private static readonly HashSet<string> SecretFields =
        new(StringComparer.Ordinal) { nameof(AppUser.PasswordHash), nameof(UserToken.TokenHash), nameof(Party.TaxIdProtected), nameof(Party.TaxIdLast4) };

    private readonly ICurrentUser _currentUser;
    private readonly ICurrentTenant _currentTenant;

    public AuditingSaveChangesInterceptor(ICurrentUser currentUser, ICurrentTenant currentTenant)
    {
        _currentUser = currentUser;
        _currentTenant = currentTenant;
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Apply(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Apply(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Apply(DbContext? context)
    {
        if (context is null) return;

        var now = DateTime.UtcNow;
        var userId = _currentUser.IsAuthenticated ? _currentUser.Id : Guid.Empty;
        // Background flows (seeders, workers) have no HttpContext; they set the DbContext's
        // CurrentTenantId instead, so fall back to it before giving up.
        var contextTenant = _currentTenant.IsAvailable
            ? _currentTenant.Value
            : (context as RowDbContext)?.CurrentTenantId ?? Guid.Empty;

        // Snapshot first: appending AuditLog rows mutates the change tracker mid-enumeration.
        var entries = context.ChangeTracker.Entries()
            .Where(e => e.Entity is not AuditLog && e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList();

        foreach (var entry in entries)
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    // A user created without a sign-in name signs in with the email.
                    if (entry.Entity is AppUser { Username: null or "" } newUser) newUser.Username = newUser.Email;
                    StampCreated(entry, now, userId, contextTenant);
                    Append(context, entry, AuditAction.Create, null, null, Snapshot(entry, useOriginal: false), userId, now, contextTenant);
                    break;

                case EntityState.Modified:
                    GuardTenantUnchanged(entry);
                    StampUpdated(entry, now, userId);
                    foreach (var prop in entry.Properties)
                    {
                        if (!prop.IsModified || StampFields.Contains(prop.Metadata.Name)) continue;
                        // Compare the real values; redact only afterwards, or a changed secret would look unchanged.
                        if (Equals(prop.OriginalValue, prop.CurrentValue)) continue;
                        Append(context, entry, AuditAction.Update, prop.Metadata.Name,
                            Format(prop.Metadata.Name, prop.OriginalValue),
                            Format(prop.Metadata.Name, prop.CurrentValue),
                            userId, now, contextTenant);
                    }
                    break;

                case EntityState.Deleted:
                    Append(context, entry, AuditAction.Delete, null, Snapshot(entry, useOriginal: true), null, userId, now, contextTenant);
                    break;
            }
        }
    }

    private static void StampCreated(EntityEntry entry, DateTime now, Guid userId, Guid contextTenant)
    {
        if (entry.Entity is IAuditable a)
        {
            if (a.CreatedAt == default) a.CreatedAt = now;
            if (a.CreatedBy == default) a.CreatedBy = userId;
            a.UpdatedAt = now;
            a.UpdatedBy = userId;
        }

        if (entry.Entity is ITenantOwned t)
        {
            if (t.TenantId == Guid.Empty) t.TenantId = contextTenant;
            if (t.TenantId == Guid.Empty)
                throw new InvalidOperationException(
                    $"Cannot save {entry.Metadata.ClrType.Name}: it has no tenant and no tenant context is available.");
        }
    }

    private static void StampUpdated(EntityEntry entry, DateTime now, Guid userId)
    {
        if (entry.Entity is not IAuditable a) return;
        a.UpdatedAt = now;
        a.UpdatedBy = userId;
    }

    private static void GuardTenantUnchanged(EntityEntry entry)
    {
        if (entry.Entity is not ITenantOwned) return;
        var tenant = entry.Property(nameof(ITenantOwned.TenantId));
        if (tenant.IsModified && !Equals(tenant.OriginalValue, tenant.CurrentValue))
            throw new InvalidOperationException(
                $"Cannot save {entry.Metadata.ClrType.Name}: moving an entity to another tenant is not allowed.");
    }

    private static string Snapshot(EntityEntry entry, bool useOriginal)
    {
        // Scalar properties only; navigations would cause object cycles.
        var values = entry.Properties
            .Where(p => !StampFields.Contains(p.Metadata.Name))
            .ToDictionary(
                p => p.Metadata.Name,
                p => Format(p.Metadata.Name, useOriginal ? p.OriginalValue : p.CurrentValue));
        return JsonSerializer.Serialize(values);
    }

    private static string? Format(string property, object? value) =>
        SecretFields.Contains(property) ? Redacted : value?.ToString();

    private static void Append(
        DbContext context, EntityEntry entry, AuditAction action, string? field,
        string? oldValue, string? newValue, Guid userId, DateTime now, Guid contextTenant)
    {
        if (entry.Entity is not ITenantOwned owned) return;
        if (entry.Property("Id").CurrentValue is not Guid entityId) return;

        context.Set<AuditLog>().Add(new AuditLog
        {
            TenantId = owned.TenantId == Guid.Empty ? contextTenant : owned.TenantId,
            Action = action,
            EntityType = entry.Metadata.ClrType.Name,
            EntityId = entityId,
            Field = field,
            OldValue = oldValue,
            NewValue = newValue,
            ChangedBy = userId,
            ChangedAt = now,
        });
    }
}
