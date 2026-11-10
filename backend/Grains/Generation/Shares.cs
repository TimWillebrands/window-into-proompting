using System.Security.Cryptography;

namespace PartyTown.Grains.Generation;

/// <summary>Sticky split of the pool by Share (ADR 0048): weighted rendezvous hashing.</summary>
public static class Shares
{
    public const int Default = 100;

    public static int Weight(int share) => Math.Max(1, share);

    /// <summary>Decisions have no prompt cache to keep warm, so each call takes the next provider instead of a persona staying put.</summary>
    public static bool TakesTurns(JobComplexity tier) => tier == JobComplexity.Decisions;

    /// <summary>Lower wins. A key keeps its order over providers; work without a key goes to the biggest Share.</summary>
    public static double Rank(Guid? key, Guid providerId, int share)
    {
        var weight = Weight(share);
        if (key is not { } id) return -weight;

        // Not HashCode: that one is seeded per process, and a restart would reshuffle everyone.
        Span<byte> input = stackalloc byte[32];
        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        id.TryWriteBytes(input);
        providerId.TryWriteBytes(input[16..]);
        SHA256.HashData(input, hash);
        var unit = ((BitConverter.ToUInt64(hash) >> 11) + 1) / (double)(1UL << 53);
        return -Math.Log(unit) / weight;
    }
}
