# Copyright © Erickson Lopez. MIT License.
import json
import os
import sys
import glob
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

def find_json_reports(directory):
    results = []
    if not os.path.exists(directory):
        return results
    for root, _, files in os.walk(directory):
        for file in files:
            if file.endswith(".json") and not file.endswith(".html.json") and not file.endswith("metadata.json") and not file.startswith("summary-") and not file.startswith("stryker-release-manifest"):
                results.append(os.path.join(root, file))
    return results

def main():
    target_dir = sys.argv[1] if len(sys.argv) > 1 else "StrykerOutput/ci"
    pkg_name = sys.argv[2] if len(sys.argv) > 2 else "Idempotency"
    config_file = sys.argv[3] if len(sys.argv) > 3 else "stryker-config.json"

    thresholds = load_thresholds(config_file)
    t_high = thresholds["high"]
    t_low = thresholds["low"]
    t_break = thresholds["break"]

    score = 0.0
    killed = 0
    total = 0
    found_report = False

    json_files = find_json_reports(target_dir)
    if json_files:
        try:
            with open(json_files[0], "r", encoding="utf-8") as f:
                data = json.load(f)

            if "mutationScore" in data and data["mutationScore"] is not None:
                score = float(data["mutationScore"])

            files = data.get("files", {})
            for file_obj in files.values():
                for mutant in file_obj.get("mutants", []):
                    st = str(mutant.get("status", "")).lower()
                    if st in ["killed", "timeout"]:
                        killed += 1
                        total += 1
                    elif st in ["survived", "nocoverage"]:
                        total += 1

            if total > 0 and "mutationScore" not in data:
                score = round((killed / total) * 100.0, 2)
            elif total == 0:
                score = 100.0

            found_report = True
        except Exception as e:
            print(f"Warning: Error parsing {json_files[0]}: {e}")

    # Condition & status classification identical to EricksonLopez.SqlBuilder standard
    passed_gate = found_report and (score >= t_break or total == 0)
    if found_report:
        if score >= t_high or total == 0:
            status_label = "✅ HIGH"
        elif score >= t_low:
            status_label = "🟡 LOW"
        elif score >= t_break:
            status_label = "🟠 WARNING"
        else:
            status_label = "❌ FAILED"
    else:
        status_label = "❌ FAILED"

    sha = os.environ.get("GITHUB_SHA", "unknown")
    repo = os.environ.get("GITHUB_REPOSITORY", "")
    run_id = os.environ.get("GITHUB_RUN_ID", "")
    server_url = os.environ.get("GITHUB_SERVER_URL", "https://github.com")
    run_url = f"{server_url}/{repo}/actions/runs/{run_id}" if repo and run_id else ""
    execution_date = datetime.now(timezone.utc).isoformat()

    metadata = {
        "package": pkg_name,
        "commit_sha": sha,
        "execution_date": execution_date,
        "mutation_score": score,
        "mutants_killed": killed,
        "total_mutants": total,
        "threshold_high": t_high,
        "threshold_low": t_low,
        "threshold_break": t_break,
        "status": status_label,
        "passed_break": passed_gate,
        "run_url": run_url
    }

    os.makedirs("StrykerOutput", exist_ok=True)
    summary_file_path = os.path.join("StrykerOutput", f"summary-{pkg_name}.json")
    with open(summary_file_path, "w", encoding="utf-8") as f:
        json.dump(metadata, f, indent=2)

    step_summary_path = os.environ.get("GITHUB_STEP_SUMMARY")
    if step_summary_path:
        summary_md = f"""
## 🛡️ Stryker Mutation Testing Results — {pkg_name}

| Metric | Value |
|--------|-------|
| **Mutation Score** | **{score:.2f}%** |
| **Mutants Killed** | {killed} |
| **Total Mutants** | {total} |
| **Threshold High** | ≥{t_high}% |
| **Threshold Low** | ≥{t_low}% |
| **Threshold Break** | ≥{t_break}% |
| **Status** | {status_label} |
| **Commit SHA** | `{sha[:7] if len(sha) >= 7 else sha}` |
| **Execution date** | {execution_date} |
"""
        with open(step_summary_path, "a", encoding="utf-8") as f:
            f.write(summary_md + "\n")

    github_output_path = os.environ.get("GITHUB_OUTPUT")
    if github_output_path:
        with open(github_output_path, "a", encoding="utf-8") as f:
            f.write(f"score={score}\n")
            f.write(f"passed_gate={'true' if passed_gate else 'false'}\n")
            f.write(f"status={status_label}\n")
            f.write(f"killed={killed}\n")
            f.write(f"total={total}\n")

    print(f"[{pkg_name}] Stryker Score: {score:.2f}% ({killed}/{total}) - {status_label}")

    if not passed_gate:
        print(f"[{pkg_name}] ❌ Quality gate failed: score {score:.2f}% is below break threshold {t_break}%", file=sys.stderr)
        sys.exit(1)

if __name__ == "__main__":
    main()
