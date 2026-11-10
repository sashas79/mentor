import { useEffect, useState, type FormEvent } from "react";
import { ApiProblem, expect, type Schemas } from "@/api/client";
import { useIdentity } from "@/identity/IdentityProvider";
import { useSession } from "@/session/SessionProvider";
import { Facts, Field, Outcome, Page, ProblemMessage, Section } from "@/ui/components";
import { useCommand, useLoad } from "@/ui/hooks";
import { when } from "@/ui/labels";

type Profile = Schemas["EmployeeProfile"];

/**
 * The employee's own application profile (FR-005): the display name and the optional level or title the register shows.
 * It is kept in the application, with no external profile integration (AC-FR-005-02). The Entra identity it is keyed by
 * is shown and never edited.
 */
export function EmployeeProfile() {
  const { api } = useIdentity();
  const { refresh } = useSession();
  const profile = useLoad<Profile | null>(
    () =>
      expect(api.GET("/api/employees/me")).catch((error: unknown) => {
        // No record yet: saving the profile creates it.
        if (error instanceof ApiProblem && error.status === 404) return null;
        throw error;
      }),
    [],
  );
  const [displayName, setDisplayName] = useState("");
  const [levelTitle, setLevelTitle] = useState("");
  const command = useCommand();

  useEffect(() => {
    if (profile.data) {
      setDisplayName(profile.data.displayName);
      setLevelTitle(profile.data.levelTitle ?? "");
    }
  }, [profile.data]);

  const save = (event: FormEvent) => {
    event.preventDefault();
    void command.run(
      async () => {
        await expect(api.PUT("/api/employees/me", { body: { displayName, levelTitle: levelTitle.trim() ? levelTitle : null } }));
        await profile.reload();
        await refresh();
      },
      "Your profile is saved.",
    );
  };

  return (
    <Page title="My profile" intro="How you appear to mentors, protégés and Talent Management.">
      <ProblemMessage problem={profile.problem} />
      {profile.data ? (
        <Section title="Identity">
          <Facts
            items={[
              ["Microsoft Entra object identifier", <span className="hash">{profile.data.entraObjectId}</span>],
              ["Last saved", when(profile.data.updatedAt)],
            ]}
          />
        </Section>
      ) : null}
      {profile.data === null ? <p className="message message-note">You have no profile yet. Save one to begin.</p> : null}
      <Section title="Details">
        <form className="stack" onSubmit={save}>
          <Field label="Display name">
            <input value={displayName} onChange={(event) => setDisplayName(event.target.value)} required />
          </Field>
          <Field label="Level or title" hint="Optional, for example Senior Engineer.">
            <input value={levelTitle} onChange={(event) => setLevelTitle(event.target.value)} />
          </Field>
          <Outcome command={command} />
          <div>
            <button type="submit" disabled={command.pending}>
              Save profile
            </button>
          </div>
        </form>
      </Section>
    </Page>
  );
}
