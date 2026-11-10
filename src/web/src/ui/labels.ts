/**
 * How the contract's values read on screen. These are words only: which values exist comes from the contract
 * (contractEnums), and which value a record holds comes from the server. A value without a label shows as itself.
 */
const labels: Record<string, string> = {
  // Relationship attributes, as the BRS names them (FR-044 to FR-046).
  InProgress: "In Progress",
  OnHold: "On hold",
  Complete: "Complete",
  Blank: "Blank",
  NeedsReassigning: "Needs reassigning",
  Y: "Y",
  N: "N",
  // Duration categories (BR-010).
  SixMonths: "6 months",
  TwelveMonths: "12 months",
  TwelvePlusMonths: "12+ months",
  // Candidates and responses.
  Invited: "Invited",
  Declined: "Declined",
  Accepted: "Accepted",
  Drafting: "Drafting the agreement",
  Formed: "Formed",
  Withdrawn: "Withdrawn",
  Pending: "Pending",
  Approved: "Approved",
  Rejected: "Rejected",
  Open: "Open",
  // Evidence.
  Applicable: "Applies to the current draft",
  Superseded: "Superseded by a replacement draft",
  Digital: "Formed in the application",
  Legacy: "Historical (entered from the tracker)",
  Incomplete: "Incomplete",
  // Review exceptions.
  UnresolvedPerson: "Unresolved person",
  NoMentor: "No mentor in the source",
  UnresolvedValue: "Unresolved value",
  HeldSourceRow: "Held for review",
  Relationship: "Relationship",
  // Roles.
  Employee: "Employee",
  Applicant: "Mentor applicant",
  Mentor: "Mentor",
  TalentManagement: "Talent Management",
  TalentManagementAdministrator: "Talent Management administrator",
  Protege: "Protégé",
  // Notifications.
  MentorApplicationDecided: "Mentor application decided",
  InvitationSent: "Invitation sent",
  MentorResponded: "Mentor responded",
  WillingnessRecorded: "Willingness to proceed recorded",
  CandidateWithdrawn: "Pairing withdrawn",
  RevisionCreated: "Agreement draft revised",
  AcceptanceRecorded: "Agreement accepted",
  FormationDecided: "Formation decided",
  GoalCompletionRequested: "Goal completion requested",
  GoalCompletionDecided: "Goal completion decided",
  None: "None",
  RespondToInvitation: "Respond to the invitation",
  SelectAnotherMentor: "Select another mentor",
  RecordWillingness: "Record your willingness to proceed",
  PrepareAgreementDraft: "Prepare the agreement draft",
  AcceptCurrentRevision: "Review and accept the current draft",
  ReviseDraft: "Revise the draft",
  ArrangeFirstSession: "Arrange the first session",
  DecideGoalCompletion: "Decide the completion request",
  SubmitProgress: "Submit progress",
  Simulated: "Simulated",
  Entra: "Microsoft Entra ID",
  Introductory: "Introductory meeting",
  RecurringSession: "Recurring session",
  AdministrativeReference: "Administrative reference",
  AdministrativeRemark: "Administrative remark",
  Days: "days",
  Weeks: "weeks",
};

export function label(value: string | null | undefined): string {
  if (value === null || value === undefined) {
    return "Unknown";
  }

  return labels[value] ?? value;
}

/** A date or a time as the server sent it, readable; an absent one is unknown, never guessed. */
export function when(value: string | null | undefined): string {
  if (!value) {
    return "Unknown";
  }

  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime())) {
    return value;
  }

  // A date without a time is shown as the date it is, with no shift into another day.
  return /^\d{4}-\d{2}-\d{2}$/.test(value)
    ? parsed.toLocaleDateString(undefined, { timeZone: "UTC", year: "numeric", month: "short", day: "numeric" })
    : parsed.toLocaleString(undefined, { year: "numeric", month: "short", day: "numeric", hour: "2-digit", minute: "2-digit" });
}

/** A short form of a content identity (a SHA-256 hash), with the whole of it available on hover. */
export function shortHash(hash: string): string {
  return hash.length > 16 ? `${hash.slice(0, 16)}…` : hash;
}
