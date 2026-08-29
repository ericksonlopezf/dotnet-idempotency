// Copyright © Erickson Lopez. MIT License.
const fs = require('fs');
const path = require('path');
const https = require('https');

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

function findSummaryFiles(dir) {
  let results = [];
  if (!fs.existsSync(dir)) return results;
  const entries = fs.readdirSync(dir, { withFileTypes: true });
  for (const entry of entries) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) {
      results = results.concat(findSummaryFiles(full));
    } else if (entry.name.startsWith('summary-') && entry.name.endsWith('.json')) {
      results.push(full);
    }
  }
  return results;
}

async function postCommitStatus(owner, repo, sha, token, statusData) {
  if (!owner || !repo || !sha || !token) {
    console.log('Skipping commit status post: GITHUB_REPOSITORY, GITHUB_SHA, or GITHUB_TOKEN not provided.');
    return;
  }

  const postData = JSON.stringify({
    state: statusData.state,
    target_url: statusData.target_url || '',
    description: statusData.description.substring(0, 140),
    context: 'quality-gate/stryker-mutation'
  });

  const options = {
    hostname: 'api.github.com',
    port: 443,
    path: `/repos/${owner}/${repo}/statuses/${sha}`,
    method: 'POST',
    headers: {
      'User-Agent': 'dotnet-idempotency-stryker-gate',
      'Authorization': `Bearer ${token}`,
      'Content-Type': 'application/json',
      'Content-Length': Buffer.byteLength(postData)
    }
  };

  return new Promise((resolve) => {
    const req = https.request(options, (res) => {
      let body = '';
      res.on('data', chunk => { body += chunk; });
      res.on('end', () => {
        if (res.statusCode >= 200 && res.statusCode < 300) {
          console.log(`Successfully posted commit status to ${sha}: ${statusData.state}`);
        } else {
          console.warn(`GitHub API status update returned ${res.statusCode}: ${body}`);
        }
        resolve();
      });
    });

    req.on('error', (e) => {
      console.warn(`Error posting commit status: ${e.message}`);
      resolve();
    });

    req.write(postData);
    req.end();
  });
}

async function main() {
  const searchDir = process.argv[2] || 'StrykerOutput';
  const configFile = process.argv[3] || 'stryker-config.json';
  const thresholds = loadThresholds(configFile);

  const summaryFiles = findSummaryFiles(searchDir);
  console.log(`Found ${summaryFiles.length} package summary files in ${searchDir}`);

  const packageResults = {};
  let totalKilled = 0;
  let totalMutants = 0;
  let minScore = 100;
  let allPassed = summaryFiles.length > 0;
  let sha = process.env.GITHUB_SHA || 'unknown';
  let repo = process.env.GITHUB_REPOSITORY || '';
  let runId = process.env.GITHUB_RUN_ID || '';
  let serverUrl = process.env.GITHUB_SERVER_URL || 'https://github.com';
  let runUrl = repo && runId ? `${serverUrl}/${repo}/actions/runs/${runId}` : '';

  for (const file of summaryFiles) {
    try {
      const data = JSON.parse(fs.readFileSync(file, 'utf8'));
      const pkg = data.package || path.basename(file, '.json').replace('summary-', '');
      packageResults[pkg] = data;

      totalKilled += (data.mutants_killed || 0);
      totalMutants += (data.total_mutants || 0);

      const pkgScore = Number(data.mutation_score ?? 0);
      if (pkgScore < minScore) {
        minScore = pkgScore;
      }

      if (!data.passed_break) {
        allPassed = false;
      }

      if (data.commit_sha && data.commit_sha !== 'unknown') {
        sha = data.commit_sha;
      }
      if (data.run_url) {
        runUrl = data.run_url;
      }
    } catch (err) {
      console.warn(`Error reading summary file ${file}: ${err.message}`);
    }
  }

  const overallScore = totalMutants > 0
    ? Math.round((totalKilled / totalMutants) * 10000) / 100
    : (summaryFiles.length > 0 ? 100 : 0);

  let overallStatus = '❌ FAILED';
  if (allPassed && summaryFiles.length > 0) {
    if (minScore >= thresholds.high) overallStatus = '✅ HIGH';
    else if (minScore >= thresholds.low) overallStatus = '🟡 LOW';
    else if (minScore >= thresholds.break) overallStatus = '🟠 WARNING';
    else overallStatus = '❌ FAILED';
  } else {
    overallStatus = '❌ FAILED';
  }

  const executionDate = new Date().toISOString();
  const manifest = {
    commit_sha: sha,
    execution_date: executionDate,
    overall_mutation_score: overallScore,
    lowest_package_score: minScore,
    total_mutants_killed: totalKilled,
    total_mutants: totalMutants,
    threshold_high: thresholds.high,
    threshold_low: thresholds.low,
    threshold_break: thresholds.break,
    overall_status: overallStatus,
    passed_gate: allPassed,
    run_url: runUrl,
    packages_count: Object.keys(packageResults).length,
    packages: packageResults
  };

  fs.mkdirSync('StrykerOutput', { recursive: true });
  fs.writeFileSync(path.join('StrykerOutput', 'stryker-release-manifest.json'), JSON.stringify(manifest, null, 2));

  // Write Aggregate Step Summary
  const stepSummaryPath = process.env.GITHUB_STEP_SUMMARY;
  if (stepSummaryPath) {
    let tableRows = '';
    for (const [pkg, res] of Object.entries(packageResults)) {
      tableRows += `| \`${pkg}\` | **${res.mutation_score}%** | ${res.mutants_killed} | ${res.total_mutants} | ${res.status} | ${res.passed_break ? '✅ Pass' : '❌ Fail'} |\n`;
    }

    const summary = `
# 🛡️ Stryker Mutation Testing Quality Gate Summary

| Metric | Value |
|--------|-------|
| **Overall Status** | ${overallStatus} |
| **Overall Mutation Score** | **${overallScore}%** |
| **Lowest Package Score** | **${minScore}%** |
| **Total Mutants Killed** | ${totalKilled} / ${totalMutants} |
| **Thresholds (Break / Low / High)** | ≥${thresholds.break}% / ≥${thresholds.low}% / ≥${thresholds.high}% |
| **Commit SHA** | \`${sha.substring(0, 7)}\` |
| **Execution Date** | ${executionDate} |

### 📦 Package Matrix Breakdown

| Package | Score | Killed | Total | Status | Gate |
|---------|-------|--------|-------|--------|------|
${tableRows || '| _No packages recorded_ | - | - | - | - | - |\n'}
`;
    fs.appendFileSync(stepSummaryPath, summary);
  }

  // Set GitHub Outputs
  const outputPath = process.env.GITHUB_OUTPUT;
  if (outputPath) {
    fs.appendFileSync(outputPath, `overall_score=${overallScore}\n`);
    fs.appendFileSync(outputPath, `lowest_score=${minScore}\n`);
    fs.appendFileSync(outputPath, `passed_gate=${allPassed}\n`);
    fs.appendFileSync(outputPath, `status=${overallStatus}\n`);
    fs.appendFileSync(outputPath, `packages_count=${Object.keys(packageResults).length}\n`);
  }

  // Post GitHub Commit Status
  const token = process.env.GITHUB_TOKEN;
  if (repo && sha && sha !== 'unknown') {
    const [owner, repoName] = repo.split('/');
    const statusData = {
      state: allPassed ? 'success' : 'failure',
      target_url: runUrl,
      description: allPassed
        ? `Mutation Gate PASSED: min ${minScore}%, avg ${overallScore}% (${overallStatus})`
        : `Mutation Gate FAILED: min ${minScore}% is below break threshold ${thresholds.break}%`
    };
    await postCommitStatus(owner, repoName, sha, token, statusData);
  }

  console.log(`\n==================================================`);
  console.log(`STRYKER MUTATION QUALITY GATE: ${overallStatus}`);
  console.log(`Overall Score: ${overallScore}% | Lowest Package: ${minScore}% | Passed: ${allPassed}`);
  console.log(`==================================================\n`);

  if (!allPassed) {
    console.error(`Aggregate quality gate failed: one or more packages fell below break threshold ${thresholds.break}%`);
    process.exit(1);
  }
}

main();
