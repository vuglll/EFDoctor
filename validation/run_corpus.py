#!/usr/bin/env python3
"""Run EFDoctor against a pinned corpus of EF Core projects and prepare triage files.

For each project in the corpus file, this script:

1. checks out the pinned commit (a shallow clone in the cache directory),
2. restores the target with `dotnet restore`,
3. runs the repository-built EFDoctor CLI with JSON output,
4. writes the report and run metadata to the results directory, and
5. creates or updates `<results>/triage/<name>.md`, keeping any verdicts
   already recorded there.

Usage:
    python3 validation/run_corpus.py [--corpus FILE] [--results DIR] [--cache DIR] [--only NAME ...]

Private codebases can be validated the same way by passing a private corpus
file and a results directory outside this repository.
"""

from __future__ import annotations

import argparse
import json
import re
import subprocess
import sys
import time
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
VERDICTS = ("TP", "FP", "debatable", "acceptable", "TODO")
CACHE_ROOT = Path.home() / ".cache" / "efdoctor-corpus"


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--corpus", type=Path, default=REPO_ROOT / "validation" / "corpus.json")
    parser.add_argument("--results", type=Path, default=REPO_ROOT / "validation" / "results")
    parser.add_argument("--cache", type=Path, default=Path.home() / ".cache" / "efdoctor-corpus")
    parser.add_argument("--only", nargs="*", help="Run only the named projects.")
    parser.add_argument("--skip-restore", action="store_true", help="Assume targets are already restored.")
    args = parser.parse_args()
    global CACHE_ROOT
    CACHE_ROOT = args.cache.resolve()

    corpus = json.loads(args.corpus.read_text())
    projects = [p for p in corpus["projects"] if not args.only or p["name"] in args.only]
    args.results.mkdir(parents=True, exist_ok=True)
    (args.results / "triage").mkdir(exist_ok=True)
    args.cache.mkdir(parents=True, exist_ok=True)

    cli = build_cli()
    efdoctor_commit = git(REPO_ROOT, "rev-parse", "HEAD")
    for project in projects:
        run_project(project, cli, efdoctor_commit, args)
    return 0


def build_cli() -> Path:
    print("Building EFDoctor CLI (Release)...", flush=True)
    run(["dotnet", "build", str(REPO_ROOT / "src" / "EFDoctor.Cli" / "EFDoctor.Cli.csproj"), "-c", "Release", "--nologo", "-v", "q"], REPO_ROOT, check=True)
    dlls = sorted((REPO_ROOT / "src" / "EFDoctor.Cli" / "bin" / "Release").glob("net*/efdoctor.dll"))
    if not dlls:
        sys.exit("efdoctor.dll was not found after the build.")
    return dlls[-1]


def run_project(project: dict, cli: Path, efdoctor_commit: str, args: argparse.Namespace) -> None:
    name = project["name"]
    checkout = args.cache / name
    print(f"\n== {name} ({project['commit'][:12]})", flush=True)
    ensure_checkout(checkout, project["repository"], project["commit"])
    target = checkout / project["target"]

    meta = {
        "name": name,
        "repository": project["repository"],
        "commit": project["commit"],
        "target": project["target"],
        "efdoctorCommit": efdoctor_commit,
    }

    projects = solution_projects(target, checkout)
    if not args.skip_restore:
        # Restore project by project so one broken auxiliary project (a MAUI client, a test
        # project that treats audit warnings as errors) does not block the EF projects.
        started = time.monotonic()
        failures = []
        for project_file in projects:
            restore = run(["dotnet", "restore", str(project_file), "--nologo", "-v", "q", "-p:NuGetAudit=false"], checkout)
            if restore.returncode != 0:
                failures.append({"project": rel(project_file, checkout), "error": tail(restore.stdout + restore.stderr, 5)})
        meta["restoreSeconds"] = round(time.monotonic() - started, 1)
        meta["restoreFailures"] = failures
        if failures:
            print(f"   {len(failures)} of {len(projects)} projects failed to restore", flush=True)

    # An EF project without restore assets compiles without EF Core types, so every rule stays
    # silent for it. Such a run is incomplete, not clean.
    unrestored = [rel(p, checkout) for p in projects if references_ef(p) and not assets_file(p, checkout).exists()]
    meta["projectCount"] = len(projects)
    meta["efProjectCount"] = sum(1 for p in projects if references_ef(p))
    meta["unrestoredEfProjects"] = unrestored
    meta["complete"] = not unrestored
    if unrestored:
        print(f"   INCOMPLETE: {len(unrestored)} EF project(s) not restored: {', '.join(unrestored)}", flush=True)

    started = time.monotonic()
    analysis = run(["dotnet", str(cli), "analyze", str(target), "--format", "json", "--quiet"], checkout)
    meta["analysisSeconds"] = round(time.monotonic() - started, 1)
    meta["analysisExitCode"] = analysis.returncode
    if analysis.stderr.strip():
        meta["analysisStderr"] = tail(analysis.stderr)
    # EFDoctor warns about projects whose EF Core types don't resolve (and exits 2 when none
    # resolves); either way part of the target was not analyzed, so the run is incomplete.
    warnings = [line for line in analysis.stderr.splitlines() if line.startswith("EFDoctor warning:")]
    meta["analysisWarnings"] = [tail(line, 1) for line in warnings]
    if warnings or analysis.returncode == 2:
        meta["complete"] = False
        print(f"   INCOMPLETE: {len(warnings)} EFDoctor warning(s), exit {analysis.returncode}", flush=True)

    findings: list[dict] = []
    if analysis.returncode in (0, 1):
        report = json.loads(analysis.stdout)
        findings = report.get("findings", [])
        for finding in findings:
            finding["sourceFile"] = relative_source(finding.get("sourceFile", ""), checkout)
        report["findings"] = findings
        (args.results / f"{name}.json").write_text(json.dumps(report, indent=2) + "\n")
    else:
        # In JSON mode an analysis failure is the error envelope on standard output. Record it,
        # and drop any report left from an earlier run so it can't pass for a clean result.
        try:
            # raw_decode: MSBuildLocator's SDK query can print the SDK list after the envelope.
            envelope, _ = json.JSONDecoder().raw_decode(analysis.stdout.lstrip())
            meta["analysisError"] = tail(envelope["error"]["message"], 20)
        except (ValueError, KeyError, TypeError):
            meta["analysisError"] = tail(analysis.stdout + analysis.stderr)
        (args.results / f"{name}.json").unlink(missing_ok=True)
    meta["findingCount"] = len(findings)
    meta["findingsByRule"] = count_by_rule(findings)
    (args.results / f"{name}.run.json").write_text(json.dumps(meta, indent=2) + "\n")

    print(f"   exit {analysis.returncode}, {len(findings)} findings in {meta['analysisSeconds']}s: {meta['findingsByRule']}", flush=True)
    if analysis.returncode in (0, 1):
        write_triage(args.results / "triage" / f"{name}.md", project, findings, checkout)


def solution_projects(target: Path, checkout: Path) -> list[Path]:
    if target.suffix == ".csproj":
        return [target]
    listing = run(["dotnet", "sln", str(target), "list"], checkout)
    projects = []
    for line in listing.stdout.splitlines():
        line = line.strip().replace("\\", "/")
        if line.endswith(".csproj"):
            projects.append((target.parent / line).resolve())
    return projects


def assets_file(project_file: Path, checkout: Path) -> Path:
    # Ask MSBuild, because repositories may relocate restore output (for example UseArtifactsOutput).
    result = run(["dotnet", "msbuild", str(project_file), "-getProperty:ProjectAssetsFile", "-nologo"], checkout)
    value = result.stdout.strip().splitlines()[-1].strip() if result.returncode == 0 and result.stdout.strip() else ""
    return Path(value) if value else project_file.parent / "obj" / "project.assets.json"


def references_ef(project_file: Path) -> bool:
    try:
        return "EntityFrameworkCore" in project_file.read_text(errors="replace")
    except OSError:
        return False


def rel(path: Path, root: Path) -> str:
    try:
        return str(path.resolve().relative_to(root.resolve()))
    except ValueError:
        return str(path)


def ensure_checkout(checkout: Path, repository: str, commit: str) -> None:
    if not (checkout / ".git").exists():
        checkout.mkdir(parents=True, exist_ok=True)
        git(checkout, "init", "-q")
        git(checkout, "remote", "add", "origin", repository)
    if git(checkout, "rev-parse", "HEAD", check=False) != commit:
        git(checkout, "fetch", "-q", "--depth", "1", "origin", commit)
        git(checkout, "checkout", "-q", "--force", "FETCH_HEAD")


def write_triage(path: Path, project: dict, findings: list[dict], checkout: Path) -> None:
    previous = parse_triage(path) if path.exists() else {}
    lines = [
        f"# Triage: {project['name']}",
        "",
        f"- Repository: {project['repository']} @ `{project['commit'][:12]}`",
        f"- Target: `{project['target']}`",
        f"- Findings: {len(findings)}",
        "",
        "Set each **Verdict** to one of: `TP` (real issue), `FP` (wrong), `debatable`, "
        "or `acceptable` (correct but fine in context, mainly for advisory rules). Add a short note.",
        "",
    ]
    for finding in sorted(findings, key=finding_key):
        key = finding_key(finding)
        verdict, note = previous.get(key, ("TODO", ""))
        start = finding["range"]
        location = f"{finding['sourceFile']}:{start['startLine']}:{start['startColumn']}"
        lines += [
            f"## {finding['ruleId']} · {location}",
            f"<!-- key: {key} -->",
            "",
            f"- **Verdict:** {verdict}",
            f"- **Note:** {note}",
            f"- **Confidence:** {finding.get('confidence')} · **Severity:** {finding.get('severity')}",
            f"- **Code:** `{source_line(checkout, finding).replace('`', chr(39))}`",
            f"- **Evidence:** {finding.get('evidence', '')}",
            "",
        ]
    path.write_text("\n".join(lines))


def parse_triage(path: Path) -> dict[str, tuple[str, str]]:
    verdicts: dict[str, tuple[str, str]] = {}
    for block in re.split(r"^## ", path.read_text(), flags=re.M)[1:]:
        key = re.search(r"<!-- key: (.+?) -->", block)
        verdict = re.search(r"^- \*\*Verdict:\*\* *(\S*)", block, re.M)
        note = re.search(r"^- \*\*Note:\*\* *(.*)$", block, re.M)
        if key and verdict:
            value = verdict.group(1) if verdict.group(1) in VERDICTS else "TODO"
            verdicts[key.group(1)] = (value, note.group(1).strip() if note else "")
    return verdicts


def finding_key(finding: dict) -> str:
    start = finding["range"]
    return f"{finding['ruleId']}|{finding['sourceFile']}|{start['startLine']}|{start['startColumn']}"


def source_line(checkout: Path, finding: dict) -> str:
    try:
        text = (checkout / finding["sourceFile"]).read_text(errors="replace").splitlines()
        return text[finding["range"]["startLine"] - 1].strip()[:200]
    except (OSError, IndexError):
        return ""


def relative_source(source: str, checkout: Path) -> str:
    try:
        return str(Path(source).resolve().relative_to(checkout.resolve()))
    except ValueError:
        return source


def count_by_rule(findings: list[dict]) -> dict[str, int]:
    counts: dict[str, int] = {}
    for finding in findings:
        counts[finding["ruleId"]] = counts.get(finding["ruleId"], 0) + 1
    return dict(sorted(counts.items()))


def tail(text: str, lines: int = 40) -> str:
    # Captured output must not leak local paths into committed results.
    text = text.replace(str(CACHE_ROOT), "<corpus>").replace(str(Path.home()), "~")
    return "\n".join(text.strip().splitlines()[-lines:])


def git(cwd: Path, *arguments: str, check: bool = True) -> str:
    result = run(["git", *arguments], cwd, check=check)
    return result.stdout.strip()


def run(command: list[str], cwd: Path, check: bool = False) -> subprocess.CompletedProcess:
    result = subprocess.run(command, cwd=cwd, capture_output=True, text=True)
    if check and result.returncode != 0:
        sys.exit(f"Command failed ({result.returncode}): {' '.join(command)}\n{tail(result.stdout + result.stderr)}")
    return result


if __name__ == "__main__":
    sys.exit(main())
