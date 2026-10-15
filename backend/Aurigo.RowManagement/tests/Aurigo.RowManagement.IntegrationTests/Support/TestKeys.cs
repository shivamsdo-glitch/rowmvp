namespace Aurigo.RowManagement.IntegrationTests.Support;

/// <summary>Keys for hosts that run outside Development, where the app refuses to start without them.</summary>
public static class TestKeys
{
    /// <summary>Base64 of 32 bytes (ROW-018 tax ID encryption).</summary>
    public const string TaxIdKey = "dGVzdC10YXgtaWQta2V5LTMyLWJ5dGVzLWxvbmctISE=";
}
