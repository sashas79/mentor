using System.Security.Cryptography;
using Mentor.Core.Agreements;

namespace Mentor.Tests.Unit.Persistence;

public sealed class AgreementContentHashTests
{
    [Theory]
    [InlineData("abc", "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")]
    [InlineData("{}", "44136fa355b3678a1146ad16f7e8649e94fb4fc21fe77e8310c060f61caaff8a")]
    public void The_hash_is_the_lowercase_hexadecimal_sha256_of_the_content(string content, string expected)
    {
        Assert.Equal(expected, AgreementContentHash.Compute(content));
    }

    [Fact]
    public void The_content_is_hashed_as_utf8()
    {
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData([0xC3, 0xA9])), AgreementContentHash.Compute("é"));
    }
}
