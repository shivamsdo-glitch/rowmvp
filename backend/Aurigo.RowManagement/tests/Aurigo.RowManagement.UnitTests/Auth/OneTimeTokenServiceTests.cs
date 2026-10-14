using Aurigo.RowManagement.Application.Auth;
using Aurigo.RowManagement.Domain.Entities;
using Aurigo.RowManagement.Infrastructure.Auth;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Aurigo.RowManagement.UnitTests.Auth;

public class OneTimeTokenServiceTests
{
    private static readonly Guid User = Guid.NewGuid();

    [Fact]
    public async Task Issue_stores_only_a_hash_never_the_raw_token()
    {
        var store = TestDb.NewStoreName();
        await using var db = TestDb.Create(store, tenant: null);
        var raw = await new OneTimeTokenService(db).IssueAsync(TestDb.TenantA, User, TokenPurpose.Reset, TimeSpan.FromHours(1));

        raw.Should().NotBeNullOrWhiteSpace().And.HaveLength(43); // 32 random bytes, base64url, no padding
        var row = await db.UserTokens.IgnoreQueryFilters().SingleAsync();
        row.TokenHash.Should().NotBe(raw).And.NotContain(raw);
        row.TenantId.Should().Be(TestDb.TenantA);
        row.UserId.Should().Be(User);
    }

    [Fact]
    public async Task A_valid_token_returns_the_user_and_is_marked_used_only_once_the_caller_saves()
    {
        var store = TestDb.NewStoreName();
        string raw;
        await using (var issue = TestDb.Create(store, tenant: null))
            raw = await new OneTimeTokenService(issue).IssueAsync(TestDb.TenantA, User, TokenPurpose.Reset, TimeSpan.FromHours(1));

        await using (var consume = TestDb.Create(store, tenant: null))
        {
            var userId = await new OneTimeTokenService(consume).TryConsumeAsync(raw, TokenPurpose.Reset);
            userId.Should().Be(User);
            await consume.SaveChangesAsync(); // the caller commits, together with whatever the token authorises
        }

        await using var again = TestDb.Create(store, tenant: null);
        (await new OneTimeTokenService(again).TryConsumeAsync(raw, TokenPurpose.Reset)).Should().BeNull();
    }

    [Fact]
    public async Task Consuming_without_saving_does_not_burn_the_token()
    {
        var store = TestDb.NewStoreName();
        string raw;
        await using (var issue = TestDb.Create(store, tenant: null))
            raw = await new OneTimeTokenService(issue).IssueAsync(TestDb.TenantA, User, TokenPurpose.Reset, TimeSpan.FromHours(1));

        await using (var failed = TestDb.Create(store, tenant: null))
            (await new OneTimeTokenService(failed).TryConsumeAsync(raw, TokenPurpose.Reset)).Should().Be(User); // no SaveChanges

        await using var retry = TestDb.Create(store, tenant: null);
        (await new OneTimeTokenService(retry).TryConsumeAsync(raw, TokenPurpose.Reset)).Should().Be(User);
    }

    [Fact]
    public async Task Wrong_purpose_unknown_and_blank_tokens_are_rejected()
    {
        var store = TestDb.NewStoreName();
        await using var db = TestDb.Create(store, tenant: null);
        var service = new OneTimeTokenService(db);
        var raw = await service.IssueAsync(TestDb.TenantA, User, TokenPurpose.Reset, TimeSpan.FromHours(1));

        (await service.TryConsumeAsync(raw, TokenPurpose.Activation)).Should().BeNull();
        (await service.TryConsumeAsync("not-a-real-token", TokenPurpose.Reset)).Should().BeNull();
        (await service.TryConsumeAsync("", TokenPurpose.Reset)).Should().BeNull();
        (await service.TryConsumeAsync("   ", TokenPurpose.Reset)).Should().BeNull();
    }

    [Fact]
    public async Task An_expired_token_is_rejected()
    {
        var store = TestDb.NewStoreName();
        await using var db = TestDb.Create(store, tenant: null);
        var service = new OneTimeTokenService(db);
        var raw = await service.IssueAsync(TestDb.TenantA, User, TokenPurpose.Reset, TimeSpan.FromMinutes(-1));

        (await service.TryConsumeAsync(raw, TokenPurpose.Reset)).Should().BeNull();
    }
}
