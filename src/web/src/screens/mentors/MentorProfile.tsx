import { ApiProblem, expect, type Schemas } from "@/api/client";
import { useIdentity } from "@/identity/IdentityProvider";
import { useSession } from "@/session/SessionProvider";
import { Badge, Facts, Page, ProblemMessage, Section, Show } from "@/ui/components";
import { useCommand, useLoad } from "@/ui/hooks";
import { label, when } from "@/ui/labels";
import { MentorProfileForm } from "./MentorProfileForm";

type Profile = Schemas["MentorProfileView"];

/**
 * Applying to mentor, and maintaining the mentor profile (FR-007, FR-010). An employee who has not applied is offered
 * the application (AC-FR-007-01); one who has sees its standing, which Talent Management decides, and maintains the
 * biography and attributes protégés read (AC-FR-010-01).
 */
export function MentorProfile() {
  const { api } = useIdentity();
  const { can, refresh } = useSession();
  const command = useCommand();
  const own = useLoad<Profile | null>(
    () =>
      expect(api.GET("/api/mentor-profiles/me")).catch((error: unknown) => {
        if (error instanceof ApiProblem && error.status === 404) return null;
        throw error;
      }),
    [],
  );

  return (
    <Page title="Mentor profile" intro="Any employee may apply to mentor. Talent Management approves applications before mentors appear to protégés.">
      <Show loaded={own}>
        {(profile) =>
          profile === null ? (
            <Section title="Apply to become a mentor">
              {can("mentor-application.create") ? (
                <MentorProfileForm
                  submitText="Apply to mentor"
                  command={command}
                  onSubmit={(edit) =>
                    void command.run(async () => {
                      await expect(api.POST("/api/mentor-applications", { body: edit }));
                      await own.reload();
                      await refresh();
                    }, "Your application is submitted. Talent Management will decide it.")
                  }
                />
              ) : (
                <ProblemMessage problem={new ApiProblem(403, "Applying to mentor is not available to you", undefined, {})} />
              )}
            </Section>
          ) : (
            <>
              <Section title="Your application">
                <Facts
                  items={[
                    [
                      "Standing",
                      <Badge tone={profile.approvalStatus === "Approved" ? "good" : profile.approvalStatus === "Pending" ? "info" : "warn"}>
                        {label(profile.approvalStatus)}
                      </Badge>,
                    ],
                    ["Decided", profile.approvalDecidedAt ? when(profile.approvalDecidedAt) : "Not yet"],
                    ["Applied", when(profile.createdAt)],
                  ]}
                />
              </Section>
              <Section title="Biography and attributes">
                {can("mentor-profile.update") ? (
                  <MentorProfileForm
                    key={profile.updatedAt}
                    initial={profile}
                    submitText="Save mentor profile"
                    command={command}
                    onSubmit={(edit) =>
                      void command.run(async () => {
                        await expect(api.PUT("/api/mentor-profiles/me", { body: edit }));
                        await own.reload();
                      }, "Your mentor profile is saved.")
                    }
                  />
                ) : (
                  <p>{profile.biography}</p>
                )}
              </Section>
            </>
          )
        }
      </Show>
    </Page>
  );
}
