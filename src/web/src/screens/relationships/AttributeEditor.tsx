import { useState, type FormEvent } from "react";
import type { Schemas } from "@/api/client";
import { contractEnums } from "@/api/enums.gen";
import { Field, Outcome } from "@/ui/components";
import type { Command } from "@/ui/hooks";
import { label } from "@/ui/labels";

type Edit = Schemas["RelationshipAttributesRequest"];

const keep = "";

/**
 * Talent Management's edit of State, Status and IsActive (FR-047 to FR-049, ADR-006). Each is independent of the
 * others: any value the contract names may be chosen for any of them, alone or together, and an attribute left as it is
 * is not sent. No combination is derived, defaulted or refused here; the server records each change, or says why not.
 */
export function AttributeEditor({ command, onSubmit }: { command: Command; onSubmit: (edit: Edit) => void }) {
  const [state, setState] = useState(keep);
  const [status, setStatus] = useState(keep);
  const [isActive, setIsActive] = useState(keep);

  const submit = (event: FormEvent) => {
    event.preventDefault();
    const edit: Edit = {};
    if (state !== keep) edit.state = state as Edit["state"];
    if (status !== keep) edit.status = status as Edit["status"];
    if (isActive !== keep) edit.isActive = isActive as Edit["isActive"];
    onSubmit(edit);
  };

  const choice = (name: string, value: string, set: (value: string) => void, values: readonly string[]) => (
    <Field label={name}>
      <select value={value} onChange={(event) => set(event.target.value)}>
        <option value={keep}>Leave as it is</option>
        {values.map((option) => (
          <option key={option} value={option}>
            {label(option)}
          </option>
        ))}
      </select>
    </Field>
  );

  return (
    <form className="stack" onSubmit={submit} aria-label="Change relationship attributes">
      <div className="row">
        {choice("State", state, setState, contractEnums.RelationshipState)}
        {choice("Status", status, setStatus, contractEnums.RelationshipStatus)}
        {choice("IsActive", isActive, setIsActive, contractEnums.ActiveFlag)}
      </div>
      <Outcome command={command} />
      <div>
        <button type="submit" disabled={command.pending || (state === keep && status === keep && isActive === keep)}>
          Save attributes
        </button>
      </div>
    </form>
  );
}
