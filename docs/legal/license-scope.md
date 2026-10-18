# 项目许可范围

自V0.5起，项目原创代码使用GNU Affero General Public License version 3。
许可标识为 **AGPL-3.0-or-later**：本仓库原创代码允许按第3版或任何后续版本使用。
完整许可正文见仓库根目录[LICENSE](../../LICENSE)，来源为
[GNU官方文本](https://www.gnu.org/licenses/agpl-3.0.txt)。

已有第三方代码、适配来源、字体、二进制和其他资源保留各自许可证与版权声明，
不因项目许可而被重新许可。参见`eng/dependencies.json`、`eng/licenses/`及
`docs/legal/infirmary-source-notice.md`。许可证文件的加入不代表所有第三方资源分发审核已经完成。
未收录于仓库的参考材料与教材原文不属于项目许可范围或发布范围。

交付二进制时应附带许可、适用的第三方声明及对应版本的完整源码和必要构建材料；
网络交互适用AGPL第13条时须提供相应源码获取方式。后续贡献按上述项目许可接受，已有第三方归属保持不变。

治疗语音来源、录音校验清单与声明位于 `eng/audio/voices/zh-CN/F1/` 和 `eng/audio/voices/en/E1/`。
中文使用 F1 模型；英文流程录音使用 E1 模型。
运行时嵌入固定 WAV，来源声明和许可证随输出放入 `therapy-licenses/`；模型不随应用打包。
生成语音不因嵌入而重新许可为 AGPL，既有来源说明与发布范围备注继续适用。
