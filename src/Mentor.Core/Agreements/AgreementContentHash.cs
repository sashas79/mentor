using System.Security.Cryptography;
using System.Text;

namespace Mentor.Core.Agreements;

/// <summary>
/// The content identity rule of CONTRACT-AGREEMENT-CONTENT: the lowercase hexadecimal SHA-256 of the canonical JSON,
/// encoded as UTF-8. The database checks every stored hash against the same rule.
/// </summary>
public static class AgreementContentHash
{
    public static string Compute(string canonicalContent) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalContent)));
}
