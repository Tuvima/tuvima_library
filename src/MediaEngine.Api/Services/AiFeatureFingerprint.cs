using System.Security.Cryptography;
using System.Text;

namespace MediaEngine.Api.Services;

internal static class AiFeatureFingerprint
{
    public static string Compute(params IEnumerable<string?>[] segments)
    {
        var payload = new StringBuilder();
        foreach (var segment in segments)
        {
            foreach (var value in segment.Where(value => !string.IsNullOrWhiteSpace(value)))
            {
                payload.Append(value!.Trim()).Append('\n');
            }

            payload.Append("--\n");
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload.ToString())));
    }

    public static string Compute(params string?[] values) => Compute(values.AsEnumerable());
}
