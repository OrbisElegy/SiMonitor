# 起搏与除颤波形依据

实现入口：[起搏源](../../../src/Monitor.Simulation/Physiology/PacingReference.cs)、[放电源](../../../src/Monitor.Simulation/Therapy/DefibrillationWaveform.cs)。本文区分文献支持的定性特征与本项目作者选择的数值。所有模板均为固定教学示意，不是设备编程器或患者模型。

## 文献与实现映射

| 依据 | 支持的内容 | 实现选择及边界 |
|---|---|---|
| [Medtronic 起搏器介绍](https://www.medtronic.com/content/dam/medtronic-wide/public/western-europe/products/cardiac-vascular/cardiac-rhythm/pacing-systems/brady-pacemaker-brochure-uk-en.pdf) | 单腔可位于心房或心室，双腔具有心房和心室导线；无导线装置直接植入心内 | 将腔室、起搏部位和植入方式写在模板名称中。植入途径不能唯一决定 ECG；临时经静脉与常规右室起搏允许共享形态 |
| [EHRA 传导系统起搏植入共识（2023）](https://pmc.ncbi.nlm.nih.gov/articles/PMC10105857/) | 希氏束起搏、左束支区域起搏的定义及十二导联评估；选择性希氏束起搏可有刺激至 QRS 的等电间隔，左束支区域起搏常见 V1 终末正向成分但并非必有 | 选择性希氏束示意保留 40 ms 刺激至 QRS 间隔；左束支区域示意采用 110 ms QRS 与 V1 终末正向形态，不宣称仅凭模板确证左束支夺获 |
| [右室起搏部位研究（2011）](https://academic.oup.com/europace/article/13/12/1738/2398613)和[双心室起搏 ECG 研究（2017）](https://pmc.ncbi.nlm.nih.gov/articles/PMC5840759/) | 右室心尖起搏可有下壁负向 QRS，双心室起搏常有 V1 正向成分，但有个体及融合差异 | 用于约束作者示意的电轴与胸导联方向，绝不将固定 QRS 时宽或某一导联符号视为普遍诊断规则 |
| [2021 ESC 起搏与 CRT 指南](https://pmc.ncbi.nlm.nih.gov/articles/PMC13179788/) | 常规右室、双心室及传导系统起搏是不同的心室激动策略 | 右室示意保留既有 LBBB 样胸导联轮廓，并在电极域调整为下壁导联负向的上向电轴；CRT、左室心外膜采用 V1 正向的作者轮廓；不是导线定位或 CRT 疗效判定模型 |
| [Pacemaker Troubleshooting: Common Clinical Scenarios（2016）](https://pmc.ncbi.nlm.nih.gov/articles/PMC5067035/) | 无输出、失夺获、感知不足、过度感知在 ECG 上的区别 | 分离刺激、心房／心室电活动与机械事件；感知不足示意在自身 QRS 内放出不适当刺激，处于不应期不再产生第二个 QRS；过度感知示意间歇抑制输出 |
| [ZOLL 单相与双相技术说明](https://www.zoll.com/en-en/about/medical-technology/rectilinear-biphasic-technology)及[官方 AED Plus 操作指南](https://arcdistributor.zoll.com/-/media/public-site/products/aed-plus/9650-0301-05-sf_c.ashx) | 单相电流不反向，双相电流反向；直线双相具有受控首相及反向次相，设备资料提供相时长与间隔示例 | 一次性放电源支持单相阻尼正弦、单相截断指数、双相截断指数、直线双相；相时长、电流峰值与间隔显式输入，不按焦耳直接推导 ECG 幅度 |

## 起搏模板

模板索引追加在现有目录之后，165–173 为起搏方式，174–179 为起搏失败，保留旧模板身份与偏好映射。

| 模板 | 电活动与刺激 |
|---|---|
| 心房 AAI | 心房尖峰后 P 波，经自身房室传导产生窄 QRS；本示意没有竞争的自身心房活动 |
| 右室心尖 VVI | 60/min 心室起搏，独立 75/min 心房活动；示意基础完全房室阻滞，没有自身心室竞争 |
| 双腔 DDD | 心房、心室顺序刺激，200 ms 刺激间期；全部搏动为 AP–VP，不实现完整感知／跟踪算法 |
| CRT | 心房顺序起搏与同时双室刺激，合并为一个可见心室刺激，140 ms 作者 QRS |
| 选择性希氏束 | 心房顺序起搏，心室刺激后 40 ms 等电间隔，80 ms QRS |
| 左束支区域 | 心房顺序起搏，110 ms 作者 QRS，V1 终末正向 |
| 无导线右室 | VVI 时序，较小的作者尖峰；不将幅度大小当作无导线设备的诊断特征 |
| 心外膜左室 | 心房顺序起搏和左室刺激，160 ms 作者 QRS，V1 正向示意 |
| 临时经静脉右室 | 与右室 VVI 共用时序和形态；“临时”并不意味着独有的 ECG |
| 心房失夺获 | 保留心房尖峰而无 P 波／心房收缩，心室仍可刺激夺获 |
| 心室失夺获 | 心房刺激正常；每次心室尖峰后无 QRS，也无新机械射血 |
| 心室输出失败 | 心房刺激正常；应有心室刺激处无尖峰、无 QRS、无新射血；无逸搏示意 |
| 心室感知不足 | 自身窄 QRS 开始后 32 ms 出现不适当尖峰；不应期内不重复夺获 |
| 心室过度感知 | 每四周期的后两周期抑制心室输出，保留心房起搏 |
| 间歇心室失夺获 | 与过度感知使用相同缺搏位置，但保留所有心室尖峰 |

时序基于仿真时间，支持分批、checkpoint 与有限回看。普通刺激至电活动间隔采用 8 ms，电活动至心室机械事件采用 80 ms，均为作者参数。尖峰为 250 Hz 显示用 8 ms 支持区间，在网格内产生可见样本；这不是植入器脉宽、输出电压或采集前端脉冲检测器。监护 II 与十二导联使用同一电极源投影。

只有存在机械事件才触发 Pleth、ABP、PA 与 CVP 的心室成分。压力保留已有储库的被动衰减，不能要求失夺获后压力立即归零；未夺获的心房不会生成 CVP a 波或充盈贡献。可独立关闭有效射血；模板基础频率固定，心率编辑显示不适用。

这些模板通过独立仿真旁路向当前 ECG 警报提供心房／心室脉冲及来源标记，不能把作者事件当成设备检测结果。QRS、HR 和起搏失败由采样波形与脉冲的关系判断，不读取故障名称。源形态也不能单独证明感知异常的具体病因。参见[起搏报警接口](../../interfaces/alarms/ecg-monitoring.md#起搏旁路证据)。

## 除颤放电源

`DefibrillationWaveform.Create(plan, deliveredAtSimTimeNs)` 是单次有限支持的电流源，时间使用 ns，输出使用 mA，区间外返回零。`Sample` 使用调用方指定的采样间隔，保留毫秒级放电形态；不得用普通 250 Hz ECG 采样来表示放电电流细节。

阻尼正弦轮廓是 `sin(πx) exp(-1.2x)` 的 65 点归一化作者表；指数轮廓使用 64 步、每步 0.98 倍的整数衰减和线性插值。直线双相首相采用理想恒定电流，次相指数衰减。波形的衰减、峰值比、负载和能量之间没有电路校准关系，也没有仿真真实设备的阻抗补偿。

除颤不出现在波形模板目录。该源不自动执行治疗、不改变基础心律，也不把电流直接写进 ECG 微伏通道；调用方应在权威状态机真正交付电击后传入源时间。手动除颤宿主在确认实际交付后映射到仿真源时间，监护 ECG 的饱和／恢复由独立前端示意层实现，见下文。

## 电击后转复：模板响应与医疗依据的边界

[AHA 2025 成人高级生命支持](https://cpr.heart.org/en/resuscitation-science/cpr-and-ecc-guidelines/adult-advanced-life-support/)
及其[电复律流程](https://cpr.heart.org/-/media/CPR-Files/CPR-Guidelines-Files/2025-Algorithms/Algorithm-ACLS-Electrical-Cardioversion-250514.pdf)
用于区分同步电复律与非同步电击：房性／室上性快速心律及有脉单形室速通常按同步路径处理，
VF／无脉 VT 与持续多形室速使用非同步电击。该电复律流程的脚注也明确：同步发生延迟且
临床情况危急时立即使用非同步电击。因此“有有效射血”不能等同于“稳定”，更不能据此断言
非同步放电不可能转复。本项目允许已开启响应的室速在实际非同步放电后按配置转复，不自动
判断血流动力学状态，也不把这一响应规则当作治疗方式推荐。资料同时指出转复成败不只由能量决定。

本项目允许上述模板的作者指定是否响应电击，以及两类波形独立的严格能量下限。固定阈值、
确定性成功、转为固定窦律源及 200／150 J 初值都是教学场景选择，不是指南推荐剂量或疗效模型。
默认关闭此响应，避免将旧教学场景自动变成可电击转复；单纯预激、室早、逸搏、静止和 PEA
不因高能量变为可转复。双向室速示意保留可配置响应，不建模洋地黄中毒等具体病因及其处理。
实现细节与持久化边界见[电击转复接口](../../interfaces/electrical-conversion.md)。

## 电击伪迹、监护恢复与真实停顿

| 一手资料 | 可支持的结论 | 本项目边界 |
|---|---|---|
| [Perkins 等，2000，Reliability of ECG monitoring with a gel pad/paddle combination after defibrillation](https://pubmed.ncbi.nlm.nih.gov/10825621/) | 台架中使用某些胶垫／电击板组合时，输入节律仍存在，但电击后显示会暂时无信号或假性心静止；结果随电极及阻抗而变 | 不能把监护平线直接等同于心脏停搏；恢复段标记为不可判读，不能计作 QRS 或停搏证据 |
| [Poçi 等，2013，Sinus Bradycardia and Sinus Pauses Immediately after Electrical Cardioversion of Persistent Atrial Fibrillation](https://pmc.ncbi.nlm.nih.gov/articles/PMC6932396/) | 140 例成功转复的持续 AF 患者中，首分钟有 31 例出现研究定义的缓慢／停顿，16 例出现超过 2 秒的停顿；并非所有转复都停顿 | 真实停顿是场景选项，不能把该 AF 队列频率外推到 VF 或所有患者，也不随机套用一个概率 |
| [Mindray BeneHeart D3 官方说明](https://www.mindray.com/en/products-solutions/products/defibrillation-system/beneheart-d3) | 将 ECG 恢复时间单列为设备性能，示例产品标示 2.5 秒 | 支持把恢复时间作为设备配置；Generic 的 1 秒初值是作者选择，不是复刻该设备或普遍生理常数 |

正常 ECG 上的电击干扰不等于电击回路电流图：电极耦合、保护电路、放大器饱和及基线恢复都会
改变监护形态。`DefibrillationEcgArtifact` 采用归一化毫秒级放电轮廓激励一个限幅前端示意，
最大 ±6000 μV，随后以不超过 300 ms 的平方衰减尾部返回基线，再维持监护恢复段至配置时间。
此增益、衰减及恢复空窗均是作者参数，没有校准焦耳、阻抗、接触电阻或设备传递函数。
单相 10 ms、双相 8+6 ms 同样是示意相时长；不会把电流的 mA 值直接写作 ECG 的 μV。

每次实际放电均有伪迹，包括能量不足、错误模式、模板不支持或关闭转复。只有原有规则判定成功，
才在放电结束后的采集边界接入窦律；可选的逐模板 `PostShockPauseMilliseconds` 默认为 0，
启用后先生成无心脏电活动／无新增射血的源，再转窦律。呼吸保留，血管与脉搏尾部自然衰减。
该停顿与设备 `EcgRecoveryMilliseconds` 独立，允许真实心律已恢复但监护仍不可判读。
恢复后的第一搏还受现有窦律相位影响；不把恢复空窗、按压中断时间或心肌机械顿抑当作等价的
“心脏停止几秒”。本轮未模拟 CPR、再颤或患者特异性顿抑机制。

## 半自动 AED 流程

[RCUK 2025 成人 BLS 指南](https://www.resus.org.uk/professional-library/2025-resuscitation-guidelines/adult-basic-life-support-guidelines)
区分自动放电与需操作者确认的半自动 AED；电击或不建议电击后继续 CPR，按设备提示再次分析。
[RCUK 社区 CPR/AED 培训标准](https://www.resus.org.uk/library/quality-standards-cpr/quality-standards-cpr-and-aed-training-community)
描述两分钟 CPR 与复评循环，有明确生命迹象时另行处理。本项目据此连接半自动确认与
120 秒 CPR 倒计时；倒计时不产生按压血流，不代表设备自行判断恢复自主循环。

8 秒分析、最后 2 秒建议稳定、200 ms 数据新鲜度和 0.5 秒按住是项目教学交互参数，
不是指南规定或某设备认证性能。分析来自实时 ECG，独立于模板的转复响应许可。
快速宽复合波采用 >150/min 和现有采样算法 ≥100 ms 的宽度代理；AED 使用独立的速率与
新鲜度规则，不要求监护器先发出室速报警。监护器的启动宽波筛查也不再依赖初始参考标签。
该代理并非临床 QRS 测量，
也不能可靠区分所有伴差传室上速、噪声和室速，更不能单凭 ECG 判断是否有脉搏。
Generic AED 假设操作者已确认成人心搏骤停教学情景；不将这组规则包装成临床 AED 算法。
当前使用进入模式前选择的能量和设备配置，不另造自动递增能量方案；实际转复继续服从
逐模板独立配置，手动与 AED 各有独立规则。E1/F1 语音与状态机接入同一监护音频流；
CPR 节拍沿用已选试听中的 110/min、30:2 和 5 秒通气窗，属于教学交互参数，不代表按压血流模型。
