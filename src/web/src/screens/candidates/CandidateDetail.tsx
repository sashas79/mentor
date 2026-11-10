import { Link, useParams } from "react-router-dom";
import { expect, ifMatch, type Schemas } from "@/api/client";
import { useIdentity } from "@/identity/IdentityProvider";
import { useSession } from "@/session/SessionProvider";
import { Badge, Facts, Outcome, Page, Section, Show } from "@/ui/components";
import { useCommand, useLoad } from "@/ui/hooks";
import { label, when } from "@/ui/labels";
import { MeetingForm, MeetingList } from "../meetings/Meetings";

type Candidate = Schemas["CandidateView"];

/**
 * One pairing of a request with a mentor (FR-018 to FR-020, ADR-015). The invited mentor accepts or declines; each
 * participant records their own willingness to proceed, shown separately, so that one person's decision is never read
 * as both (AC-FR-019-02, AC-FR-020-01, AC-FR-020-02); either participant may withdraw. Each decision names the version
 * read (If-Match) and the server decides whether it may be taken now. The participants propose an introductory meeting,
 * which may be arranged outside the application (AC-FR-018-02).
 */
export function CandidateDetail() {
  const { candidateId = "" } = useParams();
  const { api } = useIdentity();
  const { can, isMe } = useSession();
  const candidate = useLoad(() => expect(api.GET("/api/candidates/{candidateId}", { params: { path: { candidateId } } })), [candidateId]);
  const meetings = useLoad(() => expect(api.GET("/api/candidates/{candidateId}/meetings", { params: { path: { candidateId } } })), [candidateId]);
  const command = useCommand();
  const meetingCommand = useCommand();

  const decide = (found: Candidate, action: "accept" | "decline" | "willingness" | "withdraw", outcome: string) =>
    void command.run(
      async () => {
        const options = { params: { path: { candidateId } }, headers: ifMatch(found.version) };
        switch (action) {
          case "accept":
            await expect(api.POST("/api/candidates/{candidateId}/accept", options));
            break;
          case "decline":
            await expect(api.POST("/api/candidates/{candidateId}/decline", options));
            break;
          case "willingness":
            await expect(api.POST("/api/candidates/{candidateId}/willingness", options));
            break;
          case "withdraw":
            await expect(api.POST("/api/candidates/{candidateId}/withdraw", options));
            break;
        }

        await candidate.reload();
      },
      outcome,
      candidate.reload,
    );

  return (
    <Show loaded={candidate}>
      {(found) => {
        const asMentor = isMe(found.mentorEmployeeId);
        const asProtege = isMe(found.protegeEmployeeId);
        const participant = asMentor || asProtege;
        return (
          <Page title={`${found.protegeDisplayName} with ${found.mentorDisplayName}`}>
            <Section title="Pairing">
              <Facts
                items={[
                  ["Protégé", found.protegeDisplayName],
                  ["Mentor", found.mentorDisplayName],
                  ["Standing", <Badge tone="info">{label(found.status)}</Badge>],
                  ["Mentor's response", `${label(found.mentorResponse)}${found.mentorRespondedAt ? `, ${when(found.mentorRespondedAt)}` : ""}`],
                  ["Protégé willing to proceed", found.protegeWillingAt ? when(found.protegeWillingAt) : "Not recorded"],
                  ["Mentor willing to proceed", found.mentorWillingAt ? when(found.mentorWillingAt) : "Not recorded"],
                  ["Invited", when(found.invitedAt)],
                  ...(found.withdrawnAt ? ([["Withdrawn", when(found.withdrawnAt)]] as [string, string][]) : []),
                ]}
              />
              <p className="row">
                <Link to={`/candidates/${found.id}/draft`}>Agreement draft</Link>
                <Link to={`/candidates/${found.id}/formation`}>Formation</Link>
                {participant ? null : <span className="muted">You are viewing this pairing as Talent Management.</span>}
              </p>
            </Section>
            {participant ? (
              <Section title="Your decisions">
                <p className="muted">
                  Decisions are taken on the pairing as shown above. If it has changed in the meantime, it is read again for you to
                  decide on.
                </p>
                <div className="row">
                  {asMentor && can("candidate.respond") ? (
                    <>
                      <button type="button" disabled={command.pending} onClick={() => decide(found, "accept", "You accepted the invitation.")}>
                        Accept invitation
                      </button>
                      <button
                        type="button"
                        className="secondary"
                        disabled={command.pending}
                        onClick={() => decide(found, "decline", "You declined the invitation.")}
                      >
                        Decline invitation
                      </button>
                    </>
                  ) : null}
                  {can("candidate.record-willingness") ? (
                    <button
                      type="button"
                      disabled={command.pending}
                      onClick={() => decide(found, "willingness", "Your willingness to proceed is recorded.")}
                    >
                      Record my willingness to proceed
                    </button>
                  ) : null}
                  {can("candidate.withdraw") ? (
                    <button
                      type="button"
                      className="secondary"
                      disabled={command.pending}
                      onClick={() => decide(found, "withdraw", "The pairing is withdrawn.")}
                    >
                      Withdraw
                    </button>
                  ) : null}
                </div>
                <Outcome command={command} />
              </Section>
            ) : null}
            <Section title="Introductory meetings">
              <Show loaded={meetings}>
                {(listed) => (
                  <MeetingList
                    meetings={listed}
                    canRecord={participant && can("meeting.record")}
                    command={meetingCommand}
                    onUpdate={(meeting, change) =>
                      void meetingCommand.run(async () => {
                        await expect(api.PUT("/api/meetings/{meetingId}", { params: { path: { meetingId: meeting.id } }, body: change }));
                        await meetings.reload();
                      }, "The meeting is saved.")
                    }
                  />
                )}
              </Show>
              {participant && can("meeting.record") ? (
                <>
                  <h3>Propose an introductory meeting</h3>
                  <MeetingForm
                    submitText="Propose meeting"
                    command={meetingCommand}
                    onSubmit={(meeting) =>
                      void meetingCommand.run(async () => {
                        await expect(api.POST("/api/candidates/{candidateId}/meetings", { params: { path: { candidateId } }, body: meeting }));
                        await meetings.reload();
                      }, "The introductory meeting is proposed.")
                    }
                  />
                </>
              ) : null}
            </Section>
          </Page>
        );
      }}
    </Show>
  );
}
