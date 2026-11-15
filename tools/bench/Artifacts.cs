using System.Text.Json;
using System.Text.Json.Serialization;

namespace PartyTown.Bench;

public sealed record Observation(DateTimeOffset At, string Label, object? Data);

public sealed class ProbeArtifact(string probe, DateTimeOffset startedAt)
{
    private readonly List<Observation> _observations = [];
    private readonly Lock _gate = new();

    public string Probe { get; } = probe;
    public DateTimeOffset StartedAt { get; } = startedAt;
    public IReadOnlyList<Observation> Observations { get { lock (_gate) return _observations.ToList(); } }
    public List<LlmCallRecorder.Recorded> LlmCalls { get; set; } = [];

    public void Observe(string label, object? data)
    {
        lock (_gate) _observations.Add(new Observation(DateTimeOffset.UtcNow, label, data));
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly JsonSerializerOptions CompactOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string Write()
    {
        var dir = Path.Combine("bench-runs", Probe);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"{StartedAt:yyyyMMdd-HHmmss}.json");
        for (var i = 2; File.Exists(path); i++)
            path = Path.Combine(dir, $"{StartedAt:yyyyMMdd-HHmmss}-{i}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
        return path;
    }

    public void Render(string artifactPath)
    {
        var line = new string('─', 76);
        Console.WriteLine();
        Console.WriteLine(line);
        Console.WriteLine($"  PROBE  {Probe}");
        Console.WriteLine($"  AT     {StartedAt:u}");
        Console.WriteLine($"  FILE   {artifactPath}");
        Console.WriteLine(line);

        var observations = Observations;
        Console.WriteLine($"\n  Observations ({observations.Count})\n");
        foreach (var o in observations)
        {
            string compact;
            try { compact = JsonSerializer.Serialize(o.Data, CompactOptions); }
            catch (Exception ex) { compact = $"<unserializable: {ex.Message}>"; }
            Console.WriteLine($"  · [{o.At:HH:mm:ss}] {o.Label}");
            Console.WriteLine($"      {compact}");
        }

        Console.WriteLine($"\n  LLM calls ({LlmCalls.Count})\n");
        if (LlmCalls.Count == 0)
        {
            Console.WriteLine("  (none — math path, no model invoked)");
        }
        foreach (var c in LlmCalls)
        {
            Console.WriteLine($"  #{c.Seq} {c.Method} → {c.EndpointGrain}  [{c.Complexity}]  {c.InvokeMs:F0}ms");
            if (c.ResponseFormat is not null)
                Console.WriteLine($"     response_format: {c.ResponseFormat}");
            Console.WriteLine();
            foreach (var m in c.Messages)
            {
                var who = m.Name is null ? m.Role : $"{m.Role}:{m.Name}";
                Console.WriteLine($"     ┌─ {who} " + new string('─', Math.Max(0, 64 - who.Length)));
                foreach (var prompt in (m.Content ?? "").Split('\n'))
                    Console.WriteLine($"     │ {prompt}");
                Console.WriteLine($"     └" + new string('─', 67));
            }
            Console.WriteLine();
        }
        Console.WriteLine(line);
    }
}
