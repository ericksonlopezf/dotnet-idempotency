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

function fetchJson(url, token) {
  return new Promise((resolve, reject) => {
    const urlObj = new URL(url);
    const options = {
      hostname: urlObj.hostname,
      port: 443,
      path: urlObj.pathname + urlObj.search,
      method: 'GET',
      headers: {
        'User-Agent': 'dotnet-idempotency-release-gate',
        'Accept': 'application/vnd.github.v3+json'
      }
    };
    if (token) {
      options.headers['Authorization'] = `Bearer ${token}`;
    }

    const req = https.request(options, (res) => {
      let body = '';
      res.on('data', chunk => { body += chunk; });
      res.on('end', () => {
        if (res.statusCode >= 200 && res.statusCode < 300) {
          try {
            resolve(JSON.parse(body));
          } catch (e) {
            reject(new Error(`Failed to parse JSON response: ${e.message}`));
          }
        } else {
          reject(new Error(`GitHub API request failed with status ${res.statusCode}: ${body}`));
        }
      });
    });

    req.on('error', reject);
    req.end();
  });
}

async function queryCommitStatus(owner, repo, sha, token) {
  const url = `https://api.github.com/repos/${owner}/${repo}/commits/${sha}/statuses`;
  try {
    const statuses = await fetchJson(url, token);
    if (Array.isArray(statuses)) {
      const mutationStatus = statuses.find(s => s.context === 'quality-gate/stryker-mutation');
      if (mutationStatus) {
        return {
          source: 'commit_status',
          state: mutationStatus.state,
          description: mutationStatus.description,
          target_url: mutationStatus.target_url,
          created_at: mutationStatus.created_at,
          updated_at: mutationStatus.updated_at
        };
      }
    }
  } catch (err) {
    console.warn(`Could not query commit statuses: ${err.message}`);
  }
  return null;
}

async function queryWorkflowRuns(owner, repo, sha, token) {
  const url = `https://api.github.com/repos/${owner}/${repo}/actions/workflows/mutation-testing.yml/runs?head_sha=${sha}&status=completed`;
  try {
    const data = await fetchJson(url, token);
    if (data && data.workflow_runs && data.workflow_runs.length > 0) {
      const latestRun = data.workflow_runs[0];
      return {
        source: 'workflow_run',
        conclusion: latestRun.conclusion,
        created_at: latestRun.created_at,
        updated_at: latestRun.updated_at,
        html_url: latestRun.html_url
      };
    }
  } catch (err) {
    console.warn(`Could not query workflow runs: ${err.message}`);
  }
  return null;
}

function checkLocalManifest(sha) {
  const manifestPath = path.join('StrykerOutput', 'stryker-release-manifest.json');
  if (fs.existsSync(manifestPath)) {
    try {
      const manifest = JSON.parse(fs.readFileSync(manifestPath, 'utf8'));
      if (!sha || manifest.commit_sha === sha || manifest.commit_sha === 'unknown') {
        return {
          source: 'local_manifest',
          passed: manifest.passed_gate,
          score: manifest.overall_mutation_score,
          minScore: manifest.lowest_package_score,
          date: manifest.execution_date,
          status: manifest.overall_status,
          manifest
        };
      }
    } catch (err) {
      console.warn(`Could not read local manifest: ${err.message}`);
    }
  }
  return null;
}

async function main() {
  const targetSha = process.argv[2] || process.env.GITHUB_SHA || '';
  const configFile = process.argv[3] || 'stryker-config.json';
  const thresholds = loadThresholds(configFile);

  const repo = process.env.GITHUB_REPOSITORY || '';
  const token = process.env.GITHUB_TOKEN || '';

  console.log(`==================================================`);
  console.log(`  RELEASE MUTATION TESTING QUALITY GATE AUDITOR   `);
  console.log(`  Target Commit SHA: ${targetSha || 'HEAD'}`);
  console.log(`  Break Threshold:   ≥${thresholds.break}%`);
  console.log(`==================================================\n`);

  let commitAnalyzed = targetSha || 'Unknown';
  let executionDate = 'N/A';
  let mutationScoreText = 'N/A';
  let passedBreak = false;
  let releasePermitted = false;
  let statusBadge = '❌ FAILED';
  let verificationDetails = '';

  // 1. Check local manifest first (if available in artifacts)
  const localManifest = checkLocalManifest(targetSha);
  if (localManifest) {
    commitAnalyzed = localManifest.manifest.commit_sha || targetSha;
    executionDate = localManifest.date;
    mutationScoreText = `${localManifest.score}% (Lowest Package: ${localManifest.minScore}%)`;
    passedBreak = localManifest.passed;
    releasePermitted = passedBreak;
    statusBadge = localManifest.status;
    verificationDetails = `Verified from local release manifest (${localManifest.manifest.packages_count} packages evaluated).`;
  }

  // 2. Query GitHub Commit Status API if not already verified or to corroborate
  if (!releasePermitted && repo && targetSha && token) {
    const [owner, repoName] = repo.split('/');
    const commitStatus = await queryCommitStatus(owner, repoName, targetSha, token);
    if (commitStatus) {
      commitAnalyzed = targetSha;
      executionDate = commitStatus.updated_at || commitStatus.created_at;
      mutationScoreText = commitStatus.description || 'Recorded in commit status';
      passedBreak = (commitStatus.state === 'success');
      releasePermitted = passedBreak;
      statusBadge = passedBreak ? '✅ PASSED' : '❌ FAILED';
      verificationDetails = `Verified from GitHub Commit Status [${commitStatus.state}]: ${commitStatus.description}`;
    } else {
      // 3. Check workflow runs API as fallback
      const workflowRun = await queryWorkflowRuns(owner, repoName, targetSha, token);
      if (workflowRun) {
        commitAnalyzed = targetSha;
        executionDate = workflowRun.updated_at || workflowRun.created_at;
        mutationScoreText = `Workflow Run conclusion: ${workflowRun.conclusion}`;
        passedBreak = (workflowRun.conclusion === 'success');
        releasePermitted = passedBreak;
        statusBadge = passedBreak ? '✅ PASSED' : '❌ FAILED';
        verificationDetails = `Verified from GitHub Actions workflow run: ${workflowRun.html_url}`;
      }
    }
  }

  const releaseDecisionText = releasePermitted ? '✅ RELEASE PERMITTED' : '❌ RELEASE BLOCKED';

  console.log(`[Q1] Which commit was analyzed?`);
  console.log(`     → ${commitAnalyzed}`);
  console.log(`[Q2] When?`);
  console.log(`     → ${executionDate}`);
  console.log(`[Q3] What mutation score was obtained?`);
  console.log(`     → ${mutationScoreText}`);
  console.log(`[Q4] Did it pass the break threshold (≥${thresholds.break}%)?`);
  console.log(`     → ${passedBreak ? 'YES' : 'NO'}`);
  console.log(`[Q5] Can the release proceed?`);
  console.log(`     → ${releaseDecisionText}\n`);

  // Write GitHub Step Summary
  const stepSummaryPath = process.env.GITHUB_STEP_SUMMARY;
  if (stepSummaryPath) {
    const summary = `
# 🚀 Release Mutation Quality Gate Verification

| Release Audit Question | Result |
|------------------------|--------|
| **1. Commit Analyzed** | \`${commitAnalyzed.substring(0, 10)}\` |
| **2. Execution Date** | ${executionDate} |
| **3. Mutation Score Result** | **${mutationScoreText}** |
| **4. Passed Break Threshold (≥${thresholds.break}%)** | ${passedBreak ? '✅ YES' : '❌ NO'} |
| **5. Release Gate Decision** | **${releaseDecisionText}** |

> **Audit Details**: ${verificationDetails || 'No valid mutation quality gate evidence found for target commit.'}
`;
    fs.appendFileSync(stepSummaryPath, summary);
  }

  // Set GitHub Outputs
  const outputPath = process.env.GITHUB_OUTPUT;
  if (outputPath) {
    fs.appendFileSync(outputPath, `release_permitted=${releasePermitted}\n`);
    fs.appendFileSync(outputPath, `commit_analyzed=${commitAnalyzed}\n`);
    fs.appendFileSync(outputPath, `passed_break=${passedBreak}\n`);
  }

  if (!releasePermitted) {
    console.error(`\n❌ RELEASE BLOCKED: Commit ${commitAnalyzed} has not satisfied the mutation testing quality gate (score >= ${thresholds.break}%).`);
    console.error(`Please ensure that mutation testing on 'main' has completed successfully before publishing a release.`);
    process.exit(1);
  }

  console.log(`✅ RELEASE PERMITTED: Mutation score meets or exceeds break threshold (${thresholds.break}%). Proceeding to package publication.\n`);
}

main();
