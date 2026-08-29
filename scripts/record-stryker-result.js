// Copyright © Erickson Lopez. MIT License.
const fs = require('fs');
const path = require('path');

function loadThresholds(configPath = 'stryker-config.json') {
  let thresholds = { high: 100, low: 98, break: 95 };
  try {
    if (fs.existsSync(configPath)) {
      const config = JSON.parse(fs.readFileSync(configPath, 'utf8'));
      const t = config['stryker-config']?.thresholds || config.thresholds || {};
      thresholds = { high: t.high ?? 100, low: t.low ?? 98, break: t.break ?? 95 };
    }
  } catch (err) {
    console.warn(`Could not parse ${configPath}: ${err.message}`);
  }
  return thresholds;
}

function findJsonReports(dir) {
  let results = [];
  if (!fs.existsSync(dir)) return results;
  const entries = fs.readdirSync(dir, { withFileTypes: true });
  for (const entry of entries) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) {
      results = results.concat(findJsonReports(full));
    } else if (entry.name.endsWith('.json') && !entry.name.endsWith('.html.json') && !entry.name.endsWith('metadata.json') && !entry.name.startsWith('summary-')) {
      results.push(full);
    }
  }
  return results;
}

function main() {
  const targetDir = process.argv[2] || 'StrykerOutput/ci';
  const pkgName = process.argv[3] || 'Idempotency';
  const configFile = process.argv[4] || 'stryker-config.json';

  const thresholds = loadThresholds(configFile);
  let score = 0;
  let killed = 0;
  let total = 0;
  let foundReport = false;

  const jsonFiles = findJsonReports(targetDir);
  if (jsonFiles.length > 0) {
    try {
      const data = JSON.parse(fs.readFileSync(jsonFiles[0], 'utf8'));
      if (data.mutationScore !== undefined) {
        score = Number(data.mutationScore);
      }
      const files = data.files || {};
      for (const f of Object.values(files)) {
        for (const m of (f.mutants || [])) {
          const st = String(m.status || '').toLowerCase();
          if (st === 'killed' || st === 'timeout') {
            killed++;
            total++;
          } else if (st === 'survived' || st === 'nocoverage') {
            total++;
          }
        }
      }
      if (total > 0 && data.mutationScore === undefined) {
        score = Math.round((killed / total) * 10000) / 100;
      }
      foundReport = true;
    } catch (err) {
      console.warn(`Error parsing ${jsonFiles[0]}: ${err.message}`);
    }
  }

  const passedGate = score >= thresholds.break && foundReport;
  let statusLabel = '❌ FAILED';
  if (score >= thresholds.high) statusLabel = '✅ HIGH';
  else if (score >= thresholds.low) statusLabel = '🟡 LOW';
  else if (score >= thresholds.break) statusLabel = '🟠 WARNING';

  const sha = process.env.GITHUB_SHA || 'unknown';
  const repo = process.env.GITHUB_REPOSITORY || '';
  const runId = process.env.GITHUB_RUN_ID || '';
  const serverUrl = process.env.GITHUB_SERVER_URL || 'https://github.com';
  const runUrl = repo && runId ? `${serverUrl}/${repo}/actions/runs/${runId}` : '';

  const executionDateIso = new Date().toISOString();

  // Save metadata artifact
  const metadata = {
    package: pkgName,
    commit_sha: sha,
    execution_date: executionDateIso,
    mutation_score: score,
    mutants_killed: killed,
    total_mutants: total,
    threshold_high: thresholds.high,
    threshold_low: thresholds.low,
    threshold_break: thresholds.break,
    status: statusLabel,
    passed_break: passedGate,
    run_url: runUrl
  };

  const outDir = 'StrykerOutput';
  if (!fs.existsSync(outDir)) {
    fs.mkdirSync(outDir, { recursive: true });
  }

  const summaryFile = path.join(outDir, `summary-${pkgName}.json`);
  fs.writeFileSync(summaryFile, JSON.stringify(metadata, null, 2), 'utf8');

  console.log(`============================================================`);
  console.log(`  STRYKER MUTATION TEST SUMMARY: ${pkgName}`);
  console.log(`============================================================`);
  console.log(`Mutation Score  : ${score}% (${statusLabel})`);
  console.log(`Mutants Killed  : ${killed} / ${total}`);
  console.log(`Break Threshold : ≥${thresholds.break}%`);
  console.log(`Gate Status     : ${passedGate ? '✅ PASSED' : '❌ FAILED'}`);
  console.log(`Summary Output  : ${summaryFile}`);
  console.log(`============================================================\n`);

  // Write markdown step summary if supported
  const summaryEnv = process.env.GITHUB_STEP_SUMMARY;
  if (summaryEnv && fs.existsSync(path.dirname(summaryEnv))) {
    const md = `
### 🧬 Stryker Mutation Testing Result — ${pkgName}

| Metric | Value |
|---|---|
| **Package** | \`${pkgName}\` |
| **Mutation Score** | **${score}%** |
| **Mutants Killed** | ${killed} / ${total} |
| **Threshold High / Low / Break** | ≥${thresholds.high}% / ≥${thresholds.low}% / ≥${thresholds.break}% |
| **Status** | ${statusLabel} |
| **Passed Break Gate** | ${passedGate ? '✅ Yes' : '❌ No'} |
| **Commit SHA** | \`${sha.substring(0, 7)}\` |
`;
    fs.appendFileSync(summaryEnv, md, 'utf8');
  }
}

main();
