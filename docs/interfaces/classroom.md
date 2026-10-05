# 课堂与考试接口 / Classroom and exams

[接口总览](README.md) · 公开声明：[Application](api/application.md)、[Infrastructure](api/infrastructure.md)。

教师端在局域网内开放一个 TCP 监听，学生端凭地址、端口和加入码连接。仿真是确定性的，
因此不传输波形样本：教师端发送自上次从头开始以来的事件日志和仿真时钟，学生端在相同仿真
时间重放事件，得到相同的波形、测量和报警。学生端只能提交考试答案，不能改变教师的仿真。

## 帧与消息 / Framing and messages

源码：[ClassroomProtocol.cs](../../src/Monitor.Infrastructure/Classroom/ClassroomProtocol.cs)。

- 每帧为 4 字节大端长度加 UTF-8 JSON，长度上限 512 KiB；长度为零或超限在读取正文前拒绝。
- 消息按 `type` 判别：`hello`、`welcome`、`rejected`、`clock`、`event`、`examOpened`、
  `examClosed`、`submitAnswers`、`submissionReceipt`。未知类型、未声明成员、缺少必填成员
  和格式错误均拒绝。协议版本为 `ClassroomFraming.ProtocolVersion`。
- 连接后学生必须在 10 秒内发送 `hello`；协议版本、加入码（定长比较）和 1–40 字符姓名均校验，
  最多 100 名学生。拒绝原因码为 `Classroom.ProtocolMismatch`、`Classroom.WrongJoinCode`、
  `Classroom.InvalidName`、`Classroom.Full`。加入后学生只能发送 `submitAnswers`，其他消息断开连接。
- 传输未加密，只用于受信任的教室局域网；加入码每次开放课堂时随机生成 6 位数字。

## 事件与时钟 / Events and clock

`ClassroomEvent(Kind, Generation, AtSimulationTimeNs, …)` 记录一次仿真变化：

| Kind | 内容 | 学生端重放 |
|---|---|---|
| `Restart` | 完整偏好文档（显示、报警、生成器） | 恢复设置后从头开始，`Generation` 加一，时间回到 0 |
| `Apply` | 完整偏好文档 | 恢复设置后按其接续延迟应用 |
| `VitalStep` | 偏离已应用值的体征数值 | 以已应用输入重建并立即接续 |
| `Ventilation` | `VentilationTransportPlan` 与耗氧倍增器 | 更新实时氧合通气 |

偏好文档与本地偏好文件使用同一格式和校验（`DisplayPreferenceStore.Serialize` / `Deserialize`）。
`ClockMessage(Generation, SimulationTimeNs, Running)` 给出教师当前仿真时间；学生端推进到事件时间
执行事件，不超过最新时钟。教师从头开始时日志被替换，迟到的学生只重放当前代。日志最多 20000 条。
发送队列满的学生会被断开而不是跳过消息，重新加入后从日志重放。

## 考试 / Exams

源码：[Exam.cs](../../src/Monitor.Application/Classroom/Exam.cs)。

`ExamDefinition` 含标题、练习或正式、时限（0 为不限，最长 4 小时）、考试期间是否锁定手动测量，
以及 1–50 道单选或数值题。数值题在 `CorrectValue ± Tolerance`（含边界）内判对。
学生收到的 `WithoutAnswers()` 副本不含正确选项、正确值或容差。`ExamRun` 以会话仿真时间判断时限，
每名学生保留最后一次有效提交；关闭后拒绝提交。`ToCsv()` 每名学生一行，姓名加引号，开头的
`= + - @` 前加单引号以防电子表格公式执行。练习关闭时向每名学生返回其得分和答案；正式考试只返回已收到。
