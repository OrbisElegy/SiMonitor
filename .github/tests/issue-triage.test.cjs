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
    payload: {
      action: options.action || 'opened',
      [options.eventName === 'pull_request_target' ? 'pull_request' : 'issue']: {
        number: 123, body: options.staleBody || text
      }
    }
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
  assert.equal(calls.length, 7);
  assert.ok(calls.every(([method, args]) => method === 'createLabel' && !['bug', 'invalid'].includes(args.name)));
  assert.deepEqual(await simulate(null, {
    eventName: 'push',
    existingLabels: ['bug', 'enhancement', 'area:program', 'area:waveform', 'needs-triage', 'invalid', 'triage:blind-check', 'No human-supervised', 'Suspicious human-supervised']
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

const originTitle = '内容来源与人工审核 / Content origin and human review';
const issueAgent = 'Agent 自动提交，本次内容未经人类审核 / Submitted automatically by an AI agent without human review';
const prAgent = 'Agent 自动提交，本次改动未经人类审核 / Submitted automatically by an AI agent without human review';
const prTemplate = readFileSync(path.join(__dirname, '../pull_request_template.md'), 'utf8');

function prBody({ agent = false, blind = false } = {}) {
  return prTemplate.replace(`- [ ] ${prAgent}`, `- [${agent ? 'x' : ' '}] ${prAgent}`)
    .replace(`- [ ] ${statements[2]}`, `- [${blind ? 'x' : ' '}] ${statements[2]}`);
}

async function simulatePr(text, options = {}) {
  const calls = await simulate(text, { eventName: 'pull_request_target', ...options,
    issue: { pull_request: {}, ...options.issue } });
  assert.deepEqual(updates(calls), [], 'PR intake must never close or otherwise update a PR');
  assert.ok(calls.every(([method]) => method !== 'removeLabel'), 'PR intake only adds labels');
  return calls;
}

test('issues declaring an unreviewed agent submission get the exact requested label without closing', async () => {
  for (const [kind] of kinds) {
    for (const action of ['opened', 'edited']) {
      const text = body(false, kind) + `\n\n### ${originTitle}\n\n${issueAgent}`;
      const calls = await simulate(text, { action });
      assert.ok(addedLabels(calls).includes('No human-supervised'));
      assert.deepEqual(updates(calls), []);
    }
  }
});

test('human-reviewed, unknown, absent, quoted, or commented issue declarations do not assert lack of review', async () => {
  for (const declaration of [
    '人类编写并提交 / Written and submitted by a human',
    'AI 参与编写，人类已逐项审核本次提交内容 / AI-assisted, with this submission fully reviewed by a human',
    '无法确认是否经过人类审核 / Human review status is unknown',
    '', `> ${issueAgent}`, `<!-- ${issueAgent} -->`, '```\n' + issueAgent + '\n```'
  ]) {
    const calls = await simulate(body() + `\n\n### ${originTitle}\n\n${declaration}`);
    assert.ok(!addedLabels(calls).includes('No human-supervised'));
  }
});

test('blind issues keep their existing closure policy alongside the new declaration label', async () => {
  const calls = await simulate(body(true) + `\n\n### ${originTitle}\n\n${issueAgent}`);
  assert.deepEqual(addedLabels(calls), ['invalid', 'triage:blind-check', 'No human-supervised']);
  assert.equal(updates(calls).length, 1);
});

test('PR agent and blind selections add independent tags and never close the PR', async () => {
  for (const action of ['opened', 'edited', 'reopened']) {
    for (const agent of [false, true]) {
      for (const blind of [false, true]) {
        const calls = await simulatePr(prBody({ agent, blind }), { action });
        assert.deepEqual(addedLabels(calls), [
          ...(agent ? ['No human-supervised'] : []), ...(blind ? ['triage:blind-check'] : [])
        ]);
      }
    }
  }
});

test('normal and ambiguous PR review declarations do not assert lack of review', async () => {
  for (const option of [
    '人类编写并提交 / Written and submitted by a human',
    'AI 参与编写，人类已逐项审核本次改动 / AI-assisted, with this change fully reviewed by a human'
  ]) {
    const text = prTemplate.replace(`- [ ] ${option}`, `- [x] ${option}`);
    assert.deepEqual(addedLabels(await simulatePr(text)), []);
    const contradictory = text.replace(`- [ ] ${prAgent}`, `- [x] ${prAgent}`);
    assert.deepEqual(addedLabels(await simulatePr(contradictory)), []);
  }
});

test('PR markers must be in their own sections, outside quotes, comments, and fences', async () => {
  for (const wrap of [
    text => '> ' + text,
    text => '<!--\n' + text + '\n-->',
    text => '```markdown\n' + text + '\n```',
    text => '~~~~markdown\n' + text + '\n~~~~',
    text => '    ' + text
  ]) {
    const text = prTemplate.replace(`- [ ] ${prAgent}`, wrap(`- [x] ${prAgent}`))
      .replace(`- [ ] ${statements[2]}`, wrap(`- [x] ${statements[2]}`));
    assert.deepEqual(addedLabels(await simulatePr(text)), []);
  }
  const elsewhere = `## Example\n\n- [x] ${prAgent}\n- [x] ${statements[2]}\n\n` + prTemplate;
  assert.deepEqual(addedLabels(await simulatePr(elsewhere)), []);
  assert.deepEqual(addedLabels(await simulatePr(prBody({ agent: true, blind: true }) + '\n' + prTemplate)), []);
});

test('PR parsing accepts uppercase checkmarks and CRLF while ignoring template comments', async () => {
  const text = prBody({ agent: true, blind: true }).replaceAll('[x]', '[X]').replaceAll('\n', '\r\n');
  assert.deepEqual(addedLabels(await simulatePr(text)), ['No human-supervised', 'triage:blind-check']);
});

test('PR content cannot enter the issue-closing path even if it imitates the issue form', async () => {
  await simulatePr(body(true) + '\n\n' + prBody({ blind: true }));
});

test('stale PR events use the latest declaration and leave corrected or closed PRs alone', async () => {
  assert.deepEqual(await simulatePr(prTemplate, { action: 'edited', staleBody: prBody({ agent: true, blind: true }) }), []);
  assert.deepEqual(await simulatePr(prBody({ blind: true }), { issue: { state: 'closed' } }), []);
  assert.deepEqual(await simulatePr(null), []);
});

test('PR tagging supports existing labels and concurrent initialization without resetting triage', async () => {
  const text = prBody({ agent: true, blind: true });
  const existing = await simulatePr(text, { existingLabels: ['No human-supervised', 'triage:blind-check'] });
  assert.ok(existing.every(([method]) => method === 'addLabels'));
  assert.deepEqual(addedLabels(await simulatePr(text, { labelRace: true })), ['No human-supervised', 'triage:blind-check']);
});

test('explicitly unknown issue review status gets Suspicious human-supervised, without asserting no human review or closing', async () => {
  for (const [kind] of kinds) {
    const text = body(false, kind) + `\n\n### ${originTitle}\n\n无法确认是否经过人类审核 / Human review status is unknown`;
    const calls = await simulate(text, { action: 'edited' });
    assert.deepEqual(addedLabels(calls), ['Suspicious human-supervised']);
    assert.deepEqual(updates(calls), []);
  }
});

test('unknown PR review status gets Suspicious human-supervised independently of the blind-check tag', async () => {
  for (const blind of [false, true]) {
    const text = prBody({ blind }).replace(
      '- [ ] 无法确认是否经过人类审核 / Human review status is unknown',
      '- [x] 无法确认是否经过人类审核 / Human review status is unknown'
    );
    const calls = await simulatePr(text);
    assert.deepEqual(addedLabels(calls), ['Suspicious human-supervised', ...(blind ? ['triage:blind-check'] : [])]);
    const ambiguous = text.replace(`- [ ] ${prAgent}`, `- [x] ${prAgent}`);
    assert.deepEqual(addedLabels(await simulatePr(ambiguous)), blind ? ['triage:blind-check'] : []);
  }
});

test('absent or quoted unknown declarations are not labeled Suspicious human-supervised', async () => {
  const unknown = '无法确认是否经过人类审核 / Human review status is unknown';
  for (const text of [body(), body() + `\n\n### ${originTitle}\n\n> ${unknown}`]) {
    assert.ok(!addedLabels(await simulate(text)).includes('Suspicious human-supervised'));
  }
  const text = prTemplate.replace(`- [ ] ${unknown}`, `> - [x] ${unknown}`);
  assert.deepEqual(addedLabels(await simulatePr(text)), []);
});
