# Copyright © Erickson Lopez. MIT License.
import json
import os
import sys
import urllib.request
import urllib.error

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="backslashreplace")
if hasattr(sys.stderr, "reconfigure"):
    sys.stderr.reconfigure(encoding="utf-8", errors="backslashreplace")

def load_thresholds(config_path="stryker-config.json"):
    thresholds = {"high": 100, "low": 98, "break": 95}
    if os.path.exists(config_path):
        try:
            with open(config_path, "r", encoding="utf-8") as f:
                config = json.load(f)
            t = config.get("stryker-config", {}).get("thresholds", config.get("thresholds", {}))
            thresholds = {
                "high": t.get("high", 100),
                "low": t.get("low", 98),
                "break": t.get("break", 95)
            }
        except Exception as e:
            print(f"Warning: Could not parse {config_path}: {e}")
    return thresholds

def fetch_json(url, token=None):
    req = urllib.request.Request(url, method="GET")
    req.add_header("User-Agent", "dotnet-idempotency-release-gate")
    req.add_header("Accept", "application/vnd.github.v3+json")
    if token:
        req.add_header("Authorization", f"Bearer {token}")
    try:
        with urllib.request.urlopen(req) as resp:
            if 200 <= resp.status < 300:
                return json.loads(resp.read().decode("utf-8"))
    except Exception as e:
        print(f"Warning: API request error for {url}: {e}")
    return None

def query_commit_status(owner, repo, sha, token):
    url = f"https://api.github.com/repos/{owner}/{repo}/commits/{sha}/statuses"
    statuses = fetch_json(url, token)
    if isinstance(statuses, list):
        for s in statuses:
            if s.get("context") == "quality-gate/stryker-mutation":
                return {
                    "source": "commit_status",
                    "state": s.get("state"),
                    "description": s.get("description"),
                    "target_url": s.get("target_url"),
                    "created_at": s.get("created_at"),
                    "updated_at": s.get("updated_at")
                }
    return None

def query_workflow_runs(owner, repo, sha, token):
    url = f"https://api.github.com/repos/{owner}/{repo}/actions/workflows/mutation-testing.yml/runs?head_sha={sha}&status=completed"
    data = fetch_json(url, token)
    if data and "workflow_runs" in data and len(data["workflow_runs"]) > 0:
        latest_run = data["workflow_runs"][0]
        return {
            "source": "workflow_run",
            "conclusion": latest_run.get("conclusion"),
            "created_at": latest_run.get("created_at"),
            "updated_at": latest_run.get("updated_at"),
            "html_url": latest_run.get("html_url")
        }
    return None

def check_local_manifest(sha):
    manifest_path = os.path.join("StrykerOutput", "stryker-release-manifest.json")
    if os.path.exists(manifest_path):
        try:
            with open(manifest_path, "r", encoding="utf-8") as f:
                manifest = json.load(f)
            if not sha or manifest.get("commit_sha") == sha or manifest.get("commit_sha") == "unknown":
                return {
                    "source": "local_manifest",
                    "passed": manifest.get("passed_gate", False),
                    "score": manifest.get("overall_mutation_score", 0),
                    "min_score": manifest.get("lowest_package_score", 0),
                    "date": manifest.get("execution_date", "N/A"),
                    "status": manifest.get("overall_status", "N/A"),
                    "manifest": manifest
                }
        except Exception as e:
            print(f"Warning: Could not read local manifest: {e}")
    return None

def main():
    target_sha = sys.argv[1] if len(sys.argv) > 1 else os.environ.get("GITHUB_SHA", "")
    config_file = sys.argv[2] if len(sys.argv) > 2 else "stryker-config.json"
    thresholds = load_thresholds(config_file)
    t_break = thresholds["break"]

    repo = os.environ.get("GITHUB_REPOSITORY", "")
    token = os.environ.get("GITHUB_TOKEN", "")

    print("==================================================")
    print("  RELEASE MUTATION TESTING QUALITY GATE AUDITOR   ")
    print(f"  Target Commit SHA: {target_sha or 'HEAD'}")
    print(f"  Break Threshold:   ≥{t_break}%")
    print("==================================================\n")

    commit_analyzed = target_sha or "Unknown"
    execution_date = "N/A"
    mutation_score_text = "N/A"
    passed_break = False
    release_permitted = False
    status_badge = "❌ FAILED"
    verification_details = ""

    local_manifest = check_local_manifest(target_sha)
    if local_manifest:
        commit_analyzed = local_manifest["manifest"].get("commit_sha") or target_sha
        execution_date = local_manifest["date"]
        mutation_score_text = f"{local_manifest['score']:.2f}% (Lowest Package: {local_manifest['min_score']:.2f}%)"
        passed_break = local_manifest["passed"]
        release_permitted = passed_break
        status_badge = local_manifest["status"]
        verification_details = f"Verified from local release manifest ({local_manifest['manifest'].get('packages_count', 0)} packages evaluated)."

    if not release_permitted and repo and target_sha and token and "/" in repo:
        owner, repo_name = repo.split("/", 1)
        commit_status = query_commit_status(owner, repo_name, target_sha, token)
        if commit_status:
            commit_analyzed = target_sha
            execution_date = commit_status.get("updated_at") or commit_status.get("created_at") or "N/A"
            mutation_score_text = commit_status.get("description") or "Recorded in commit status"
            passed_break = (commit_status.get("state") == "success")
            release_permitted = passed_break
            status_badge = "✅ PASSED" if passed_break else "❌ FAILED"
            verification_details = f"Verified from GitHub Commit Status [{commit_status.get('state')}]: {commit_status.get('description')}"
        else:
            workflow_run = query_workflow_runs(owner, repo_name, target_sha, token)
            if workflow_run:
                commit_analyzed = target_sha
                execution_date = workflow_run.get("updated_at") or workflow_run.get("created_at") or "N/A"
                mutation_score_text = f"Workflow Run conclusion: {workflow_run.get('conclusion')}"
                passed_break = (workflow_run.get("conclusion") == "success")
                release_permitted = passed_break
                status_badge = "✅ PASSED" if passed_break else "❌ FAILED"
                verification_details = f"Verified from GitHub Actions workflow run: {workflow_run.get('html_url')}"

    release_decision_text = "✅ RELEASE PERMITTED" if release_permitted else "❌ RELEASE BLOCKED"

    print("[Q1] Which commit was analyzed?")
    print(f"     → {commit_analyzed}")
    print("[Q2] When?")
    print(f"     → {execution_date}")
    print("[Q3] What mutation score was obtained?")
    print(f"     → {mutation_score_text}")
    print(f"[Q4] Did it pass the break threshold (≥{t_break}%)?")
    print(f"     → {'YES' if passed_break else 'NO'}")
    print("[Q5] Can the release proceed?")
    print(f"     → {release_decision_text}\n")

    step_summary_path = os.environ.get("GITHUB_STEP_SUMMARY")
    if step_summary_path:
        summary_md = f"""
# 🚀 Release Mutation Quality Gate Verification

| Release Audit Question | Result |
|------------------------|--------|
| **1. Commit Analyzed** | `{commit_analyzed[:10]}` |
| **2. Execution Date** | {execution_date} |
| **3. Mutation Score Result** | **{mutation_score_text}** |
| **4. Passed Break Threshold (≥{t_break}%)** | {'✅ YES' if passed_break else '❌ NO'} |
| **5. Release Gate Decision** | **{release_decision_text}** |

> **Audit Details**: {verification_details or 'No valid mutation quality gate evidence found for target commit.'}
"""
        with open(step_summary_path, "a", encoding="utf-8") as f:
            f.write(summary_md + "\n")

    github_output_path = os.environ.get("GITHUB_OUTPUT")
    if github_output_path:
        with open(github_output_path, "a", encoding="utf-8") as f:
            f.write(f"release_permitted={'true' if release_permitted else 'false'}\n")
            f.write(f"commit_analyzed={commit_analyzed}\n")
            f.write(f"passed_break={'true' if passed_break else 'false'}\n")

    if not release_permitted:
        print(f"\n❌ RELEASE BLOCKED: Commit {commit_analyzed} has not satisfied the mutation testing quality gate (score >= {t_break}%).", file=sys.stderr)
        print("Please ensure that mutation testing on 'main' has completed successfully before publishing a release.", file=sys.stderr)
        sys.exit(1)

    print(f"✅ RELEASE PERMITTED: Mutation score meets or exceeds break threshold ({t_break}%). Proceeding to package publication.\n")

if __name__ == "__main__":
    main()
