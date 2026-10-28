# Seele's SiMonitor

<img src="src/Monitor.Desktop/Assets/app-icon.png" alt="Seele's SiMonitor 临时图标" width="128" height="128" />

**简体中文** | [English](docs/en/README.md)

Seele's SiMonitor 是用于教学的心电图及其他监护波形模拟器。**本项目不是医疗器械，不用于临床监护、诊断或治疗。**

仓库包含 .NET 桌面应用、确定性仿真库、原生音频适配器、构建工具、可执行规格检查、锁定的依赖和许可证资料。

技术资料见 [文档导航](docs/README.md) 和 [英文摘要](docs/en/summary.md)。参与开发前请阅读 [贡献指南](CONTRIBUTING.md)。

## 运行截图

**主监护界面**

![显示心电图及其他生命体征波形的主监护界面](docs/assets/screenshots/main-monitor-cn.png)

**报警**

![报警时的监护界面（带皮肤）](docs/assets/screenshots/alarm-cn.png)

**十二导联心电图**

![十二导联心电图快照](docs/assets/screenshots/ecg-12-lead-cn.png)

**波形预设设置**

![显示波形预设及预览卡片的设置界面](docs/assets/screenshots/waveform-preset-cn.png)

## 环境要求

- Python 3.11+。
- .NET 10 SDK；详细规则见 [`global.json`](global.json)。
- CMake 3.20+，以及用于编译原生音频包装库（Windows WASAPI）的 C 编译器。
- Windows 上还需要 Visual Studio C++ Build Tools 和 Windows SDK。

如无特殊说明，命令均从仓库根目录执行。Linux 和 macOS 使用 `python3`；Windows 可使用 `python3` 或 `py -3`。构建脚本会恢复锁定的依赖，并校验下载源码的 SHA-256。

## 构建与运行

x64 和 ARM64 的 CPU 内核分别编译，运行时根据可用指令集选择。详见 [CPU 分派与性能工具](docs/research/performance/cpu-kernels.md)。

构建当前平台的开发版本：

```sh
python3 tools/build.py --jobs 32
```

此命令准备依赖、编译生产环境原生音频包装库（Windows WASAPI）并构建 Release 配置的完整解决方案。构建结果位于 `src/Monitor.Desktop/bin/Release/net10.0/`。运行桌面应用：

```sh
dotnet run --project src/Monitor.Desktop --no-build --configuration Release
```

清理后，Windows 下应先重新运行构建脚本以恢复原生音频 DLL。若使用默认 Debug 配置的 `dotnet run --project src/Monitor.Desktop/Monitor.Desktop.csproj`，将会报错找不到 WASAPI 音频库，请先执行 `python3 tools/build.py --configuration Debug`。重编译 WASAPI 音频库后，再运行不带 `--no-build` 参数的 `dotnet run`，才会将新 DLL 复制到对应输出目录；缺失 DLL 时启动检查会提示上述恢复命令。

也可以先准备依赖，再使用本地缓存构建：

```sh
python3 tools/fetch_dependencies.py
python3 tools/build.py --offline --jobs 32
```

生成本地产品候选版本：

```sh
python3 tools/build_release.py --jobs 32
```

产品输出位于 `artifacts/release/`。Windows 声音默认使用 WASAPI 音频库，Linux 通过系统 ALSA 库（也覆盖 PipeWire、PulseAudio）出声，不需要原生音频包装库；macOS 暂无声音输出（因为我并没有相关设备）。

**注意**：一个平台上的构建或检查不能代替其他平台设备的实际验证。

`artifacts/` 中主要存放构建脚本创建的各种文件，Git 默认忽略此目录。项目的原生音频包装源码位于 `native/sim_audio_native/`；Windows 后端使用 WASAPI。依赖脚本将固定版本的 `vendor/miniaudio.h` 下载到该目录，核对其 SHA-256 后才用于构建。如需审阅这份上游头文件，请先通过命令准备依赖。源码位置、输出布局及原生诊断方法见 [原生音频指南](native/sim_audio_native/README.md)。

## 验证

准备依赖后，可运行依赖检查、Release 构建及可执行文件规格检查：

```sh
python3 tools/fetch_dependencies.py
python3 tools/verify_dependency_ledger.py
dotnet build Monitor.slnx --no-restore --configuration Release
dotnet run --project tests/Monitor.Specs/Monitor.Specs.csproj --no-build --configuration Release
```

这里的 `--no-restore` 依赖第一步已完成的依赖准备。其他专项检查应按改动范围及相关模块文档执行。

**注意**：构建或规格检查通过不等于 Windows 实机、音频设备或发布验收通过。

## 清理本地生成内容

清除编译缓存、测试输出和 `artifacts/` 下的生成结果：

```sh
python3 tools/clean.py clean
```

进一步清除 `.cache/` 及依赖脚本下载的 `native/sim_audio_native/vendor/miniaudio.h`：

```sh
python3 tools/clean.py dirclean
```

如需预览将被删除的文件夹，请添加 `--dry-run` 参数，例如 `python3 tools/clean.py clean --dry-run`。

**清理会删除整个 `artifacts/`，包括编译的软件包和自行放入的文件**；执行前请将需要保留的文件移出该目录。

清理后如需重新合成当前选用的语音，运行：

```sh
python3 tools/fetch_dependencies.py --tts-only
python3 tools/fetch_dependencies.py --tts-only --check
python3 tools/generate_selected_therapy_voices.py --offline
```

`--tts-only` 会恢复 FastSpeech2-A（AISHELL-3 SSB0534）、PWGAN 声码器、英文 E1（VITS LJS）、词典、来源说明及独立的 CPU Python 环境，默认放在 `.cache/tts/`。需要 Python 3.11+ 和可用的 pip/venv；脚本不安装 Torch/CUDA。模型下载地址、大小上限及 SHA-256 见 [`eng/audio/tts-models.json`](eng/audio/tts-models.json)，Python 依赖库见 [`eng/audio/tts-requirements.txt`](eng/audio/tts-requirements.txt)。下载和解压均校验哈希，完成后删除临时压缩包；已有完整资源不会重复下载。

`--tts-cache-dir PATH` 可指定恢复位置；生成工具也需传入相同参数。`--check` 只进行校验，`--offline` 在资源缺失时只报告错误。普通构建无需 TTS 模型；如需同时准备构建依赖和 TTS，使用 `--tts`。开发过程中考虑过的 Qwen、Kokoro、CosyVoice 等 AED 候选语音模型及试听历史不会自动恢复。

重新合成的语音覆盖中英文 AED 操作提示与 CPR 按压指导，输出目录为 `artifacts/therapy-selected-resynthesis/`，可使用 `--language zh-CN` 参数只生成中文语音。

**注意**：推理存在随机性，新的输出需要重新试听，无法保证与目前采用的 WAV 完全一致。生成工具不会覆盖仓库中已确认的语音；这些位于 `eng/audio/voices/` 的受版本控制的语音资源也不会被清理命令删除。
脚本只处理仓库内生成内容，不卸载系统工具链，也不清理供其他项目共用的全局 NuGet 缓存。

## 许可证与归属

项目原创代码采用 AGPL-3.0-or-later，详见 [`LICENSE`](LICENSE) 和 [许可证范围说明](docs/legal/license-scope.md)。第三方代码及资源保留各自的许可证。依赖来源与对应哈希见 [`eng/dependencies.json`](eng/dependencies.json)，相关权利声明等见 [`eng/licenses/`](eng/licenses/) 和 [Infirmary 来源声明](docs/legal/infirmary-source-notice.md)。

## Todo

- 正式图标设计（当前为临时图标）
- 更完善的 UI 设计
- 更完善的皮肤接口
- 独立教师端、学生端
- 教学与考试功能
- macOS 与移动设备适配
- 配套的模拟设备？
