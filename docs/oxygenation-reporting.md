# 氧合第三步：深低数值、测量恢复与报警验证

日期：2026-10-03。按已确定的产品行为，**低于 70% 且信号有效时继续显示数值**，
并使用该测量值判断下限提示。数值报告范围为 0–100%，不显示 `<70`，不夹在
70%／75%，也不因为数值低而改为 `PoorSignal`。

显示范围和准确度范围是不同概念。例如
[Philips 5500 IFU，印刷页 181](https://www.documents.philips.com/assets/Instruction%20for%20Use/20251028/723ae1fdf34a4b219d36b384012166a8.pdf?feed=ifu_docs_feed)
分别列出 0–100% 的显示范围和指定传感器在 70–100% 的准确度规格。
本项目采用前述数值显示策略；整个示意标定均未经过患者数据校准，不能把扩展范围
解释为获得了深低血氧的临床准确度。

## 实现与边界

[PulseOximeterIllustrationSource](../src/Monitor.Simulation/Authoring/PulseOximeterIllustrationSource.cs)
现在接受 0–100% 的固定目标或模型 SaO₂。模型源仍按每个光学样本的原始时间读取，
越过物理范围、缺失历史或时间不符继续拒绝，不使用旧值代替。

全范围回归发现，旧的线性减光方式会使红光均值随低血氧明显下降，导致 AC/DC 比值
偏移；这不是饱和度报告范围的问题。因此，前向示意改为指数衰减：

```text
R = (110 − SaO₂%) / 25
I_IR  = 20000 × exp(−x)
I_red = 16000 × exp(−R × x)
```

`x` 由已采集的外周脉动和显式光学调制系数决定。实现使用有界整数多项式，固定
20 项，以确定性的舍入产生 ADC 样本；不使用运行时浮点指数。最大绝对指数为 1.76。
与独立浮点指数计算的测试比较覆盖零、正负调制及最大支持指数，误差不超过半个
ADC 计数加测试容差。原来 100% 附近的 0.2 个百分点源侧保护余量继续保留。
这仍是项目编写的教学光学模型，不含人体组织光谱标定。

[OpticalSaturationMeasurement.CreateIllustration](../src/Monitor.Application/Measurements/OpticalSaturationMeasurement.cs)
统一提供两处实时测量所有者使用的示意标定，范围为 R=0.4–4.4，对应 100–0%。
原来的四秒估计窗口、双波长相关性、往返脉动、质量标志、最低调制和 PI 门限保留。
内部模型 SaO₂ 不直接进入读数、报警或音高判断。

只有显式选择该教学模式时，物理端点附近的有限 ADC／窗口误差才收束到 0%／100%；
中间范围不设平台。调用者自有标定不自动外推，也不启用此端点策略；光学比值超出其
覆盖范围时返回 `OutOfRange`，显示 `---` 和“SpO₂超出测量范围”，与信号质量不足区分。
比值超过估计器支持上界仍不生成数值。

模型目标不保证等于每个测量窗口的读数。全范围回归对 0%、1%、5%、20%、40%、
69%、70%、74%、75%、98%、100% 及三种调制幅度使用 4 个百分点的教学误差预算；
这是一项工程回归门限，不是准确度声明。零目标也可能测得略高于零的数值，显示和
报警均使用实际估计值，不为了匹配目标而覆盖结果。直接的双波长参照用例另行验证
估计器确实能报告数值 0，且不会把 0 当成无数据。

设置页的教学目标同步扩展为 0–100%。预设波动在物理范围内平滑连接端点，仍与
生理氧合模式互斥；预设波动不代表耗氧。光学模型版本改为
`PulseOximeterAttenuationIllustration@2`，波动版本改为 `SeededOpticalExcursions@4`。
原来的正常范围生成结果可能因光学算法修正而有小幅变化，不能声称字节兼容旧版本。

## 数值、质量与报警行为

| 情况 | 数值／显示 | 现有下限提示与声音 |
| --- | --- | --- |
| 有效信号，SpO₂ <70% | 继续显示数值；0 也是数值 | 继续比较未取整测量值，默认进入 Critical |
| 弱但仍通过质量门限 | 数值及原有低 PI 问号 | 不因问号自动屏蔽低值 |
| 无有效脉动、质量标志或不相关信号 | `---`，信号质量不足 | 移除当前低氧数值提示，保留技术信息 |
| 没有新样本 | `---`，无数据 | 不保留旧的低值作为当前测量 |
| 恢复有效脉动 | 按完整干净窗口重新测量 | 仍低则重新提示；不能因“刚恢复”默认正常 |
| 氧合恢复 | 显示实测恢复值 | 越过阈值后降级、解除 |

`SpO2LimitNotice` 保持既有即时、非锁存教学逻辑：低于 Warning 下限产生 Warning，
低于 Critical 下限产生 Critical，等于下限不触发该级别；默认下限为 92% 和 85%。
提示默认仍需用户启用。数值底色、提示轮换和声音请求消费同一测量结果。
有效深低数值使用现有音高曲线的最低音高档；这只是声音映射，不把读数改成 70%。

阈值附近的短暂等级切换仍可能发生：当前 SpO₂ 路径没有确认延时、迟滞或锁存。
本阶段验证现有提示链路，不将它宣称为完成了带确认／静音／锁存语义的正式报警系统。
失去信号后的低氧提示解除也不表示患者氧合已经恢复。

## 可重现的物理模型回放

情景：正常通气 30 s，开放气道呼吸暂停 150 s，恢复通气 120 s；整个过程中持续
规则机械射血。其余物理参数同[第二步回放](oxygenation-transport.md)。总计 37500 个
8 ms 输入区间和 37501 个模型 SaO₂ 样本；不模拟无流停搏后的氧输送。

```sh
dotnet run --project tests/Monitor.Specs/Monitor.Specs.csproj --no-build --configuration Release -- --oxygen-deep-transport-fixture artifacts/oxygenation-deep/input.json
python3 tools/replay_oxygenation_transport.py artifacts/oxygenation-deep/input.json --output artifacts/oxygenation-deep/result.json
dotnet run --project tests/Monitor.Specs/Monitor.Specs.csproj --no-build --configuration Release -- --oxygenation-deep-replay-check artifacts/oxygenation-deep/result.json
```

最后一条命令检查模型轨迹经过实际预览采集、红光／红外转换、四秒测量及阈值提示的
结果，输出最低值、低于 70% 的读数次数、预热后无效读数次数和报警等级转换时刻。
输出区分仿真推进时间与最后测量样本时间，避免将采集延迟当成生理延迟。

本轮采用修正后光学模型的回放结果：

| 项目 | 结果 |
| --- | --- |
| 模型 SaO₂ 最低值 | 42.016% |
| 实测 SpO₂ 基线／最低／末次 | 97.734%／42.667%／96.125% |
| 低于 70% 的查询读数 | 336 次，均保持 Critical |
| 预热后无效查询读数 | 0 次 |
| 氧收支最大残差 | 6.66e-10 mL(STPD) |

回放输出包含 Warning、Critical、恢复降级及解除的记录，也保留阈值附近的短暂
转换；这些是当前即时阈值逻辑的实际输出，未在结果文件中平滑或删去。

## 回归覆盖

- [全范围与恢复规格](../tests/Monitor.Specs/DeepOxygenationSpecifications.cs)：
  多个深低目标与调制幅度、端点、独立指数参照、自有标定边界、质量失败、缺失样本、
  窗口中途恢复、25 ms／250 ms 推进一致性、机械停搏时不可测，以及报警 PCM 的产生和清除。
- [桌面验证](../src/Monitor.Desktop/DeepOxygenationSmokeChecks.cs)：
  实际设置 Apply 接受零值；通过模型时间序列和机械事件获得的测量驱动数值、红色底色、
  技术信息、Critical 声音请求及恢复，不注入伪造的 SpO₂ 读数。
- 完整规格与桌面分片入口沿用 `tools/run_parallel_checks.py`；Linux 通过 Xvfb 执行。
  PCM 与声音请求验证不替代 Windows 实机音频设备验证。
