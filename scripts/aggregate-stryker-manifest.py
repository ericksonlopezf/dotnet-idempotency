# Copyright © Erickson Lopez. MIT License.
import json
import os
import sys
import glob
import urllib.request
import urllib.error
from datetime import datetime, timezone

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

def find_summary_files(directory):
    results = []
    if not os.path.exists(directory):
        return results
    for root, _, files in os.walk(directory):
        for file in files:
            if file.startswith("summary-") and file.endswith(".json"):
                results.append(os.path.join(root, file))
    return results

def post_commit_status(owner, repo, sha, token, status_data):
    if not owner or not repo or not sha or not token or sha == "unknown":
        print("Skipping commit status post: GITHUB_REPOSITORY, GITHUB_SHA, or GITHUB_TOKEN not provided.")
        return

    url = f"https://api.github.com/repos/{owner}/{repo}/statuses/{sha}"
    payload = json.dumps({
        "state": status_data["state"],
        "target_url": status_data.get("target_url", ""),
        "description": status_data["description"][:140],
        "context": "quality-gate/stryker-mutation"
    }).encode("utf-8")

    req = urllib.request.Request(url, data=payload, method="POST")
    req.add_header("User-Agent", "dotnet-idempotency-stryker-gate")
    req.add_header("Authorization", f"Bearer {token}")
    req.add_header("Content-Type", "application/json")

    try:
        with urllib.request.urlopen(req) as resp:
            if 200 <= resp.status < 300:
                print(f"Successfully posted commit status to {sha}: {status_data['state']}")
            else:
                print(f"GitHub API status update returned status: {resp.status}")
    except Exception as e:
        print(f"Warning: Error posting commit status: {e}")

def main():
    search_dir = sys.argv[1] if len(sys.argv) > 1 else "StrykerOutput"
    config_file = sys.argv[2] if len(sys.argv) > 2 else "stryker-config.json"
    thresholds = load_thresholds(config_file)
    t_high = thresholds["high"]
    t_low = thresholds["low"]
    t_break = thresholds["break"]

    summary_files = find_summary_files(search_dir)
    print(f"Found {len(summary_files)} package summary files in {search_dir}")

    package_results = {}
    total_killed = 0
    total_mutants = 0
    min_score = 100.0
    all_passed = len(summary_files) > 0
    sha = os.environ.get("GITHUB_SHA", "unknown")
    repo = os.environ.get("GITHUB_REPOSITORY", "")
    run_id = os.environ.get("GITHUB_RUN_ID", "")
    server_url = os.environ.get("GITHUB_SERVER_URL", "https://github.com")
    run_url = f"{server_url}/{repo}/actions/runs/{run_id}" if repo and run_id else ""

    for file_path in summary_files:
        try:
            with open(file_path, "r", encoding="utf-8") as f:
                data = json.load(f)
            pkg = data.get("package") or os.path.basename(file_path).replace("summary-", "").replace(".json", "")
            package_results[pkg] = data

            total_killed += int(data.get("mutants_killed", 0))
            total_mutants += int(data.get("total_mutants", 0))

            pkg_score = float(data.get("mutation_score", 0.0))
            if pkg_score < min_score:
                min_score = pkg_score

            if not data.get("passed_break", False):
                all_passed = False

            if data.get("commit_sha") and data.get("commit_sha") != "unknown":
                sha = data["commit_sha"]
            if data.get("run_url"):
                run_url = data["run_url"]
        except Exception as e:
            print(f"Warning: Error reading summary file {file_path}: {e}")

    overall_score = round((total_killed / total_mutants) * 100.0, 2) if total_mutants > 0 else (100.0 if summary_files else 0.0)

    if all_passed and summary_files:
        if min_score >= t_high:
            overall_status = "✅ HIGH"
        elif min_score >= t_low:
            overall_status = "🟡 LOW"
        elif min_score >= t_break:
            overall_status = "🟠 WARNING"
        else:
            overall_status = "❌ FAILED"
    else:
        overall_status = "❌ FAILED"

    execution_date = datetime.now(timezone.utc).isoformat()
    manifest = {
        "commit_sha": sha,
        "execution_date": execution_date,
        "overall_mutation_score": overall_score,
        "lowest_package_score": min_score,
        "total_mutants_killed": total_killed,
        "total_mutants": total_mutants,
        "threshold_high": t_high,
        "threshold_low": t_low,
        "threshold_break": t_break,
        "overall_status": overall_status,
        "passed_gate": all_passed,
        "run_url": run_url,
        "packages_count": len(package_results),
        "packages": package_results
    }

    os.makedirs("StrykerOutput", exist_ok=True)
    manifest_path = os.path.join("StrykerOutput", "stryker-release-manifest.json")
    with open(manifest_path, "w", encoding="utf-8") as f:
        json.dump(manifest, f, indent=2)

    step_summary_path = os.environ.get("GITHUB_STEP_SUMMARY")
    if step_summary_path:
        table_rows = []
        for pkg, res in package_results.items():
            pass_badge = "✅ Pass" if res.get("passed_break") else "❌ Fail"
            table_rows.append(f"| `{pkg}` | **{res.get('mutation_score', 0):.2f}%** | {res.get('mutants_killed', 0)} | {res.get('total_mutants', 0)} | {res.get('status', 'N/A')} | {pass_badge} |")

        rows_str = "\n".join(table_rows) if table_rows else "| _No packages recorded_ | - | - | - | - | - |"
        summary_md = f"""
# 🛡️ Stryker Mutation Testing Quality Gate Summary

| Metric | Value |
|--------|-------|
| **Overall Status** | {overall_status} |
| **Overall Mutation Score** | **{overall_score:.2f}%** |
| **Lowest Package Score** | **{min_score:.2f}%** |
| **Total Mutants Killed** | {total_killed} / {total_mutants} |
| **Thresholds (Break / Low / High)** | ≥{t_break}% / ≥{t_low}% / ≥{t_high}% |
| **Commit SHA** | `{sha[:7] if len(sha) >= 7 else sha}` |
| **Execution Date** | {execution_date} |

### 📦 Package Matrix Breakdown

| Package | Score | Killed | Total | Status | Gate |
|---------|-------|--------|-------|--------|------|
{rows_str}
"""
        with open(step_summary_path, "a", encoding="utf-8") as f:
            f.write(summary_md + "\n")

    github_output_path = os.environ.get("GITHUB_OUTPUT")
    if github_output_path:
        with open(github_output_path, "a", encoding="utf-8") as f:
            f.write(f"overall_score={overall_score}\n")
            f.write(f"lowest_score={min_score}\n")
            f.write(f"passed_gate={'true' if all_passed else 'false'}\n")
            f.write(f"status={overall_status}\n")
            f.write(f"packages_count={len(package_results)}\n")

    token = os.environ.get("GITHUB_TOKEN")
    if repo and sha and sha != "unknown" and "/" in repo:
        owner, repo_name = repo.split("/", 1)
        status_data = {
            "state": "success" if all_passed else "failure",
            "target_url": run_url,
            "description": f"Mutation Gate PASSED: min {min_score:.1f}%, avg {overall_score:.1f}% ({overall_status})" if all_passed else f"Mutation Gate FAILED: min {min_score:.1f}% below break {t_break}%"
        }
        post_commit_status(owner, repo_name, sha, token, status_data)

    print("\n==================================================")
    print(f"STRYKER MUTATION QUALITY GATE: {overall_status}")
    print(f"Overall Score: {overall_score:.2f}% | Lowest Package: {min_score:.2f}% | Passed: {all_passed}")
    print("==================================================\n")

    if not all_passed:
        print(f"Aggregate quality gate failed: one or more packages fell below break threshold {t_break}%", file=sys.stderr)
        sys.exit(1)

if __name__ == "__main__":
    main()
