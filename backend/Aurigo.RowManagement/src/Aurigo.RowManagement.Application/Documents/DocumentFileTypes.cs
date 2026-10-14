using System.Text;

namespace Aurigo.RowManagement.Application.Documents;

/// <summary>Limits bound from the <c>Documents</c> configuration section.</summary>
public sealed class DocumentOptions
{
    public const string SectionName = "Documents";

    /// <summary>Largest accepted file. Files are buffered in memory in the MVP, so keep this modest.</summary>
    public long MaxBytes { get; set; } = 25L * 1024 * 1024;
}

/// <summary>
/// The file types ROW users attach (deeds and plats, reports, spreadsheets, photos, survey CAD) and their
/// content types. The stored content type comes from here, never from the client, and anything not listed is refused.
/// </summary>
public static class DocumentFileTypes
{
    private static readonly IReadOnlyDictionary<string, string> ContentTypeByExtension =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".pdf"] = "application/pdf",
            [".doc"] = "application/msword",
            [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            [".xls"] = "application/vnd.ms-excel",
            [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            [".csv"] = "text/csv",
            [".txt"] = "text/plain",
            [".png"] = "image/png",
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".tif"] = "image/tiff",
            [".tiff"] = "image/tiff",
            [".heic"] = "image/heic",
            [".dwg"] = "application/acad",
            [".dgn"] = "application/octet-stream",
        };

    public static IReadOnlyCollection<string> AllowedExtensions => ContentTypeByExtension.Keys.ToArray();

    /// <summary>The content type for the file's extension, or null when the extension is not allowed.</summary>
    public static string? ContentTypeFor(string fileName) =>
        ContentTypeByExtension.TryGetValue(Path.GetExtension(fileName ?? string.Empty), out var ct) ? ct : null;

    /// <summary>
    /// Reduces a client-supplied name to a safe one: no path, no control or reserved characters, bounded
    /// length, extension preserved. The result is only ever used as a label and a download name.
    /// </summary>
    public static string SanitizeFileName(string fileName)
    {
        // Browsers send bare names, but some clients send full paths in either style.
        var name = fileName.Replace('\\', '/');
        name = name[(name.LastIndexOf('/') + 1)..];

        var invalid = new HashSet<char>(Path.GetInvalidFileNameChars()) { '"', '<', '>', '|', ':', '*', '?' };
        var cleaned = new StringBuilder(name.Length);
        foreach (var c in name)
            cleaned.Append(char.IsControl(c) || invalid.Contains(c) ? '_' : c);

        var result = cleaned.ToString().Trim().TrimEnd('.');
        const int max = 150;
        if (result.Length > max)
        {
            var ext = Path.GetExtension(result);
            result = result[..(max - ext.Length)] + ext;
        }
        return result.Length == 0 ? "file" : result;
    }
}
