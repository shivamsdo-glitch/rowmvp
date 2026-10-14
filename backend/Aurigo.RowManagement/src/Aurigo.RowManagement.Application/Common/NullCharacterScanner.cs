using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;

namespace Aurigo.RowManagement.Application.Common;

/// <summary>
/// Finds U+0000 anywhere in a request: in string properties, in collections, in dictionary keys and values, and in nested
/// request objects from this solution. Postgres text and jsonb cannot store it, so an unchecked one becomes a 500 deep inside
/// a save; the validation pipeline turns it into a normal 400 instead.
/// </summary>
internal static class NullCharacterScanner
{
    private const int MaxDepth = 8;
    private const int MaxHits = 20;
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> Properties = new();

    /// <summary>The property paths (for example "Parts[1].Number") that contain a null character.</summary>
    public static IReadOnlyList<string> Find(object? root)
    {
        var hits = new List<string>();
        Walk(root, string.Empty, 0, hits);
        return hits;
    }

    private static void Walk(object? value, string path, int depth, List<string> hits)
    {
        if (value is null || depth > MaxDepth || hits.Count >= MaxHits) return;

        if (value is string s)
        {
            if (s.Contains('\0')) hits.Add(path);
            return;
        }

        var type = value.GetType();
        if (type.IsPrimitive || type.IsEnum || value is decimal or Guid or DateTime or DateTimeOffset or DateOnly or TimeOnly or TimeSpan) return;

        if (value is IDictionary dictionary)
        {
            foreach (DictionaryEntry entry in dictionary)
            {
                var key = entry.Key?.ToString() ?? string.Empty;
                if (key.Contains('\0')) hits.Add($"{path}[key]");
                Walk(entry.Value, $"{path}[{key.Replace('\0', '?')}]", depth + 1, hits);
            }
            return;
        }

        if (value is IEnumerable items)
        {
            var i = 0;
            foreach (var item in items) Walk(item, $"{path}[{i++}]", depth + 1, hits);
            return;
        }

        // Only our own request objects are walked; framework types (streams, files) are left alone.
        if (type.Namespace?.StartsWith("Aurigo.", StringComparison.Ordinal) != true) return;
        foreach (var p in Properties.GetOrAdd(type, PublicReadableProperties))
            Walk(p.GetValue(value), path.Length == 0 ? p.Name : $"{path}.{p.Name}", depth + 1, hits);
    }

    private static PropertyInfo[] PublicReadableProperties(Type t) =>
        t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
            .ToArray();
}
