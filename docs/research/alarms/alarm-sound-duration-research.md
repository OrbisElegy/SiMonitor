# 短／长警报声音语义与实现映射

资料：[Philips Patient Information Center iX Instructions for Use，Release 4.4](https://www.documents.philips.com/assets/Instruction%20for%20Use/20250321/d80393498eb1407e8912b2a7015f378e.pdf?feed=ifu_docs_feed)，4536 650 29231，2025 年 2 月第一版。以下为印刷页码。

- 6-5～6-6：区分长、短黄色警报，短警报沿用黄色警报声音但持续较短；这些段落没有给出可直接作为本项目常数的统一短警报秒数。
- 6-12 与 7-10：HR 高、低限在相应心律失常配置中可采用短或长黄色警报；长 HR 警报没有该短警报 timeout。
- 7-11：Cardiotach 模式的 HR 高、低限固定为长黄色警报。因此不能从上述可配置例子推断所有厂商报警、所有监护模式都允许任意短／长选择。

本项目采用独立的教学配置：每个已登记条件可跟随默认或显式选择短／长，独立于严重程度和生理触发／恢复确认。短警报采用本项目原有同级声音的一组，长警报在条件活动期间持续按组播放；不复制厂商波形、音长、患者适用范围或全部优先级链。现有条件中包括同一方向的 Warning/Critical 升降级，时长配置作用于整个条件，不把短警报自动降为较低颜色或等级。

短警报的重复抑制与提醒保持独立。长警报不使用短警报的重复抑制和额外提醒，但保留这些配置供切回；其声音随已确认活动状态解除或中断，不包含尚未实现的用户确认、保持或全局报警链。完整接口见[通知配置](../../interfaces/alarms/alarm-notification-policy.md)与[声音路由](../../interfaces/alarms/alarm-notification-sound.md)。
