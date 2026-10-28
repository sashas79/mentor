namespace Mentor.Infrastructure.Persistence.Configuration;

/// <summary>The database form of <see cref="Mentor.Core.Agreements.AgreementContentHash"/>.</summary>
internal static class ContentHashRule
{
    public const string Check = "content_hash = encode(sha256(convert_to(content, 'UTF8')), 'hex')";
}
