using System.Reflection;

namespace PartyTown.Bench;

/// <summary>
/// Marks a static method as a Probe: a pointed runner of one subsystem slice whose
/// deliverable is a Probe Artifact, not a pass/fail (ADR 0011). Signature:
/// <c>static Task MyProbe(Bench bench)</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class ProbeAttribute(string description) : Attribute
{
    public string Description { get; } = description;

    /// <summary>
    /// Declares this probe needs a real <c>MemoryRepository</c> over AGE (recall / Stance / the
    /// union read behind #91). Only such probes start the bench's ephemeral Testcontainers AGE
    /// (ADR 0011 amendment); prompt-composition probes stay Docker-free (tier-0). Default false.
    /// </summary>
    public bool RequiresMemory { get; init; }
}

public sealed record ProbeInfo(string Name, string Description, bool RequiresMemory, Func<Bench, Task> Run);

public static class ProbeRegistry
{
    public static IReadOnlyList<ProbeInfo> Discover()
        => typeof(ProbeRegistry).Assembly
            .GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            .Select(m => (Method: m, Attr: m.GetCustomAttribute<ProbeAttribute>()))
            .Where(x => x.Attr is not null)
            .Select(x =>
            {
                var ps = x.Method.GetParameters();
                if (x.Method.ReturnType != typeof(Task) || ps.Length != 1 || ps[0].ParameterType != typeof(Bench))
                    throw new InvalidOperationException(
                        $"[Probe] {x.Method.DeclaringType?.Name}.{x.Method.Name} must be 'static Task {x.Method.Name}(Bench bench)'.");
                return new ProbeInfo(
                    x.Method.Name,
                    x.Attr!.Description,
                    x.Attr.RequiresMemory,
                    bench => (Task)x.Method.Invoke(null, [bench])!);
            })
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
}
