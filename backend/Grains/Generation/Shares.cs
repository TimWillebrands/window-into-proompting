using System.Security.Cryptography;

namespace PartyTown.Grains.Generation;

/// <summary>Sticky split of the pool by Share (ADR 0048): weighted rendezvous hashing.</summary>
public static class Shares
{
    public const int Default = 100;

    /// <summary>Lower wins. A key keeps its order over providers; work without a key goes to the biggest Share.</summary>
    public static double Rank(Guid? key, Guid providerId, int share)
    {
        var weight = Math.Max(1, share);
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
