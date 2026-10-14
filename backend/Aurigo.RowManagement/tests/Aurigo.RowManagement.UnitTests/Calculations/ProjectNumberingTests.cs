using Aurigo.RowManagement.Application.Calculations;
using FluentAssertions;

namespace Aurigo.RowManagement.UnitTests.Calculations;

/// <summary>ROW-001: configurable project-number format.</summary>
public class ProjectNumberingTests
{
    [Theory]
    [InlineData("{FY}-{SEQ:4}", 2026, 7, "2026-0007")]
    [InlineData("P{YY}/{SEQ:3}", 2026, 12, "P26/012")]
    [InlineData("ROW-{SEQ:1}", 2026, 5, "ROW-5")]
    [InlineData("{FY}-{SEQ:4}", 2026, 12345, "2026-12345")]
    public void ROW_001_Render_fills_tokens_and_pads_the_sequence(string pattern, int fy, int seq, string expected) =>
        ProjectNumbering.Render(pattern, fy, seq).Should().Be(expected);

    [Theory]
    [InlineData("{FY}-{SEQ:4}")]
    [InlineData("{YY}{SEQ:9}")]
    [InlineData("ROW_{SEQ:1}")]
    public void ROW_001_Valid_patterns_have_no_errors(string pattern) =>
        ProjectNumbering.Validate(pattern).Should().BeEmpty();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{FY}-")]                  // no sequence
    [InlineData("{SEQ:4}-{SEQ:4}")]        // two sequences
    [InlineData("{SEQ:0}")]                // width below range
    [InlineData("{SEQ:10}")]               // width above range
    [InlineData("{SEQ}")]                  // width required
    [InlineData("{FY:4}-{SEQ:4}")]         // year tokens take no width
    [InlineData("{BOGUS}-{SEQ:4}")]        // unknown token
    [InlineData("A B-{SEQ:4}")]            // space not allowed
    [InlineData("<x>-{SEQ:4}")]            // illegal literal characters
    public void ROW_001_Invalid_patterns_are_reported(string? pattern) =>
        ProjectNumbering.Validate(pattern).Should().NotBeEmpty();

    [Fact]
    public void ROW_001_A_pattern_whose_rendered_number_could_exceed_the_limit_is_rejected()
    {
        var pattern = new string('A', ProjectNumbering.MaxLength) + "{SEQ:4}";
        ProjectNumbering.Validate(pattern).Should().NotBeEmpty();
    }

    [Fact]
    public void ROW_001_Sequence_restarts_per_year_only_when_the_pattern_has_a_year_token()
    {
        ProjectNumbering.ScopeKey("{FY}-{SEQ:4}", 2026).Should().Be("2026");
        ProjectNumbering.ScopeKey("P{YY}-{SEQ:4}", 2027).Should().Be("2027");
        ProjectNumbering.ScopeKey("ROW-{SEQ:4}", 2026).Should().Be(ProjectNumbering.AllScope);
    }

    [Theory]
    [InlineData(2026, 1, 1, 1, 2026)]     // calendar-year agency
    [InlineData(2026, 12, 31, 1, 2026)]
    [InlineData(2025, 9, 30, 10, 2025)]   // day before an October start is still the old year
    [InlineData(2025, 10, 1, 10, 2026)]   // first day of an October-start year is named for the year it ends
    [InlineData(2026, 1, 15, 7, 2026)]
    [InlineData(2026, 7, 1, 7, 2027)]
    public void ROW_001_Fiscal_year_boundaries(int y, int m, int d, int startMonth, int expected) =>
        ProjectNumbering.FiscalYearOf(new DateOnly(y, m, d), startMonth).Should().Be(expected);
}
