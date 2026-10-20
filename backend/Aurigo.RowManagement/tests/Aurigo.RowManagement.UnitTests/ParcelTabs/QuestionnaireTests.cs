using Aurigo.RowManagement.Application.ParcelTabs;
using FluentAssertions;

namespace Aurigo.RowManagement.UnitTests.ParcelTabs;

/// <summary>Phase 1.5 slice 7b (ADR 0019): Questionnaire rules that need no database. Matches AT-RA-40, AT-X-53.</summary>
public class QuestionnaireTests
{
    private static AddQuestionnaireCommand Cmd(
        string type = "Residential", IReadOnlyList<QuestionnaireInterestInput>? interests = null, IReadOnlyList<QuestionnaireQuestionInput>? questions = null) =>
        new(Guid.NewGuid(), type, null, null, interests, questions);

    private static bool Valid(AddQuestionnaireCommand c) => new AddQuestionnaireCommandValidator().Validate(c).IsValid;

    [Theory]
    [InlineData("Residential", true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    public void AT_RA_40_A_questionnaire_needs_a_relocation_type(string type, bool valid) => Valid(Cmd(type)).Should().Be(valid);

    [Fact]
    public void AT_RA_40_Text_has_length_limits()
    {
        Valid(Cmd(new string('r', 100))).Should().BeTrue();
        Valid(Cmd(new string('r', 101))).Should().BeFalse();
        Valid(Cmd(questions: new[] { new QuestionnaireQuestionInput("Q", new string('a', 4000), null) })).Should().BeTrue();
        Valid(Cmd(questions: new[] { new QuestionnaireQuestionInput("Q", new string('a', 4001), null) })).Should().BeFalse();
        Valid(Cmd(interests: new[] { new QuestionnaireInterestInput("N", null, null, null, new string('a', 501)) })).Should().BeFalse();
    }

    [Fact]
    public void AT_RA_40_Interests_need_a_name_and_questions_need_text()
    {
        Valid(Cmd(interests: new[] { new QuestionnaireInterestInput("Kamali", null, null, null, null) })).Should().BeTrue();
        Valid(Cmd(interests: new[] { new QuestionnaireInterestInput("", null, null, null, null) })).Should().BeFalse();
        Valid(Cmd(questions: new[] { new QuestionnaireQuestionInput(" ", null, null) })).Should().BeFalse();
    }

    [Fact]
    public void AT_RA_40_Each_grid_holds_at_most_fifty_rows()
    {
        Valid(Cmd(questions: Enumerable.Range(0, 50).Select(i => new QuestionnaireQuestionInput($"Q{i}", null, null)).ToList())).Should().BeTrue();
        Valid(Cmd(questions: Enumerable.Range(0, 51).Select(i => new QuestionnaireQuestionInput($"Q{i}", null, null)).ToList())).Should().BeFalse();
        Valid(Cmd(interests: Enumerable.Range(0, 51).Select(i => new QuestionnaireInterestInput($"N{i}", null, null, null, null)).ToList())).Should().BeFalse();
    }
}
