using Aurigo.RowManagement.Domain.Enums;

namespace Aurigo.RowManagement.Domain.Common;

/// <summary>What each party role means: its list, and whether it carries a share of the land.</summary>
public static class PartyRoles
{
    public static PartyCategory CategoryOf(PartyRole role) => role switch
    {
        PartyRole.FeeOwner or PartyRole.EasementHolder or PartyRole.Lienholder or PartyRole.Trustee
            or PartyRole.LifeEstateHolder or PartyRole.OtherInterest => PartyCategory.Ownership,
        PartyRole.Attorney or PartyRole.Bank or PartyRole.OwnerDesignee or PartyRole.OwnerRepresentative
            or PartyRole.OtherContact => PartyCategory.Contact,
        PartyRole.Tenant or PartyRole.Occupant => PartyCategory.Occupant,
        PartyRole.Abutter or PartyRole.InterestedParty => PartyCategory.Abutter,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown party role."),
    };

    /// <summary>The roles whose holders are "the owners" of a parcel: they give the parcel its owner name. Lienholders and trustees are interests, not owners.</summary>
    public static bool IsOwnerRole(PartyRole role) => role is PartyRole.FeeOwner or PartyRole.EasementHolder or PartyRole.LifeEstateHolder or PartyRole.OtherInterest;

    /// <summary>Undivided ownership interests are held in shares that cannot add up to more than the whole.</summary>
    public static bool CarriesShare(PartyRole role) => role is PartyRole.FeeOwner or PartyRole.EasementHolder or PartyRole.LifeEstateHolder;

    /// <summary>Only tenants have lease and insurance details.</summary>
    public static bool HasLease(PartyRole role) => role is PartyRole.Tenant;
}
