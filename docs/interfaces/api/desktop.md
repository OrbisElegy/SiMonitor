# Monitor.Desktop 公开声明索引

[接口总览](../README.md)

本页按源码路径列出当前工作区中对程序集外可见的 C# `public` 类型和显式声明的公开成员，保留参数、默认值、单位命名、泛型约束及枚举值。接口中省略 `public` 的成员也包括在内。方法体、属性实现与非 const 字段初始化值已省略；省略实现的声明用于查阅，不能直接当作可编译代码。

位置 record 的参数也定义其同名属性；编译器合成的构造器、相等性方法以及继承成员不重复展开。`internal` 类型即使有 `public` 成员也不属于本索引。按开发构建的预处理分支读取；桌面产品构建差异见[桌面与命令行入口](../desktop-tools.md)。行为约束和集成顺序见总览中的分模块文档。

## MainWindow.cs

源码：[MainWindow.cs](../../../src/Monitor.Desktop/MainWindow.cs) · 命名空间：`Monitor.Desktop`

```csharp
public sealed class MainWindow : Window
{
    public MainWindow();
    public CapturedRecordSvgPublication? CurrentPublication { get; private set; }
    public void ApplyPublication(CapturedRecordSvgPublication publication, RecordStudyControl? readyContent = null);
}
```

## MonitorApp.cs

源码：[MonitorApp.cs](../../../src/Monitor.Desktop/MonitorApp.cs) · 命名空间：`Monitor.Desktop`

```csharp
public sealed class MonitorApp : Avalonia.Application
{
    public override void Initialize();
    public override void OnFrameworkInitializationCompleted();
}
```

## NativeDragInput.cs

源码：[NativeDragInput.cs](../../../src/Monitor.Desktop/NativeDragInput.cs) · 命名空间：`Monitor.Desktop`

```csharp
public sealed class NativeDragInput : IDisposable
{
    public NativeDragInput(RecordStudyPresenter presenter, Func<RecordStudyCommandContext> currentContext, double radius, Action<NativeStudyDrag> interrupted);
    public void Dispose();
}
```

## NativeStudyDrag.cs

源码：[NativeStudyDrag.cs](../../../src/Monitor.Desktop/NativeStudyDrag.cs) · 命名空间：`Monitor.Desktop`

```csharp
public sealed class NativeStudyDrag
{
    public bool IsFinished { get; private set; }
    public void Preview(RecordStudyCommandContext context, Point windowPoint, Point currentPageOrigin);
    public void Commit(RecordStudyCommandContext context, Point windowPoint, Point currentPageOrigin);
    public void Cancel(RecordStudyCommandContext context);
    public void ReleaseWithoutRollback();
}
```

## RecordStudyControl.cs

源码：[RecordStudyControl.cs](../../../src/Monitor.Desktop/RecordStudyControl.cs) · 命名空间：`Monitor.Desktop`

```csharp
public sealed class RecordStudyControl : Control
{
    public RecordStudyControl(CapturedRecordSvgPublication publication, EcgPaperGridSvgStyle gridStyle, EcgManualCursorSvgStyle cursorStyle);
    public CapturedRecordSvgInputSession InputSession { get; }
    public RecordCursorHits HoveredCursors { get; private set; }
    public override void Render(DrawingContext context);
}
```

## RecordStudyPresenter.cs

源码：[RecordStudyPresenter.cs](../../../src/Monitor.Desktop/RecordStudyPresenter.cs) · 命名空间：`Monitor.Desktop`

```csharp
public sealed record RecordStudyCommandContext(bool CanPreserveGlobalSafetyOverlay, CapturedRecordSvgLayout Layout, RecordScreenZoomLayout Screen, EcgPaperGridSvgStyle GridStyle, EcgManualCursorSvgStyle CursorStyle, bool AllowAuxiliaryRate)
{
}
public sealed class RecordStudyPresenter
{
    public NativeStudyDrag? ActiveDrag { get; private set; }
    public RecordStudyPresenter(MainWindow window, CapturedRecordSvgPresentation presentation);
    public void Refresh(bool canPreserveGlobalSafetyOverlay, CapturedRecordSvgLayout layout, RecordScreenZoomLayout screen, EcgPaperGridSvgStyle gridStyle, EcgManualCursorSvgStyle cursorStyle, bool allowAuxiliaryRate, CancellationToken cancellationToken = default);
    public void Withdraw();
    public void BindClearButton(Func<RecordStudyCommandContext> currentContext);
    public void UnbindClearButton();
    public void BindPointerQueries(Func<RecordStudyCommandContext> currentContext, double radius);
    public void UnbindPointerQueries();
    public void ClearPair(CapturedRecordSvgInputSession expectedInput, bool canPreserveGlobalSafetyOverlay, CapturedRecordSvgLayout layout, RecordScreenZoomLayout screen, EcgPaperGridSvgStyle gridStyle, EcgManualCursorSvgStyle cursorStyle, bool allowAuxiliaryRate);
    public RecordCursorHits HitTest(CapturedRecordSvgInputSession expectedInput, bool canPreserveGlobalSafetyOverlay, CapturedRecordSvgLayout layout, RecordScreenZoomLayout screen, Point windowPoint, Point currentPageOrigin, double radius);
    public NativeStudyDrag BeginDrag(CapturedRecordSvgInputSession expectedInput, RecordStudyCommandContext context, Point windowPoint, Point currentPageOrigin, double radius);
}
```
