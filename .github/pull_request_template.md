<!--
请用中文或英文填写，删除不适用部分；未完成的验证请如实说明。
Complete in Chinese or English and remove inapplicable sections. State any validation still pending.
标题建议：subsystem: describe the change
Suggested title: subsystem: describe the change
-->

## 目的与关联 / Purpose and related issues

<!-- 描述要解决的问题、触发场景及修改前后的行为。链接相关 issue；仅在本 PR 完整解决时使用 Closes #123。
Describe the problem, trigger, and behavior before/after. Link related issues; use Closes #123 only when fully resolved by this PR. -->

## 改动类型 / Change type

<!-- 勾选适用项。Select all that apply. -->

- [ ] 程序逻辑修复 / Program bug fix
- [ ] 波形、生理模型或测量修复 / Waveform, physiology, or measurement fix
- [ ] 新功能或功能改进 / Feature or enhancement
- [ ] CI、构建或开发工具 / CI, build, or developer tooling
- [ ] 文档或其他维护 / Documentation or other maintenance

## 实现与影响 / Implementation and impact

<!-- 说明主要改动、选择该方案的原因，以及兼容性、设置/数据迁移或其他风险。界面改动可附截图。
Explain the main changes, why this approach was chosen, and compatibility, settings/data migration, or other risks. Include screenshots for UI changes when useful. -->

## 验证 / Validation

<!-- 只填写实际执行的检查及结果；尚未执行请注明原因。CI 通过不替代 Windows 实机或音频设备验证。
Report checks actually run and their results; explain anything not run. Passing CI does not replace Windows hardware or audio-device validation. -->

- 本地检查（命令、平台、结果） / Local checks (commands, platform, results):
- CI 运行链接与结果 / CI run link and result:
- 手工验证或待验证项 / Manual validation or pending checks:

## 波形相关依据 / Waveform evidence

<!-- 仅涉及波形、生理模型或测量时填写；否则删除本节。
Complete only for waveform, physiology, or measurement changes; otherwise remove this section. -->

- 通道/导联、输入参数与单位、预期输出 / Channels/leads, inputs and units, expected output:
- 参考依据（教材版次/页码、DOI 或链接）与适用边界 / References (edition/pages, DOI, or links) and limits:
- 相关知识背景与不确定部分 / Relevant expertise and uncertainties:

## 内容来源与人工审核 / Content origin and human review

<!-- 仅勾选一项，范围包括本次代码、文档和 PR 说明。人工审核指逐项阅读并核验此次改动；仅布置任务或批准 Agent 运行不计为审核。
Select exactly one, covering the code, documentation, and PR description. Human review means reading and checking this specific change; assigning a task or authorizing an agent to run does not count. -->

- [ ] 人类编写并提交 / Written and submitted by a human
- [ ] AI 参与编写，人类已逐项审核本次改动 / AI-assisted, with this change fully reviewed by a human
- [ ] Agent 自动提交，本次改动未经人类审核 / Submitted automatically by an AI agent without human review
- [ ] 无法确认是否经过人类审核 / Human review status is unknown

<!-- 如使用 AI 工具，说明工具/模型、参与范围及人工审核范围。提交记录中的工具署名遵循 CONTRIBUTING.md。
If AI tools were used, identify tools/models, their contribution, and the scope of human review. Follow CONTRIBUTING.md for attribution in commits. -->

## 提交前确认 / Submission checklist

- [ ] 我已阅读[贡献指南](https://github.com/OrbisElegy/SiMonitor/blob/master/CONTRIBUTING.md)，提交身份、署名与 sign-off 符合要求。 / I read the contribution guide and followed its authorship, attribution, and sign-off requirements.
- [ ] 我已说明验证结果与未验证部分，并按需更新相关文档和回归检查。 / I described validation results and gaps, and updated relevant documentation and regression checks where needed.
- [ ] I'm checking these boxes blindly.
- [ ] 我保留了适用的许可证和第三方归属声明。 / I preserved applicable licenses and third-party attribution.
