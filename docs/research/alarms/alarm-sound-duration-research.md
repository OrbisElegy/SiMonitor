# 短／长警报声音语义与实现映射

资料：[Philips Patient Information Center iX Instructions for Use，Release 4.4](https://www.documents.philips.com/assets/Instruction%20for%20Use/20250321/d80393498eb1407e8912b2a7015f378e.pdf?feed=ifu_docs_feed)，4536 650 29231，2025 年 2 月第一版。以下为印刷页码。

- 6-5～6-6：黄色警报分为长、短两种，短警报沿用黄色警报声音但持续较短；但没有给出具体的短警报持续时间。
- 6-12 与 7-10：HR 高、低限在相应心律失常配置中可配置为短或长黄色警报；长 HR 警报不会超时后停止声音警报。
- 7-11：Cardiotach 模式的 HR 高、低限固定为长黄色警报。因此不同厂家、不同型号监护仪的默认警报策略会有不同。

结论：

本项目采用独立的教学配置：每个已实现的警报可跟随默认或显式选择短／长，此选择独立于严重程度和生理触发／恢复确认。短警报采用本项目原有同级声音的单组，长警报在条件活动期间持续按组播放；不1：1复刻厂商警报音、患者适用范围或全部优先级链。现有条件中包括同一方向的 Warning/Critical 升降级，时长配置作用于整个条件，不把短警报自动降为较低等级。

短警报的重复抑制与提醒保持独立。长警报不使用短警报的重复抑制和额外提醒，但保留这些配置供切换后使用；其声音随已确认活动状态解除或中断，确认后的警报行为和恢复后视觉保持见[确认与保持接口](../../interfaces/alarms/alarm-attention.md)，全局报警链仍未完整实现。相关接口见[通知配置](../../interfaces/alarms/alarm-notification-policy.md)与[声音路由](../../interfaces/alarms/alarm-notification-sound.md)。
