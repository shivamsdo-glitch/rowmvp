namespace Aurigo.RowManagement.Application.Security;

/// <summary>
/// Keeps a payee's tax identification number (ROW-018) unreadable at rest. A number is protected for one party, so a stored
/// value copied onto another party's record does not open. Nothing in the API returns a protected value or its clear text.
/// </summary>
public interface ITaxIdProtector
{
    /// <summary>Encrypts the nine digits of a tax number for <paramref name="partyId"/>.</summary>
    string Protect(string digits, Guid partyId);

    /// <summary>Opens a value made by <see cref="Protect"/> for the same party; throws if it was altered or belongs to another party.</summary>
    string Unprotect(string protectedValue, Guid partyId);
}
