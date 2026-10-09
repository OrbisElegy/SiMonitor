# 音频输出、原生 ABI 与本地偏好接口

[接口总览](README.md) · 公开声明：[Infrastructure](api/infrastructure.md)。

本文记录 `Monitor.Infrastructure.Audio`、`Preferences` 和 `native/sim_audio_native` 的当前契约。
监护音频是随桌面窗口启动的本地输出链，试听由用户触发；身份 SQLite 与加密备份见
[权威、身份与恢复接口](authority-identity-recovery.md)。公开声明之外的桌面 UI 行为以调用方为准。

## 时间线与线程所有权

源码：[ToneVoice](../../src/Monitor.Infrastructure/Audio/ToneVoice.cs)、
[SampleToneRenderer](../../src/Monitor.Infrastructure/Audio/SampleToneRenderer.cs)、
[AudioClockBridge](../../src/Monitor.Infrastructure/Audio/AudioClockBridge.cs)。

内部时间线固定为 **48000 Hz、mono、float32 PCM**；frame 是一帧单声道采样，48 frames=1 ms。
Renderer.Position / RenderedThroughFrame 是下一个尚未渲染的位置，不是硬件播放游标。
控制/调度线程串行处理命令、设备生命周期和 Pump；唯一原生 consumer callback 可与之并发。
不要在 callback 里打开/关闭设备、加载资源、写 WAV、调用生命周期 owner 或更新 UI。

```csharp
AudioClockBridge(long ticksPerSecond, long initialTicks, long initialFrame);
void Observe(long monotonicTicks, long engineFrame);
long MapToFrame(long monotonicTicks);
```

ClockBridge 根据最近一对严格递增 ticks/frame 观测估算仿射比例；初始斜率为 48000 frames/s。
MapToFrame 使用向上取整，定位到请求时间及之后第一帧，也可映射 exclusive expiry。
所有输入 ticks/frame 非负，频率大于零；倒退、相同观测或映射溢出抛参数/溢出异常。
观测必须属于同一 48 kHz 引擎时间线；callback 到达时刻、另一采样率的原生 frame 不能直接输入。
该类不做设备时间质量过滤、重采样偏移补偿或物理延迟标定。

## 音色、调度与 PCM 缓冲

`TonePreset(Id, FrequencyMilliHz, AttackFrames, HoldFrames, ReleaseFrames, GainQ15)`
另含 Sample、BeatPitchPercent。频率单位 milliHz，范围 20000–20000000；Attack/Release 至少 1 frame，
Hold 非负，合计不超过 480000 frames；GainQ15 为 0–16384，BeatPitchPercent 为 70–97。
默认 BeatAudition 为 795 Hz，包络 240/4800/720 frames，GainQ15=8192。
采样音色 TotalFrames 取资源长度，其余取包络合计；无效 preset 在创建或调度时抛 ArgumentException。

```csharp
ToneVoice(TonePreset preset);
int ToneVoice.Render(Span<float> destination);
void ToneVoice.Cancel();
ToneVoiceState ToneVoice.CaptureState();
static ToneVoice ToneVoice.Restore(ToneVoiceState state);
ToneScheduleResult SampleToneRenderer.Schedule(
    long cancellationKey, TonePreset preset, long targetFrame, long expiryFrame);
void SampleToneRenderer.Cancel(long cancellationKey);
void SampleToneRenderer.DiscardForDiscontinuity(long nextFrame);
void SampleToneRenderer.Render(Span<float> destination);
```

Voice.Render 返回实际 voice 帧数，剩余 destination 清零；Finished 以 voice 结束位置判断。
Cancel 未开始 voice 立即结束，已开始 voice 从当前相位最多淡出 240 frames（5 ms）。
CaptureState/Restore 用于离线确定性延续，包含 preset/frame/cancelFrame，非法快照被拒绝。

Renderer 最多 32 个待播/活动 voice；Schedule 返回 Accepted/Expired/Duplicate/Full。
expiryFrame 是 **exclusive 最晚开始时刻**，不是声音截断时刻；`expiry <= Position` 为 Expired。
仍有效的迟到命令从 Position 开始。key 仅对仍占据 slot 的 voice 去重，全局去重和 epoch 策略不在这里。
混音完成后整体限幅到 [-1,1]；Render 使 Position 前进 destination.Length。
Cancel 清除尚未开始的 cue，已开始的使用淡出；已提交给设备的 PCM 无法收回。
DiscardForDiscontinuity 只接受不倒退的新位置，丢弃所有旧命令；调用者还必须退役排队 PCM。

源码：[资源音色](../../src/Monitor.Infrastructure/Audio/SelectedMonitorTones.cs)、
[资源 manifest](../../src/Monitor.Infrastructure/Audio/SelectedTones/manifest.json)、
[WAV 导出](../../src/Monitor.Infrastructure/Audio/ToneWaveFixture.cs)。

SelectedMonitorTones.Alarm(level, volumePercent) / Heartbeat(volumePercent, saturationPercent=97)
返回采样 preset；volume 为 0–100。Heartbeat pitch 使用 70–97 的离散 bank，97 使用原 heartbeat。
PCM 是程序集嵌入的 48 kHz signed 16-bit little-endian mono，预加载后才进入 render 路径。
Info/Notice/Warning/Critical/Heartbeat 长度分别为 8640/7680/6240/168000/4800 frames；
HeartbeatPitchA 含 28×4800 frames。缺资源或长度错误抛 `AudioTone.MissingAsset` / `AudioTone.InvalidAsset`。
这些采样与 timing 是项目当前选用的素材和编排，不表示音色认证或厂商设备兼容性。

`ToneWaveFixture.Write(Stream output, TonePreset preset, int beatCount, int periodFrames,
CancellationToken cancellationToken=default)` 输出 RIFF/WAVE 48 kHz mono signed PCM16。
beatCount 1–128、periodFrames 至少 preset.TotalFrames、总长最多 60 s；需要可写 Stream。
取消抛 OperationCanceledException，已经写出的前缀不会回滚；调用者拥有 stream，方法只 Flush、不关闭。

源码：[AudioPcmBuffer](../../src/Monitor.Infrastructure/Audio/AudioPcmBuffer.cs)、
[AudioRenderSession](../../src/Monitor.Infrastructure/Audio/AudioRenderSession.cs)。

| 类型/方法 | 契约 |
| --- | --- |
| `AudioPcmBuffer(int sampleRate=48000, int channels=1, int capacityMilliseconds=40)` | 通用缓冲范围 8–192 kHz、1–8 channels、20–100 ms；capacity 向整数 frame 计算 |
| `bool TryWrite(ReadOnlySpan<float> interleaved)` | producer 单独调用；完整 frames、有限值且绝对值≤1，空间足够才整体发布，否则 false |
| `int Read(Span<float> destination)` | consumer 单独调用；返回实际 PCM frames，尾部静音；非完整帧 geometry 全部静音且不消费 |
| `WritableFrames` | producer 侧快照，consumer 只能释放容量 |
| `AudioRenderSession(long initialFrame=0, int capacityMilliseconds=40)` | 固定 48 kHz mono，拥有 renderer、缓冲和 scratch，不打开设备 |
| `ToneScheduleResult? Schedule(long key, TonePreset preset, long targetFrame, long expiryFrame)` | 退役后返回 null，否则交给 renderer |
| `bool TryProduce(int frames)` | frames 必须 1…CapacityFrames；满缓冲或已退役返回 false，满缓冲不推进 renderer |
| `int Read(Span<float> destination)` | 第一次 underrun 输出已有前缀+静音，累计缺帧并永久退役；之后静音 |
| `void Cancel(long key)` / `void Retire()` | 取消渲染 cue / 原子锁存退役，Retire 不等于停止 OS 设备 |

每流仅一个 producer 与一个 consumer；替换时创建新对象，不重置活跃 queue。
RequiresReplacement 一旦为 true 不会恢复；UnderrunFrames 表示缺失引擎帧数。
BufferedFrames、RenderedThroughFrame 供调度端查看，不是硬件时钟或延迟测量。

## 设备端口与生命周期

源码：[AudioOutputLifecycle](../../src/Monitor.Infrastructure/Audio/AudioOutputLifecycle.cs)、
[SoundPreviewPlayback](../../src/Monitor.Infrastructure/Audio/SoundPreviewPlayback.cs)。

```csharp
interface IAudioOutputDevice {
    bool Start();
    bool StopAndClose();
}
interface IAudioOutputFactory {
    IAudioOutputDevice? Open(string? deviceId, AudioRenderSession session, long generation);
}
interface IPumpedAudioOutput : IAudioOutputFactory, IDisposable {
    bool Pump();
    void WaitForQueueSpace(int timeoutMilliseconds);
}
```

Open(null, ...) 跟随系统默认设备，返回尚未启动的 device；null 表示失败且 factory 没留下 callback/资源。
如设备格式不同，adapter 显式转换。StopAndClose=true 表示 callback 已 join 且资源已释放；
false 必须保留所有权允许重试，不能卸载仍可能被调用的原生代码。

`AudioOutputLifecycle(factory)` 暴露 State/Failure/Generation，只有 Running 时 Session 非空。
`Replace(string? deviceId, long initialFrame, int capacityMilliseconds=40)` 先验证和分配 candidate、
预填一队列静音，再退役/关闭旧流，然后增加 generation、Open、Start。
旧流 StopAndClose 失败时返回 false，不开新流；Open/Start 失败也返回 false。
State 为 Stopped/Running/DeviceLost/StopFailed；Failure 区分 None/Open/Start/Stop/Underrun/DeviceLost/SessionRetired。
`CheckHealth()` 由控制线程调用，发现退役就关闭并返回 false；不自动重试。
`DeviceLost(long generation)` 只接受当前 generation，过期设备通知忽略。
`Stop()` 成功置 Stopped/None，失败保留 StopFailed 与资源所有权；adapter 的意外异常仍可能向外传播。

源码：[NativeAudioOutputFactory](../../src/Monitor.Infrastructure/Audio/NativeAudioOutputFactory.cs)、
[NativeAudioClockSample](../../src/Monitor.Infrastructure/Audio/NativeAudioClockSample.cs)。

NativeAudioOutputFactory(string libraryPath, bool allowTestBackend=false, int queueTargetMilliseconds=20)
实现 IPumpedAudioOutput。libraryPath 必须 fully qualified，显式加载，无 DLL search-path fallback；ABI 版本必须为 1。
`DefaultLibraryPath` 是应用目录下按当前平台命名的生产库（Windows `sim_audio_native.dll`）。
queueTargetMilliseconds 为 5–100，越界在加载前抛 ArgumentOutOfRangeException。
默认拒绝导出 sa_test_render 的测试库；只可同时拥有一个 device，重复 Open 抛 DeviceStillOwned。
设备 ID 按 UTF-8 传递，禁止内嵌 NUL；原生 open 失败映射 null。
`Pump()` 每次最多搬运一队列容量，先 drain managed staging，再填 native queue；每次生产最多 240 frames。
managed staging 总是完整 drain；新 PCM 只把 native queue 补到 `QueueTargetFrames`，不再填满容量。
库导出 sa_wait_writable 时，目标取 queueTargetMilliseconds 与两个设备 period（按 48 kHz 向上换算）的较大者，
并以 session 容量为上限；旧 ABI1 库没有该符号时目标等于容量，保持原 40 ms 行为。
`QueueTargetFrames` 在无 device 时为 null。native underrun 映射 session 缺帧，其他退役原因令 session 退役。
`WaitForQueueSpace(int timeoutMilliseconds)` 只在 owner 线程调用，范围 1–1000：有 sa_wait_writable 时
阻塞到 consumer 运行一次、流退役或超时；旧库或无 device 时退回 1 ms sleep。它不读写 PCM，
返回后由下一次 Pump 观察队列和退役状态。目标值未经过 Windows 实机延迟或欠载鉴定。
Status / PeriodSnapshot 在无 device 时为 null；Open 后的诊断字段见下文 sa_info。
`ReadClock()` 在无 device 或旧库无该可选 symbol 时返回 Result=-2 的零值采样。
`Dispose()` 要求先成功关闭 device，否则抛 CloseBeforeUnload；成功后卸载动态库。

源码：[WasapiAudioOutput](../../src/Monitor.Infrastructure/Audio/WasapiAudioOutput.cs)、
[WasapiRenderEndpoint](../../src/Monitor.Infrastructure/Audio/WasapiRenderEndpoint.cs)。

`WasapiAudioOutput(int queueTargetMilliseconds=20)` 是不依赖原生库的 IPumpedAudioOutput：owner 线程用
源生成 COM 互操作直接写 WASAPI shared-mode 流缓冲，没有原生 ring 或我方 callback 线程；非 Windows 平台 Open 返回 null。
混音格式为 48 kHz float 时直接写入（单声道写到前两个声道，其余声道静音）；默认引擎周期的两倍超过队列目标且
设备支持 IAudioClient3 时，尝试不超过目标一半的最大支持周期；若短周期初始化失败，释放该客户端并重新激活后回退到普通流。仅周期查询失败时可继续使用原客户端。其他混音格式由 Windows 从 48 kHz 单声道 float 转换。
`StreamPath` 依次为 None、MixFormat、ShortEnginePeriod、WindowsConversion；`BufferFrames`、`PeriodFrames`、
`QueueTargetFrames` 以 48 kHz frame 计，无流时为 null。staged PCM 先 drain，新 PCM 只补到目标；运行中的流
padding 为 0 时仍可补充 PCM，不把系统刚取走最后一包数据误报成欠载。WASAPI 的 padding 查询不能确认实际缺帧，当前适配器不提供确切欠载计数。默认设备切换、设备移除或停用、流失效时退役，
适配器不自行重连，由监护播放 owner 关闭旧流并自动打开新流。Start 为 owner 线程申请 MMCSS "Pro Audio"，Stop 撤销；Dispose 会先关闭仍打开的流而不抛异常。
该实现尚未在 Windows 实机上验证延迟与欠载余量。
`AlsaAudioOutput(int queueTargetMilliseconds=20)` 是 Linux 的托管输出：通过系统 `libasound.so.2` 打开 48 kHz 单声道 float
流，PCM 名称默认 `default`（也经由 PipeWire、PulseAudio 的 ALSA 插件），缺少库、设备或格式时 Open 返回 null。
ALSA 明确返回 xrun 时按 underrun 退役（缺帧数只记下限 1）；正常返回的空队列仍可补充 PCM，其他 ALSA 错误按设备丢失退役。PipeWire 插件按图量子成块取数，
并按软件参数自行推算缓冲，因此 owner 线程不依赖 avail_min 唤醒，而是按当前排队量睡到目标一半再补数据。
该路径的持续播放及发声延迟仍需设备验证；无时钟采样，ReadClock 返回不可用。

`AudioOutputSelection.Create(AudioOutputSelection.Current)` 是桌面声音设置使用的唯一选择点：
环境变量 `SIMONITOR_AUDIO_OUTPUT` 为 `native`、`wasapi` 或 `alsa` 时按其选择，否则 Linux 用 ALSA、其他平台用
`NativeAudioOutputFactory.DefaultLibraryPath`。

NativeAudioClockSample 包含 Result、HResult、DevicePosition、DeviceFrequency、Qpc100Nanoseconds。
`Nominal48kElapsedFrames` 只有 Result=0、HResult=0、frequency 非零且转换不溢出时才非空，
按 `position * 48000 / frequency` 向下取整。它只是名义设备累计时间，不能直接当 renderer frontier。

`AudioOutputDevices.Enumerate()` 在 Windows 枚举活动播放端点，返回 `AudioOutputDeviceInfo(Id, Name)`；
其他平台返回空列表。名称取端点友好名称，ID 原样传给音频后端。桌面展开设备下拉框时刷新列表，
已选择但不可用的设备保留在列表中；“系统默认”独立于具体设备 ID。
Windows 枚举与属性读取遵循 [Microsoft Core Audio 设备属性接口](https://learn.microsoft.com/en-us/windows/win32/coreaudio/device-properties)。

## 本地试听与报警播放入口

源码：[MonitorAlarmPlayback](../../src/Monitor.Infrastructure/Audio/MonitorAlarmPlayback.cs)。

`SoundPreviewPlayback(Func<IPumpedAudioOutput>).PlayAsync(int volumePercent, CancellationToken)`
在专用 owner 线程播放三段设置试听、运行约 2.4 s；
SetOutput(string? deviceId, int volumePercent, bool muted) 设置所选设备和实时主增益；桌面切换设备时取消当前试听。volume 0–100，并发调用抛 AlreadyPlaying。
`MonitorAlarmPlayback(Func<IPumpedAudioOutput>)` 提供
SetOutputDevice(string? deviceId)、SetVolume(int volumePercent, bool muted)、
SetHeartbeatEnabled(bool)、SubmitHeartbeat(int volumePercent, int pitchPercent=97)、
SetRequest(MonitorAlarmSoundRequest?)、RunAsync(CancellationToken)。
请求包含 Level、VolumePercent、MonitorSoundTiming；null 清除报警音请求。
新增 NotificationSequence：0 沿用持续播放，正值指定 owner 内的一次声音组身份。
RunAsync 仅允许一个 worker；设备失败后在同一 owner 线程关闭旧流，每 250 ms 重试，取消可立即结束等待。
null 设备 ID 表示系统默认，每次重开重新解析；具体 ID 保持固定，不回退默认设备。关闭失败保留所有权并终止重连。
重连保留已退役通知序号，恢复当前持续请求或未完成的当前组，丢弃断开期间的心搏 mailbox，不排队补播。
Reconnecting 表示正在等待恢复，OutputActive 仅在成功 Pump 后为真。
SetVolume 设置 0–100 主音量与静音；AudioRenderSession.Gain（0–1）在 PCM 离开托管缓冲时缩放，
同时影响已渲染的报警、心搏和试听，硬件队列中的少量 PCM 仍按原音量播放。桌面请求使用满幅报警与相对心搏音量，避免重复缩放。
SubmitHeartbeat 单槽保留最新 cue，worker 丢弃超过 250 ms 的旧 cue。
两个入口的专用 owner 线程以 Highest 优先级运行，每次 Pump 后调用 `WaitForQueueSpace(10)` 等待下一次 consumer
运行，而不是固定 sleep；请求和心搏 cue 最迟在下一次唤醒时进入排程。
OutputActive 只表示曾成功 pumping 且 worker 尚活动，不保证物理出声或已达到延迟目标。
监护入口在暂时失败时保持运行，直到取消或关闭失败；试听入口不自动重播。
返回 SoundPreviewResult：Completed（试听正常结束）、Stopped（取消）、Unavailable（打开/加载不可用）、
Interrupted（Pump/健康失败）、StopFailed（关闭失败，优先保留资源所有权）。

`MonitorAlarmSequencer.Update(request)` 根据当前请求重新排程；同一请求沿 rendered timeline 推进，
不补播错过的 burst。UpdateHeartbeat(bool enabled, int? volumePercent, int pitchPercent=97)
收到非空 volume 才新增 beat。报警 cue 最晚启动期限为 target+2400 frames（50 ms），beat 为+12000（250 ms）。
请求变更取消当前已知 key，音量零不排程；Info 只有 Timing.InfoTone=true 才播放。
正序号请求只执行一组；重复/过期身份不重播，同/低等级的新请求在组忙碌时合并，更高等级可打断。
`AlarmNotificationSoundRouter` 提供来源事件绑定、最高等级合并、暂停/恢复及有界路由记录；
sequencer 提供有界 Dispatches、DroppedDispatchCount 与 MissedNotificationCount。
混合路由使用完整提示快照，已注册条件遵循独立短／长策略；未注册的可听信息、技术及测试提示保留持续声音，同级由持续来源承担，静音输出故障不参与仲裁。软件排程记录不等同于物理交付，产品模式与完整行为见[通知声音执行接口](alarms/alarm-notification-sound.md)。
该链没有网络报警 director、全局事件去重或权威 epoch 接入。

## native/sim_audio_native ABI 1

权威声明：[sim_audio.h](../../native/sim_audio_native/sim_audio.h)；
实现：[sim_audio.c](../../native/sim_audio_native/sim_audio.c)；
构建边界：[native README](../../native/sim_audio_native/README.md)。

导出 C ABI，managed delegate 使用 Cdecl；sa_output 为 opaque handle。
生产构建只启用 Windows WASAPI shared playback；其他平台 open 返回 unavailable，
不会偷偷回退 null backend。SIM_AUDIO_TEST 使用 null backend，只允许默认设备。

```c
uint32_t sa_abi_version(void); /* 1 */
int32_t sa_open(const char* device_id_utf8, uint32_t capacity_ms, sa_output** out);
int32_t sa_submit(sa_output* output, const float* pcm, uint32_t frames);
int32_t sa_start(sa_output* output);
int32_t sa_close(sa_output* output);
uint32_t sa_info(sa_output* output, uint32_t key);
int32_t sa_clock_sample(sa_output* output, uint64_t* position,
    uint64_t* frequency, uint64_t* qpc_100ns, uint32_t* hresult);
int32_t sa_wait_writable(sa_output* output, uint32_t timeout_ms);
/* SIM_AUDIO_TEST only */
void sa_test_render(sa_output* output, float* pcm, uint32_t frames);
```

| 返回码 | 意义与具体边界 |
| --- | --- |
| 0 | 成功；sa_clock_sample 表示准确采样 |
| -1 | 无效参数/PCM/设备 ID；空句柄通常为 -1，sa_info 例外返回 0 |
| -2 | 后端、设备、分配、启动或时钟不可用；sa_close 停止失败时也为 -2 |
| -3 | sa_submit 空间不足，整次输入不写入 |
| -4 | 流已退役，不能继续 submit/start/clock |
| 1 | sa_clock_sample：S_FALSE 降低精度，不能用于调度校准；sa_wait_writable：超时 |

sa_open 的 out 非空；验证后先置 *out=NULL。capacity_ms 为 20–100，设备 ID 可 NULL 但不可空字符串。
Windows ID 需有效 UTF-8 并能转换到后端的 64-wide-char ID 容器；不支持时返回 -1/-2。
成功打开 48 kHz mono float 接口和 `48 * capacity_ms` frame ring，但尚未启动。
调用顺序：open → submit 静音或有效 PCM → start → submit/info/clock → close。
sa_submit frames 是 float 元素数；所有值有限且在 [-1,1]，先完整校验再写。
若写入过程中被退役，结果仍可为 -4；不要重播给旧 handle 或假定队列仍能恢复。

callback 先清零，再读有效 PCM；首次不足保存 missing frames，退役 reason 由 0 原子改为 1。
原生配置关闭 miniaudio 的固定大小 callback 包装，按实际请求的 frame 数消费，避免额外预读一个 period 导致启动时提前欠载；consumer 支持任意请求长度。
stop/reroute/interruption 通知将 reason 设为 2，underrun 不得把 2 覆盖为 1。
之后 callback 保持静音。sa_close 先退役为 2，再 stop/uninit/join、释放 clock/ring/context/handle；
停止失败保留 handle，必须重试 close。成功 close 后不得再访问句柄或相关缓冲。
sa_test_render 走同一 consumer，但要求有效 handle/buffer；它不是可向生产传入任意指针的安全包装。

| sa_info key | 数值/单位 |
| --- | --- |
| 1 / 2 / 3 | 原生 sample rate Hz / channels / format：1=f32、2=s16、3=s24、4=s32、5=u8 |
| 4 | 后端报告 period frames，不能解读成物理延迟 |
| 5 | Windows 实际 WASAPI buffer frames；不支持返回 0 |
| 6 / 7 / 8 | retired reason 0/1/2；第一次 underrun 缺帧；producer 可写引擎 frames |
| 9 | low-latency qualified，当前恒为 0 |
| 10 | 打开时 period snapshot 状态：0 不可用/旧库、1 可用、2 查询失败 |
| 11–15 | default/fundamental/minimum/maximum/current engine period frames |
| 16 / 17 / 18 | snapshot engine rate Hz / HRESULT bits / engine channels |

未知 key 或空句柄返回 0；11–18 为打开时快照，不是实时观测。旧 ABI1 DLL 的可选项可能全零，
零表示不可用，不能据此声称零周期或零延迟。
sa_clock_sample 是可选 ABI1 扩展，只允许 owner thread。有效输出指针先全部清零；空输出指针为 -1。
设备 position 必须除以 frequency 才是秒，QPC 已为 100 ns 单位，不是 QueryPerformanceCounter ticks。
Windows 从当前流拥有的 IAudioClock 读取；失败可在 hresult 保留 HRESULT，其余数值为零。
生产 qualification、声音实际到达扬声器的延迟仍无此 API 的直接证据。
sa_wait_writable 也是可选 ABI1 扩展，只允许 producer/owner thread，timeout_ms 为 1–1000。
consumer 每次运行（含 underrun 退役）以及 stop/reroute/interruption 通知都会置位一次唤醒；
已置位的唤醒在下一次等待时立即返回并被消费，不会丢失。返回 0 表示 consumer 已运行，1 超时，
-4 已退役，-2 等待机制不可用。Windows 使用 auto-reset event，callback 中只调用非阻塞的 SetEvent；
SIM_AUDIO_TEST 的非 Windows 构建以 1 ms 轮询原子标志实现，不代表唤醒延迟。

## 本地偏好文件与失败语义

源码：[DisplayPreferenceStore](../../src/Monitor.Infrastructure/Preferences/DisplayPreferenceStore.cs)、
[显示配置](../../src/Monitor.Application/Presentation/MonitorDisplayConfiguration.cs)、
[报警偏好](../../src/Monitor.Application/Presentation/MonitorAlarmPreferences.cs)、
[确认时间](../../src/Monitor.Application/Presentation/MeasurementConfirmationTiming.cs)、
[声音偏好](../../src/Monitor.Application/Presentation/MonitorSoundPreferences.cs)、
[生成器偏好](../../src/Monitor.Application/Presentation/MonitorGeneratorPreferences.cs)。

```csharp
record DisplayPreferences(MonitorDisplayConfiguration Display, int PaperLayout,
    MonitorAlarmPreferences? Alarms=null, MonitorSoundPreferences? Sound=null,
    MonitorGeneratorPreferences? Generator=null);
DisplayPreferenceStore(string path);
DisplayPreferences Load(out bool rejected);
bool Save(DisplayPreferences preferences);
```

path 为调用者指定路径，Save 规范化成绝对路径并创建父目录；没有内建默认位置。
存储只包含显示/报警/声音配置和可选生成器编辑输入，不保存波形状态、活动报警、测试 notice。
Sound.HeartbeatEnabled 控制心搏音；输出随窗口启动，主音量与静音不关闭播放服务。

文件为 UTF-8 JSON，属性名大小写沿用 C# 名称，缩进输出；最多读取 32768 bytes、最大深度 8。
未配置字符串枚举 converter，普通 enum 属性以整数表示；字典键按 System.Text.Json 的键表示规则。
未知属性拒绝，构造器必需参数必须存在。顶层必需 Version/Skin/Slots/PaperLayout，slot 必需
Channel/Automatic/Minimum/Maximum/Speed；Speed 为 0.1 mm/s，合法值 125/250/500。
Skin 为 ThreeRows=0/FiveRows=1/SevenRows=2，slot 数量必须分别 3/5/7；channel 0–6。
PaperLayout 仅 0 或 1；它在基础设施层是索引，具体版式名称由桌面调用方解释。

| 版本 | Load 兼容规则 |
| --- | --- |
| 1 | Alarms/Sound/Generator 可缺省 |
| 2 | Alarms 必须非空 |
| 3 | Alarms、Sound 必须非空 |
| 4 | Alarms、Sound、Generator 必须非空 |
| 5–14 | Alarms、Sound 必须非空；Generator 属性必须出现，值允许 null |

Save 总是写 Version=14；缺省 Alarms/Sound 用各自 Default，Generator=null 明确写入文件。
所有提供的 Alarms/Sound/Generator 在 Load 和 Save 时调用 Validate，显示 slot/range 也通过配置构造校验。
Alarms.PlaybackMode 默认 Continuous（0），Notifications（1）表示默认短警报；它只作用于跟随默认的条件。
Alarms.Notifications 按已注册条件 ID 保存 AlarmNotificationSettings 覆盖项，缺省为空。
各项保存 SoundMode（Inherit=0、SingleGroup=1、Continuous=2）、RepeatSuppressionMilliseconds、ReminderEnabled、ReminderMilliseconds；关闭提醒或切成长警报仍保留短警报配置。版本 8 缺少 SoundMode 时使用 Inherit，保持原默认模式。
版本 1–7 缺少新字段时保留持续声音及默认通知策略，不恢复活动事件、计时或通知记录。
完整范围及桌面映射见[通知策略接口](alarms/alarm-notification-policy.md)。
Alarms.ConfirmationTimings 是以 MonitorNumeric 为键的可选覆盖字典，旧文件省略时得到空字典；
每项包含 CriticalLow/WarningLow/WarningHigh/CriticalHigh，各自 TriggerMilliseconds/RecoveryMilliseconds 为 0–600000。
未覆盖项经 ConfirmationFor 使用 DefaultFor；ABP/PA/CVP mean 默认低限触发/恢复 4000/3000 ms，
高限 10000/3000 ms，其他支持指标默认 0/0。空字典不等于所有指标固定零时长。
Alarms.NoExpirationConfirmation 独立保存 CO₂ 未检出呼吸条件的 TriggerMilliseconds/RecoveryMilliseconds，
范围同为 0–600000；旧文件省略时默认 0/0，显式 null、缺少边界时间字段或非法范围拒绝。
此时间不包含原有 NoExpirationSeconds 呼吸等待时限，且不复用 RR·CO₂ 数值覆盖。
其余报警阈值倍率随各 MeasurementLimits 描述定义，不能把存储整数当 UI 显示单位。

版本 13 增加 Sound.OutputDeviceId（null 表示系统默认）和 Muted（默认 false）；旧版本缺省时使用这两个默认值。
设备 ID 禁止空白、NUL 或超过 1024 字符。保存设置不要求设备当前在线。

版本 14 增加 Generator.ElectricalConversions，按稳定 ECG 模板标识保存允许转复与独立的单／双相能量阈值。旧文件缺省为空映射；切换到窦律不删除原模板配置。见[电击转复接口](electrical-conversion.md)。

Sound 保存 Volume/HeartbeatVolume（0–100）、HeartbeatEnabled、BeatSource（0–2）、PitchSource（0–1）、
PauseSeconds（1–3600）、Timing；Timing 周期单位 ms，Info 5000–120000、Notice 1500–60000、
Warning 3500–60000、Critical 250–2000，另有 InfoTone。
Generator 保存 Ecg/EcgName、Respiration/Ejection、Seed、Numbers/Flags/Choices、ApplyDelaySeconds、Oxygenation；
ApplyDelaySeconds 为 0–60、0.1 s 步进，三个字典上限分别 64/32/16。它不是仿真 checkpoint。

Load 找不到文件/目录：返回 `MonitorDisplayConfiguration.Default()`、PaperLayout=0，rejected=false。
IO、权限、JSON 或参数校验失败：返回相同默认值，rejected=true，不改写坏文件。
有效旧文件保留缺省 Alarms/Sound/Generator 为 null，应用调用方自行选择回退值。
Save 先做参数校验，再创建随机同目录 `.tmp` 文件、序列化、Flush(flushToDisk:true)、
File.Move(overwrite:true) 发布；finally 尝试删除临时文件。
IO/UnauthorizedAccessException 返回 false；非法参数、路径规范化或序列化的其他异常不会统一转 false。
因此调用方应区分用户输入无效、加载被拒绝、保存失败和文件缺失；成功 Save 只说明文件发布成功。

版本 10 的通知项还保存 LatchingMode（NonLatching=0、UntilAcknowledged=1）；旧文件缺省为非保持。仅控制恢复后的视觉提示，不恢复确认状态或声音请求。用户确认、确认后提醒和软件退役水位见[确认与保持接口](alarms/alarm-attention.md)。
