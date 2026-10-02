# Seele's SiMonitor

**简体中文** | [English](docs/README.en.md)

Seele's SiMonitor 是用于教学的心电图及其他监护波形模拟器。**本项目不是医疗器械，不用于临床监护、诊断或治疗。**

仓库包含 .NET 桌面应用、确定性仿真库、原生音频适配器、构建工具、可执行规格检查、锁定的依赖和许可证资料。本地产品构建仅是待检查的候选产物，不代表已通过发布验收。

技术资料的英文概览见 [英文摘要](docs/english-summary.md)。参与开发前请阅读 [贡献指南](CONTRIBUTING.md)。

## 环境要求

- Python 3.11 或更新版本。
- 稳定版 .NET 10 SDK；具体版本与回退规则见 [`global.json`](global.json)。
- CMake 3.20 或更新版本，以及用于编译原生音频模块的 C 编译器。
- Windows 上还需要 Visual Studio C++ Build Tools 和 Windows SDK。

以下命令均从仓库根目录执行。Linux 和 macOS 使用 `python3`；Windows 将命令中的 `python3` 替换为 `py -3`。构建脚本会恢复锁定的依赖，并校验下载的原生源码的 SHA-256。

## 构建与运行

构建当前平台的开发版本：

```sh
python3 tools/build.py --jobs 32
```

这条命令准备依赖、编译生产用原生音频库并构建 Release 配置的完整解决方案。构建结果位于 `src/Monitor.Desktop/bin/Release/net10.0/`。运行桌面应用：

```sh
dotnet run --project src/Monitor.Desktop --no-build --configuration Release
```

也可以先准备依赖，再使用本地缓存构建：

```sh
python3 tools/fetch_dependencies.py
python3 tools/build.py --offline --jobs 32
```

生成本地产品候选版本：

```sh
python3 tools/build_release.py --jobs 32
```

产品输出位于 `artifacts/release/`。目前原生音频后端面向 Windows WASAPI；其他平台上的构建或检查不能代替 Windows 设备的实际验证。

`artifacts/` 中的生成结果由构建脚本创建，且被 Git 忽略。项目的原生音频包装源码位于 `native/sim_audio_native/`；依赖脚本将固定版本的 `vendor/miniaudio.h` 下载到该目录，核对其 SHA-256 后才用于构建。审阅这份上游头文件前，先运行依赖准备命令。源码位置、输出布局及原生诊断方法见 [原生音频指南](native/sim_audio_native/README.md)。

## 验证

准备依赖后，可运行依赖清单检查、Release 构建及可执行规格检查：

```sh
python3 tools/fetch_dependencies.py
python3 tools/verify_dependency_ledger.py
dotnet build Monitor.slnx --no-restore --configuration Release
dotnet run --project tests/Monitor.Specs/Monitor.Specs.csproj --no-build --configuration Release
```

这里的 `--no-restore` 依赖第一步已完成的依赖准备。其他专项检查应按改动范围及相关模块文档执行；构建或规格检查通过不等于 Windows 实机、音频设备或发布验收通过。

## 清理本地生成内容

清除编译缓存、测试输出和 `artifacts/` 下的生成结果：

```sh
python3 tools/clean.py clean
```

进一步清除 `.cache/` 及依赖脚本下载的 `native/sim_audio_native/vendor/miniaudio.h`：

```sh
python3 tools/clean.py dirclean
```

两种清理方式都可加 `--dry-run` 预览删除范围，例如 `python3 tools/clean.py clean --dry-run`。**清理会删除整个 `artifacts/`，包括候选发布包和自行放入的文件**；执行前请将需要保留的产物移出该目录。
脚本只处理仓库内生成内容，不卸载系统工具链，也不清理供其他项目共用的全局 NuGet 缓存。

## 许可证与归属

项目原创代码采用 AGPL-3.0-or-later，详见 [`LICENSE`](LICENSE) 和 [许可证范围说明](docs/license-scope.md)。第三方代码及资源保留各自的许可证。依赖来源和哈希记录在 [`eng/dependencies.json`](eng/dependencies.json)，相关声明见 [`eng/licenses/`](eng/licenses/) 和 [Infirmary 来源声明](docs/infirmary-source-notice.md)。

## Todo
- i18n
- icon
- 生命体征逐项调整
- 监护仪皮肤接口
- 12导联ecg的手动测量工具
- 事件驱动的生命征连续变化
- 独立教师端、学生端
- 教学与考试功能