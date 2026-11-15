using System.Security.Cryptography;

namespace PartyTown.Grains.Generation;

public static class Shares
{
    public const int Default = 100;

    public static int Weight(int share) => Math.Max(1, share);

    public static bool TakesTurns(JobComplexity tier) => tier == JobComplexity.Decisions;

    public static double Rank(Guid? key, Guid providerId, int share)
    {
        var weight = Weight(share);
        if (key is not { } id) return -weight;

        Span<byte> input = stackalloc byte[32];
        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        id.TryWriteBytes(input);
        providerId.TryWriteBytes(input[16..]);
        SHA256.HashData(input, hash);
        var unit = ((BitConverter.ToUInt64(hash) >> 11) + 1) / (double)(1UL << 53);
        return -Math.Log(unit) / weight;
    }
}
