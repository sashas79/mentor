# Engineering Harness — Claude Code Adapter v0.3.0

You are operating inside an Engineering Harness project.

The deterministic Engineering Harness core is authoritative for deterministic repository facts, lifecycle state, validation results, baseline validity/currentness, blockers, and next permitted action. Do not independently reconstruct those conclusions from conversational context or repository forensics.

## Authority boundaries

- `/project-pack/**` is human/project-owned input. Never silently modify it.
- `/engineering/**` is materialised only by authorised Harness profile-resolution/bootstrap operations.
- `/spec/**`, `/architecture/**`, `/ai/**`, and `/work/tasks/**` are generated only by their authorised lifecycle operations.
- `/baselines/**` and governed `/evidence/**` records are append-only under Harness semantics.
- `/work/state/**` is runtime state written only by `/execute`; it never changes the approved Work definition.
- Human approval decisions must be explicit and captured through the Harness decision mechanism.

## Configuration

`init` records how to reach the Harness core in `.claude/settings.json`:

- `ENGINEERING_HARNESS_CORE` — the core command, run from the project root (for example `../harness-core`).

The core resolves the Harness root by itself: `ENGINEERING_HARNESS_ROOT` when the operator sets it, otherwise the `_root` directory beside the binary. Never pass a root, never guess a core command, and never download or select a release yourself.

## Bootstrap

Use `/bootstrap status` to obtain deterministic status through the configured Harness core.
Use `/bootstrap` only to perform the next operation that the core reports as permitted.

On a freshly initialised project the first permitted step is completing `project-pack/` from `project-pack/PROJECT-PACK-BRIEF.md`, which is the operator's explicit written instruction. `/bootstrap` does that, stops at `PROJECT_INPUT_READY`, and leaves the decision to run `bootstrap_repository` to the operator.

For the AI-assisted operations `ingest_specification`, `derive_architecture` and `generate_work_items`, `/bootstrap` authors the input document from the Pack's sources and any baselined predecessor and hands it to the core; the core alone validates it and writes it into the repository. `derive_project_ai_policy` takes no input document: the core derives `ai/policy.yaml` from the effective policy controls and the current Architecture Baseline, and `/bootstrap` only invokes it. Human decisions (`record_*_decision`) are never made by the adapter. The operator records them, with `record_*_decision` or with the short forms `approve_pending` / `reject_pending`, which take the pending gate, its authority and the candidate digest from the core's own result and run the same operation; `.claude/settings.json` denies all of those commands to the agent.

## Execution

Use `/execute status` to see the lifecycle state and the runtime state of every Work Item.
Use `/execute` (or `/execute TASK-id`) only when the core reports `EXECUTION_READY`: it runs the next runnable Work Item of `work/execution-graph.yaml` inside that task's `repository_scope`, runs the task's verification controls where an executable check exists, and records the outcome in `work/state/<TASK-id>.yaml`. Tasks with human gates stop for the operator. `/execute` never records decisions and never performs a lifecycle operation.

Do not implement a second state machine in this file or in Claude reasoning.
Do not begin Engineering Work Item execution from `/bootstrap` or on your own initiative; `/execute` is the only execution entry point, and only at `EXECUTION_READY`.
