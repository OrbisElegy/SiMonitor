# 桌面与命令行入口

[接口总览](README.md) · [桌面公开声明](api/desktop.md)

本页记录桌面宿主装配、Avalonia 输入边界及仓库开发命令。命令从仓库根目录运行；`.dll` 路径须对应已经构建的配置。Windows 下将 `python3` 替换为 `py -3`。构建前提见[项目 README](../../README.md)。

## 桌面启动

源码：[Program.cs](../../src/Monitor.Desktop/Program.cs)、[MonitorApp.cs](../../src/Monitor.Desktop/MonitorApp.cs)、[项目配置](../../src/Monitor.Desktop/Monitor.Desktop.csproj)。

全局产品版本仅在[项目配置](../../src/Monitor.Desktop/Monitor.Desktop.csproj)的 `<Version>` 中维护。
窗口标题、侧栏和关于页通过 `ProductIdentity` 读取构建生成的程序集版本信息，显示时添加 `V` 前缀并去除 `+` 后的构建元数据，保留预发布后缀。
关于页的构建版本行保留简短提交标识，分发说明也读取同一项目配置；修改版本后需重新构建。

```sh
dotnet run --project src/Monitor.Desktop --no-build --configuration Release
```

默认入口为 `DesignPreviewWindow`，装配 `LocalMonitorPreviewSession`、`LiveMonitorTrace`、`LiveMonitorView`、设置和声音。`MainWindow` 是已捕获记录交互壳，用于开发演示和检查；它不是当前默认监护窗口。

[DesktopShell](../../src/Monitor.Desktop/DesktopShell.cs) 仅负责主导航、状态区和内容布局，
会话与页面生命周期仍由窗口持有。内容宽度达到 1280 逻辑像素时固定展开导航栏并隐藏菜单按钮；
较窄窗口默认显示图标栏，菜单按钮展开覆盖式导航，不移动或压缩内容区。
再次点击菜单、选择入口、按 Esc 或点击导航外部收起覆盖层；跨越宽度阈值时恢复对应的默认布局。
菜单按钮使用普通按钮外观，无蓝色选中背景。展开时显示图标与名称，
收起后保留五个入口的图标、选中标记和本地化提示。
收起后的底部通知按钮可查看运行状态与偏好恢复提示。
切换页面保留设置草稿、帮助搜索条件和关于页的文档选择，关闭窗口后不持久保存这些浏览状态。
帮助页的 `Ctrl+F`（macOS 为 `Command+F`）聚焦搜索框，搜索框内按 `Esc` 清空查询；
分类与关键词共同筛选，无结果时可清除全部筛选。隐藏期间切换语言，重新进入时更新正文。

设置分类、参数组和模板分组随可用宽度切换列表或下拉选择器，并将焦点交给可见的对应导航。
患者时间线在窄窗口中切换图表和详情时，同样将键盘焦点转交到事件名称或所选图表节点。
[SettingsDisplayTable](../../src/Monitor.Desktop/SettingsDisplayTable.cs) 在宽空间使用量程表格，
窄空间使用带标签的双列表单；重排复用原有输入控件，不应用或清除草稿。
量程输入即时显示格式、有限值及上下界错误，应用时仍执行完整配置验证。

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
| `--product-check ASSEMBLY_PATH` | 检查产品程序集；必须指定可读取的程序集文件路径，入口仍由开发版承载 |
| `--generate-style-previews OUTPUT_PATH` | 生成有限预览目录后退出，不启动 Avalonia 窗口 |

声音输出在 Linux 默认使用系统 ALSA 库，其他平台默认使用原生音频库。启动前设置环境变量 `SIMONITOR_AUDIO_OUTPUT` 为 `native`、`wasapi` 或 `alsa`（不区分大小写）可覆盖默认值，例如在 Windows 上用 `wasapi` 与原生输出对比；未设置或其他值使用平台默认。

未知组合／多余参数返回 `2`。`--product-check` 缺少路径时输出用法并返回 `2`；路径非法、文件不存在或无法读取时，在初始化图形界面前输出错误并返回 `2`。`ProductRelease=true` 定义 `SIMONITOR_RELEASE`，产品程序只接受无参数启动；包括 `--ui-preview` 在内的开发参数均返回 `2`。普通 `Release` 编译配置本身不等于 `ProductRelease=true`。

## 本地监护装配与时间

源码：[DesignPreviewWindow.cs](../../src/Monitor.Desktop/DesignPreviewWindow.cs)、[DemoFrameTiming.cs](../../src/Monitor.Desktop/DemoFrameTiming.cs)、[LiveMonitorView.cs](../../src/Monitor.Desktop/LiveMonitorView.cs)。这些装配类是 `internal`，不构成外部插件接口。

1. 创建配置、显示与氧合源，再调用 `DiscardStartup()` 裁去源的启动过渡段。
2. UI `DispatcherTimer` 按 60 Hz 截止时间调度，并从下一次等待中扣除本帧处理时间；超时后丢弃已错过的截止时间，不回放积压帧。由 `Stopwatch` 得到经过时间并传给本地预览。单次延迟超过 250 ms 时按 50 ms 推进，不补算整个挂起时间。仿真内部仍使用显式纳秒时间。
3. `Pulse` 只接受当前活动 timer；旧 timer 回调、已暂停或已关闭窗口不推进会话。随后刷新波形、测量、通知与声音。
4. 数字显示按 200 ms 的仿真时间桶刷新，测量有效性变化或新监测／节律事件立即刷新数字与通知；电击后不可判读状态不等待下一时间桶。扫线绘制可更频繁。所有波形读取统一的 `Samples` 历史，以 `SimulationTimeNs` 为共同前沿；`Measurements`、`BeatEvidence` 与治疗证据在同一采样推进中更新。ECG、PLETH 与压力波形保留源模型的脉搏传播和机械响应延迟。同扫速的行同时换扫，电击与测量无需等待 200 ms 记录包组块。
5. 普通应用设置使用 `ScheduleSource(next, delayNs)` 接续，在生效时切换记录快照和已应用配置；再次应用可替换未生效设置。重新开始才新建会话、裁去启动段并重置相关通知与声音状态。
6. `Pause` 停止并解绑 timer，暂停监护声音；关闭窗口还释放声音输出资源。

监护页底部“12 导联”切换 [LiveEcg12Trace](../../src/Monitor.Desktop/LiveEcg12Trace.cs)，
不导航到侧栏纸图页，也不重建或暂停会话。波形区按 3 × 4 同时显示十二导联；
当前布局为四行及以上时另加长 II，三行时隐藏长 II。右侧数字、手工参数、
报警区、治疗控制及其显隐设置保留；再次点击恢复原监护波形。
短条各显示 2.5 秒，长 II 显示 10 秒，电压范围统一为 ±2 mV，并绘制 1 mV 标尺；
窗口缩放不代表物理纸速。视图选择仅保存在当前窗口，普通应用设置和重启会话后保留。

底部“导联”打开十二项单选面板，切换普通 ECG 行显示的 I、II、III、aVR、aVL、aVF 或 V1–V6。
`SelectDisplayEcgLead` 即时重绘已有采样历史和导联标签，并仅重建 ECG 的自动量程；
其他通道的当前／上一扫量程、扫屏前沿、仿真和治疗状态保留。显示选择保存到
`MonitorEcgLead`，重新打开及应用／重启设置后保留，恢复默认设置时回到 II。
`DisplaySamples(0, ...)` 读取选定导联；`Samples(0, ...)` 仍为 II，供 HR、QT/QTc、报警、
声音、同步与 AED 检测使用。十二导联总览及其长 II、侧栏纸图的固定排列不随选择改变。

桌面会话启用 `enableTwelveLead`，由 `LiveEcgIllustrationSource` 使用当前生理配置创建
电极投影；`LiveEcgProjection` 复用监护源的事件尾部接续，在一次采样推进中发布所有导联，
不经过记录组包或延迟队列。`EcgSamples` 的时间已裁去启动段，历史保留上限与普通监护一致；
所有导联在电极投影后一次量化，投影 II 同时作为普通监护、测量和记录的 ECG 输入；
短／长 II 直接读取 `Samples(0, ...)`，避免沿用旧单通道示意幅度而破坏肢体导联关系。起搏、参数切换、除颤后的
静默／复律和电击伪迹使用同一会话边界，不能从静态纸图循环取样。

偏好路径由 `Environment.SpecialFolder.LocalApplicationData` 加 `Monitor/display-preferences.json` 构成；不依赖启动工作目录。保存失败时本次内存配置仍可生效，由界面报告不能持久保存。具体字段、版本和错误语义见[音频与持久化](audio-persistence.md)。

语言选择位于“设置 → 通用”，支持 English、简体中文、繁體中文和“随当前系统语言”，
即时生效并独立保存至 `Monitor/display-preferences.language.json`。它不应用参数或替换仿真会话；
首次启动和恢复全部默认设置使用 `system`，按当前系统 UI 语言解析。接入范围、绑定和失败语义见[本地化接口](localization.md)。

## ECG 高级参数

“设置 → 高级参数 → 心电图”使用 `EcgTemplateCapabilities` 按当前模板的实际生成路径
显示功能分页，沿用呼吸参数的 Fluent 页签形式，窄窗口下页签自动换行。
切换分页保留草稿；波形参数页的恢复默认操作仅重置当前页。模板变化后，若当前页不再
受支持，则选中首个可用页。T 轮廓、梗死、电击／AED 转复、信号干扰和起搏许可也在
各自分页中编辑。参考电极模板支持自定义时序、V1 P 分量、胸前 ST／T、U 波及 ST–T 融合；
已有心房、心室、T 轮廓和梗死模板会隐藏与其形态冲突的编辑项。仅心房活动只显示
P 时限和 P 分量；无心脏活动、室扑／室颤等固定模板不显示这些分量编辑。
三度 AVB 显示逸搏周期和相位，房颤显示差异传导和体循环脉搏短绌；
肢体接线仅在生成器接受该选项的模板中显示。模板类别仍由波形选择页决定。
高级参数导航不再提供射血页；射血模板的高级参数入口转至“生命体征 → 压力”。

时序编辑使用现有 `ProjectedEcgDemoConfiguration` 和 QT 校正方法，按模板参考周期
解析 QTc。心率覆盖仍由生命体征页控制；自定义形态使用 `CardiacRateAdjustment`，
不走仅接受默认窦性配置的 `SeededRate` 路径。隐藏或未启用的字段不作为源输入。
融合导联隐藏独立 T 倍率；全部胸前导联融合时也隐藏胸前 ST 编辑。
梗死独立 QRS 编辑在参考形态下提供显式贡献移除；未启用的区域页隐藏。

成功解析的电极定义通过 `AuthoredEcgDefinition` 捕获不可变副本；接线置换发生在
导联投影之前。监护采集 II、实时十二导联与纸质十二导联使用相同电极波形，
PR 改动同步心室电／机械事件偏移。呼吸、压力和其他节律配置继续由原配置持有。
应用前验证完整组合，失败保留运行源和已保存设置；电击转复和起搏使用各自新的心脏定义。

`MonitorGeneratorPreferences.EcgAdvanced` 保存当前模板的数字、开关与选择草稿，
缺省字段为 `null` 的旧文件恢复模板默认值。切换模板重置这些参数，恢复已保存设置时
先选择模板再还原字段。梗死贡献移除参数沿用现有生成器字段字典，并允许旧文件省略。
用户操作与约束说明位于可搜索帮助的 `ecg-advanced-*` 条目。

## 屏幕模拟来源水印

正式桌面入口的 `DesignPreviewWindow`（监护、内嵌十二导联、侧栏纸图及其他页面）、
`Ecg12PrintWindow` 和 `LiveMonitorPrintWindow` 在内容最上层添加
[ScreenWatermark](../../src/Monitor.Desktop/ScreenWatermark.cs)。系统截图和录屏捕获到的是
含标记的最终窗口像素；仅调用子控件渲染、旧版开发演示窗口、外部 PDF 阅读器及原生弹出窗口
不在这一接入范围。皮肤切换不替换窗口级标记，标记也不截获鼠标或键盘输入。

该实现为低强度频域水印原型。固定标识 `SiMonitor.Synthetic.Screen.v1` 的参数保存在
[screen-v1.json](../../src/Monitor.Desktop/Assets/Watermarks/screen-v1.json)，嵌入程序集并由检测工具
共用；不写入用户、设备或病人身份。12 个不同方向的载波生成一张 256 DIP 周期纹理，
用极低透明度黑白像素平铺；每个 RGB 通道的扰动上限为 2/255。
这只是数值上限，不是任何显示器上都不可见的保证，仍须实际显示环境的感知验收。
纹理在 UI 生命周期内共享，不增加刷新定时器，不逐帧读取屏幕或做 FFT；全窗口混合仍有绘制成本。
水印不进入采样、测量、报警、治疗或导出的 PDF。PDF 和纸张继续使用原有的“仅为教学模拟”声明。

内置监护皮肤使用离开纯黑的暗色底：Generic 为深蓝灰 `#18232D`，ePM 为炭灰 `#1C242B`，
D3 为深灰绿 `#1C2723`，D3 数值区也跟随皮肤底色。此调整用于降低低强度纹理在近黑背景上的
相对反差，不改变水印强度或波形颜色，也不保证肉眼不可见；导入皮肤仍遵循其自身配置的底色。

Google 的 [SynthID 说明](https://deepmind.google/models/synthid/)介绍其图像嵌入和检测能力，
公开的 [SynthID Text](https://github.com/google-deepmind/synthid-text) 是文本生成水印实现，
不是可直接给本地 Avalonia 窗口嵌入图像水印的 SDK。因此此原型使用自主实现的固定频域标记，
没有集成或仿称 SynthID，也不引入模型或云端依赖。

### 离线检测

[screen_watermark.py](../../tools/screen_watermark.py)读取截图或已经抽取的录屏帧，无需无水印原图，
不上传图片。检测工具单独需要 Python 3.12+、NumPy 和 Pillow；桌面程序不依赖 Python。
在独立 Python 环境安装工具依赖后，从仓库根目录运行：

```sh
python3 -m pip install -r eng/watermark-requirements.txt
python3 tools/screen_watermark.py screenshot.png
```

检测先限制文字、网格和波形强边缘的贡献，再用带 Hann 窗的 FFT 搜索重复载波；候选还需通过
相位编码的一致性校验，允许共同平移，避免把规则纸格的频谱尖峰直接当作水印。
搜索范围为最终图片每 DIP 的 0.5–4 像素，包含系统 DPI 与后续等比缩放的乘积。
分析图最长边最多 1600 像素；原图或缩小后的短边小于 256 像素时返回信息不足。
裁剪不要求对齐纹理原点，但保留面积、内容复杂度和压缩强度都会影响结果。

| JSON `status` | 退出码 | 含义 |
|---|---|---|
| `marker_detected` | 0 | 频率和相位与公开的模拟屏幕标记匹配 |
| `not_detected` | 1 | 本次未达到检测阈值，不代表真实采集 |
| `insufficient_size` | 1 | 图片太小或过窄，无法完成这一检测 |
| 输入／解码错误 | 2 | stderr 说明错误，不产生真实性结论 |

`spectral_score` 是相邻频谱背景的幅度比，`phase_consistency` 是共同平移后的相位一致性指标，
二者都不是概率或经标定的置信度；阈值是工程启发式，尚无独立大样本的误报率保证。
这是可公开复制、可移除的来源提示，既不是密码学签名，也不能保证阻止恶意伪造或修改程序。
**检出不能证明整幅图片的来源，未检出更不能证明内容真实。**

### 验证与边界

`ScreenWatermarkSmokeChecks` 通过实际 Avalonia 渲染保存三种皮肤、十二导联纸图、两类打印预览、
不同 DPI 的含标记／不含标记对照，以及动态监护帧到被 Git 忽略的 `artifacts/watermark/`。
它检查像素扰动上限、完整覆盖、输入穿透和测量不变。先运行现有桌面检查，再运行：

```sh
python3 tools/check_screen_watermark.py
```

`--fixtures PATH` 可指定桌面检查生成的样本目录，默认 `artifacts/watermark/`；缺少必需样本时以用法错误 `2` 退出。

该检查还需要 `ffmpeg`（带 `libx264`），覆盖 PNG、JPEG 90/75、等比缩放、裁剪及其组合，
并将动态帧编码为 H.264 CRF 23 后抽帧检测；纯色、规则网格和固定种子噪声作为负例。
这些条件是回归样本，不是对任意图片或编码器的鲁棒性承诺。JPEG 50、低码率视频、反复转码、
重度降噪、小区域裁剪、旋转、非等比缩放及手机拍屏不在首版保证范围；打印扫描也未接入。

## 实时监护打印

两类打印窗口共用 [PrintPreviewLayout](../../src/Monitor.Desktop/PrintPreviewLayout.cs)：
左侧滚动表单与右侧预览之间提供可拖动、可键盘调整的分隔条，底部状态和操作分行显示。
预览缩放、布局调整与导出文档的实际尺寸相互独立。

实时监护底部“打印”调用 `LocalMonitorPreviewSession.CapturePrintSnapshot()`，复制当前保留的
十二导联样本和事件历史，打开非模态 `LiveMonitorPrintWindow`。默认选择点击前最近 10 秒；
普通监护默认打印当前 ECG 导联，实时十二导联视图默认打印十二导联。预览支持切换记录类型、
导联、10/20/30 秒时长、起始会话时间、25/50 mm/s、5/10 mm/mV 和彩色／黑白。
调整时长重新定位到捕获时刻之前的最近一段；起始时间可在已捕获历史内手动调整。
后续监护继续运行，参数调整、导联选择、会话重启均不会改变已经捕获的记录。

`MonitorPrintSnapshot` 使用独立只读集合；历史保留与实时会话一致，最多约 40.4 秒。
仅输出实际已有样本，历史不足在预览和 PDF 标明，不循环补齐；不良质量样本和采样断点断线。
打印时间均为裁去启动段后的会话秒数。事件历史包括波形／参数切换、显示导联切换、通气更新、
起搏状态／输出变化、实际电击释放，以及心律和 ECG 监测检测状态变化。
源切换在实际激活时记录，尚未生效或被替换的请求不作为已发生事件。
波形模板说明使用应用参数时的界面语言；检测状态细项保留检测器标识，不等同于所有界面报警历史。

`LiveMonitorPrintDocument` 使用毫米绘制 A4 横向矢量页面，波形区域宽 250 mm；
25 mm/s 每段 10 秒，50 mm/s 每段 5 秒，十二导联每段分为两页、每页六导联。
校准脉冲为 1 mV、200 ms；分页保持物理纸速和增益，窗口大小不影响输出尺度。
事件按所选时间范围过滤，在波形上标注编号，并追加含会话时间和说明的事件页；长说明换行、
多事件续页，捕获时刻恰好发生的事件也保留。没有事件时明确标记无事件。
预览可翻页，PNG 与 PDF 重放同一矢量页面。保存使用系统文件选择器；
“打开 PDF 打印”生成独立临时文件，由系统阅读器打印。选择 A4 横向、实际大小 100%，关闭适合页面。

## 纸质十二导联打印

纸质十二导联页面工具栏的打印按钮打开 `Ecg12PrintWindow`。它固定捕获当前纸图的样本引用，
不切换实时监护、不推进仿真、不改变游标或纸图缩放。标题、副标题、可选考生信息、模板、
纸速、增益、起始时间、测量答题项目及诊断文字仅在当前宿主窗口内保留，不写入监护偏好。

`Ecg12PrintDocument` 以毫米绘制一张 A4 纵向页面，每个导联独占一行，所有行共享时间窗。
默认 25 mm/s、10 mm/mV；纸速可选 25/50，增益可选 5/10。校准脉冲从横向 13 mm 开始，其后留 1 mm 即进入波形区；
25 mm/s 时波形从 19 mm 到 198 mm，默认容纳 7.16 秒，不能为容纳更多时间而压缩波形。每行校准脉冲为 200 ms、1 mV。
导联标识透明叠加在波形起始区上方，不单独占列；纸速、增益、时间窗及教学提示均透明绘制在
网格内部，不占外部说明行。网格底部固定在 246 mm，上边界随标题、副标题和考生信息调整，
省去的信息区高度用于扩展 ECG。12 条导联的基线均匀分配在可用高度内，完整页眉下行距约
16.9 mm；仅保留标题时约 18.4 mm。测量答题区与诊断区保留在网格下方。
样本使用采集比例和偏移转换为微伏；缺失或无效样本断线，波形裁剪到 ECG 区域。
彩色模板显示完整 1 mm 方格，黑白模板保留 5 mm 大格线，并在 1 mm 交点画点。

可选的测量区域是学生答题区，可分别选择心率、PR、QRS、QT、QTc 和 QRS 电轴。
只显示所选题目、单位和空白填写线，不读取检测器，也不自动填入答案。诊断区域可保持空白。
标题及正文超长时在预览和导出中一致截断，用户应以预览核对最终文本。

预览 PNG 和矢量 PDF 重放同一份 Skia 图形；PDF 按 72/25.4 point/mm 输出，
不依赖屏幕 DPI、纸图缩放或显示器大小。保存使用系统文件选择器，取消不写出文件；
“打开 PDF 打印”生成独立临时 PDF，交给系统文件启动器打开阅读器，由用户在阅读器中打印。
临时文件保留供外部阅读器读取；打开失败提供另存 PDF 的提示。输出前冻结设置，异步文件选择
期间的编辑不改变该次输出。界面提示使用 A4 纵向、实际大小 100%，关闭适合页面。

源码：[Ecg12PrintDocument.cs](../../src/Monitor.Desktop/Ecg12PrintDocument.cs)、
[Ecg12PrintWindow.cs](../../src/Monitor.Desktop/Ecg12PrintWindow.cs)。

## 皮肤装配接口与单文件皮肤

源码：[IMonitorSkin.cs](../../src/Monitor.Desktop/IMonitorSkin.cs)、[GenericMonitorSkin.cs](../../src/Monitor.Desktop/GenericMonitorSkin.cs)。

`IMonitorSkin` 是进程内桌面装配端口，当前为 `internal`。外部皮肤使用下述 `.simskin`
声明式文件，由已有宿主控件装配，不加载外部 DLL、脚本或 XAML。
宿主创建每个视图时注入皮肤；`Id` 标识样式，`Background` 与 `ChannelBrush(channel)` 同时用于
波形、数值、脉搏指示及报警高亮恢复后的底色。`Compose(MonitorSkinContent)` 接收宿主持有的
标题／报警区、波形、数值列、温度、两个自定义参数和 NIBP 控件，每个控件只能挂载一次。
皮肤不持有采集会话或治疗权威，不能决定测量有效性或推进仿真时间。
`Channels` 声明可接入的通道；宿主将其与已配置波形行取交集，作为实际监控范围。
未接入通道的数值／ECG／无呼气报警生命周期以 Disabled 清除，包含待确认、锁存与声音；
保存的阈值和启用偏好保留，再接入后从新证据重新确认。
`LiveMonitorView` 继续更新读数、缺失状态、报警及本地化，`LiveMonitorTrace` 继续负责扫线与量程。

默认宿主注入 Generic。首次启动及恢复默认设置采用 `MonitorSkin.FourRows = 3`：
ECG（绿）、PLETH／SpO₂（蓝）、RESP（黄）、CO₂（白）。温度和两个自定义参数位于波形下方，
白色 NIBP 位于数值列下方；未启用的手动参数保留空白位置，隐藏名称、数值与单位，不生成虚构读数。
现有 3／5／7 行枚举值和保存配置不变，显示设置中可选择四行，也可逐行修改通道。
历史枚举 `MonitorSkin` 仍表示行数配置，与新的桌面皮肤装配端口不同。

“设置 → 显示 → 监护皮肤”即时切换 Generic、参考 ePM 12C 屏幕的皮肤与 D3 整机皮肤。
ePM 皮肤采用连续黑底、绿色 ECG、青色 PLETH、黄色 RESP、右侧大数字、下方血压和测量记录，
只还原屏幕布局，不模拟机壳，也不声明真实设备的算法或功能等价性。
Generic 与 ePM 共用 `GenericMonitorSkin` 的宿主命令控件，分别装配 `GenericMonitorArea` 与
`BedsideMonitorArea`；配色同时作用于扫线、读数、脉搏条和警告高亮。
内置床旁皮肤入口为 `MonitorSkinPackage.BedsideMonitor`，由 `layout = bedside` 分派布局；
`BedsideMonitorArea`、`BedsideToolbar`、`BedsideNibpCountdown` 和 `bedside.*` 本地化键按职责命名。
内置资源标识 `epm12`、皮肤标识 `epm12-inspired` 及显示名仅表示外观来源，保持原有身份及导入／导出兼容。
床旁屏幕以 1024×640 逻辑尺寸等比缩放，波形／数值区宽度为 68:32；数值左侧显示
单位及当前有效的报警上下限，禁用或无效阈值显示 `--`。次要读数保留在数值区提示中。
每条波形顶部拥有独立报警横幅，按测量来源映射并分别轮换；同一通道的重复行共享
轮换进度，重排不重置计时。横幅沿用宿主的确认、恢复与模拟时钟闪烁状态，长文字
省略显示并可查看完整提示。无通道来源的系统通知仍由顶部全局报警区呈现。
底部十个图标键连接实际宿主操作，“NIBP停止全部”停止当前测量并关闭自动测量，
“主菜单”进入设置页。内置床旁和机壳皮肤仅显示 NIBP，温度／自定义参数即使已启用也不占屏幕空间。
床旁 NIBP 上方显示自动测量倒计时：以 `NextMeasurementAtNs` 和当前仿真时间计算
剩余时长，进度条表示所选测量间隔的剩余比例；暂停仿真时冻结，下一次自动测量开始时
重置。关闭自动测量、停止全部或禁用 NIBP 后隐藏倒计时，不显示固定满进度线。
`IMonitorSkin.CompactWaveformLabels`、`NumericFontSize`、`WarningBrush` 提供显示参数，
`MonitorSkinContent.Trends` 提供宿主维护的测量记录控件。
`IMonitorSkin.SupportedManualVitals` 通过 `MonitorManualVitals` 标志集分别声明
`Temperature`、`Custom1`、`Custom2`、`Nibp` 的显示能力（默认 `All`，可选 `None`）。
宿主在装配时隐藏不支持的控件，刷新时清空其读数；皮肤实例生命周期内能力保持不变。
这只限制显示，不修改会话、采集、报警或用户已启用的生命征设置；换回支持的皮肤即可恢复显示。

切换皮肤保留采集会话、暂停状态、导联、十二导联视图、报警确认和血压记录，
取消待放电状态并退出 AED；活动起搏先安排停止。待生效的源或起搏切换完成后才能换皮肤。
普通皮肤保留行配置；有 `fixedWaveformRows` 的机壳皮肤强制相应行数，并禁用两个行数入口。
保留前面的行及其通道、量程、速度；完整原布局保存到 `UnlockedSkinDisplay`，离开机壳时恢复额外行，
期间对保留行的修改继续生效。该备份与显示偏好一起持久化，重开后仍可恢复。“使用推荐波形布局”是独立操作：Generic 恢复四行
ECG／PLETH／RESP／CO₂，ePM 恢复前三行。显式更改通道时沿用既有报警接入规则。
三、四、五、七行均可继续使用。下方记录仅保留本次会话最近四次已完成的袖带测量，
附带当时有效且已接入的 HR、SpO₂、RR；无有效读数显示 `--`，重新开始会清空记录。

### `.simskin` 文件格式

源码：[MonitorSkinPackage.cs](../../src/Monitor.Desktop/MonitorSkinPackage.cs)。
内置源码目录：[Generic](../../src/Monitor.Desktop/Assets/Skins/generic/manifest.json)、
[ePM 12C](../../src/Monitor.Desktop/Assets/Skins/epm12/manifest.json)、
[D3](../../src/Monitor.Desktop/Assets/Skins/d3/manifest.json)。每个目录含 `manifest.json` 和可选的
`assets/*.material.json`，嵌入程序集后通过相同校验路径装配。
导出为一个 ZIP 格式 `.simskin`，其中包括全部引用资源；不依赖导入时的源目录。

| 清单字段 | v2 约束 |
|---|---|
| `version` | 整数 `2`；不兼容未发布的 v1 裸 JSON |
| `id` / `name` | 小写 ASCII 标识符 1–64 字符／不含控制字符的名称 1–64 字符 |
| `layout` | `generic`、`bedside` 或 `defibrillator` |
| `supportedManualVitals` | 可选字符串数组，允许 `temperature`、`custom1`、`custom2`、`nibp`，不允许重复或未知项；空数组禁用全部手动读数。省略或 null 时按布局采用默认能力：`generic` 全部，`bedside`／`defibrillator` 仅 NIBP；旧保存包同样适用 |
| `fixedWaveformRows` | null 或 3、4、5、7；机壳布局必须声明，D3 内置值为 3 |
| `background` / `warningColor` / `nibpColor` | 不透明 `#RRGGBB` |
| `channelColors` | 七种颜色：ECG、RESP、PLETH、ABP、CO₂、PA、CVP |
| `deviceProfile` | 可选的可信设备参数档案标识；机壳布局必须声明，当前内置档案为 `biphasic-360` |
| `defaultEnergyStepsJoules` | 所有包必填；1–64 个 1–1000 J 整数，严格升序且不重复 |
| `materials` | 语义名到包内资源路径的映射；无资源时为空对象 |

`supportedManualVitals` 可在任意布局按项目覆盖默认能力，参与包语义比较并随导入／导出保存。
内置 Generic 显式声明全部四项，ePM 12C 与 D3 显式声明 `nibp`。

`layout` 选择通用绘制与操作布局，`deviceProfile` 仅从可信注册表绑定参数实例，两者分别解析。
此前未发布的 `beneheart-d3` 参数标识不再接受；旧实验包需改用 `biphasic-360` 重新打包，或从新版重新导出。
内置机壳通过 `MonitorSkinPackage.DefibrillatorEnclosure` 取得；资源目录 `d3`、清单标识
`d3-inspired` 和显示名 `D3 · Inspired` 保留外观身份与参考出处，不参与行为接口命名或分派。

`MonitorSkinPackage.DefaultEnergyStepsJoules` 返回清单能量表的只读快照；其内容不受
`deviceProfile` 内的能量表限制。Generic 与 ePM 内置包各携带原 15 档，D3 内置包携带
21 档。导入／导出和包相等性比较都保留这份默认表；缺少该字段的未发布 v2 包也会被拒绝。
用户的手动覆盖属于显示偏好，不会替换导出包里的默认表。

`defibrillator` 布局要求 `shell`、`key`、`softkey`、`charge`、`shock`、`selector` 六种材质。
每份材质使用 `version: 1`，含 `fill`、`pressedFill`、`border`、`foreground` 四种颜色，
以及 `cornerRadius`（0–80）、`borderThickness`（0–8）。这些文件控制外壳及按键的实际绘制，
不包含可执行代码、外部引用或厂家图片；目前不接受 PNG/SVG、字体或任意控件坐标。

包上限 256 KiB，总展开上限 128 KiB，最多 33 个条目；清单最多 16 KiB，单材质最多 4 KiB。
超过 1 KiB 的条目压缩比不得超过 100。拒绝路径穿越、绝对路径、符号链接、重复路径／JSON字段、
未知字段、缺失／未引用资源和不合法数值。校验完成后才发布包；材质原始字节在往返保存时保留，
比较包时同时比较清单语义、资源映射及字节内容。旧 v1 文件导入失败，旧本地皮肤会回退 Generic 并显示提示。

### 通用整机面板

宿主通过 `IMonitorDevicePanel` 订阅通用模式、增益、事件、报警复位和按住四分频命令。
`MonitorDeviceProfile` 提供手动／AED 的波形与时序、手动默认能量目标、AED 协议能量序列和放电后同步行为，
通用机壳绘制位于 `GenericMonitorSkin.Enclosure.cs`，旋钮与软键使用 `ModeSelector`、
`NavigationDial`、`SoftKeys` 和 `SoftKeyLabels` 等通用名称，本地化键采用 `devicePanel.*`。
宿主操作不按型号分支；`MonitorDeviceProfiles` 将档案标识解析为内部可信参数实例，
皮肤包不能携带可执行行为。
有效能量表按“已应用的手动表优先，否则使用皮肤清单默认表”解析，与这些设备行为参数分别处理。

内置外观由 [D3 清单](../../src/Monitor.Desktop/Assets/Skins/d3/manifest.json)与[通用机壳绘制](../../src/Monitor.Desktop/GenericMonitorSkin.Enclosure.cs)定义。外壳按固定逻辑尺寸等比缩放，
该皮肤的 4:3 屏幕固定三行；提手、电极板收纳区、报警灯、交流／电池／就绪指示、扬声器与全部操作区在同一面板内。
模式旋钮提供关机、监护、手动、AED、起搏，可点击目标角度或印刷档位，也可拖动、滚轮或用方向键选档。
两端停止、不环绕；档位文字固定印刷，仅指针表示当前档位，且只在宿主接受切换并回传状态后转动。
导航旋钮通过拖动、滚轮或方向键选择，中心短按或 Enter／Space 确认，拖动不触发确认。
能量使用独立 ± 键；黄色充电键的外壳文字固定，充电进度显示在屏幕内。
充电键在 AED 模式下不执行重新分析，恢复分析使用对应屏幕软键。
橙色放电键仍须按住，失焦、移出、松开或换模式取消授权。
内置参数档案在同步交付后退出同步；关机立即停仿真并清除治疗授权，开机先完成必要的起搏停止，再允许治疗。

三个屏下软键正对各自屏幕标签，功能与可用状态随模式对应更新。
监护提供冻结、趋势和报警复位；冻结只保存波形图像，数值和采样继续更新。
趋势显示当前会话的实际袖带记录；报警复位确认可确认的现有报警，不修改阈值。
手动模式提供同步和解除电荷；AED 提供暂停进入 CPR／恢复分析。起搏提供启停、参数与按住 4:1。
4:1 通过临时 `PulseDivision` 改变实际刺激周期，松开后在采样边界恢复，不写入治疗偏好。
导联、增益、事件、NIBP、报警暂停和菜单硬键均接入宿主；事件使用当前仿真时间写入打印记录。
增益仅修改 ECG 显示范围，当前值显示在屏幕内；打印与十二导联位于面板菜单中。

D3 内置清单默认提供 21 档能量（1–360 J）；成人教学配置的手动默认目标为 200 J，
AED 协议目标为 200→300→360 J，只在实际放电后升级。手动默认目标及 AED 每个协议目标均映射到
当前有效能量表中最近的合法档位，等距时取较低档；映射后的 AED 序列允许相邻档相同。
手动／AED 自动解除电荷分别为 60／30 秒。充电统一模拟为 7 秒，
ECG 恢复为 2.5 秒；不声称复现原机随能量、电池和阻抗变化的时序。
起搏支持 30–210 ppm、0–200 mA，仍使用本项目的教学波形模板；不实现原机基于本征节律的
Demand 抑制算法或真实患者夺获阈值。交流灯代表固定的模拟交流供电，电池没有虚构剩余容量。

导入成功后将完整配置保存到显示偏好文件同目录的 `display-preferences.skin.simskin`
（测试或自定义偏好路径替换其扩展名），后续启动不依赖导入源文件。采用临时文件写入、
刷新和替换；保存失败时本次显示仍生效，界面提示无法持久保存。导入失败保留当前皮肤；
启动读取失败回退到 Generic 并提示。恢复默认设置同时恢复 Generic。产品构建保留导入／
导出入口，不依赖开发命令行参数。

“设置 → 高级参数 → 心电图 → 起搏许可”可为每个模板独立勾选“允许此模板起搏”。
默认全部关闭，旧配置未设置的模板也关闭；应用后与模板一起在采集边界生效，未应用草稿不影响
当前会话。禁用模板的启动按钮不可用，会话 `SchedulePacing` 也拒绝调用并保留原状态。
此开关是教学配置，不自动推断临床适应证；停止当前起搏不受启动许可检查限制。

首次启动、缺失显隐配置及恢复默认时隐藏治疗控制，可通过“布局 → 显示治疗控制”开启。
明确保存的显示选择继续生效；隐藏仅改变布局，不终止已开始的治疗。

Generic 右侧提供可配置的除颤能量阶梯（档位边界 1–1000 J）、起搏频率（30–210 ppm）、电流（0–200 mA）及
现有 15 种起搏波形选择。编辑保存草稿；“启动／应用起搏”在下一采集边界切换实际播放，
运行中再次应用可改变类型与输出，“停止起搏”恢复启动前节律，保留会话时间和已有波形。
暂停时等待继续运行才生效；普通波形设置应用会取消这次起搏覆盖。十二导联纸面同步切换。
起搏状态与按钮由宿主会话同步：未启动时禁用停止，波形切换待生效时禁用启动与停止，
模板切换完成后恢复未启动状态；输入草稿保留，重新启动后停止会返回新的模板。
电流缩放教学刺激幅度，0 mA 无刺激与诱发夺获；非零电流的夺获／失败由所选示例决定，
尚无患者特定的夺获阈值模型。底部工具栏复用宿主的导联、打印、血压与报警操作。

除颤区的 AED 模式占完整一行，可切换半自动分析／充电／按住确认电击流程。状态灯在分析、
充电、等待确认、CPR 和取消阶段更新，文字提示包含 CPR 倒计时。AED 内锁定能量、同步
及起搏，取消后需重新分析；文字、状态灯及 E1/F1 语音随阶段更新。手动充电键内部按进度从左向右填充，
充满后持续亮起，放电键闪烁；同步键有独立指示灯。放电要求持续按住鼠标主键或空格／Enter
至少 0.5 秒；同步时继续按住等待新检测的 QRS。松手、失焦、离开监护页、暂停、
应用设置、起搏命令、改变档位或同步模式均取消待放电状态，充满后还有自动取消计时。
“高级参数 → 除颤器”默认开启“使用皮肤默认能量档位”，采用当前包的 `defaultEnergyStepsJoules`。
关闭该开关、输入合法递增档位并应用后，手动表在所有皮肤上优先，切换皮肤及重新启动均保留；
重新开启并应用后恢复使用当前皮肤的默认表，保留手动表以供再次启用。
`DisplayPreferences.CustomDefibrillatorEnergySteps` 明确保存这一选择；旧文件缺少该标志时，
已保存的非 Generic 默认能量表视为手动表，原默认表采用皮肤默认。
四种单向／双向波、充电时间、自动取消时间及电击后 ECG 恢复时间在无设备参数配置时可编辑，
有 `MonitorSkinPackage.DeviceProfile` 时仍由设备预设固定；能量表的手动覆盖不解除这些限制。
ECG 电复律页另有逐模板心脏停顿时间，默认 0；监护恢复不等同于
真实停顿。每次实际放电均在采样 ECG 中留下饱和／恢复伪迹，转复失败仍会显示，参见接口时序。
完整时钟、放电和转复契约见[电复律接口](electrical-conversion.md#手动除颤与设备配置)。
原侧栏设置、十二导联与帮助入口继续有效。小窗口的治疗面板可纵向滚动。

## 手动生命体征与皮肤读数

“设置 → 生命体征 → 手动体征”提供 NIBP、体温与两个独立的自定义槽位；NIBP 默认启用，体温与自定义项默认关闭。
NIBP 的收缩压、舒张压、平均压均手动输入，单位 mmHg，要求
`0 ≤ 舒张压 ≤ 平均压 ≤ 收缩压 ≤ 300`；平均压不自动计算，也不与 ABP 联动。
体温范围为 0–50 ℃、一位小数。这些范围是编辑器输入边界，不代表临床参考范围。
自定义项支持 1–24 字名称、可空的 0–12 字单位，以及 -99999–99999、最多两位小数的值。
控制字符和空白名称无效，禁用项目的草稿不参与校验。

皮肤可通过 `LocalMonitorPreviewSession.ManualVitals` 读取不可变的
`ManualVitalSigns`：`Nibp`、`TemperatureDeciCelsius`、`Custom1`、`Custom2`。
`null` 表示禁用；零是有效读数。体温以十分之一摄氏度存储，自定义数值采用 `decimal`。
槽位身份固定，不随名称变化。这组数据独立于 `Measurements`，不伪造采样、测量时间或报警输入。
当前监护界面在底部显示启用的项目，并标记“手动体征”；NIBP 配置是下一次袖带测量的目标值，显示使用 `LocalMonitorPreviewSession.Nibp.Reading`。

应用前完整校验；普通应用与延时应用沿用 `ScheduleSource` 的仿真时间边界，暂停时不推进；
重新开始使用新会话的手动值；恢复默认设置启用 NIBP 并恢复 120/80（平均 93）mmHg 目标，关闭体温与自定义项。
手动值仅在当前会话中保留，不写入本地偏好。

## NIBP 袖带测量

底部 Start NIBP / Auto NIBP 仅在已应用的 `ManualVitals.Nibp` 非空时可用。
启用后尚未测量、正在测量或读数过期时显示 `--- / ---`。测量由 `NibpMeasurement` 使用
会话仿真时间推进，充气 6 秒、放气测量 9 秒；完成后发布开始测量时固定的目标值及完成时间。
开始后再点击手动按钮可停止；停止不产生新读数，先前尚未过期的结果可恢复显示。
读数自完成时刻起保留 15 分钟，等于有效期边界时失效。

Auto NIBP 弹出自动开关和 1/2/3/5/10/15 分钟间隔，默认 5 分钟。
开启自动模式立即测量；周期按两次开始时间计算，手动开始也重新锚定下次时间。
测量中不会重叠启动；调整间隔从当前时刻重排下次计划。关闭自动仅取消后续计划，
当前测量可继续或由 Stop NIBP 停止。大幅推进时间时不补跑错过的周期。
新建和恢复默认时启用 NIBP，默认目标为 120/80（平均 93）mmHg；不自动开始测量，
完成袖带周期前显示未测量。其他手动项目仍默认关闭。

关闭 NIBP 在参数实际生效时取消测量、自动计划和旧读数；再次启用或重启会话不会恢复旧结果。
暂停监护时进度、有效期和自动计时随仿真暂停，袖带声停止，恢复后继续当前阶段。
测量开始、完成和停止事件纳入实时打印事件历史，完成事件携带 SYS/DIA/MAP。
自动开关、间隔及结果为会话状态，不写入显示偏好。

袖带音由 `NibpSoundRenderer` 合成泵声与柔和的放气阀气流声：放气前 6 秒间断释放，
后 3 秒连续释放并逐渐减弱。通过已有 48 kHz 输出混音，
遵循总音量、静音和设备选择；报警音暂停不影响袖带声。治疗语音和储能提示仍可压低背景声。
停止、完成、关闭功能或窗口会撤回袖带声；输出设备不可用不会阻塞测量完成。

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

它用 .NET `BinaryWriter` 的小端数值顺序写入 magic `0x34565053`、组合数，再写各项 `(ecg, respiration, ejection, lead)`、ECG 与 ABP 样本数组，最后依次写 RESP、PLETH、CO₂ 的独立预览数组。每组样本为 `Int32 count` 后接 `(Int64 timeNs, Double value)`；时间严格递增、值必须有限。读取器检查 magic、固定组合数量、键范围与唯一性、预期导联、样本数量及尾随数据。旧目录不能直接加载，需由构建入口重新生成。

模板页按 ECG、RESP、射血、PLETH、EtCO₂ 分页；选择卡片仅修改草稿，应用后才改变实时源。
RESP 的“自然自主呼吸”追加为索引 6，原有 0–5 索引及保存偏好保持原意；
它使用 `RespiratoryPattern.SpontaneousIllustration` 与 `RespirationIllustration.Reference`。
主界面对此模板的设定频率接受 7–55 次/分，范围外显示可操作错误并保留原会话，不夹紧输入；
这是当前模板的工程范围，非医学正常范围，其他模板继续接受 6–60 次/分。
独立 RESP 预览使用与应用相同的源配置；ECG／ABP 的组合缓存复用规则呼吸项。
独立的 PLETH／CO₂ 卡片使用同一纵轴范围，以免自动缩放隐藏低灌注或非零吸入基线。
食管插管初启和从头开始时保留最初的洗脱过程，不跳过启动波形；运行中启用的有限序列从激活后开始。

生成时先写 `OUTPUT_PATH.tmp`、重新加载校验，再替换目标文件。构建自动生成目录；产品构建要求 `UsePrebuiltStylePreviews=true` 和存在的 `StylePreviewBinary`，跨平台发布使用宿主生成的目录，不执行目标平台程序。

`help-topics.json`（简体中文）、`help-topics.en.json`（英文）与 `help-topics.zh-TW.json`（繁体中文）是嵌入式帮助资源，后两者以 `WithCulture="false"` 嵌入主程序集；三者主题 id 及顺序一致，分类一一对应。`eng/dependencies.json` 和许可文本也作为资源嵌入。它们不是用户偏好或实时患者状态的存储接口。

## 构建、依赖及发布命令

CPU 内核按目标 RID、显式 PlatformTarget、SDK 架构的顺序选择，x64／ARM64 源文件互相排除。
`-p:MonitorKernelArchitecture=scalar` 用于只包含标量内核的构建；其他显式值必须匹配目标。
运行时依指令能力选内核，不按具体处理器型号调优，详见 [CPU 分派](../research/performance/cpu-kernels.md)。

| 脚本 | 参数 | 输出与限制 |
|---|---|---|
| [build.py](../../tools/build.py) | `--configuration Debug\|Release`（默认 Release）、`--jobs N`、`--offline`、`--managed-only`、`--cache-dir PATH`、`--product-release` | 准备锁定依赖、核对清单、构建；jobs 为 1–32，默认 CPU 数与 32 的较小值。`--managed-only` 跳过原生编译但仍准备依赖。产品模式只允许 Release |
| [build_release.py](../../tools/build_release.py) | 转发 `build.py` 参数并自动加入 `--product-release` | 在 `artifacts/release/` 生成本机产品候选；先完整构建临时输出再替换旧目录 |
| [fetch_dependencies.py](../../tools/fetch_dependencies.py) | `--offline`、`--native-only`、`--check`、`--cache-dir PATH`、互斥的 `--tts` / `--tts-only`、`--tts-cache-dir PATH` | 原生缓存默认 `.cache/downloads`；TTS 默认 `.cache/tts`。`--tts` 追加已选语音资源；`--tts-only` 只准备 TTS，与 `--native-only` 互斥。`--check` 仅校验所选原生/TTS 资源，不联网、不写文件、不恢复 NuGet |
| [fetch_tts_models.py](../../tools/fetch_tts_models.py) | `--cache-dir PATH`、`--offline`、`--check`、`--models-only` | TTS 下载实现，通常由上述入口调用；按 [模型清单](../../eng/audio/tts-models.json) 校验下载及成员哈希，只解出指定普通文件，不保留重复压缩包；默认准备独立 CPU 环境，`--models-only` 跳过环境 |
| [generate_selected_therapy_voices.py](../../tools/generate_selected_therapy_voices.py) | `--tts-cache-dir PATH`、`--output PATH`、`--language both\|zh-CN\|en`、`--prompt-id ID`、`--offline` | 自动准备资源并在独立环境中合成当前固定提示；默认输出 `artifacts/therapy-selected-resynthesis/`，48 kHz 单声道 PCM16。使用记录的中文音素和英文发音词典，不依赖旧试听产物；不覆盖已确认语音，新输出需听审 |
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
| [check_localization_keys.py](../../tools/check_localization_keys.py) | 无自定义参数；核对产品 C# 源码中字面量引用的 key 是否存在于简体中文目录，跳过 bin/obj、`*SmokeChecks.cs` 与 `*Fixture.cs`；不识别运行时构造的 key。成功返回 `0`，缺失返回 `1`，参数错误返回 `2` |
| [selected_therapy_voice.py](../../tools/selected_therapy_voice.py) | 无自定义参数；核对清单所列已选中文 F1 录音及可选贴片提示扩展的 SHA-256 和 48 kHz 单声道 PCM16 格式。成功输出核验结果，资源或校验异常向外传播；同模块的 `selected_voice_path(prompt_id, text)` 还校验传入文字与清单一致，供离线生成器验证并取得录音路径 |
| [spdx_headers.py](../../tools/spdx_headers.py) | `--check` 只检查；省略时会补写许可头，范围见贡献指南 |
| [check_commit_message.py](../../tools/check_commit_message.py) | `MESSAGE_FILE`；读取当前 Git author 与消息，检查仓库提交格式，返回 0 或 1 |

CI 在 Windows 图形会话和 Linux Xvfb 显示环境中运行 `--desktop-only`，两者均使用 `Monitor.Desktop` 的原生窗口入口。没有单独的无头桌面运行器；`--specs-only` 保留用于不依赖窗口的核心规格。桌面检查的启动截图输出到 `artifacts/desktop-startup.png`，CI 失败时与分片日志一起保留。

Windows 和 Linux CI 均使用 `--jobs 1` 运行桌面检查，避免不同进程在同一个原生桌面或 Xvfb 显示中争夺焦点、干扰弹出控件与命中测试。
本地共享显示的交互检查也应使用该参数；核心规格仍可并行。桌面分片并行运行需要分别隔离图形会话。

`tests/Monitor.Specs/Program.cs` 是开发用可执行规格与夹具入口：

```sh
dotnet run --project tests/Monitor.Specs/Monitor.Specs.csproj --no-build --configuration Release -- ARGS
```

| ARGS | 输入／输出 |
|---|---|
| 无参数 | 执行全部已注册规格；逐条输出 `ok N - 名称` 或 `not ok N - 名称`，异常写入标准错误；任一失败时全部执行完再以 1 退出 |
| `--shard INDEX COUNT` | 选择规格分片；`0 <= INDEX < COUNT <= 32` |
| `--cpu-kernels` | 输出 JSON：运行时／编译架构、可执行指令能力、PCM／混音内核及当前可用内核；不运行性能测量 |
| `--list-svg-fixtures` | 标准输出列出 SVG 场景名 |
| `--svg-fixture NAME` | 标准输出合成三角波 SVG；支持 live、nodata、frozen、frozen-nodata、review、review-nodata、paused、paused-nodata、stopped、stopped-nodata、frozen-return、review-return |
| `--audio-tone-fixture` | 标准输出二进制 WAV；重定向到文件，不能将文本日志混入此流 |
| `--therapy-audio-fixture OUTPUT_DIRECTORY` | 生成中英文治疗完整流程及储能优先混音 WAV，标准输出文件路径；细节见[治疗声音](audio-persistence.md#治疗声音)。成功返回 `0`，用法错误返回 `2`，执行或取消异常向外传播 |
| `--audio-native-audition ABSOLUTE_LIBRARY [DEVICE_ID]` | 在实际生产后端试听；不接受相对库路径或测试 null 后端 |
| `--audio-native-diagnostics ABSOLUTE_LIBRARY [DEVICE_ID]` | 设备／输出时钟诊断 |
| `--audio-native-clock-probe ABSOLUTE_LIBRARY [DEVICE_ID]` | 原生音频时钟采样 |
| `--audio-native-check ABSOLUTE_TEST_LIBRARY` | 显式检查隔离测试库绑定 |
| `--audio-wasapi-audition [DEVICE_ID]` | 用托管 WASAPI 输出播放与原生试听相同的五声，供 Windows 实机对比；不加载原生库 |
| `--audio-wasapi-diagnostics [DEVICE_ID]` | 打开托管 WASAPI 流但不播放，输出流路径、缓冲、周期和队列目标 JSON；设备不可用返回 `1` |
| `--audio-alsa-audition [PCM_NAME]` | 用 Linux ALSA 输出播放同样的五声；PCM 名称默认 `default` |
| `--audio-alsa-diagnostics [PCM_NAME]` | 打开 ALSA 流但不播放，输出缓冲、周期和队列目标 JSON；`null` PCM 可在无声卡环境检查 |
| `--audio-output-devices` | 以 JSON 输出当前平台的输出设备 ID 与名称，只枚举、不打开音频流；成功返回 `0`，用法错误返回 `2`，已取消返回 `130` |
| `--oxygen-transport-fixture OUTPUT.json` | 导出短暂停呼吸／恢复的输运输入 |
| `--oxygen-deep-transport-fixture OUTPUT.json` | 导出较深去饱和输运输入 |
| `--oxygenation-replay-check RESULT.json` | 将离线回放结果接入光学测量链 |
| `--oxygenation-deep-replay-check RESULT.json` | 较深去饱和结果的测量链接入 |
| `--oxygenation-realtime-check DEEP_RESULT.json` | 对照离线结果与实时 decimal 氧合求解器 |

分派源码：[Program](../../tests/Monitor.Specs/Program.cs)、[SVG](../../tests/Monitor.Specs/SvgFixtureCommand.cs)、[监护音 WAV](../../tests/Monitor.Specs/AudioFixtureCommand.cs)、[治疗音 WAV](../../tests/Monitor.Specs/TherapyAudioFixtureCommand.cs)、[原生音频](../../tests/Monitor.Specs/NativeAudioCommand.cs)、[托管音频与设备枚举](../../tests/Monitor.Specs/ManagedAudioCommand.cs)、[氧合](../../tests/Monitor.Specs/OxygenationFixtureCommand.cs)。SVG／监护音 WAV／原生音频入口区分成功 0、执行错误 1、用法错误 2 和取消 130；氧合入口的捕获异常（含取消）返回 1，用法错误返回 2。它们是开发数据通道，不是临床 ECG、评分输入或桌面产品启动参数。

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
| [generate_therapy_auditions.py](../../tools/generate_therapy_auditions.py) | `--output PATH`、`--tts-models PATH` | 默认输出 `artifacts/therapy-auditions-r1/`；治疗非语音候选、可选离线 AED 双语语音和试听页，参数见 [therapy-audition-candidates.json](../../eng/audio/therapy-audition-candidates.json)。只供试听，不接入治疗状态机；V1 Pacer 无专用声音 |
| [generate_therapy_auditions_r2.py](../../tools/generate_therapy_auditions_r2.py) | `--output PATH`、`--tts-models PATH` | 默认输出 `artifacts/therapy-auditions-r2/`；按反馈新增机械开启式起搏候选、独立 CPR 音色及速度、平叙双语语音；[参数与调研来源](../../eng/audio/therapy-audition-r2.json)记录对首轮的修订。依赖离线模型、首轮导出及 numpy/scipy/pyworld；不改变运行时起搏映射 |
| [generate_therapy_auditions_r3.py](../../tools/generate_therapy_auditions_r3.py) | `--output PATH`、`--tts-models PATH` | 默认输出 `artifacts/therapy-auditions-r3/`；[选音与参数](../../eng/audio/therapy-audition-r3.json)记录 P7 起搏、C2 CPR 和英文平叙 A，生成英文气流瞬态处理及 Kokoro 中文新声线候选；依赖第二轮导出和隔离缓存中的模型 |
| [generate_therapy_auditions_r4.py](../../tools/generate_therapy_auditions_r4.py) | `--output PATH`、`--tts-models PATH` | 默认输出 `artifacts/therapy-auditions-r4/`；[选音状态](../../eng/audio/therapy-audition-r4.json)记录第三轮反馈，生成英文原生 VITS/Matcha 对照、中文 R2 A 历史对照和已选 FastSpeech2-A / AISHELL-3 SSB0534；中文从项目 WAV 原样复制；依赖第二、三轮导出与隔离模型缓存 |
| [generate_therapy_runtime_tones.py](../../tools/generate_therapy_runtime_tones.py) | 无 | 使用 TTS 隔离环境运行；从已选参数生成运行时就绪双声与 C2 点击及校验清单 |
| [generate_therapy_selected_flow.py](../../tools/generate_therapy_selected_flow.py) | 无 | 从第四轮英文原文件及项目已选中文 WAV 生成 E1/F1 成人 AED 单次电击与完整 120 秒 CPR 流程、中英文连播及逐句试听；输出 `artifacts/therapy-selected-flow/`，仅离线编排 |
| [generate_therapy_pad_placement.py](../../tools/generate_therapy_pad_placement.py) | 无 | 按 [位置错误提示扩展](../../eng/audio/therapy-pad-placement.json)读取已确认 E1/F1 语音并生成贴片位置互换纠正分支，输出 `artifacts/therapy-pad-placement/`；模拟器位置判定专用，未接入运行时 |
| [generate_critical_a2.py](../../tools/generate_critical_a2.py)、[a3](../../tools/generate_critical_a3.py)、[a4](../../tools/generate_critical_a4.py) | 各自 `--output PATH` | 默认输出 `artifacts/alarm-auditions/` |
| [run_oxygenation_prototype.py](../../tools/run_oxygenation_prototype.py) | `--config PATH`、`--output PATH`、`--step-ms N`、`--plot`、`--blood-volume-ml N`、`--age-years N`、`--sex male\|female`、`--height-cm N`、`--weight-kg N` | 配置默认 `eng/physiology/oxygenation-prototype.json`、输出默认 `artifacts/oxygenation-prototype/`、步长默认 50 ms；plot 依赖可选 matplotlib；物理及报告字段见[原型文档](../research/oxygenation/oxygenation-prototype.md) |
| [replay_oxygenation_transport.py](../../tools/replay_oxygenation_transport.py) | `INPUT --output OUTPUT [--config CONFIG]` | 源时间步长必须为 8 ms，连续区间、规则且不中断的循环输运；输出 SaO₂ 不再附加传感器／采集延迟；配置默认同上 |

氧合实验输入为 `OxygenTransportReplayInput@1`，包含 `Transport`、`SampleStepNs` 和 `Intervals`；结果为 `OxygenTransportReplayResult@1`，包含 `Transport`、`Oxygenation`、有效参数和来源信息。`Oxygenation` 中 `StartSimTimeNs`、`SampleStepNs`、`SaturationMilliPercent`、`ModelId` 可用于构造 `SampledArterialOxygenationState`。详细单位、范围与示例命令见[氧合输运接口](oxygenation/oxygenation-transport.md)。

`tools/oxygenation_model.py` 是供原型／回放工具导入的计算模块；`test_*.py` 使用 Python `unittest` 标准入口，没有应用自定义命令协议。生成器的 `--check` 用于比较当前资源，省略该参数时可能改写生成文件；源码与参数必须同步维护，不能只编辑生成结果。


## 患者时间线

顶部选择器切换完整的患者时间线文件，文件内的分支通过图中节点选择。
选择器旁的“文件”菜单提供新建、导入、导出与移出列表，管理最多 16 个独立文件；移出列表不删除磁盘文件。
`.simtimeline` 是版本化 UTF-8 JSON，包含完整计划，不包含会话游标、波形或活动治疗；
导入完整校验格式、分支引用与模板后才加入列表，同 ID 的文件以独立副本加入，导出前先校验完整草稿。
文件切换和导入不改变正在运行的患者；所选文件在“从头开始”时生效。
新建文件（含初始默认文件）只在创建时生成一次 256 位随机种子，保存在 `PatientTimelineProgram.SeedHex`；
文件栏的“随机种子”可手动修改或重新生成，随导入、导出、偏好保存与恢复保留，开始和重新开始不再生成。
种子须为 64 位小写十六进制；非法草稿即时提示并阻止载入，最后有效文件保持不变。
旧偏好、时间线文件库与导入文件缺少该字段、为 `null` 或为空字符串时，桌面加载边界自动补齐一次种子并保存，
已有种子保持不变；再次打开升级后的文件不会重新生成。通用程序恢复接口仍保留未设种子的旧模型契约，
但实际文件加载先完成升级，手工清空输入视为无效草稿。启用且包含事件或显式初始状态的时间线
在启动／从头开始时使用文件种子准备初始波形、独立变化及事件模板的节律序列；空文件和关闭的时间线
继续使用普通监护设置的种子。编辑种子不改变运行中的会话。
关闭顶部时间线开关立即停止后续节点、手动进入与治疗分支，并冻结已编译的体征目标曲线；
会话时钟、已采样历史和已有治疗保留。重新启用需从头开始，不恢复旧游标或补跑事件。
文件列表、有效草稿、选中项和开关在本地偏好中独立保存；无效编辑保留在当前编辑器，
不覆盖最后有效版本，修正前不允许切换文件或导出。关闭时，未完成的无效草稿不阻止普通监护会话重新开始。

编辑器使用节点与连线关系图展示所选文件的所有时间线；时间连线连接相邻节点，治疗连线连接
规则所属节点与目的时间线起点。节点卡片显示名称、时间方式、目标摘要和校验状态。
画布支持平移、缩放、全图适配及键盘节点导航，点击节点或治疗连线打开对应详情。
程序首次显示时按全图缩放，但不低于 75%，以保持卡片可读；“显示全图”不受此下限限制。
“添加事件”在当前节点后插入，“前移／后移”调整执行顺序；旧的绝对时刻列表在插入或排序时
转换为等价的相邻等待，再进行编辑。图布局从作者数据自动生成，不持久化画布位置。
较宽窗口在关系图旁常驻详情侧栏；窄窗口选中节点后使用完整编辑区展示详情，通过“返回关系图”
恢复画布与视图位置。“上一节点／下一节点”按图中顺序在当前时间线内切换。时间线起点的详情编辑
名称、初始患者状态和治疗规则，分支的“删除分支”也位于此处；事件详情底部提供前移、后移和删除。

起点的“初始患者状态”可指定体征、心律、波形模板或通气状态，无需创建零秒事件。
主线起点在会话零时刻、首次采样或事件推进前生效；分支起点在该分支实际进入的 T 时刻生效。
未指定的项目继承生成设置或分支进入前的患者状态，SpO₂ 目标与通气修改保持互斥。
起点没有等待或渐变时长，不计入事件数，不占用手动节点索引；初始治疗规则独立配置。
空起点保存为 `null`，旧文件继续使用既有基线。起点与事件一样是草稿，须从头开始才影响新会话。

“设置 → 患者时间线”编辑命名事件，包含开始时间、变化时长、独立的体征目标，
以及可选的心电、呼吸、射血、脉搏和二氧化碳波形模板。时间单位为秒，步进 0.2 秒，
事件开始与结束均不超过 3600 秒，每条时间线最多 64 项；一个计划最多 16 条命名时间线、总计 256 项事件。
事件名称、治疗权限、分支关系与执行顺序随时间线文件保存在本地偏好中。
时间线启用时，草稿在“从头开始”时整体校验并载入；编辑本身不改变正在运行的患者。
初始时间线从会话 0 开始，启动预热段不计入；暂停不推进时间，重新开始重置执行游标。
治疗分支的事件使用 T+ 相对时间；T 是该分支进入时刻。分支继承当时的患者状态、
已采集的波形、测量与事件历史，全局会话时间不归零。离开的分支后续事件不再执行。

节点的 `PatientTimelineTrigger` 决定进入方式。`TimelineTime` 保留旧数据的绝对时间语义；
`AfterPrevious` 的 `StartTimeNs` 是距上一节点实际进入时刻的等待时长，首节点从进入时间线时开始等待。
`Manual` 是人工等待点，`StartTimeNs` 必须为零；教师进入该点后，以当前会话时刻建立新的 T，
立即应用本节点目标和规则，后续相邻等待从该时刻起算。暂停时也可进入手动节点，不推进会话时钟。
手动和相邻等待节点可以只表示流程控制，不必填写体征或治疗规则。
全为 `TimelineTime` 的旧事件仍稳定按时间排序；含相邻等待或手动节点的序列保留作者顺序。
每段手动等待之间的自动进入时间加渐变时长不得超过 3600 秒。
手动等待前已经开始的渐变继续运行；手动进入后，未被该节点或后续节点改写的渐变也继续。
下一人工等待点及其后目标尚不进入仿真源，不能提前影响当前患者。

`PatientTimelineEvent` 是当前首版的作者数据，不是考试协议或通用插件事件总线。
`PatientVitalTargets` 的空字段表示不更改该项。非零时长使用线性目标曲线，零时长在触发边界切换。
新的同项目标从当时已经达到的值开始，接替尚未结束的渐变；不同体征独立执行。
同一时刻按编辑列表的稳定顺序执行，后项覆盖同项目标。
模板、通气状态和治疗规则是触发时刻的离散变化，事件时长只用于数字体征。
目标参数进入实际源样本，测量窗口、信号质量、缺失搏动及报警确认仍由原有链路处理，
因此屏幕测量值不保证在作者时长终点立即等于目标。

直接 SpO₂ 目标使用源时间的光学饱和度曲线；通气事件则使用患者氧合模型。
两者在同一事件中互斥。模型已经运行时，指定 SpO₂ 不重置其氧储备；后续通气事件恢复模型读数。
原先未启用模型时，通气事件使用设置中选定的患者基线建立模型。
通气输入包含潮气量、死腔、吸入氧浓度和气道通畅状态；它们不是直接指定血氧读数。

有创压力渐变要求起始会话已启用该通道的压力目标；模板的机械活动与灌注语义继续有效。
未启用的温度或 NIBP 必须先在起点或零时长事件中提供初值，再使用渐变。
NIBP 目标变化不自动启动袖带测量，也不改写已完成的测量结果。
心率渐变首版支持 30–180 bpm，并受模板波形时限约束；起搏、独立心室时钟、房扑、
无心脏活动和室扑／室颤不接受心率渐变目标。可在事件中切换这些模板。

手动源配置在实际生效时停止当前时间线，延迟等待期间时间线继续；重新开始可按当前草稿重放。
事件节点可通过 `PatientTimelineEvent.Treatment` 设置是否允许电击与开始／调整起搏，
以及电击成功、无效电击和开始起搏的下一条时间线。非空规则只在该节点执行时整体生效，
并持续到下一个显式设置规则的节点；空规则继承当前策略。只修改治疗规则的节点也可执行和记录。
进入时间线时使用 `PatientTimelineDefinition.Treatment` 初始策略，兼容既有计划；
未来节点的规则不会提前生效。T+0 事件节点随该时间线首次事件推进执行，同一时刻按稳定顺序依次覆盖。
起搏仍受当前 ECG 模板许可约束；
停止起搏、释放电击按键及取消储能始终可用。禁止电击时桌面会撤销储能／待放电状态，
应用入口也在产生伪迹、记录或分支前拒绝治疗。

成功电击沿用原有治疗响应与电击后停顿，最终转窦律实际生效时作为 T；
无效电击在实际释放时作为 T；首次起搏生效时作为 T。仅查询治疗效果、
储能或未完成的同步请求不产生分支；同一放电序号不重复分叉。
正在起搏时调整参数与停止起搏不分叉，停止后再次开始则属于新的治疗。
已释放电击的结果路线绑定该次放电时生效的节点规则，停顿期间执行的新规则不改写这次治疗的待执行分支。
已经开始的起搏不会被后续禁止规则自动停止；禁止规则阻止新的开始与调整，停止操作仍可用。
未指定后续时间线时继续当前命名时间线；治疗接管当前心脏源，独立体征与后续事件继续。
旧的单时间线应用接口仍保留治疗源变更终止脚本的兼容行为。
运行状态暴露计划、已触发项数、停止和完成状态；每项实际触发以
`PreviewRecordEventKind.PatientTimelineEvent` 连同名称写入监护打印事件历史；
进入时间线另记为 `PatientTimelineBranchEntered`，二者使用会话总时间。
若目的时间线与治疗后继承的状态不兼容，保留实际治疗与当前时间线，不发布部分分支配置；
`TimelineBranchError`、`TimelineBranchErrorTimelineName` 提供原因和目标名称，
桌面显示可操作的错误，并记录 `PatientTimelineBranchRejected`。
未来的主观状态提示、查体材料和动态考核尚未包含在此数据格式与执行器中。

命名时间线集成入口为 `LocalMonitorPreviewSession.ConfigurePatientTimelineProgram(program, templateSources, oxygenationModel, initialSources)`，
可选的 `initialSources` 按时间线 ID 提供起点模板源，与事件模板源分开传入；
作者模型为 `PatientTimelineProgram`、`PatientTimelineDefinition` 和 `PatientTimelineTreatmentPolicy`。
治疗路线引用稳定 ID，可以返回已访问的时间线，每次实际治疗建立新的分支代次与 T；不自动递归跳转。
`CurrentTimelineId`、`CurrentTimelineName`、`TimelineGeneration`、`TimelineOriginTimeNs` 提供运行位置。
`WaitingPatientTimelineEvent` 与 `WaitingPatientTimelineEventIndex` 标识当前等待点；
`TriggerPatientTimelineManualEvent(expectedTimelineGeneration, expectedEventIndex)` 只进入匹配的等待节点，
重复请求或治疗分叉后失效的旧请求返回 `false`。手动进入不改变时间线代次，索引仍按完整作者序列递增。

兼容单线入口为 `LocalMonitorPreviewSession.ConfigurePatientTimeline(events, templateSources, oxygenationModel)`。
调用方先完成 `DiscardStartup()`（如适用），然后在时间仍为零时配置；
`templateSources` 与 `PatientTimelineEvent.Snapshot(events)` 的排序一一对应，模板事件必须提供源定义。
兼容单线入口可使用不含分支目标的节点治疗权限；引用命名分支需使用计划入口。
计划结构、模板引用与治疗路线在载入时验证；当前自动片段的定义与曲线在发布前准备。
人工等待后的片段在实际进入时根据继承状态完整验证，失败保留原等待点。
实时推进不读取控件或墙钟，也不逐帧重建波形源。
