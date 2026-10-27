---
description: Execute the next runnable Engineering Work Item under the deterministic Engineering Harness (only at EXECUTION_READY)
argument-hint: "[status | TASK-id]"
---

Act only as the Claude Code adapter to the deterministic Engineering Harness core. This command is the runtime of the approved Work Baseline (BRS FR-036). It never changes the approved Work definition and never records a human decision.

1. Read `CLAUDE.md`. Locate the core exactly as `/bootstrap` does: the command held in `ENGINEERING_HARNESS_CORE`, run from the project root; the core resolves the Harness root itself.
2. Run `inspect_project` and read `lifecycle.state`, `execution_permitted` and `next_action` from its result. Never derive readiness yourself.
3. For `status`: report the lifecycle state, then for every task in `work/execution-graph.yaml` (in graph order) its title from `work/tasks/<TASK-id>.yaml`, its `depends_on`, and its runtime state from `work/state/<TASK-id>.yaml` (`none` when that file does not exist). `status` writes nothing.
4. Without `status`: continue only when `lifecycle.state` is `EXECUTION_READY` and `execution_permitted` is `true`. Otherwise stop, present the exact issue codes and the `next_action`, and say that `/bootstrap` performs it. Never run a lifecycle operation from this command.
5. Select the task:
   - if a `TASK-id` was given, that task; it must exist in the graph and every task in its `depends_on` must have runtime state `done`, otherwise stop and report which dependency is not done;
   - otherwise the first task in `execution-graph.yaml` order whose `depends_on` all have state `done` and whose own state is not `done`. A task in `in_progress` is resumed. If no task qualifies, report that every task is `done` or `blocked` and stop.
   - If the task's `human_gates` is not empty, write its state as `blocked` (note: the gate ids) and stop: the gate is the operator's; the adapter never satisfies it.
6. Before changing anything, write `work/state/<TASK-id>.yaml` with status `in_progress`:

   ```yaml
   schema_version: 1
   task_state:
     task: TASK-001
     status: in_progress            # in_progress | done | blocked
     work_baseline: baselines/work/WORK-0001.yaml   # the newest record under baselines/work/
     started: 2026-01-01T00:00:00Z
     updated: 2026-01-01T00:00:00Z
     verification: []
     changed_paths: []
   ```

   `work/state/**` is runtime-owned and excluded from every validation subject; the core never reads it. Keep `updated` current on every later write.
7. Implement the task. Read the referenced requirements and acceptance criteria from `spec/specification.yaml`, the referenced components, ADRs and contracts from `architecture/`, and the effective controls from `engineering/controls.yaml`. Write only to paths that match the task's `repository_scope.allowed` and none that match `repository_scope.forbidden`, plus the state file. Never write under `project-pack/`, `spec/`, `architecture/`, `engineering/`, `ai/`, `work/tasks/`, `work/execution-graph.yaml`, `baselines/`, `evidence/`, or to `.engineering-harness-template` / `.engineering-harness-adapter`; if the task cannot be completed inside its scope, set the state to `blocked` with a note and stop. Record every created or modified path in `changed_paths`.
8. Verify. For each id in the task's `verification_controls`, run the repository's executable check for that control when one exists (for example the lint or test script the project defines) and record `{ control, result: PASS | FAIL, command, note }` under `verification`. A control with no executable rule is recorded as `NOT_RUN` with a note; never record `PASS` for it (BRS §50). Confirm every acceptance criterion in `acceptance_refs` is met; if a check fails and cannot be fixed inside the scope, leave the state `in_progress` (or `blocked` with a note) and stop.
9. When every verification that could run is `PASS` and the acceptance criteria are met, set `status: done`, then run `inspect_project` again. If the state is no longer `EXECUTION_READY`, report the issue codes and stop; otherwise report the task, its `changed_paths` and the next runnable task. Do not commit unless the operator asks.
10. Never run `approve_pending`, `reject_pending` or `record_*_decision`, never broaden the write scope, never modify the Work definition, and never silently choose or download a Harness release.

The adapter must not claim a lifecycle transition, a verification result or a readiness that the deterministic core or an executed check does not support from evidence.
