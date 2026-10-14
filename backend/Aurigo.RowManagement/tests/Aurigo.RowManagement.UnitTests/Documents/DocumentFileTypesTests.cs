using Aurigo.RowManagement.Application.Documents;
using FluentAssertions;

namespace Aurigo.RowManagement.UnitTests.Documents;

public class DocumentFileTypesTests
{
    [Theory]
    [InlineData("deed.pdf", "application/pdf")]
    [InlineData("DEED.PDF", "application/pdf")]
    [InlineData("plat.TIF", "image/tiff")]
    [InlineData("site photo.heic", "image/heic")]
    [InlineData("survey.dwg", "application/acad")]
    [InlineData("survey.dgn", "application/octet-stream")]
    [InlineData("offer.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    public void Allowed_extensions_map_to_a_server_chosen_content_type(string name, string expected) =>
        DocumentFileTypes.ContentTypeFor(name).Should().Be(expected);

    [Theory]
    [InlineData("run.exe")]
    [InlineData("page.html")]
    [InlineData("image.svg")]      // can carry script
    [InlineData("script.js")]
    [InlineData("archive.zip")]
    [InlineData("macro.docm")]
    [InlineData("noextension")]
    [InlineData("deed.pdf.exe")]   // the real extension is the last one
    [InlineData("")]
    public void Anything_else_is_refused(string name) =>
        DocumentFileTypes.ContentTypeFor(name).Should().BeNull();

    [Theory]
    [InlineData(@"..\..\windows\evil.pdf", "evil.pdf")]
    [InlineData("../../etc/passwd.txt", "passwd.txt")]
    [InlineData(@"C:\Users\me\Desktop\deed.pdf", "deed.pdf")]
    [InlineData("/var/tmp/deed.pdf", "deed.pdf")]
    public void Paths_are_stripped_to_the_bare_file_name(string input, string expected) =>
        DocumentFileTypes.SanitizeFileName(input).Should().Be(expected);

    [Fact]
    public void Control_and_reserved_characters_are_replaced()
    {
        var cleaned = DocumentFileTypes.SanitizeFileName("a\0b\r\nc<d>e|f\"g:h*i?.pdf");

        cleaned.Should().EndWith(".pdf");
        cleaned.Should().NotContainAny("\0", "\r", "\n", "<", ">", "|", "\"", ":", "*", "?");
    }

    [Fact]
    public void A_name_made_only_of_dots_or_spaces_falls_back_to_a_default()
    {
        DocumentFileTypes.SanitizeFileName("...").Should().Be("file");
        DocumentFileTypes.SanitizeFileName("   ").Should().Be("file");
    }

    [Fact]
    public void A_very_long_name_is_shortened_but_keeps_its_extension()
    {
        var cleaned = DocumentFileTypes.SanitizeFileName(new string('a', 400) + ".pdf");

        cleaned.Length.Should().BeLessOrEqualTo(150);
        cleaned.Should().EndWith(".pdf");
    }

    [Fact]
    public void The_allowed_list_includes_the_survey_CAD_and_photo_formats_the_vault_requires()
    {
        // ROW-034 (DWG/DGN CAD files) and ROW-162 (HEIC photos).
        DocumentFileTypes.AllowedExtensions.Should().Contain(new[] { ".dwg", ".dgn", ".heic", ".pdf" });
    }
}
