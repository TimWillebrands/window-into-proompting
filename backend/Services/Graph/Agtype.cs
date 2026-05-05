using System.Text.Json;

namespace PartyTown.Services.Graph;

/// <summary>
/// Wraps a single AGE <c>agtype</c> value as its serialized JSON text form
/// (the result of casting <c>agtype::text</c> in Postgres).
/// </summary>
/// <remarks>
/// AGE's <c>agtype</c> superset of JSON adds suffix tags such as <c>::vertex</c>,
/// <c>::edge</c>, <c>::numeric</c>. <see cref="As{T}"/> strips the trailing tag
/// before deserializing so the result matches a plain POCO.
/// </remarks>
public readonly record struct Agtype(string? Raw)
{
    public static readonly Agtype Null = new((string?)null);

    public bool IsNull => Raw is null;

    public T? As<T>(JsonSerializerOptions? options = null)
    {
        if (Raw is null) return default;
        var json = StripTypeTag(Raw);
        return JsonSerializer.Deserialize<T>(json, options);
    }

    public JsonElement AsElement()
    {
        if (Raw is null) throw new InvalidOperationException("Agtype value is null");
        using var doc = JsonDocument.Parse(StripTypeTag(Raw));
        return doc.RootElement.Clone();
    }

    private static string StripTypeTag(string raw)
    {
        // agtype text form: "{...}::vertex", "[...]::path", "42::numeric", etc.
        // Tag is always at the end; only strip when there's a structural close before it.
        var idx = raw.LastIndexOf("::", StringComparison.Ordinal);
        if (idx < 0) return raw;
        var tail = raw.AsSpan(idx + 2);
        foreach (var c in tail)
            if (!char.IsLetter(c)) return raw;
        return raw[..idx];
    }

    public override string ToString() => Raw ?? "null";
}
