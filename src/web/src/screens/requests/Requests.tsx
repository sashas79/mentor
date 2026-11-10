import { useState, type FormEvent } from "react";
import { Link } from "react-router-dom";
import { expect } from "@/api/client";
import { useIdentity } from "@/identity/IdentityProvider";
import { useSession } from "@/session/SessionProvider";
import { Badge, Empty, Field, Outcome, Page, Section, Show } from "@/ui/components";
import { useCommand, useLoad } from "@/ui/hooks";
import { label, when } from "@/ui/labels";

/**
 * The protégé's mentorship requests (FR-014 to FR-016). A request states the motivation, the technical and career
 * development goals, and, separately, what the protégé expects beyond technical skills; the server records it against
 * the submitting employee (AC-FR-014-01, AC-FR-015-01, AC-FR-016-01).
 */
export function Requests() {
  const { api } = useIdentity();
  const { can } = useSession();
  const requests = useLoad(() => expect(api.GET("/api/mentorship-requests")), []);
  const [motivation, setMotivation] = useState("");
  const [developmentGoals, setDevelopmentGoals] = useState("");
  const [nontechnicalExpectations, setNontechnicalExpectations] = useState("");
  const command = useCommand();

  const submit = (event: FormEvent) => {
    event.preventDefault();
    void command.run(async () => {
      await expect(api.POST("/api/mentorship-requests", { body: { motivation, developmentGoals, nontechnicalExpectations } }));
      setMotivation("");
      setDevelopmentGoals("");
      setNontechnicalExpectations("");
      await requests.reload();
    }, "Your request is submitted. Invite a mentor for it from the mentor list.");
  };

  return (
    <Page title="My mentorship requests">
      <Section title="Your requests">
        <Show loaded={requests}>
          {(own) =>
            own.length === 0 ? (
              <Empty>You have not requested mentorship yet.</Empty>
            ) : (
              <ul className="list">
                {own.map((request) => (
                  <li key={request.id}>
                    <div className="row">
                      <Link to={`/requests/${request.id}`}>Request of {when(request.submittedAt)}</Link>
                      <Badge tone={request.disposition === "Open" ? "info" : "neutral"}>{label(request.disposition)}</Badge>
                    </div>
                    <p>{request.motivation}</p>
                  </li>
                ))}
              </ul>
            )
          }
        </Show>
      </Section>
      {can("mentorship-request.create") ? (
        <Section title="Request mentorship">
          <form className="stack" onSubmit={submit}>
            <Field label="Why are you seeking mentorship?">
              <textarea value={motivation} onChange={(event) => setMotivation(event.target.value)} required />
            </Field>
            <Field label="Technical and career development goals">
              <textarea value={developmentGoals} onChange={(event) => setDevelopmentGoals(event.target.value)} required />
            </Field>
            <Field label="What do you expect beyond technical skills?" hint="For example confidence, communication or leadership.">
              <textarea value={nontechnicalExpectations} onChange={(event) => setNontechnicalExpectations(event.target.value)} required />
            </Field>
            <Outcome command={command} />
            <div>
              <button type="submit" disabled={command.pending}>
                Submit request
              </button>
            </div>
          </form>
        </Section>
      ) : null}
    </Page>
  );
}
