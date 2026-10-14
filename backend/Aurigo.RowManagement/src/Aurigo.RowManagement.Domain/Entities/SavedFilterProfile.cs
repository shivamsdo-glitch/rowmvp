using Aurigo.RowManagement.Domain.Common;

namespace Aurigo.RowManagement.Domain.Entities;

/// <summary>A named set of list filters a user saved on a page, so it can be restored later.</summary>
public class SavedFilterProfile : EntityBase
{
    /// <summary>The owner. Profiles are private to their user.</summary>
    public Guid UserId { get; set; }

    /// <summary>One of <see cref="PageKeys.All"/>.</summary>
    public string PageKey { get; set; } = default!;

    public string Name { get; set; } = default!;

    /// <summary>The filter state as a JSON object. Its shape belongs to the page's UI; the API only stores it.</summary>
    public string FilterJson { get; set; } = "{}";
}
