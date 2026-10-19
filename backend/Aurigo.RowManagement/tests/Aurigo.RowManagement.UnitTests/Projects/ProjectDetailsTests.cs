using Aurigo.RowManagement.Application.ExternalServices;
using Aurigo.RowManagement.Application.Projects;
using Aurigo.RowManagement.Domain.Enums;
using Aurigo.RowManagement.Infrastructure.ExternalClients;
using FluentAssertions;

namespace Aurigo.RowManagement.UnitTests.Projects;

/// <summary>Phase 1.5 slice 4 (ADR 0016): the ROW project details rules that need no database.</summary>
public class ProjectDetailsTests
{
    private static CreateProjectCommand Cmd(
        string? phase = "Phase 1", string? location = null, string? linkedSource = null, string? linkedNumber = null,
        string? folder = null, string? properties = null, string? constructionManager = null) =>
        new(null, "Project", null, null, null, OversightType.Federal, null, null, null, null, null, null, null,
            Phase: phase, Location: location, DocumentFolderStructure: folder, DocumentProperties: properties,
            ConstructionProjectManager: constructionManager, LinkedSource: linkedSource, LinkedProjectNumber: linkedNumber);

    private static bool Valid(CreateProjectCommand c) => new CreateProjectCommandValidator().Validate(c).IsValid;

    [Fact]
    public void AT_PM_40_The_details_fields_are_all_optional_and_a_plain_project_is_valid()
    {
        Valid(Cmd(phase: null)).Should().BeTrue();
        Valid(Cmd(location: "Bangalore", folder: "TEST", properties: "MTRDOCM", constructionManager: "ritz")).Should().BeTrue();
    }

    [Theory]
    [InlineData(101)]
    public void AT_PM_40_Phase_is_limited_to_100_characters(int length) => Valid(Cmd(phase: new string('p', length))).Should().BeFalse();

    [Fact]
    public void AT_PM_40_Location_document_lists_and_construction_manager_have_length_limits()
    {
        Valid(Cmd(location: new string('l', 500))).Should().BeTrue();
        Valid(Cmd(location: new string('l', 501))).Should().BeFalse();
        Valid(Cmd(folder: new string('f', 101))).Should().BeFalse();
        Valid(Cmd(properties: new string('p', 101))).Should().BeFalse();
        Valid(Cmd(constructionManager: new string('c', 201))).Should().BeFalse();
    }

    [Theory]
    [InlineData("Planned", "PLN-2026-001", true)]
    [InlineData("Build", "BLD-2025-017", true)]
    [InlineData(null, null, true)]
    [InlineData("Other", "X-1", false)]
    [InlineData("planned", "PLN-2026-001", false)]
    [InlineData("Build", null, false)]
    [InlineData("Build", "", false)]
    [InlineData(null, "BLD-2025-017", false)]
    public void AT_PM_40_A_link_needs_both_a_known_source_and_a_number(string? source, string? number, bool valid) =>
        Valid(Cmd(linkedSource: source, linkedNumber: number)).Should().Be(valid);

    [Fact]
    public async Task AT_PM_40_The_stand_in_catalog_lists_by_source_filters_by_text_and_finds_by_number_ignoring_case()
    {
        IProjectCatalogClient catalog = new StubProjectCatalogClient();

        (await catalog.SearchAsync("Planned", null, default)).Select(p => p.Number).Should().Equal("PLN-2026-001", "PLN-2026-002", "PLN-2027-004", "PLN-2027-009");
        (await catalog.SearchAsync("Build", "bridge", default)).Should().ContainSingle().Which.Number.Should().Be("BLD-2025-017");
        (await catalog.SearchAsync("Build", "PLN", default)).Should().BeEmpty();
        (await catalog.FindAsync("Build", " bld-2025-021 ", default))!.Name.Should().Be("Pier Lane Reconstruction");
        (await catalog.FindAsync("Planned", "BLD-2025-021", default)).Should().BeNull("a Build number is not a Planned number");
    }

    [Fact]
    public void AT_PM_40_Only_Planned_and_Build_are_known_sources()
    {
        CatalogSources.All.Should().Equal("Planned", "Build");
        CatalogSources.IsKnown("Build").Should().BeTrue();
        CatalogSources.IsKnown("build").Should().BeFalse();
        CatalogSources.IsKnown(null).Should().BeFalse();
    }
}
