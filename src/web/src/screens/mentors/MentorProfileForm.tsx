import { useState, type FormEvent } from "react";
import type { Schemas } from "@/api/client";
import { Field, Outcome } from "@/ui/components";
import type { Command } from "@/ui/hooks";

type Edit = Schemas["MentorProfileEdit"];

/** A mentor's concise biography and professional attributes (FR-010), as protégés will read them. */
export function MentorProfileForm({
  initial,
  submitText,
  command,
  onSubmit,
}: {
  initial?: Partial<Schemas["MentorProfileView"]>;
  submitText: string;
  command: Command;
  onSubmit: (edit: Edit) => void;
}) {
  const [biography, setBiography] = useState(initial?.biography ?? "");
  const [roleTitle, setRoleTitle] = useState(initial?.roleTitle ?? "");
  const [industryExperience, setIndustryExperience] = useState(initial?.industryExperience ?? "");
  const [mentoringExperience, setMentoringExperience] = useState(initial?.mentoringExperience ?? "");
  const optional = (text: string) => (text.trim() ? text : null);

  const submit = (event: FormEvent) => {
    event.preventDefault();
    onSubmit({
      biography,
      roleTitle: optional(roleTitle),
      industryExperience: optional(industryExperience),
      mentoringExperience: optional(mentoringExperience),
    });
  };

  return (
    <form className="stack" onSubmit={submit}>
      <Field label="Biography" hint="A brief introduction for protégés.">
        <textarea value={biography} onChange={(event) => setBiography(event.target.value)} required />
      </Field>
      <Field label="Role or title">
        <input value={roleTitle} onChange={(event) => setRoleTitle(event.target.value)} />
      </Field>
      <Field label="Industry experience">
        <textarea value={industryExperience} onChange={(event) => setIndustryExperience(event.target.value)} />
      </Field>
      <Field label="Mentoring experience">
        <textarea value={mentoringExperience} onChange={(event) => setMentoringExperience(event.target.value)} />
      </Field>
      <Outcome command={command} />
      <div>
        <button type="submit" disabled={command.pending}>
          {submitText}
        </button>
      </div>
    </form>
  );
}
