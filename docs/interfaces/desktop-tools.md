# 桌面与命令行入口

[接口总览](README.md) · [桌面公开声明](api/desktop.md)

本页记录桌面宿主装配、Avalonia 输入边界及仓库开发命令。命令从仓库根目录运行；`.dll` 路径须对应已经构建的配置。Windows 下将 `python3` 替换为 `py -3`。构建前提见[项目 README](../../README.md)。

## 桌面启动

源码：[Program.cs](../../src/Monitor.Desktop/Program.cs)、[MonitorApp.cs](../../src/Monitor.Desktop/MonitorApp.cs)、[项目配置](../../src/Monitor.Desktop/Monitor.Desktop.csproj)。

```sh
dotnet run --project src/Monitor.Desktop --no-build --configuration Release
```

默认入口为 `DesignPreviewWindow`，装配 `LocalMonitorPreviewSession`、`LiveMonitorTrace`、`LiveMonitorView`、设置和声音。`MainWindow` 是已捕获记录交互壳，用于开发演示和检查；它不是当前默认监护窗口。

首次启动和恢复默认设置使用同一组压力设置：ABP 目标 120/80 mmHg、PA 目标 25/10 mmHg 均启用，CVP 基线为 6 mmHg。
启动时即按这些设置创建采样源，无需先点击应用。已保存的目标开关和值优先；旧生成器配置缺少目标开关时保持原脉搏倍率模式。
平均压继续从实际波形采样计算，不由收缩压／舒张压目标直接写入读数。

开发构建可在 `dotnet run ... --` 后传以下互斥启动参数，或者直接执行 `dotnet PATH/Monitor.Desktop.dll ARGS`：

| 参数 | 行为 |
|---|---|
| 无参数 | 打开默认监护界面并加载本地偏好 |
| `--ui-preview` | 默认入口的兼容别名 |
| `--waveform-demo` | 周期波形演示窗口 |
| `--physiology-demo` | 生理波形演示窗口 |
| `--electrode-demo` | 电极投影演示窗口 |
| `--study-demo` | 在记录壳中启动合成记录及卡尺交互 |
| `--smoke-test` | 启动桌面规格检查；完成后退出 |
| `--smoke-shard INDEX COUNT` | 分片桌面检查；`0 <= INDEX < COUNT <= 32`，各片为独立进程 |
| `--product-check [ASSEMBLY_PATH]` | 检查产品程序集；可指定路径，入口仍由开发版承载 |
| `--generate-style-previews OUTPUT_PATH` | 生成有限预览目录后退出，不启动 Avalonia 窗口 |

声音输出在 Linux 默认使用系统 ALSA 库，其他平台默认使用原生音频库。启动前设置环境变量 `SIMONITOR_AUDIO_OUTPUT` 为 `native`、`wasapi` 或 `alsa`（不区分大小写）可覆盖默认值，例如在 Windows 上用 `wasapi` 与原生输出对比；未设置或其他值使用平台默认。

未知组合／多余参数返回 `2`。`ProductRelease=true` 定义 `SIMONITOR_RELEASE`，产品程序只接受无参数启动；包括 `--ui-preview` 在内的开发参数均返回 `2`。普通 `Release` 编译配置本身不等于 `ProductRelease=true`。

## 本地监护装配与时间

源码：[DesignPreviewWindow.cs](../../src/Monitor.Desktop/DesignPreviewWindow.cs)、[DemoFrameTiming.cs](../../src/Monitor.Desktop/DemoFrameTiming.cs)、[LiveMonitorView.cs](../../src/Monitor.Desktop/LiveMonitorView.cs)。这些装配类是 `internal`，不构成外部插件接口。

1. 创建配置、显示与氧合源，再调用 `DiscardStartup()` 裁去源的启动过渡段。
2. UI `DispatcherTimer` 按约 16 ms 唤醒；由 `Stopwatch` 得到经过时间并传给本地预览。单次延迟超过 250 ms 时按 50 ms 推进，不补算整个挂起时间。仿真内部仍使用显式纳秒时间。
3. `Pulse` 只接受当前活动 timer；旧 timer 回调、已暂停或已关闭窗口不推进会话。随后刷新波形、测量、通知与声音。
4. 数字显示按 200 ms 的仿真时间桶刷新，扫线绘制可更频繁；`Measurements`、`FrontierNs` 与 `SimulationTimeNs` 各自代表不同边界。
5. 普通应用设置使用 `ScheduleSource(next, delayNs)` 接续，在生效时切换记录快照和已应用配置；再次应用可替换未生效设置。重新开始才新建会话、裁去启动段并重置相关通知与声音状态。
6. `Pause` 停止并解绑 timer，暂停监护声音；关闭窗口还释放声音输出资源。

偏好路径由 `Environment.SpecialFolder.LocalApplicationData` 加 `Monitor/display-preferences.json` 构成；不依赖启动工作目录。保存失败时本次内存配置仍可生效，由界面报告不能持久保存。具体字段、版本和错误语义见[音频与持久化](audio-persistence.md)。

语言选择位于“设置 → 通用”，即时切换已迁移的中英文文案，并独立保存至
`Monitor/display-preferences.language.json`。它不应用参数或替换仿真会话；恢复全部默认设置
会同时恢复中文。接入范围、绑定和失败语义见[本地化接口](localization.md)。

## 记录呈现与原生输入

| 公开入口 | 输入／输出 | 调用约束 |
|---|---|---|
| `MainWindow.ApplyPublication(publication, readyContent = null)` | `CapturedRecordSvgPublication` 与同一次发布的 `RecordStudyControl` | UI 线程；先撤去旧内容及 input。`Ready` 必须携带 input 与同一对象身份的 control；其他状态不得携带 input |
| `RecordStudyPresenter.Refresh(...)` | 当前 overlay、layout、screen、grid、cursor、auxiliary-rate 策略及取消令牌 | 串行 UI 更新；先发布 `Refreshing`。适配器成功后若原生控件创建失败，撤销适配器输入并发布失败／取消，再重抛 |
| `RecordStudyPresenter.Withdraw()` | 无返回数据 | 同时撤去适配器和窗口可交互内容 |
| `BindClearButton`／`BindPointerQueries` | `Func<RecordStudyCommandContext>`；查询另需 radius | 操作发生时读取最新上下文。半径须为有限正数；过期激活不能清除新画面 |
| `ClearPair`／`HitTest`／`BeginDrag` | 必须传入预期的 `CapturedRecordSvgInputSession`，同时提供当前 view 几何与权限 | 重查记录／视图身份；命中结果为 `RecordCursorHits`，开始拖动返回 `NativeStudyDrag` |
| `NativeStudyDrag.Preview/Commit/Cancel` | 当前 command context 与坐标 | 手势绑定其渲染输入；取消需由当前上下文决定是否可回滚。`ReleaseWithoutRollback` 只释放手势 |
| `NativeDragInput(...)`／`Dispose()` | presenter、当前上下文工厂、命中半径、interrupted 回调 | 绑定 pointer、键盘、focus/capture/window 事件；中断后的回滚／释放由调用方处理，释放时解除事件绑定 |
| `RecordStudyControl(...)` | Ready publication、grid/cursor 样式 | 持有 `InputSession`，提供绘制和 hover 状态；不取得数据所有权或授予导航权限 |

完整签名见[桌面公开声明](api/desktop.md)。源码：[RecordStudyPresenter](../../src/Monitor.Desktop/RecordStudyPresenter.cs)、[NativeStudyDrag](../../src/Monitor.Desktop/NativeStudyDrag.cs)、[NativeDragInput](../../src/Monitor.Desktop/NativeDragInput.cs)、[RecordStudyControl](../../src/Monitor.Desktop/RecordStudyControl.cs)。底层授权、绑定和失效规则见[测量与呈现](measurements-presentation.md)。

## 桌面资源格式

`style-previews.bin` 是由 [StylePreviewCatalog](../../src/Monitor.Desktop/StylePreviewCatalog.cs) 生成、从 `AppContext.BaseDirectory` 加载的内部构建资源，不是导入病例或公共网络格式。

它用 .NET `BinaryWriter` 的小端数值顺序写入 magic `0x32565053`、组合数，再写各项 `(ecg, respiration, ejection, lead)`、ECG 与 ABP 样本数组，最后写呼吸预览数组。每组样本为 `Int32 count` 后接 `(Int64 timeNs, Double value)`；时间严格递增、值必须有限。读取器检查 magic、固定组合数量、键范围与唯一性、预期导联、样本数量及尾随数据。

生成时先写 `OUTPUT_PATH.tmp`、重新加载校验，再替换目标文件。构建自动生成目录；产品构建要求 `UsePrebuiltStylePreviews=true` 和存在的 `StylePreviewBinary`，跨平台发布使用宿主生成的目录，不执行目标平台程序。

`help-topics.json`（简体中文）与 `help-topics.en.json`（英文）是嵌入式帮助资源，后者以 `WithCulture="false"` 嵌入主程序集；两者主题 id 及顺序一致，分类一一对应。`eng/dependencies.json` 和许可文本也作为资源嵌入。它们不是用户偏好或实时患者状态的存储接口。

## 构建、依赖及发布命令

| 脚本 | 参数 | 输出与限制 |
|---|---|---|
| [build.py](../../tools/build.py) | `--configuration Debug\|Release`（默认 Release）、`--jobs N`、`--offline`、`--managed-only`、`--cache-dir PATH`、`--product-release` | 准备锁定依赖、核对清单、构建；jobs 为 1–32，默认 CPU 数与 32 的较小值。`--managed-only` 跳过原生编译但仍准备依赖。产品模式只允许 Release |
| [build_release.py](../../tools/build_release.py) | 转发 `build.py` 参数并自动加入 `--product-release` | 在 `artifacts/release/` 生成本机产品候选；先完整构建临时输出再替换旧目录 |
| [fetch_dependencies.py](../../tools/fetch_dependencies.py) | `--offline`、`--native-only`、`--check`、`--cache-dir PATH` | 默认缓存 `.cache/downloads`；`--check` 仅校验原生源码，不联网、不写文件、不恢复 NuGet；离线恢复只用已有缓存 |
| [verify_dependency_ledger.py](../../tools/verify_dependency_ledger.py) | 无自定义参数 | 校验仓库依赖、资源和许可证清单 |
| [build_native_audio.py](../../tools/build_native_audio.py) | `--windows-syntax`、`--production-only`、`--jobs N`（默认 32） | 使用已准备的原生源码；输出 `artifacts/native-audio/`，默认另构建隔离测试库 `artifacts/native-audio-test/`。Windows syntax 选项依赖 clang 和 MinGW 头文件 |
| [publish_desktop_cross.py](../../tools/publish_desktop_cross.py) | `RID`、`--native-audio-binary PATH`、`--output PATH` | 默认新目录 `artifacts/Monitor.Desktop-RID`；拒绝已存在输出。Windows 目标必须提供名称为 `sim_audio_native.dll` 且 PE 架构匹配的生产库；离线检查必需 ABI 导出，并拒绝测试后端导出。其他 RID 必须省略该参数；发布发生在隔离副本 |
| [desktop_distribution.py](../../tools/desktop_distribution.py) | `DIRECTORY` | 校验已发布候选目录及清单；本命令不创建远端发布 |
| [clean.py](../../tools/clean.py) | `clean\|dirclean`、`--dry-run` | clean 删除本仓库 bin/obj/测试输出和整个 artifacts；dirclean 还删 `.cache` 和下载的 miniaudio 头文件；dry-run 仅列目标 |

以上脚本的成功通常为退出 `0`；参数解析错误为 `2`，执行错误为非零，具体错误信息由脚本或被调用进程给出。不能用统一错误码表替代各脚本实现。

## 检查与合成数据命令

| 入口 | 参数和契约 |
|---|---|
| [run_parallel_checks.py](../../tools/run_parallel_checks.py) | `--jobs N`（1–32）、`--configuration Debug\|Release`（默认 Debug）、互斥的 `--specs-only`／`--desktop-only`（兼容别名 `--native-only`）；使用已构建产物，不负责构建，日志位于 `artifacts/parallel-checks/`，桌面分片日志使用 `desktop-` 前缀。桌面检查打开原生窗口，需要图形会话；Linux 无显示环境时用 `xvfb-run -a python3 tools/run_parallel_checks.py ...` |
| [check_desktop_launch.py](../../tools/check_desktop_launch.py) | `ASSEMBLY`、`--development`；默认检查 Linux 开发版或产品版无参启动，开发版可追加检查 `--ui-preview`。需可用的图形显示、`xwininfo` 和 `xprop`；按进程 ID 确认窗口，不依赖版本及界面语言，运行目录隔离为临时目录 |
| [check_native_audio_binding.py](../../tools/check_native_audio_binding.py) | 无自定义参数；使用 Debug `Monitor.Specs.dll` 和隔离测试音频库，同时确认生产试听入口拒绝测试后端 |
| [native ABI 检查](../../native/sim_audio_native/tests/check_abi.py) | `LIBRARY [--production-unavailable]`；详见[原生音频说明](../../native/sim_audio_native/README.md) |
| [spdx_headers.py](../../tools/spdx_headers.py) | `--check` 只检查；省略时会补写许可头，范围见贡献指南 |
| [check_commit_message.py](../../tools/check_commit_message.py) | `MESSAGE_FILE`；读取当前 Git author 与消息，检查仓库提交格式，返回 0 或 1 |

CI 在 Windows 图形会话和 Linux Xvfb 显示环境中运行 `--desktop-only`，两者均使用 `Monitor.Desktop` 的原生窗口入口。没有单独的无头桌面运行器；`--specs-only` 保留用于不依赖窗口的核心规格。桌面检查的启动截图输出到 `artifacts/desktop-startup.png`，CI 失败时与分片日志一起保留。

`tests/Monitor.Specs/Program.cs` 是开发用可执行规格与夹具入口：

```sh
dotnet run --project tests/Monitor.Specs/Monitor.Specs.csproj --no-build --configuration Release -- ARGS
```

| ARGS | 输入／输出 |
|---|---|
| 无参数 | 执行全部已注册规格；逐条输出 `ok N - 名称` 或 `not ok N - 名称`，异常写入标准错误；任一失败时全部执行完再以 1 退出 |
| `--shard INDEX COUNT` | 选择规格分片；`0 <= INDEX < COUNT <= 32` |
| `--list-svg-fixtures` | 标准输出列出 SVG 场景名 |
| `--svg-fixture NAME` | 标准输出合成三角波 SVG；支持 live、nodata、frozen、frozen-nodata、review、review-nodata、paused、paused-nodata、stopped、stopped-nodata、frozen-return、review-return |
| `--audio-tone-fixture` | 标准输出二进制 WAV；重定向到文件，不能将文本日志混入此流 |
| `--audio-native-audition ABSOLUTE_LIBRARY [DEVICE_ID]` | 在实际生产后端试听；不接受相对库路径或测试 null 后端 |
| `--audio-native-diagnostics ABSOLUTE_LIBRARY [DEVICE_ID]` | 设备／输出时钟诊断 |
| `--audio-native-clock-probe ABSOLUTE_LIBRARY [DEVICE_ID]` | 原生音频时钟采样 |
| `--audio-native-check ABSOLUTE_TEST_LIBRARY` | 显式检查隔离测试库绑定 |
| `--audio-wasapi-audition [DEVICE_ID]` | 用托管 WASAPI 输出播放与原生试听相同的五声，供 Windows 实机对比；不加载原生库 |
| `--audio-wasapi-diagnostics [DEVICE_ID]` | 打开托管 WASAPI 流但不播放，输出流路径、缓冲、周期和队列目标 JSON；设备不可用返回 `1` |
| `--audio-alsa-audition [PCM_NAME]` | 用 Linux ALSA 输出播放同样的五声；PCM 名称默认 `default` |
| `--audio-alsa-diagnostics [PCM_NAME]` | 打开 ALSA 流但不播放，输出缓冲、周期和队列目标 JSON；`null` PCM 可在无声卡环境检查 |
| `--oxygen-transport-fixture OUTPUT.json` | 导出短暂停呼吸／恢复的输运输入 |
| `--oxygen-deep-transport-fixture OUTPUT.json` | 导出较深去饱和输运输入 |
| `--oxygenation-replay-check RESULT.json` | 将离线回放结果接入光学测量链 |
| `--oxygenation-deep-replay-check RESULT.json` | 较深去饱和结果的测量链接入 |
| `--oxygenation-realtime-check DEEP_RESULT.json` | 对照离线结果与实时 decimal 氧合求解器 |

分派源码：[Program](../../tests/Monitor.Specs/Program.cs)、[SVG](../../tests/Monitor.Specs/SvgFixtureCommand.cs)、[WAV](../../tests/Monitor.Specs/AudioFixtureCommand.cs)、[原生音频](../../tests/Monitor.Specs/NativeAudioCommand.cs)、[氧合](../../tests/Monitor.Specs/OxygenationFixtureCommand.cs)。SVG／WAV／原生音频入口区分成功 0、执行错误 1、用法错误 2 和取消 130；氧合入口的捕获异常（含取消）返回 1，用法错误返回 2。它们是开发数据通道，不是临床 ECG、评分输入或桌面产品启动参数。

## 生成器和氧合实验

| 工具 | 自定义参数 | 文件边界 |
|---|---|---|
| [generate_demo_morphology.py](../../tools/generate_demo_morphology.py) | `--check` | 校验／生成当前波形表 |
| [generate_audio_tables.py](../../tools/generate_audio_tables.py) | `--check` | 校验／生成音频数值表 |
| [generate_monitor_tones.py](../../tools/generate_monitor_tones.py) | `--check` | 校验／生成已选择监护音资源 |
| [generate_beat_pitch_bank.py](../../tools/generate_beat_pitch_bank.py) | `--check` | 校验／生成节拍音高资源 |
| [generate_oxygenation_defaults.py](../../tools/generate_oxygenation_defaults.py) | `--check`、`--verify-workbook PATH` | 校验／生成患者氧合默认值；来源见[默认参数文档](oxygenation/oxygenation-defaults.md) |
| [generate_beat_pitch_auditions.py](../../tools/generate_beat_pitch_auditions.py) | `--check`、`--output PATH` | 默认输出 `artifacts/beat-pitch-auditions/` |
| [generate_alarm_auditions.py](../../tools/generate_alarm_auditions.py) | `--output PATH` | 默认输出 `artifacts/alarm-auditions/` |
| [generate_critical_a2.py](../../tools/generate_critical_a2.py)、[a3](../../tools/generate_critical_a3.py)、[a4](../../tools/generate_critical_a4.py) | 各自 `--output PATH` | 默认输出 `artifacts/alarm-auditions/` |
| [run_oxygenation_prototype.py](../../tools/run_oxygenation_prototype.py) | `--config PATH`、`--output PATH`、`--step-ms N`、`--plot`、`--blood-volume-ml N`、`--age-years N`、`--sex male\|female`、`--height-cm N`、`--weight-kg N` | 配置默认 `eng/physiology/oxygenation-prototype.json`、输出默认 `artifacts/oxygenation-prototype/`、步长默认 50 ms；plot 依赖可选 matplotlib；物理及报告字段见[原型文档](../research/oxygenation/oxygenation-prototype.md) |
| [replay_oxygenation_transport.py](../../tools/replay_oxygenation_transport.py) | `INPUT --output OUTPUT [--config CONFIG]` | 源时间步长必须为 8 ms，连续区间、规则且不中断的循环输运；输出 SaO₂ 不再附加传感器／采集延迟；配置默认同上 |

氧合实验输入为 `OxygenTransportReplayInput@1`，包含 `Transport`、`SampleStepNs` 和 `Intervals`；结果为 `OxygenTransportReplayResult@1`，包含 `Transport`、`Oxygenation`、有效参数和来源信息。`Oxygenation` 中 `StartSimTimeNs`、`SampleStepNs`、`SaturationMilliPercent`、`ModelId` 可用于构造 `SampledArterialOxygenationState`。详细单位、范围与示例命令见[氧合输运接口](oxygenation/oxygenation-transport.md)。

`tools/oxygenation_model.py` 是供原型／回放工具导入的计算模块；`test_*.py` 使用 Python `unittest` 标准入口，没有应用自定义命令协议。生成器的 `--check` 用于比较当前资源，省略该参数时可能改写生成文件；源码与参数必须同步维护，不能只编辑生成结果。
