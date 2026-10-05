# 本地化接口 / Localization API

[接口总览](README.md)

提供可独立调用的 i18n 接口、英文与简体中文资源，以及产品桌面界面的完整接入。
“通用”位于设置分类首项，仅呈现立即生效的应用偏好，不显示仿真参数的应用操作。
在“设置 → 通用 → 语言”选择 `English` 或 `简体中文`，立即生效并保存。
产品窗口的导航、页面标题、全部设置分类与参数面板、波形模板名称与分组、形态摘要、
监护画面、报警提示横幅、十二导联纸图与手动测量、帮助和关于页均随语言切换。
开发入口（`WaveformDemoWindow`、`MainWindow` 及其专用控件）只面向开发核验，保留原文。
设置导航只把点分格式的标题（如 `settings.alarms`）当作资源 key 查询，其余标题按原文显示，
不会出现缺失标记。

## 显示文本与稳定标识 / Display text and identities

- 波形模板与分组的中文名称是持久化偏好和分组比较使用的稳定标识，不随语言变化。
  显示名称由模板序号或分组位置得到的 key 提供（`ecgTemplate.t000`、`ecgGroup.g00`、
  `respirationTemplate.t0`、`ejectionTemplate.t0` 等）；中文译文必须与标识一致。
  这些 key 在运行时构造，静态 key 检查看不到，由桌面本地化检查逐项核对。
- `Monitor.Application.Localization.TextMessage` 携带目录 key 和参数（字面文本或嵌套消息），
  由 `Render(ITextLocalizer)` 按读者语言生成文本。监护提示（`MonitorNotice.Message`）和
  测量顶部提示（`MeasurementDisplay.TopNoticeMessage`）在保留中文默认文本的同时附带消息；
  消息不参与行为判断，按结构比较相等。
- 校验错误、字段名和原因说明在桌面层以 key 保存，状态文本在语言切换时重新生成；
  领域与仿真异常仍使用 `ReasonCode`，由桌面层映射到 key。
- 帮助主题按语言分别存放：`help-topics.json` 为中文参考集，`help-topics.en.json` 为英文；
  每种语言须列出相同 id 且顺序一致，分类一一对应，切换语言时帮助页保留分类筛选与搜索词。

## 桌面语言生命周期 / Desktop lifecycle

- 每个 `DesignPreviewWindow` 拥有独立的 `DesktopLocalization`，内部调用
  `ITextLocalizer`。显示属性绑定至翻译值，切换时发布属性变更；可访问性名称同步刷新。
  注册表弱引用翻译值，避免长期持有已替换的设置页、对话框或动态行控件。
  下拉选项使用可通知的翻译值数据项和绑定模板，展开列表与收起后的选中项同步刷新；
  不通过清空或重设选择来刷新，不触发参数选择事件。
- 语言切换不重建窗口、设置编辑器、仿真会话、显示行、定时器或待生效源，也不应用
  参数、不清空历史、不改变输入解析 culture；宽窄导航共用同一语言。
- `LanguagePreferenceStore` 独立于 `DisplayPreferenceStore`。路径由现有显示配置
  路径替换扩展名为 `.language.json` 得到；默认即本地应用数据目录下的
  `Monitor/display-preferences.language.json`。旧的显示配置版本与内容不变。
  无持久化路径的预览窗口只改变本窗口语言。
- 文件格式为 `{"Version":1,"Locale":"en"}`，语言只能是规范码 `en` 或 `zh-CN`。
  必需字段、版本、未知字段、语言和 1024 字节上限均校验。缺失文件安静默认中文；
  无效文件保留原内容，默认中文并在侧栏提示。选择另一语言后原子保存；保存失败
  保留本次语言，侧栏提示检查目录写入权限。不会保存或覆盖未应用的参数编辑。
- “恢复全部默认设置”确认内容包含语言；确认后恢复中文并保存。语言保存与显示配置
  保存分别报告失败，不声明两份文件具有跨文件事务一致性。

## 接入 / Integration

`Monitor.Application.Localization.ITextLocalizer` 是显示层使用的端口；
`Monitor.Infrastructure.Localization` 提供 JSON 目录和内嵌资源实现。
Domain、Simulation 不引用本地化；协议字段、枚举值、预设标识、日志 ReasonCode、
数值解析及持久化格式不随语言变化。资源按稳定语义 key 查询，不按中文原文查询。

```csharp
using Monitor.Application.Localization;
using Monitor.Infrastructure.Localization;

ITextLocalizer text = BuiltInLocalizations.Create("cn");
string apply = text.GetString("common.apply");               // 应用
string range = text.Format("validation.integerRange", 1, 5); // 请输入 1–5 的整数。
LocalizedText result = text.Resolve("common.apply");
// result.Value, result.RequestedLocale, result.ResolvedLocale,
// result.IsFallback, result.IsMissing
```

实例语言固定，资源不可变，可以在不同视图／线程并存。切换语言时由宿主创建新实例，
在 UI 线程重新绑定或刷新文案；接口不会发送全局事件或改变 `CurrentCulture`／
`CurrentUICulture`，也不会重置仿真。`Supported` 返回只读语言列表，自称名称为
`English`、`简体中文`。未来 UI 应保存规范语言码，而不是列表序号或翻译后的名称。

| 输入 | 规范结果 |
|---|---|
| 未传、null、空白 | `zh-CN`，保留当前产品的中文默认方向 |
| `cn`、`zh`、`zh-CN`、`zh-Hans`、`zh-Hans-*`，忽略大小写及前后空白 | `zh-CN` |
| `en`、英语地区变体 | `en` |
| 其他输入，包括尚未支持的繁体中文 | `en`，不宣称提供对应语言 |

`ResolveLocale` 只返回本轮支持的两个语言码。`Create` 返回的 `Locale` 和
`Resolve` 的 `RequestedLocale` 均指选择后的规范语言码，不保留原始输入。

## 资源与失败语义 / Catalog and failure contract

- 内嵌资源位于 `src/Monitor.Infrastructure/Localization/Resources/{en,zh-CN}.json`。
  发布时随程序集携带，运行时不依赖工作目录，也不自动扫描或下载外部语言包。
- JSON 是扁平的 `key: text` 对象，key 区分大小写。禁止重复 key、空白 key、
  key 首尾空白、非字符串值和空白译文。JSON 语法错误抛 `JsonException`；
  条目、格式或目录不一致抛 `FormatException`。
- `TranslationCatalog.Parse(locale, json)` 完整验证后生成不可变目录，使用 .NET
  `CultureInfo` 规范语言码；无效 culture 抛 `CultureNotFoundException`。
  `cn` 是内置选择入口的兼容别名，直接创建目录应使用 `zh-CN`。
- 英文目录定义 key 和参数契约。`ValidateAgainst(fallback)` 拒绝未知 key、
  丢失／增加的参数和更改的格式说明符；允许译文重排或重复参数。
  `requireComplete: true` 另外要求全部 key 齐全。内置中文加载时强制完整校验。
- 格式使用 .NET composite format：`{0}`、`{1}`、`{0:0.0}`，字面花括号写成
  `{{`、`}}`。参数索引必须从 0 连续排列，最多 32 个；相同参数的格式说明符
  也必须与英文一致。占位符所在顺序、重复次数和对齐宽度可以随译文调整。
- `Resolve` 返回原始模板与来源信息，不进行格式化。`GetString` 等价于零参数
  `Format`；有参数的模板必须调用 `Format`。已知 key 的实参数量必须精确匹配，
  不足或多余均抛 `FormatException`，不静默吞掉调用错误。参数类型须满足模板的
  .NET 格式约定；格式器异常直接向上传播。
- 缺失译文回退至传入的 fallback 目录；两边都没有则返回 `[[key]]`，
  `IsMissing = true`、`ResolvedLocale = null`，不抛缺失 key 异常。
  此时 `Format` 同样返回标记，忽略其非 null 参数数组；空白 key 或 null 参数数组
  仍会拒绝。部分翻译的回退可通过 `IsFallback` 识别。
- `Format` 显式使用所选目录的 culture，即使模板来自英文回退。它只格式化显示
  字符串；物理量先由调用方按既有倍率换算，输入与存储继续使用既有确定性规则。

## 翻译协作 / Translation contributions

1. 开发者先在 `en.json` 定义稳定的点分 key，如 `settings.saveFailed`。
   每个 key 表达完整消息，不拼接语序片段；改变参数含义时同步检查所有调用方。
2. 同一改动更新 `zh-CN.json`，保留 key、参数索引及格式说明符；允许调整语序。
   参数契约：`settings.saveFailed` 的 `{0}` 是失败原因；`validation.integerRange`
   的 `{0}`／`{1}` 是包含边界的最小／最大整数；`sound.volumePercent` 的 `{0}`
   是 0–100 百分比；`app.developmentVersion` 的 `{0}` 是版本标签；`settings.templateSelected`
   的 `{0}` 是模板显示名称；`settings.pageDefaultsRestored` 的 `{0}` 是参数分组名称。
3. 使用现有 Release 构建和规格入口验证。`LocalizationSpecifications` 在加载内嵌
   中文时验证所有 key 和占位符，不仅验证示例中引用的 key。
4. 确定需要新语言后，再加入对应资源、自称名称和明确的语言选择映射，并加入完整性
   验证。无需修改 `ITextLocalizer`，无需添加新的仿真分支。本轮只发行 `en`、`zh-CN`。

English contribution contract: translate values only; preserve stable keys, argument indices
and format specifiers. English defines the complete catalog. Simplified Chinese must have
the same keys and argument signatures. Reordering or repeating placeholders is allowed;
literal braces use `{{` and `}}`. Do not translate protocol IDs or persist display text as
identity. Add other languages only after demand is confirmed. The desktop shell and display
settings use the immutable localizer through property bindings. Language changes preserve
simulation state and unapplied edits; the language preference is saved separately.
All product panels, template display names and help topics are translated; Chinese template
names remain persisted identities, and development-only windows keep their original text.
