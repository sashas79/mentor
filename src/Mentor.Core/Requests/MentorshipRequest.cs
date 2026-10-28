namespace Mentor.Core.Requests;

public enum RequestDisposition
{
    Open,
    Formed,
    Withdrawn,
}

/// <summary>
/// A protégé's mentorship request (DR-003). It stays a record of its own before and after any agreement is formed
/// from it (FR-017).
/// </summary>
public sealed class MentorshipRequest
{
    public Guid Id { get; init; }

    public Guid ProtegeEmployeeId { get; init; }

    public required string Motivation { get; set; }

    /// <summary>The technical and career development goals (FR-015).</summary>
    public required string DevelopmentGoals { get; set; }

    /// <summary>What the protégé expects beyond technical skills, kept apart from the goals (FR-016).</summary>
    public required string NontechnicalExpectations { get; set; }

    public RequestDisposition Disposition { get; set; }

    public DateTimeOffset SubmittedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }
}
