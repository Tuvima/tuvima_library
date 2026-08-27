using System.Security.Cryptography;

namespace MediaEngine.Api.Security;

public sealed class BootstrapClaimService
{
    private readonly byte[] _claimCode = RandomNumberGenerator.GetBytes(16);

    public string DisplayCode => Convert.ToHexString(_claimCode);

    public bool Verify(string? supplied)
    {
        if (string.IsNullOrWhiteSpace(supplied)) return false;
        try
        {
            var bytes = Convert.FromHexString(supplied.Trim());
            return CryptographicOperations.FixedTimeEquals(_claimCode, bytes);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
