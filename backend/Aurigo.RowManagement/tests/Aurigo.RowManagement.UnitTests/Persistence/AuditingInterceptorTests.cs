using Aurigo.RowManagement.Domain.Entities;
using Aurigo.RowManagement.Domain.Enums;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Aurigo.RowManagement.UnitTests.Persistence;

public class AuditingInterceptorTests
{
    private static AppUser NewUser(string email = "u@row.local") =>
        new() { FirstName = "Ada", LastName = "Lovelace", Email = email };

    [Fact]
    public async Task Create_stamps_tenant_and_audit_columns_and_writes_a_create_row()
    {
        var store = TestDb.NewStoreName();
        Guid id;
        await using (var db = TestDb.Create(store, TestDb.TenantA, TestDb.UserA))
        {
            var user = NewUser();
            id = user.Id;
            db.AppUsers.Add(user);
            await db.SaveChangesAsync();

            user.TenantId.Should().Be(TestDb.TenantA);
            user.CreatedBy.Should().Be(TestDb.UserA);
            user.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
            user.UpdatedBy.Should().Be(TestDb.UserA);
        }

        await using var read = TestDb.Create(store, TestDb.TenantA);
        var log = await read.AuditLogs.SingleAsync();
        log.Action.Should().Be(AuditAction.Create);
        log.EntityType.Should().Be(nameof(AppUser));
        log.EntityId.Should().Be(id);
        log.TenantId.Should().Be(TestDb.TenantA);
        log.ChangedBy.Should().Be(TestDb.UserA);
        log.Field.Should().BeNull();
        log.NewValue.Should().Contain("u@row.local");
    }

    [Fact]
    public async Task Update_writes_one_row_per_changed_field_with_old_and_new_values()
    {
        var store = TestDb.NewStoreName();
        Guid id;
        await using (var create = TestDb.Create(store, TestDb.TenantA, TestDb.UserA))
        {
            var user = NewUser();
            id = user.Id;
            create.AppUsers.Add(user);
            await create.SaveChangesAsync();
        }

        await using (var update = TestDb.Create(store, TestDb.TenantA, TestDb.UserA))
        {
            var user = await update.AppUsers.SingleAsync(u => u.Id == id);
            user.LastName = "Byron";
            user.IsLocked = true;
            await update.SaveChangesAsync();
        }

        await using var read = TestDb.Create(store, TestDb.TenantA);
        var updates = await read.AuditLogs.Where(a => a.Action == AuditAction.Update).ToListAsync();
        updates.Should().HaveCount(2);
        updates.Single(a => a.Field == "LastName").Should().Match<AuditLog>(a =>
            a.OldValue == "Lovelace" && a.NewValue == "Byron" && a.EntityId == id);
        updates.Single(a => a.Field == "IsLocked").NewValue.Should().Be("True");
    }

    [Fact]
    public async Task Update_does_not_audit_the_stamp_columns_the_interceptor_maintains_itself()
    {
        var store = TestDb.NewStoreName();
        Guid id;
        await using (var create = TestDb.Create(store, TestDb.TenantA, TestDb.UserA))
        {
            var user = NewUser();
            id = user.Id;
            create.AppUsers.Add(user);
            await create.SaveChangesAsync();
        }

        await using (var update = TestDb.Create(store, TestDb.TenantA, TestDb.UserA))
        {
            (await update.AppUsers.SingleAsync(u => u.Id == id)).FirstName = "Augusta";
            await update.SaveChangesAsync();
        }

        await using var read = TestDb.Create(store, TestDb.TenantA);
        var fields = await read.AuditLogs.Where(a => a.Action == AuditAction.Update).Select(a => a.Field).ToListAsync();
        fields.Should().Equal("FirstName");
    }

    [Fact]
    public async Task Delete_writes_a_delete_row_with_the_final_snapshot()
    {
        var store = TestDb.NewStoreName();
        Guid id;
        await using (var create = TestDb.Create(store, TestDb.TenantA, TestDb.UserA))
        {
            var user = NewUser("gone@row.local");
            id = user.Id;
            create.AppUsers.Add(user);
            await create.SaveChangesAsync();
        }

        await using (var delete = TestDb.Create(store, TestDb.TenantA, TestDb.UserA))
        {
            delete.AppUsers.Remove(await delete.AppUsers.SingleAsync(u => u.Id == id));
            await delete.SaveChangesAsync();
        }

        await using var read = TestDb.Create(store, TestDb.TenantA);
        (await read.AppUsers.AnyAsync()).Should().BeFalse();
        var log = await read.AuditLogs.SingleAsync(a => a.Action == AuditAction.Delete);
        log.EntityId.Should().Be(id);
        log.OldValue.Should().Contain("gone@row.local");
        log.NewValue.Should().BeNull();
    }

    [Fact]
    public async Task Secrets_are_redacted_in_every_audit_row()
    {
        var store = TestDb.NewStoreName();
        Guid id;
        await using (var create = TestDb.Create(store, TestDb.TenantA, TestDb.UserA))
        {
            var user = NewUser();
            user.PasswordHash = "100000.c2FsdA==.SUPER-SECRET-HASH";
            id = user.Id;
            create.AppUsers.Add(user);
            create.UserTokens.Add(new UserToken { UserId = id, Purpose = "Reset", TokenHash = "TOKEN-HASH-VALUE", ExpiresAt = DateTime.UtcNow.AddHours(1) });
            await create.SaveChangesAsync();
        }

        await using (var update = TestDb.Create(store, TestDb.TenantA, TestDb.UserA))
        {
            (await update.AppUsers.SingleAsync(u => u.Id == id)).PasswordHash = "100000.c2FsdA==.NEW-SECRET-HASH";
            await update.SaveChangesAsync();
        }

        await using var read = TestDb.Create(store, TestDb.TenantA);
        var rows = await read.AuditLogs.ToListAsync();
        var everything = string.Join("|", rows.SelectMany(r => new[] { r.OldValue, r.NewValue }));
        everything.Should().NotContain("SUPER-SECRET-HASH")
            .And.NotContain("NEW-SECRET-HASH")
            .And.NotContain("TOKEN-HASH-VALUE");

        var change = rows.Single(r => r.Field == "PasswordHash");
        change.OldValue.Should().Be("[redacted]");
        change.NewValue.Should().Be("[redacted]");
    }

    [Fact]
    public async Task The_audit_log_is_never_audited_itself()
    {
        var store = TestDb.NewStoreName();
        await using (var db = TestDb.Create(store, TestDb.TenantA, TestDb.UserA))
        {
            db.AppUsers.Add(NewUser());
            await db.SaveChangesAsync();
        }

        await using var read = TestDb.Create(store, TestDb.TenantA);
        (await read.AuditLogs.AnyAsync(a => a.EntityType == nameof(AuditLog))).Should().BeFalse();
    }

    [Fact]
    public async Task Saving_a_tenant_owned_entity_with_no_tenant_anywhere_is_refused()
    {
        await using var db = TestDb.Create(TestDb.NewStoreName(), tenant: null);
        db.AppUsers.Add(NewUser());

        var act = () => db.SaveChangesAsync();
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*no tenant*");
    }

    [Fact]
    public async Task An_explicit_tenant_on_the_entity_is_allowed_without_a_tenant_context()
    {
        // Used by anonymous flows, e.g. issuing an activation token for a known user.
        var store = TestDb.NewStoreName();
        await using (var db = TestDb.Create(store, tenant: null))
        {
            db.UserTokens.Add(new UserToken
            {
                TenantId = TestDb.TenantA, UserId = Guid.NewGuid(), Purpose = "Activation",
                TokenHash = "h", ExpiresAt = DateTime.UtcNow.AddDays(1),
            });
            await db.SaveChangesAsync();
        }

        await using var read = TestDb.Create(store, TestDb.TenantA);
        (await read.AuditLogs.SingleAsync()).TenantId.Should().Be(TestDb.TenantA);
    }

    [Fact]
    public async Task Moving_an_entity_to_another_tenant_is_refused()
    {
        var store = TestDb.NewStoreName();
        Guid id;
        await using (var create = TestDb.Create(store, TestDb.TenantA))
        {
            var user = NewUser();
            id = user.Id;
            create.AppUsers.Add(user);
            await create.SaveChangesAsync();
        }

        await using var move = TestDb.Create(store, TestDb.TenantA);
        (await move.AppUsers.SingleAsync(u => u.Id == id)).TenantId = TestDb.TenantB;

        var act = () => move.SaveChangesAsync();
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*another tenant*");
    }

    [Fact]
    public async Task Anonymous_saves_record_an_empty_actor_rather_than_failing()
    {
        var store = TestDb.NewStoreName();
        await using (var db = TestDb.Create(store, TestDb.TenantA, user: null))
        {
            db.AppUsers.Add(NewUser());
            await db.SaveChangesAsync();
        }

        await using var read = TestDb.Create(store, TestDb.TenantA);
        (await read.AuditLogs.SingleAsync()).ChangedBy.Should().Be(Guid.Empty);
    }

    [Fact]
    public void Synchronous_SaveChanges_is_audited_too()
    {
        var store = TestDb.NewStoreName();
        using (var db = TestDb.Create(store, TestDb.TenantA, TestDb.UserA))
        {
            db.AppUsers.Add(NewUser());
            db.SaveChanges();
        }

        using var read = TestDb.Create(store, TestDb.TenantA);
        read.AuditLogs.Count().Should().Be(1);
    }
}
