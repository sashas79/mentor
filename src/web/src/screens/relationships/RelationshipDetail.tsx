import { Link, useParams } from "react-router-dom";
import { expect, ifMatch, type Schemas } from "@/api/client";
import { useIdentity } from "@/identity/IdentityProvider";
import { useSession } from "@/session/SessionProvider";
import { Badge, Empty, Facts, Outcome, Page, Section, Show } from "@/ui/components";
import { useCommand, useLoad } from "@/ui/hooks";
import { label, when } from "@/ui/labels";
import { MeetingForm, MeetingList } from "../meetings/Meetings";
import { AttributeEditor } from "./AttributeEditor";
import { GoalCard } from "./Goals";

/** An attribute as the server holds it; a legacy value the source left unresolved is unknown, never a value. */
function attribute(value: string | null | undefined) {
  return value === null || value === undefined ? <span className="unknown">Unknown — unresolved in the source</span> : label(value);
}

/**
 * A formed mentorship (COMP-RELATIONSHIP-ADMIN, COMP-GOALS-PROGRESS, COMP-MEETINGS-NOTIFICATIONS). The parties read it.
 * Talent Management reads it and changes State, Status and IsActive on the version it read (FR-047 to FR-049, ADR-005).
 * The protégé submits progress and requests completion, and the mentor decides (FR-035 to FR-037). Both see the agreed
 * meeting frequency and length and the planned sessions (FR-055, FR-056).
 */
export function RelationshipDetail() {
  const { relationshipId = "" } = useParams();
  const { api } = useIdentity();
  const { can, isMe } = useSession();
  const relationship = useLoad(() => expect(api.GET("/api/relationships/{relationshipId}", { params: { path: { relationshipId } } })), [relationshipId]);
  const goals = useLoad(() => expect(api.GET("/api/relationships/{relationshipId}/goals", { params: { path: { relationshipId } } })), [relationshipId]);
  const schedule = useLoad(() => expect(api.GET("/api/relationships/{relationshipId}/meetings", { params: { path: { relationshipId } } })), [relationshipId]);
  const edit = useCommand();
  const goalCommand = useCommand();
  const meetingCommand = useCommand();

  return (
    <Show loaded={relationship}>
      {(found) => {
        const asProtege = isMe(found.protege.employeeId);
        const asMentor = isMe(found.mentor.employeeId);
        const participant = asProtege || asMentor;

        const progress = (goal: Schemas["GoalView"], update: string, completion: boolean) =>
          goalCommand.run(
            async () => {
              const options = { params: { path: { goalId: goal.id } }, body: { progressUpdate: update } };
              await expect(completion ? api.POST("/api/goals/{goalId}/completion-requests", options) : api.POST("/api/goals/{goalId}/progress", options));
              await goals.reload();
            },
            completion ? "Completion requested. The goal stays incomplete until your mentor decides." : "Your progress is submitted.",
            goals.reload,
          );

        return (
          <Page title={`${found.protege.displayName} mentored by ${found.mentor.displayName}`}>
            <Section title="Mentorship">
              <Facts
                items={[
                  ["Provenance", <Badge tone={found.provenance === "Legacy" ? "warn" : "neutral"}>{label(found.provenance)}</Badge>],
                  ["State", attribute(found.state)],
                  ["Status", attribute(found.status)],
                  ["IsActive", attribute(found.isActive)],
                  ["Duration", found.durationCategory ? label(found.durationCategory) : <span className="unknown">Unknown</span>],
                  ["Start date", found.startDate ? when(found.startDate) : <span className="unknown">Unknown</span>],
                  ["End date", found.endDate ? when(found.endDate) : <span className="unknown">Unknown</span>],
                  ...(found.source ? ([["Source", `${found.source.file}, row ${found.source.row}`]] as [string, string][]) : []),
                ]}
              />
              {found.candidateId ? (
                <p>
                  <Link to={`/candidates/${found.candidateId}/formation`}>Formation and the formed agreement</Link>
                </p>
              ) : null}
            </Section>

            {can("relationship.update-attributes") ? (
              <Section title="Change attributes">
                <p className="muted">
                  State, Status and IsActive are independent: change any of them, alone or together. Changes are made on the values shown
                  above; if the mentorship has changed meanwhile it is read again for you to decide on.
                </p>
                <AttributeEditor
                  key={String(found.version)}
                  command={edit}
                  onSubmit={(attributes) =>
                    void edit.run(
                      async () => {
                        await expect(
                          api.PATCH("/api/relationships/{relationshipId}", {
                            params: { path: { relationshipId } },
                            headers: ifMatch(found.version),
                            body: attributes,
                          }),
                        );
                        await relationship.reload();
                      },
                      "The attributes are saved.",
                      relationship.reload,
                    )
                  }
                />
              </Section>
            ) : null}

            <Section title="Development goals">
              <Outcome command={goalCommand} />
              <Show loaded={goals}>
                {(agreed) =>
                  agreed.length === 0 ? (
                    <Empty>This mentorship has no agreed goals.</Empty>
                  ) : (
                    <ul className="list">
                      {agreed.map((goal) => (
                        <GoalCard
                          key={goal.id}
                          goal={goal}
                          mentorName={found.mentor.displayName}
                          asProtege={asProtege && can("goals.submit-progress")}
                          asMentor={asMentor && can("goals.decide-completion")}
                          command={goalCommand}
                          onProgress={(item, update) => progress(item, update, false)}
                          onRequestCompletion={(item, update) => progress(item, update, true)}
                          onDecide={(item, requestId, decision) =>
                            void goalCommand.run(
                              async () => {
                                await expect(
                                  api.POST("/api/goals/{goalId}/completion-requests/{requestId}/decision", {
                                    params: { path: { goalId: item.id, requestId } },
                                    body: { decision },
                                  }),
                                );
                                await goals.reload();
                              },
                              decision === "Approved" ? "Completion approved: the goal is complete." : "Completion rejected: the goal stays incomplete.",
                              goals.reload,
                            )
                          }
                        />
                      ))}
                    </ul>
                  )
                }
              </Show>
            </Section>

            <Section title="Meetings">
              <Show loaded={schedule}>
                {(planned) => (
                  <>
                    <Facts
                      items={[
                        ["Agreed frequency", planned.agreedFrequencyWeeks !== null ? `Every ${planned.agreedFrequencyWeeks} weeks` : <span className="unknown">Not agreed</span>],
                        [
                          "Agreed approximate length",
                          planned.agreedDurationMinutes !== null ? `${planned.agreedDurationMinutes} minutes` : <span className="unknown">Not agreed</span>,
                        ],
                      ]}
                    />
                    <h3>Sessions</h3>
                    <MeetingList
                      meetings={planned.sessions}
                      canRecord={participant && can("meeting.record")}
                      command={meetingCommand}
                      onUpdate={(meeting, change) =>
                        void meetingCommand.run(async () => {
                          await expect(api.PUT("/api/meetings/{meetingId}", { params: { path: { meetingId: meeting.id } }, body: change }));
                          await schedule.reload();
                        }, "The session is saved.")
                      }
                    />
                  </>
                )}
              </Show>
              {participant && can("meeting.record") ? (
                <>
                  <h3>Plan a session</h3>
                  <MeetingForm
                    submitText="Plan session"
                    command={meetingCommand}
                    onSubmit={(meeting) =>
                      void meetingCommand.run(async () => {
                        await expect(api.POST("/api/relationships/{relationshipId}/meetings", { params: { path: { relationshipId } }, body: meeting }));
                        await schedule.reload();
                      }, "The session is planned.")
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
