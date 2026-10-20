using Aurigo.RowManagement.Application.ParcelTabs;
using FluentAssertions;

namespace Aurigo.RowManagement.UnitTests.ParcelTabs;

/// <summary>Phase 1.5 slice 7a (ADR 0019): Negotiation rules that need no database. Matches AT-NE-42, AT-X-52.</summary>
public class NegotiationTests
{
    private static SaveNegotiationCommand Cmd(
        decimal? total = null, DateOnly? jatSubmitted = null, DateOnly? jatResponse = null, DateOnly? puaRequested = null, DateOnly? puaApproved = null,
        DateOnly? puaExecuted = null, DateOnly? puaClosed = null, IReadOnlyList<NegotiationOfferInput>? offers = null, IReadOnlyList<NegotiationNoteInput>? notes = null,
        IReadOnlyList<NegotiationAgentInput>? agents = null, IReadOnlyList<NegotiationSettlementInput>? settlements = null, IReadOnlyList<NegotiationRightOfEntryInput>? roe = null) =>
        new(Guid.NewGuid(), null, total, null, jatSubmitted, jatResponse, null, false, puaRequested, puaApproved, null, puaExecuted, puaClosed, notes, agents, roe, offers, settlements);

    private static bool Valid(SaveNegotiationCommand c) => new SaveNegotiationCommandValidator().Validate(c).IsValid;
    private static DateOnly D(int month, int day) => new(2026, month, day);

    [Fact]
    public void AT_NE_42_Amounts_are_zero_or_more()
    {
        Valid(Cmd(total: 0m)).Should().BeTrue();
        Valid(Cmd(total: -0.01m)).Should().BeFalse();
        Valid(Cmd(offers: new[] { new NegotiationOfferInput(null, null, null, -1m, null) })).Should().BeFalse();
        Valid(Cmd(settlements: new[] { new NegotiationSettlementInput(null, -1m, null, null, null, null, null, null) })).Should().BeFalse();
    }

    [Fact]
    public void AT_NE_42_Dates_cannot_go_backwards()
    {
        Valid(Cmd(jatSubmitted: D(6, 1), jatResponse: D(6, 1))).Should().BeTrue();
        Valid(Cmd(jatSubmitted: D(6, 2), jatResponse: D(6, 1))).Should().BeFalse();
        Valid(Cmd(puaRequested: D(7, 2), puaApproved: D(7, 1))).Should().BeFalse();
        Valid(Cmd(puaExecuted: D(7, 2), puaClosed: D(7, 1))).Should().BeFalse();
        Valid(Cmd(offers: new[] { new NegotiationOfferInput(null, D(5, 2), D(5, 1), null, null) })).Should().BeFalse();
        Valid(Cmd(roe: new[] { new NegotiationRightOfEntryInput(D(5, 2), D(5, 1), null, null, null, null) })).Should().BeFalse();
        Valid(Cmd(settlements: new[] { new NegotiationSettlementInput(null, null, D(5, 2), D(5, 1), null, null, null, null) })).Should().BeFalse();
    }

    [Fact]
    public void AT_NE_42_Notes_and_agents_need_text_and_each_grid_holds_at_most_fifty_rows()
    {
        Valid(Cmd(notes: new[] { new NegotiationNoteInput(D(5, 1), "Called.") })).Should().BeTrue();
        Valid(Cmd(notes: new[] { new NegotiationNoteInput(D(5, 1), " ") })).Should().BeFalse();
        Valid(Cmd(agents: new[] { new NegotiationAgentInput("") })).Should().BeFalse();
        Valid(Cmd(offers: Enumerable.Range(0, 50).Select(_ => new NegotiationOfferInput(null, null, null, 1m, null)).ToList())).Should().BeTrue();
        Valid(Cmd(offers: Enumerable.Range(0, 51).Select(_ => new NegotiationOfferInput(null, null, null, 1m, null)).ToList())).Should().BeFalse();
    }
}
