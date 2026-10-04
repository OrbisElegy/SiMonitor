# 文档导航

[项目说明](../README.md) · [English](en/README.md)

接口文档描述当前实现的调用契约；技术调研记录模型依据、作者选择和适用边界。

## 英文指南

- [Seele's SiMonitor](en/README.md)
- [Seele's SiMonitor: English summary](en/summary.md)

## 接口与集成

- [当前实现接口总览](interfaces/README.md)
- [报警确认配置接口](interfaces/alarms/alarm-confirmation.md)
- [音频输出、原生 ABI 与本地偏好接口](interfaces/audio-persistence.md)
- [权威、身份、连续性与持久化接口](interfaces/authority-identity-recovery.md)
- [桌面与命令行入口](interfaces/desktop-tools.md)
- [测量、通知与波形呈现接口](interfaces/measurements-presentation.md)
- [成人氧合基线与教师耗氧倍增器](interfaces/oxygenation/oxygenation-defaults.md)
- [氧合第三步：深低数值、测量恢复与报警验证](interfaces/oxygenation/oxygenation-reporting.md)
- [氧合第二步：物理输入与原始采样时间](interfaces/oxygenation/oxygenation-transport.md)
- [仿真、采集与波形接口](interfaces/simulation.md)

## 技术调研

- [独立氧合下降模型调研](research/oxygenation/oxygenation-model-research.md)
- [独立氧合模型：原型与参数验证](research/oxygenation/oxygenation-prototype.md)
- [逐搏充盈与房室时序](research/physiology/cardiac-filling-perfusion.md)
- [潮式呼吸与呼出 CO₂ 的单向教学响应](research/physiology/cheyne-stokes-co2-coupling.md)
- [长 RR 间期 Pleth 衰减调研](research/physiology/pleth-runoff-research.md)
- [SVT 充盈限制与血压修正](research/physiology/svt-perfusion.md)
- [机械射血停止与恢复后的血管压力：调研与实现方案](research/physiology/vascular-pressure-runoff-research.md)

## 许可与归属

- [Infirmary Integrated 来源与归属声明](legal/infirmary-source-notice.md)
- [项目许可范围](legal/license-scope.md)

## 文档资源

运行截图位于 `assets/screenshots/`，由中英文项目说明引用。
