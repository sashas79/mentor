namespace Mentor.Core.Audit;

/// <summary>
/// The outcomes an entry records (ADR-007). The column is text, so a module whose action has another outcome names it
/// here rather than inventing a spelling; an action with only one outcome records none.
/// </summary>
public static class AuditOutcome
{
    /// <summary>An invitation, a draft revision or a completion request was accepted or approved by its recipient.</summary>
    public const string Accepted = "ACCEPTED";

    public const string Declined = "DECLINED";

    /// <summary>A formation, a mentor application or a goal completion was approved.</summary>
    public const string Approved = "APPROVED";

    public const string Rejected = "REJECTED";

    /// <summary>The candidate was withdrawn; the entry names the evidence that stopped applying.</summary>
    public const string Withdrawn = "WITHDRAWN";

    /// <summary>A replacement revision returned the draft to Step 1; the entry names the evidence that stopped applying.</summary>
    public const string ResetToStep1 = "RESET_TO_STEP_1";
}
