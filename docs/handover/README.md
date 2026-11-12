# Handover runbook: running TASK-16-PLATFORM-OPERATIONS on your own machines

For the Dariel managed services DevOps crew. This runbook takes an engineer from an empty machine to a completed
TASK-16 under the Engineering Harness, and back to a reviewed pull request. Read it once end to end before
starting; section 8 lists the rules that are expensive to break.

The handover brief that accompanies this repository gives the context. This file gives the exact steps.

## 1. What you are receiving

- **The repository**, branch `execute/orchestrated`, 442 tracked files at handover. Fifteen of the sixteen
  Engineering Work Items are done, each with its runtime state in `work/state/`. The Harness lifecycle is
  `EXECUTION_READY`, and `TASK-16-PLATFORM-OPERATIONS` is the only runnable task.
- **The Harness distribution bundle** `mentor-harness-v0.3.0-darwin-arm64.tar.gz`: the `harness-core` binary and
  the `_root/releases/v0.3.0` release data. Release v0.3.0 of https://github.com/sashas79/engineering-harness.git.
- **This kit** under `docs/handover/`: this runbook, `preflight.sh`, `platform-decisions.template.md` and
  `prepare-bundle.sh` (delivery team only).
- **The decision authorities** recorded in `project-pack/project.yaml`: product_domain `lebo`, architecture
  `sasha`, security_platform `wayne`, engineering `sasha`, release `petre`.

## 2. Machine prerequisites

| Need | Requirement | Reference machine at handover |
| --- | --- | --- |
| Hardware and OS | Apple Silicon Mac. `harness-core` is a Mach-O arm64 binary linked only against system libraries. | macOS, arm64 |
| .NET SDK | 10.0, current patch. There is no `global.json`; packages are pinned at 10.0.14. | 10.0.403 |
| Node.js and npm | Node 20.17 or later, npm 10 | Node 22.17.0, npm 10.9.2 |
| Docker | Docker Desktop or a daemon Testcontainers can reach. The integration tests start `postgres:17-alpine`. | Docker 29.9.3 |
| Git | 2.x | 2.56.0 |
| Claude Code | VS Code extension or CLI, signed in to a Dariel Anthropic account. Use the most capable model your account offers. | |
| Network | GitHub (your repository), NuGet (api.nuget.org), the npm registry, Docker Hub or your mirror, and Anthropic. Configure proxies for `dotnet`, `npm` and Docker where needed. | |
| Optional | `psql` 16 or later, for legacy entry later; not needed for TASK-16. | |

Another platform (Intel Mac, Linux, Windows): the release data under `_root` is plain YAML and JSON and is
portable, but the binary is not. Ask the delivery team for a build of the core for your platform from the Harness
repository at v0.3.0, place it where section 4 shows, and expect `preflight.sh` to warn that its checksum differs.
Never run a different Harness version: every baseline in this repository is bound to v0.3.0.

## 3. Transfer the repository with its full history

Mirror the `execute/orchestrated` branch with its history. The specification and architecture baselines name
commits `c89c27f` and `5faa9e0`; both are in that history, and a squash would break the traceability.

```sh
git clone --branch execute/orchestrated --single-branch <delivery-repository-url> mentor
cd mentor
git remote rename origin delivery
git remote add origin <your-repository-url>
git push -u origin execute/orchestrated
git push origin execute/orchestrated:main      # make main your default branch, then work on main
```

Do not transfer the `master` branch (17 commits behind) or the `worktree-*` and `execute/task-13-*` branches
(exhausted working branches). Check the transfer:

```sh
git ls-files | wc -l         # 442 at handover, more after later commits
git log --oneline | head     # reaches the tip commit named in the handover message
git cat-file -e c89c27f25e6ac12b74b342157b0036db2d521734 && git cat-file -e 5faa9e013672b74217f9dc055f2bf0e5db1b963b && echo baselines-traceable
```

Nothing generated is tracked: `node_modules/`, `bin/`, `obj/`, `src/web/dist/` and the generated API client files
are ignored and are rebuilt by the build.

## 4. Place the Harness beside the repository

The committed `.claude/settings.json` points the Claude Code adapter at `../harness-core`, relative to the
repository root. The core finds its release data in `_root` beside itself. The layout is therefore:

```
<workspace>/
  harness-core                 executable
  _root/releases/v0.3.0/       release data
  mentor/                      the repository (any folder name; the path to the core is relative to it)
```

```sh
cd <workspace>
shasum -a 256 -c mentor-harness-v0.3.0-darwin-arm64.tar.gz.sha256
tar -xzf mentor-harness-v0.3.0-darwin-arm64.tar.gz
chmod +x harness-core
xattr -d com.apple.quarantine harness-core 2>/dev/null || true   # set by macOS on files that arrived by browser or AirDrop
cd mentor
../harness-core inspect_project --project-root . --pretty
```

Expected in that output: `"outcome": "OK"`, `"harness_version": "v0.3.0"`, `"state": "EXECUTION_READY"`,
`"execution_permitted": true`, `"issues": []`, and `next_action.action` equal to `none_bootstrap_complete`.

If `_root` must live elsewhere, set `ENGINEERING_HARNESS_ROOT` to its path. The core takes no root argument on the
command line. Do not change `ENGINEERING_HARNESS_CORE` in the committed settings file; a machine-specific override
belongs in `.claude/settings.local.json`, which you keep out of version control.

## 5. Preflight

From the repository root:

```sh
bash docs/handover/preflight.sh
```

It checks the platform, the core and its checksum, the release root, the lifecycle state, the fifteen completed
work items, the git history, the .NET, Node and Docker toolchain, package-source reachability, the Claude Code
adapter files, and whether your platform decisions are in place. It writes nothing. Clear every `FAIL` before
anyone runs `/execute`; a `WARN` is a judgement call explained on its line.

Then prove the toolchain once outside the Harness, which also warms the package caches:

```sh
dotnet build src/Mentor.slnx
dotnet test src/Mentor.slnx                     # needs Docker; several minutes on the first run
cd src/web && npm ci && npm run build && npm test && cd ../..
```

All three must pass on your machine before TASK-16 is attempted. They passed on the reference machine at handover.

## 6. Bring your platform decisions

TASK-16 writes the production-readiness records the BRS requires before production acceptance: hosting and a
recoverability decision for the authoritative store, production ownership, controlled operator and data access,
retention and protection controls. Those are your decisions, and the work item must not invent them.

1. Copy `docs/handover/platform-decisions.template.md` to `infra/PLATFORM-DECISIONS.md`.
2. Fill every `FILL IN` with a value or `UNDECIDED`. Never guess; an undecided value is recorded as unresolved.
3. Put no secret value in it. Names and locations only.
4. Obtain the sign-offs in its section 11 from the named authorities.
5. Commit it before running `/execute`:

   ```sh
   git add infra/PLATFORM-DECISIONS.md && git commit -m "Platform decisions for TASK-16"
   ```

A human-authored file under `infra/` is expected: ADR-013 says platform decisions are documented in `infra/`
before production acceptance, and `infra/` is the work item's allowed scope.

## 7. Run TASK-16

1. Open the repository in Claude Code (VS Code: open the `mentor` folder; CLI: run `claude` from the repository
   root).
2. Run `/execute status`. It reports the lifecycle state and, for every task in graph order, its runtime state:
   fifteen `done`, TASK-16 `none`.
3. Run:

   ```
   /execute TASK-16-PLATFORM-OPERATIONS The platform decisions are in infra/PLATFORM-DECISIONS.md; treat that file as the authoritative human input and do not rewrite it.
   ```

   The task id must come first; the sentence after it is context for the adapter.
4. What happens, in order: the adapter inspects the project through the core; writes
   `work/state/TASK-16-PLATFORM-OPERATIONS.yaml` as `in_progress`; reads the referenced requirements, acceptance
   criteria, components, ADRs and contracts; writes only under `infra/`; runs the two verification controls
   (`control://verification/build` and `control://verification/unit-test`) and records their commands and results;
   confirms the seven acceptance criteria; sets the state to `done`; inspects the project again.
5. Permission prompts: approve build, test, container and file operations inside the repository. Deny anything that
   writes into the repository outside `infra/` and `work/state/`, and anything named in section 8.
6. If it stops as `blocked` or stays `in_progress`, read the note in the state file, fix the environment (never the
   task definition), and run `/execute TASK-16-PLATFORM-OPERATIONS` again; the task resumes.

What the work item must produce (from `work/tasks/TASK-16-PLATFORM-OPERATIONS.yaml` and
COMP-PLATFORM-OPERATIONS in `architecture/architecture.yaml`):

- Container definitions for the API, the frontend and PostgreSQL, and a way to build and run them locally.
- Environment configuration: the Entra settings, the TLS-required connection string, the prototype switches that
  default off outside prototype configuration, and the CORS origins.
- Provisioning for the legacy-entry database roles and accounts (`infra/legacy-entry/` already holds the entry
  template and the account authorisation script).
- The production-readiness decision records, derived from your `PLATFORM-DECISIONS.md`.

Review it as engineers before you hand it back: build the images, start the stack, call `/health/live` and
`/health/ready`, and read the decision records against your decisions.

## 8. Rules that must not be broken

- **Never edit** `project-pack/`, `spec/`, `architecture/`, `engineering/`, `ai/`, `work/tasks/`,
  `work/execution-graph.yaml`, `baselines/`, `evidence/`, `CLAUDE.md`, `.claude/commands/`,
  `.engineering-harness-adapter` or `.engineering-harness-template`. A change under `project-pack/` alters its
  fingerprint and the core reports the project as blocked or invalid; the only fix is to revert the change.
- **Never run** `approve_pending`, `reject_pending` or any `record_*_decision`. Every lifecycle gate is closed;
  nothing is pending. The committed settings deny them to the agent; do not run them by hand either.
- **Never run `/bootstrap`** except `/bootstrap status`. Bootstrap is complete.
- **Never change `src/` or `tests/`** as part of TASK-16. The work item's scope forbids it, and the adapter sets the
  task `blocked` rather than do it. A platform need that requires an application change is raised with the
  delivery team as a new work item.
- **Never change the Harness version or release.** Every baseline is bound to v0.3.0.
- **Never put secrets in the repository**: no connection strings with passwords, no client secrets, no private
  keys. `PLATFORM-DECISIONS.md` and everything TASK-16 writes hold names and locations only.
- **Never rewrite history** on the branch the crew works on.

## 9. Hand back

1. Commit the outcome with the convention the earlier tasks used, including `infra/**` and
   `work/state/TASK-16-PLATFORM-OPERATIONS.yaml`:

   ```sh
   git add infra work/state && git commit -m "execute TASK-16-PLATFORM-OPERATIONS (done)"
   ```

2. Push and open a pull request for the delivery team: the engineering authority reviews the whole change, the
   security_platform authority reviews the decision records.
3. Attach the state file's verification section and the signed-off `PLATFORM-DECISIONS.md`.
4. Log, do not fix, the known follow-ups: the trigger function `enforce_legacy_entry` has no fixed `search_path`
   (noted in `work/state/TASK-13-LEGACY-RECORDS.yaml`); managed identity for the API's database connection;
   forwarded-header handling behind your edge; runtime frontend configuration instead of a build per environment.
   Each is an application change and needs a new work item.
