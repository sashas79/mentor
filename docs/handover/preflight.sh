#!/usr/bin/env bash
# Preflight for running TASK-16-PLATFORM-OPERATIONS under the Engineering Harness on this machine.
#
#   bash docs/handover/preflight.sh        (from the repository root)
#
# Writes nothing into the repository. Exit status 0 when every check passes; warnings do not fail it.
# Each line is PASS, WARN or FAIL followed by what was checked and, on failure, what to do.

set -u

pass=0; fail=0; warn=0
ok()  { pass=$((pass + 1)); printf 'PASS  %s\n' "$1"; }
bad() { fail=$((fail + 1)); printf 'FAIL  %s\n' "$1"; }
meh() { warn=$((warn + 1)); printf 'WARN  %s\n' "$1"; }

if [ ! -f CLAUDE.md ] || [ ! -d work/tasks ] || [ ! -f .claude/settings.json ]; then
  echo "Run this from the repository root (the directory that holds CLAUDE.md)."
  exit 2
fi

echo "== Platform =="
case "$(uname -sm)" in
  "Darwin arm64") ok "macOS on Apple Silicon: the supplied harness-core binary runs here" ;;
  *) bad "Platform is '$(uname -sm)'. The supplied harness-core runs only on macOS arm64; obtain a build for this platform (README, section 2)" ;;
esac

echo "== Engineering Harness core =="
core=$(sed -n 's/.*"ENGINEERING_HARNESS_CORE"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' .claude/settings.json | head -1)
expected_sha="de033672aec751882a39112428bd953f0669da00087ab422db0ee896c8f5ddfa"
if [ -z "$core" ]; then
  bad "ENGINEERING_HARNESS_CORE is not set in .claude/settings.json"
elif [ ! -x "$core" ]; then
  bad "Harness core '$core' (relative to the repository root) is missing or not executable: place the bundle as README section 4 shows"
else
  ok "Harness core found at $core"
  actual_sha=$(shasum -a 256 "$core" | cut -d' ' -f1)
  if [ "$actual_sha" = "$expected_sha" ]; then
    ok "Harness core checksum matches the handed-over build"
  else
    meh "Harness core checksum is $actual_sha, not the handed-over build; acceptable only if you built it yourself for this platform"
  fi
  if xattr -p com.apple.quarantine "$core" >/dev/null 2>&1; then
    meh "Harness core carries the macOS quarantine attribute; run: xattr -d com.apple.quarantine $core"
  fi
  root="${ENGINEERING_HARNESS_ROOT:-$(dirname "$core")/_root}"
  if [ -f "$root/releases/v0.3.0/VERSION" ]; then
    ok "Harness root release v0.3.0 present at $root"
  else
    bad "Harness root release v0.3.0 not found at $root: _root belongs beside harness-core, or set ENGINEERING_HARNESS_ROOT"
  fi
  out=$("$core" inspect_project --project-root . --pretty 2>&1); status=$?
  if [ $status -eq 0 ] && printf '%s' "$out" | grep -q '"outcome": "OK"'; then
    ok "inspect_project reports outcome OK"
  else
    bad "inspect_project failed (exit $status): $(printf '%s' "$out" | head -3 | tr '\n' ' ')"
  fi
  printf '%s' "$out" | grep -q '"harness_version": "v0.3.0"' && ok "Harness version v0.3.0" || bad "Harness version is not v0.3.0; the project was built against v0.3.0"
  printf '%s' "$out" | grep -q '"state": "EXECUTION_READY"' && ok "Lifecycle state EXECUTION_READY" || bad "Lifecycle state is not EXECUTION_READY; read the inspect_project output and do not run /execute"
  printf '%s' "$out" | grep -q '"execution_permitted": true' && ok "Execution permitted" || bad "Execution is not permitted; read the inspect_project output"
  printf '%s' "$out" | grep -q '"issues": \[\]' && ok "No issues reported by the core" || bad "The core reports issues; read the inspect_project output"
fi

echo "== Work items =="
done_count=$(grep -l '^  status: done' work/state/*.yaml 2>/dev/null | wc -l | tr -d ' ')
if [ "$done_count" = "15" ]; then
  ok "15 work items recorded as done under work/state/"
else
  bad "Expected 15 work items done, found $done_count; the repository is not at the handover state"
fi
if [ -f work/state/TASK-16-PLATFORM-OPERATIONS.yaml ]; then
  st=$(sed -n 's/^  status: *//p' work/state/TASK-16-PLATFORM-OPERATIONS.yaml | head -1)
  meh "TASK-16 already has runtime state '$st'; /execute resumes it rather than starting afresh"
else
  ok "TASK-16 has no runtime state yet: it is the next runnable task"
fi

echo "== Git =="
if git rev-parse --is-inside-work-tree >/dev/null 2>&1; then
  printf 'INFO  HEAD is %s on %s\n' "$(git rev-parse --short HEAD)" "$(git rev-parse --abbrev-ref HEAD)"
  if [ -z "$(git status --porcelain)" ]; then
    ok "Working tree clean"
  else
    meh "Working tree has uncommitted changes; commit or stash them before /execute so its changed paths are attributable"
  fi
  for c in c89c27f25e6ac12b74b342157b0036db2d521734 5faa9e013672b74217f9dc055f2bf0e5db1b963b; do
    if git cat-file -e "$c^{commit}" 2>/dev/null; then
      ok "Baseline commit ${c:0:7} present in history"
    else
      bad "Baseline commit ${c:0:7} is missing: the repository was not transferred with its full history (README, section 3)"
    fi
  done
else
  bad "Not a git repository"
fi

echo "== .NET =="
if command -v dotnet >/dev/null 2>&1; then
  v=$(dotnet --version 2>/dev/null)
  case "$v" in
    10.*) ok ".NET SDK $v" ;;
    *) bad ".NET SDK 10.0 is required, found '${v:-none}'" ;;
  esac
  if (cd src && dotnet tool restore >/dev/null 2>&1); then
    ok "dotnet-ef tool restored from src/dotnet-tools.json"
  else
    meh "dotnet tool restore failed in src/; needed for migration scripts, usually a NuGet access problem"
  fi
else
  bad "dotnet not found; install the .NET SDK 10.0"
fi

echo "== Node.js =="
if command -v node >/dev/null 2>&1; then
  nv=$(node --version | sed 's/^v//'); major=${nv%%.*}; rest=${nv#*.}; minor=${rest%%.*}
  if [ "$major" -gt 20 ] || { [ "$major" -eq 20 ] && [ "$minor" -ge 17 ]; }; then
    ok "Node.js $nv"
  else
    bad "Node.js 20.17 or later is required, found $nv"
  fi
else
  bad "node not found; install Node.js 22 LTS"
fi
command -v npm >/dev/null 2>&1 && ok "npm $(npm --version)" || bad "npm not found"

echo "== Docker =="
if command -v docker >/dev/null 2>&1; then
  if docker info >/dev/null 2>&1; then
    ok "Docker daemon reachable (server $(docker version --format '{{.Server.Version}}' 2>/dev/null))"
  else
    bad "Docker daemon not reachable; the integration tests start postgres:17-alpine through Testcontainers"
  fi
else
  bad "docker not found; install Docker Desktop or a compatible daemon"
fi

echo "== Package sources =="
if command -v curl >/dev/null 2>&1; then
  curl -sSf -m 10 https://api.nuget.org/v3/index.json >/dev/null 2>&1 && ok "NuGet reachable" || meh "NuGet (api.nuget.org) not reachable; configure your proxy or mirror"
  curl -sSf -m 10 https://registry.npmjs.org/ >/dev/null 2>&1 && ok "npm registry reachable" || meh "npm registry not reachable; configure your proxy or mirror"
  # The anonymous registry ping answers 401; any HTTP answer means the registry is reachable.
  hub=$(curl -sS -m 10 -o /dev/null -w '%{http_code}' https://registry-1.docker.io/v2/ 2>/dev/null || true)
  case "$hub" in
    200|301|302|401) ok "Docker Hub reachable (HTTP $hub)" ;;
    *) meh "Docker Hub not reachable (HTTP ${hub:-none}); pull postgres:17-alpine from your mirror first" ;;
  esac
fi

echo "== Claude Code adapter =="
if command -v claude >/dev/null 2>&1; then
  ok "Claude Code CLI $(claude --version 2>/dev/null | head -1)"
else
  meh "Claude Code CLI is not on PATH; fine when you run it from the VS Code extension"
fi
[ -f .claude/commands/execute.md ] && ok "/execute command present" || bad ".claude/commands/execute.md is missing"
[ -f .claude/commands/bootstrap.md ] && ok "/bootstrap command present" || bad ".claude/commands/bootstrap.md is missing"
grep -q 'approve_pending' .claude/settings.json && ok "Decision commands are denied to the agent in .claude/settings.json" || bad ".claude/settings.json does not deny the decision commands"

echo "== Platform decisions input =="
if [ -f infra/PLATFORM-DECISIONS.md ]; then
  if grep -q 'FILL IN' infra/PLATFORM-DECISIONS.md; then
    meh "infra/PLATFORM-DECISIONS.md still holds FILL IN markers; replace each with a value or UNDECIDED"
  else
    ok "infra/PLATFORM-DECISIONS.md present with no FILL IN markers"
  fi
else
  meh "infra/PLATFORM-DECISIONS.md is absent; copy docs/handover/platform-decisions.template.md there and fill it before /execute"
fi

printf '\n%s passed, %s warnings, %s failed\n' "$pass" "$warn" "$fail"
[ "$fail" -eq 0 ]
