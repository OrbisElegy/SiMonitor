// SPDX-License-Identifier: AGPL-3.0-or-later
const assert = require('node:assert/strict');
const { readFileSync } = require('node:fs');
const path = require('node:path');
const test = require('node:test');

// Run the actual inline workflow script against a fake API, without dependencies.
const workflow = readFileSync(path.join(__dirname, '../workflows/issue-triage.yml'), 'utf8');
const scriptBlock = workflow.match(/^          script: \|\n((?: {12}[^\n]*\n|\n)+)$/m);
assert.ok(scriptBlock, 'Expected one final inline script block in the workflow');
const AsyncFunction = Object.getPrototypeOf(async function () {}).constructor;
const run = new AsyncFunction('github', 'context', scriptBlock[1].replace(/^ {12}/gm, ''));

const typeHeading = '### 反馈类型 / Issue type';
const checklistHeading = '### 提交前确认 / Submission checklist';
const statements = [
  '我已搜索现有 issues，未找到相同反馈。 / I searched existing issues and found no duplicate.',
  '我理解本项目仅用于教学模拟。 / I understand this project is for teaching simulation only.',
  "I'm checking these boxes blindly.",
  '我已核对以上信息，并愿意按需补充定位材料。 / I checked the information above and can provide clarification if needed.'
];
const kinds = [
  ['程序逻辑 Bug / Program bug', ['bug', 'area:program']],
  ['波形问题 / Waveform problem', ['bug', 'area:waveform']],
  ['程序功能建议 / Program feature request', ['enhancement', 'area:program']],
  ['波形功能建议 / Waveform feature request', ['enhancement', 'area:waveform']]
];

function checklist(blind = false) {
  return checklistHeading + '\n\n' + statements.map(
    (statement, index) => `- [${index === 2 && !blind ? ' ' : 'x'}] ${statement}`
  ).join('\n');
}

function body(blind = false, kind = kinds[0][0]) {
  return `${typeHeading}\n\n${kind}\n\n### 实际行为 / Actual behavior\n\nExample\n\n${checklist(blind)}`;
}

async function simulate(text, options = {}) {
  const calls = [];
  const labels = new Set(options.existingLabels || []);
  const issue = { body: text, state: 'open', ...options.issue };
  const apiError = status => Object.assign(new Error(`API ${status}`), { status });
  const github = { rest: { issues: {
    get: async () => ({ data: issue }),
    getLabel: async ({ name }) => {
      if (options.readError) throw apiError(options.readError);
      if (!labels.has(name)) throw apiError(404);
    },
    createLabel: async args => {
      calls.push(['createLabel', args]);
      if (options.createError) throw apiError(options.createError);
      labels.add(args.name);
      if (options.labelRace) throw apiError(422);
    },
    addLabels: async args => { calls.push(['addLabels', args]); },
    removeLabel: async args => {
      calls.push(['removeLabel', args]);
      if (options.removeError) throw apiError(options.removeError);
    },
    update: async args => { calls.push(['update', args]); }
  } } };
  await run(github, {
    repo: { owner: 'example', repo: 'simulator' },
    eventName: options.eventName || 'issues',
    payload: { action: options.action || 'opened', issue: { number: 123, body: options.staleBody || text } }
  });
  return calls;
}

const updates = calls => calls.filter(([method]) => method === 'update');
const addedLabels = calls => calls.filter(([method]) => method === 'addLabels').flatMap(([, args]) => args.labels);

test('each normal form gets its type and area labels and stays open', async () => {
  for (const [kind, labels] of kinds) {
    const calls = await simulate(body(false, kind));
    assert.deepEqual(addedLabels(calls), [...labels, 'needs-triage']);
    assert.deepEqual(updates(calls), []);
  }
});

test('checked blind item closes each recognized form as not planned', async () => {
  for (const [kind] of kinds) {
    const calls = await simulate(body(true, kind));
    assert.deepEqual(addedLabels(calls), ['invalid', 'triage:blind-check']);
    assert.equal(calls.find(([method]) => method === 'removeLabel')[1].name, 'needs-triage');
    assert.deepEqual(updates(calls), [['update', {
      owner: 'example', repo: 'simulator', issue_number: 123, state: 'closed', state_reason: 'not_planned'
    }]]);
  }
});

test('uppercase checked markers and Windows newlines are accepted on edits', async () => {
  const calls = await simulate(body(true).replaceAll('[x]', '[X]').replaceAll('\n', '\r\n'), { action: 'edited' });
  assert.equal(updates(calls).length, 1);
});

test('mentioning or quoting the phrase outside the checklist does not close', async () => {
  for (const prefix of [
    `### Example\n\n- [x] ${statements[2]}\n\n`,
    checklist(true).split('\n').map(line => '> ' + line).join('\n') + '\n\n',
    '```markdown\n' + checklist(true) + '\n```\n\n',
    '~~~~markdown\n' + checklist(true) + '\n~~~~\n\n',
    '````markdown\n```\n' + checklist(true) + '\n````\n\n'
  ]) {
    assert.deepEqual(updates(await simulate(prefix + body())), []);
  }
});

test('unknown types, incomplete checklists, and duplicate headings do not close', async () => {
  for (const text of [
    checklist(true),
    body(true, 'Unknown'),
    body(true).replace(`- [x] ${statements[0]}\n`, ''),
    body(true) + '\n\n' + checklist(true),
    `${typeHeading}\n\n${kinds[0][0]}\n\n` + body(true),
    body(true).replace(checklistHeading, '### Other section'),
    body(true).replace(`- [x] ${statements[2]}`, `> - [x] ${statements[2]}`),
    body(true).replace(`- [x] ${statements[2]}`, '````\n- [x] ' + statements[2] + '\n````')
  ]) {
    assert.deepEqual(updates(await simulate(text)), []);
  }
});

test('an old queued event reads the corrected current issue body', async () => {
  const calls = await simulate(body(), { staleBody: body(true), action: 'edited' });
  assert.deepEqual(calls, []);
});

test('closed issues, pull requests, and missing bodies are untouched', async () => {
  assert.deepEqual(await simulate(body(true), { issue: { state: 'closed' } }), []);
  assert.deepEqual(await simulate(body(true), { issue: { pull_request: {} } }), []);
  assert.deepEqual(await simulate(null), []);
});

test('normal edits preserve manual triage labels', async () => {
  assert.deepEqual(await simulate(body(), { action: 'edited' }), []);
});

test('bootstrap only creates missing labels and does not touch issues', async () => {
  const calls = await simulate(null, { eventName: 'workflow_dispatch', existingLabels: ['bug', 'invalid'] });
  assert.equal(calls.length, 5);
  assert.ok(calls.every(([method, args]) => method === 'createLabel' && !['bug', 'invalid'].includes(args.name)));
  assert.deepEqual(await simulate(null, {
    eventName: 'push',
    existingLabels: ['bug', 'enhancement', 'area:program', 'area:waveform', 'needs-triage', 'invalid', 'triage:blind-check']
  }), []);
});

test('concurrent label creation and an absent triage label are recoverable', async () => {
  assert.equal(updates(await simulate(body(true), { labelRace: true, removeError: 404 })).length, 1);
});

test('permission and validation failures surface instead of silently closing', async () => {
  for (const options of [{ readError: 403 }, { createError: 422 }, { createError: 403 }, { removeError: 403 }]) {
    await assert.rejects(simulate(body(true), options), /API/);
  }
});

test('issue content is handled as data', async () => {
  const text = body().replace('Example', '${{ secrets.GITHUB_TOKEN }}\n`throw new Error("injected")`\n$(exit 1)');
  assert.deepEqual(updates(await simulate(text)), []);
  assert.ok(!scriptBlock[1].includes('${{'), 'Never interpolate workflow expressions into JavaScript');
});
