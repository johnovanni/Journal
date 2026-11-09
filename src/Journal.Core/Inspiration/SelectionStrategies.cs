using System.Security.Cryptography;
using System.Text;

namespace Journal.Core.Inspiration;

public interface ISelectionStrategy
{
    int SelectIndex(int count, DateOnly date, Guid vaultId, string salt);
}

public sealed class DayOfYearStrategy : ISelectionStrategy
{
    public int SelectIndex(int count, DateOnly date, Guid vaultId, string salt) =>
        count == 0 ? -1 : (date.DayOfYear - 1) % count;
}

public sealed class SeededRandomStrategy : ISelectionStrategy
{
    public int SelectIndex(int count, DateOnly date, Guid vaultId, string salt)
    {
        if (count == 0) return -1;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{vaultId:N}|{date:yyyy-MM-dd}|{salt}"));
        return (int)(BitConverter.ToUInt32(hash, 0) % (uint)count);
    }
}
