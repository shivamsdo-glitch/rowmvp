using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Aurigo.RowManagement.IntegrationTests.Support;

/// <summary>A class fixture hosting the real API over a fresh, real Postgres database.</summary>
public sealed class PgApi : IAsyncLifetime
{
    public RowApiFactory Factory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        if (PgTestDatabase.UnavailableReason is { } reason)
        {
            // Tests are skipped by [PgFact]; but when Postgres is REQUIRED (release gate) a missing database must fail loudly.
            if (PgTestDatabase.Required) throw new InvalidOperationException(reason);
            return;
        }
        // Booting the host blocks (database creation, migrations, seeding). Do that on the thread pool: xUnit runs fixture setup on
        // a sync context with one slot per core, and a blocked slot per fixture deadlocks once there are more fixtures than cores.
        Factory = await Task.Run(() =>
        {
            var factory = RowApiFactory.WithPostgres();
            _ = factory.Services; // boot the host now so migrations and seeding have run before any test queries the database
            return factory;
        });
    }

    public async Task DisposeAsync()
    {
        if (Factory is not null) await Factory.DisposeAsync();
    }

    public string Database => Factory.PgDatabase!;

    public Task<TestUser> As(string role, Guid? tenant = null, Guid? user = null) => TestUser.SignInAsync(Factory, role, tenant, user ?? Guid.NewGuid());
}

/// <summary>A signed-in caller with a few helpers so tests read like the jobs they check.</summary>
public sealed class TestUser
{
    public string Role { get; }
    public string Token { get; }
    public Guid UserId { get; }
    public Guid TenantId { get; }
    public HttpClient Http { get; }

    private TestUser(string role, string token, Guid userId, Guid tenantId, HttpClient http)
    {
        Role = role; Token = token; UserId = userId; TenantId = tenantId; Http = http;
    }

    public static async Task<TestUser> SignInAsync(RowApiFactory factory, string role, Guid? tenant, Guid user)
    {
        var anon = factory.CreateClient();
        var issued = await anon.GetFromJsonAsync<DevToken>(
            $"/api/v1/auth/dev-token?role={role}&user={user}" + (tenant is null ? "" : $"&tenant={tenant}"), RowApiFactory.JsonOptions);
        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", issued!.Token);
        return new TestUser(role, issued.Token, issued.UserId, issued.TenantId, http);
    }

    private sealed record DevToken(string Token, Guid UserId, Guid TenantId);

    public static MultipartFormDataContent Form(string entityType, Guid entityId, string fileName, byte[] bytes, string? docType = null)
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent(entityType), "entityType" },
            { new StringContent(entityId.ToString()), "entityId" },
        };
        if (docType is not null) form.Add(new StringContent(docType), "docType");
        form.Add(new ByteArrayContent(bytes), "file", fileName);
        return form;
    }

    /// <summary>A multipart body with the file name written byte-for-byte, for names HttpClient itself would refuse to send.</summary>
    public static ByteArrayContent RawUpload(string entityType, Guid entityId, string rawFileName, byte[] bytes)
    {
        const string boundary = "----rowtestboundary";
        const string crlf = "\r\n";
        using var ms = new MemoryStream();
        void W(string text) => ms.Write(Encoding.UTF8.GetBytes(text));
        W("--" + boundary + crlf + "Content-Disposition: form-data; name=\"entityType\"" + crlf + crlf + entityType + crlf);
        W("--" + boundary + crlf + "Content-Disposition: form-data; name=\"entityId\"" + crlf + crlf + entityId + crlf);
        W("--" + boundary + crlf + "Content-Disposition: form-data; name=\"file\"; filename=\"" + rawFileName + "\"" + crlf
          + "Content-Type: application/pdf" + crlf + crlf);
        ms.Write(bytes);
        W(crlf + "--" + boundary + "--" + crlf);
        var content = new ByteArrayContent(ms.ToArray());
        content.Headers.ContentType = MediaTypeHeaderValue.Parse("multipart/form-data; boundary=" + boundary);
        return content;
    }

    public Task<HttpResponseMessage> Upload(string entityType, Guid entityId, string fileName, byte[] bytes, string? docType = null) =>
        Http.PostAsync("/api/v1/documents", Form(entityType, entityId, fileName, bytes, docType));

    public async Task<JsonElement> ReadJson(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    public Task<HttpResponseMessage> PostJson(string url, string rawJson) =>
        Http.PostAsync(url, new StringContent(rawJson, Encoding.UTF8, "application/json"));

    public async Task<JsonElement> Permissions() => await ReadJson(await Http.GetAsync("/api/v1/auth/permissions"));
}
