using Aurigo.RowManagement.Domain.Common;

namespace Aurigo.RowManagement.Domain.Entities;

/// <summary>
/// A relocation questionnaire for a parcel (Phase 1.5 Questionnaire tab, ADR 0019): the type of relocation, when it is due and was sent to the owner,
/// the ownership interests it covers and the questions with their answers. Many per parcel, numbered "QST-&lt;parcel number&gt;-&lt;sequence&gt;".
/// </summary>
public class ParcelQuestionnaire : EntityBase
{
    public Guid ParcelId { get; set; }
    public int Sequence { get; set; }
    public string Number { get; set; } = default!;
    /// <summary>A value of the Library's relocation type list.</summary>
    public string RelocationType { get; set; } = default!;
    public DateOnly? DueByDate { get; set; }
    public DateOnly? SentToOwnerDate { get; set; }
    public List<QuestionnaireInterest> Interests { get; set; } = new();
    public List<QuestionnaireQuestion> Questions { get; set; } = new();
}

/// <summary>An owner or interest holder the questionnaire is for, as written.</summary>
public class QuestionnaireInterest : EntityBase
{
    public Guid QuestionnaireId { get; set; }
    public string Name { get; set; } = default!;
    public string? Phone { get; set; }
    public string? Contact { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
}

public class QuestionnaireQuestion : EntityBase
{
    public Guid QuestionnaireId { get; set; }
    public string Question { get; set; } = default!;
    public string? Answer { get; set; }
    public string? Notes { get; set; }
}
