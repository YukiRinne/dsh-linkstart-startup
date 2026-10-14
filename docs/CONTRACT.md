# 接口冻结 / 技术契约 — `dsh-linkstart-startup`

由 Lead 在实现开始**之前**冻结。所有实现文件必须严格遵守本文件。
如需偏离：先向 Lead 提出，由 Lead 修改本文件并通知所有成员，**不要各自悄悄偏离**。

- 行为规格（唯一权威）：`dsh-launcher/docs/SPEC.md`（本项目内副本：`docs/SPEC.md`）
- 参考实现（**行为参考，不是抄写模板**）：`dsh-launcher/Launcher.cs`、`dsh-launcher/Panels.cs`、`dsh-launcher/Account.cs`
- 参考实现里**已经修好**的坑：SPEC §5 全部条目。新实现必须**结构性地**规避，而不是"碰巧没踩到"。

---

## 0. 交付物

| 路径 | 内容 | 写入者 |
|---|---|---|
| `Launcher.cs` | 窗口 / 阶段流程 / 播放 / 几何 / 淡出 / 单实例 / 配置 / 日志 | impl-core |
| `Panels.cs` | `DshAccount` + `Panels` + `PanelLayer`（三块面板矢量绘制 + 标题渐入） | impl-panels |
| `Account.cs` | `AccountReader`（凭据读取 + Platform 接口 + 异步） | impl-account |
| `build.ps1` / `app.manifest` / `launcher.json` / `README.md` | 构建与发行 | Lead |
| `assets/` | `intro_dsh.mp4` `link-start.wav` `title.png` `dsh.ico` | Lead（已就位） |
| `out/` | 构建产物 | Lead（仅 Lead 写） |

**exe 名：`DSH LinkStart Startup.exe`**（`build.ps1` 里用变量 `$exeName`，改一处即可）。
运行时全部资源都相对于 `AppDomain.CurrentDomain.BaseDirectory`（exe 同目录）。

---

## 1. 构建环境（不可协商）

```
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
/target:winexe /platform:x64 /optimize+
/win32icon:assets\dsh.ico  /win32manifest:app.manifest
/reference:System.dll /reference:System.Core.dll /reference:System.Xaml.dll
/reference:System.Web.Extensions.dll
/reference:<fx>\WPF\WindowsBase.dll /reference:<fx>\WPF\PresentationCore.dll
/reference:<fx>\WPF\PresentationFramework.dll
Launcher.cs Panels.cs Account.cs          ← 三个文件同一次编译，顺序无关
```

不需要 SDK / NuGet / 工程文件。`app.manifest` 提供 per-monitor v2 DPI 感知。

---

## 2. C# 5 语言约束（SPEC §5.1、§6.1）

编译器是 **C# 5**。以下一律**禁止**，出现即编译失败：

| 禁止 | 替代 |
|---|---|
| `?.` / `?[` 空条件运算符 | `a == null ? fallback : a.B` |
| `nameof(...)` | 字符串字面量 |
| `$"..."` 字符串插值 | `String.Format` / `string.Format` |
| 表达式体成员 `int X => 1;` `void M() => ...;` | 完整方法体 / 属性 `get { }` |
| 自动属性初始化器 `public int X { get; set; } = 1;` | 字段 + 构造函数赋值 |
| `using static` | 全限定名 |
| `out var x`（C# 7） | 先声明 `string x; M(out x);` |
| 元组 / 解构 / `ValueTuple` | 显式 out 参数或小类 |
| `async`/`await`（可用但**本契约禁止**） | `Task.Run` + 回调（与参考实现一致） |
| 静态 `using`、局部函数、`default(T)` 字面量形式 `default` | `default(T)` |

其它 C# 5 注意点（SPEC §5.1）：

- `MediaFailed` 的事件参数类型是 `System.Windows.ExceptionRoutedEventArgs`，**不是** `ExceptionEventArgs`；其属性是 `ErrorException`。
- lambda 泛型推断失败时用显式 `delegate(object s, EventArgs e) { }`，**不要**写 `(s, e) =>` 赌推断。
- lambda 参数名不要与外层方法参数同名（CS0136）。尤其 `e`。
- 声明为 `internal`/`public` 的类型必须与 `Program` 的可见性可组合（见 §4）。

---

## 3. 冻结的公共接口

三个文件同属一个编译单元。**以下签名逐字冻结**，不得重命名、不得改可见性、不得改字段/属性形态。

### 3.1 `Panels.cs` 提供（impl-panels）

```csharp
public sealed class DshAccount
{
    public string displayName = "";        // 平台返回的名字；取不到则空
    public string locale = "ja-JP";
    public string status = "unknown";      // signedIn | signedOut | apiKey | unknown
    public string nameSource = "unavailable";
    public string detail = "";

    public static string Clean(string s, int max);
    public string Language();              // locale → "Japanese" / "Chinese" / ...
    public string Title();                 // 确认面板标题（日文）
    public string Lines();                 // 返回 string[]，长度 2
    public string Name();                  // 名字条上的文字
}

public sealed class Panels
{
    public double SourceFrame = 0;         // 源视频时间轴帧号 f = 90 + 秒数 × 60
    public DshAccount State = new DshAccount();   // 永不为 null
    public System.Windows.Media.ImageSource TitleImage;   // 可为 null

    public static double WelcomeOpacity(double f);        // SPEC §3.4 曲线
    public void Render(System.Windows.Media.DrawingContext d);   // 1920×1080 设计坐标
}

public sealed class PanelLayer : System.Windows.FrameworkElement
{
    public Panels Panels = new Panels();
    public bool Fill = false;              // true = 覆盖式裁剪；false = 完整画面
    public string StretchMode = "fit";     // "fit" | "fill" | "stretch"
}
```

`Render(d)` 必须在**设计分辨率 1920×1080 坐标系**里作画；缩放/平移由 `PanelLayer.OnRender` 负责。
`PanelLayer` 必须 `IsHitTestVisible = false`。

### 3.2 `Account.cs` 提供（impl-account）

```csharp
public static class AccountReader
{
    public static string CredentialsPath();
    public static void ReadGrant(out string token, out string issuer);
    public static void Load(DshAccount state);                        // 同步，可能慢（≤5s）
    public static void LoadAsync(DshAccount state, Action<DshAccount> done);
}
```

- 静态构造函数里**显式打开 TLS 1.2**：`ServicePointManager.SecurityProtocol |= (SecurityProtocolType)(3072 | 768 | 192);`
  （用数字字面量，老 mscorlib 可能没有 `SecurityProtocolType.Tls12` 枚举名。）
- `LoadAsync`：在后台线程（`Task.Run`）跑 `Load`，完成后调用 `done(work)`。
  **`done` 在后台线程被调用** —— 调用方负责回 UI 线程（见 §3.3 第 7 条）。`done` 可为 null。
- **绝不允许抛异常给调用方**：任何失败都写进 `state`（`status` / `nameSource` / `detail`）并返回。

### 3.3 `Launcher.cs` 提供（impl-core）

```csharp
public static class Program
{
    public static string DshExe;                                  // 由 launcher.json 的 DshPath 决定
    public static string DshProcessName { get; }                  // Path.GetFileNameWithoutExtension(DshExe)
    [STAThread] public static void Main(string[] args);
    internal static void Log(string evt, string detail);
    internal static bool TryLaunchDsh();
}
```

`internal` 的 `LauncherOptions` / `Win32` / `GeometryCache` / `StartupWindow` 形态自定，但必须满足：

1. **`StartupWindow` 构造函数接收 `string[] args`**，并且 `Loaded` 之后才 `Reveal()`（播语音）。
2. `StartupWindow.Prepare()` 公开、无参数、**方法体内不得出现任何 `return` 语句**（SPEC §5.2 / §6.4）。
3. `Application.Run()` **不传窗口参数**；`ShutdownMode = ShutdownMode.OnExplicitShutdown`；
   `app.Startup += ... window.Prepare()`；`window.Closed += ... app.Shutdown()`。
4. 隐藏窗口用 `Visibility.Hidden`，**禁止** `Opacity = 0`（SPEC §5.3）。
5. 几何确定后才 `Show()`；`Left/Top/Width/Height` 先设好再 `Show()`。
6. 面板填充与视频缩放**用同一条规则**：`VideoFit=="aspect"` → `film.Stretch=Uniform` + `PanelLayer.StretchMode="fit"`；
   `"fill"` → `UniformToFill` + `"fill"`；`"stretch"` → `Fill` + `"stretch"`；`"fit"` → `Uniform` + `"fit"`。
   同时按同样规则设置 `PanelLayer.Fill`。
7. 账号回调必须回到 UI 线程再写 `layer.Panels.State`：
   `Dispatcher.BeginInvoke((Action)delegate { layer.Panels.State = s; });`
8. 帧号定时器（`DispatcherTimer`，≈16ms）从 `film.Position.TotalSeconds` 驱动：
   `layer.Panels.SourceFrame = 90 + pos * 60; layer.InvalidateVisual();`
9. 淡出**由播放位置触发**：`pos > 0.5 && pos >= film.NaturalDuration.TimeSpan.TotalSeconds - FadeLeadSeconds`
   （`NaturalDuration.HasTimeSpan` 必须判）。**不要**另开计时器。
10. `PanelLayer` 加到 `Grid` 里、位于 `film` **之上**；关闭按钮 `✕` 在更上层（`Panel.SetZIndex(close, 100)`）。

---

## 4. 日志契约（SPEC §6.9）

- 文件：`<exe目录>\logs\launcher.log`，`File.AppendAllText`，每次一行，UTF-8。
- 行格式：`<ISO8601 本地时间> <event> <detail>`；`event` 是**第一个空格分隔的 token**。
- 必须出现的事件（验收会机械解析）：

| event | 时机 | detail 约定 |
|---|---|---|
| `begin` | `Prepare()` 一开始 | `screen=WxH ...` |
| `geometry` | 几何确定时 | 含最终 `x,y WxH` |
| `show` | `Show()` 之前 | `x,y WxH` |
| `window` | `Loaded`/`Reveal` 时 | `visible=... opacity=... topmost=...` |
| `reveal` | 开始播语音 | `x,y WxH` |
| `intro` | 视频开始播放 | `t=<秒>` |
| `film-ended` | 视频自然结束（若未提前淡出） | `t=<秒>` |
| `fade-out` | 淡出开始 | `<秒>s at t=<秒>` |
| `fade-done` | 淡出结束（随后 `Close()`） | `t=<秒>` |

- 允许的其它事件（失败/诊断，**必须**记录原因）：`dsh-launched` `dsh-missing` `dsh-launch-failed`
  `dsh-default` `geometry-cache` `already-running` `fatal` `options` `title-missing` `title-failed`
  `voice-missing` `voice-failed` `film-missing` `film-failed` `hold-failed` `fade-failed` `front-failed`
  `skip` `account` `account-ready` `credentials` `tls` `devices`
- 未列出的 event 名也可以，但不得与上表重名。
- 日志写入自身**永不抛异常**（`try { } catch { }`）。

---

## 5. `launcher.json` 契约（SPEC §6.10、§5.7）

运行时从 exe 同目录读取；**缺文件/缺键/值非法都必须回落到默认值**，绝不崩。
默认值**不得包含开发者本机的路径或坐标**。

| 键 | 类型 | 默认 | 说明 |
|---|---|---|---|
| `DshPath` | string | `""` | 空 → 走 §5.1 的通用探测 |
| `WindowBounds` | `"auto"` \| `"fixed"` | `"auto"` | `auto`：首次检测 DSH 客户区并写入 `geometry.json`，之后直接读缓存立即显示 |
| `WindowLeft` `WindowTop` `WindowWidth` `WindowHeight` | double | `0` | 仅 `fixed` 用；<200 视为无效 |
| `VideoFit` | `"aspect"`\|`"fill"`\|`"fit"`\|`"stretch"` | `"aspect"` | 见 §3.3 第 6 条 |
| `FadeLeadSeconds` | double | `0.7` | 片尾前多久开始淡出（SPEC §2.1: 16.73） |
| `FadeOutSeconds` | double | `0.7` | 淡出时长（17.43 − 16.73） |
| `MatchDshWindow` | bool | `true` | 是否对齐 DSH 窗口 |
| `MatchWaitSeconds` | double | `6.0` | 等待 DSH 窗口上限 |
| `ExtraHoldSeconds` | double | `1.2` | 片尾后额外保持 |
| `MaxHoldSeconds` | double | `25.0` | **硬上限**（SPEC §2.4）。自**窗口上屏**起算；上屏前最多再等 `MatchWaitSeconds`（窗口必在 `MatchWaitSeconds` 内上屏）。上屏后最坏正常播放 = voice 2.5 + 片长 17.43 + 淡出 0.7 = 20.63s < 25s，所以这个上限**永远不会打断一次正常播放**，只救卡死 |
| `AllowSkip` | bool | `true` | `Esc` 跳过 |

### 5.1 `DshPath` 为空时的通用探测顺序（写进 README，日志用 `dsh-default`）

1. 环境变量 `DSH_EXE`
2. `%LOCALAPPDATA%\Programs\DeepSeek Harness\DeepSeek Harness.exe`
3. `%ProgramFiles%\DeepSeek Harness\DeepSeek Harness.exe`
4. `%LOCALAPPDATA%\DeepSeek Harness\DeepSeek Harness.exe`
5. exe 同目录的 `DeepSeek Harness.exe`

全部不存在 → `DshExe` 保持空串，`TryLaunchDsh()` 记 `dsh-missing` 并返回 false；
动画照常播（SPEC §2.4 的失败路径），窗口照常关闭。

---

## 6. 诊断模式（SPEC §4.1、§5.4）

`Main` 里最先处理，**不开窗口、不启动 DSH、不占互斥锁**，各写一个文件后 `return`：

- `--check` → `logs\check.txt`：凭据路径、是否存在、`status`、`nameSource`、`displayName`、
  `Title()`、`Name()`、`Lines()[0]`、`Lines()[1]`、`detail`。
  必须**同步**调用 `AccountReader.Load`（TLS 1.2 的 bug 只有这样才暴露得出来）。
- `--bounds` → `logs\bounds.txt`：DSH 窗口客户区矩形 + 可直接粘贴的 `fixed` 配置四行 +
  16:9 覆盖框的 `x,y WxH`；找不到窗口时给出人话说明。

---

## 7. 关键几何/时序不变量（SPEC §3）

1. 几何取自 **DSH 客户区**：`GetClientRect` + `ClientToScreen`，**不是** `GetWindowRect`（差一个标题栏 ≈32px）。
2. DPI：`scale = GetSystemMetrics(SM_CXSCREEN) / SystemParameters.PrimaryScreenWidth`；
   物理像素 ÷ scale = WPF 的 DIP。
3. `aspect` 模式取**最小覆盖**的 16:9 框（不是最大内接）：
   `if (w/h < 16/9) w = h*16/9; else h = w/(16/9);` 然后以客户区中心居中。
4. 帧号映射：`f = 90 + 秒数 × 60`；`秒数 = (f − 90) / 60`。
5. 单实例：`new Mutex(true, @"Local\DeepSeekHarnessStartup", out first)`，`!first` → 记 `already-running` 后退出。
6. 失败路径**全部**通向"启动 DSH"，`MaxHoldSeconds`（25s，自窗口上屏起算）保证窗口一定关闭。
   上屏前最多等 `MatchWaitSeconds`，因此进程总寿命有界（≤ `MatchWaitSeconds` + `MaxHoldSeconds` + 淡出）。
7. `--check`/`--bounds` 不开窗口；正常情况下 DSH 在 `Prepare()` 一开始就异步启动（不等待）。
8. 淡出用 `BeginAnimation(Window.OpacityProperty, ...)`，只在 `Loaded` 之后才可靠。

---

## 8. 面板精确数据基线（SPEC §3.3–3.5）

**面板缩放必须与视频用同一条规则**（见 §3.3 第 6 条），否则美术会偏离测量位置。

帧区间（源视频时间轴，`f = 90 + 秒数×60`）：

| 面板 | 帧 |
|---|---|
| 语言选择 | 545 – 621 |
| 登录 | 625 – 713 |
| 确认 | 713 – 823 |
| 标题渐入 | 853 – 885（852 起曲线，930 交给视频） |

绝对矩形（SPEC §3.5 表格为准；表里没给的文字/描边细节以参考实现 `dsh-launcher/Panels.cs` 为行为基准）：

| 元素 | 矩形 | r | 颜色 |
|---|---|---|---|
| 语言主面板 | (334,315) 605×110 | 30 | `#0482FD` |
| 语言次面板 | (854,347) 605×110 | 30 | `#0086EE`→`#00D3D9` |
| 登录面板 | (483,361) 954×361 | 43 | `#0784B8`→`#037FB3` |
| 确认标题 | (616,219) 693×103 | 39 | `#0083B7` |
| 确认主体 | (310,347) 1306×557 | 52 | `#0083B7`→`#007FB3` |
| 名字条 | (737,627) 447×66 | — | `#00CBDE` |
| YES | (614,788) 201×53 | 26.5 | — |
| NO | (1105,788) 204×53 | 26.5 | — |

`Panel()` 绘制三件套：阴影（rect 下移 2px，`#55808080`，线宽 `edge+3`）、填充+描边 `#929697`（线宽 `edge`）、
内线（内缩 `edge/2`，`#32FFFFFF`，线宽 1.3）。文字 `#F7FAF9`；日文 `Yu Gothic`，英文 `Segoe UI Light`。

标题渐入曲线**照抄**（16 个采样点，`Frames`/`Opacity`），参考 `BootTimeline.WelcomeOpacity`：

```
Frames  = {852,853,855,858,860,863,865,868,870,873,875,878,881,883,885,942}
Opacity = {0,.035967,.074795,.115770,.165673,.244212,.325668,.400337,
           .485093,.572516,.664800,.752496,.846940,.922395,1,1}
```

`TitleImage` 在 `f < 930` 时按 `WelcomeOpacity(f)` 画在 `Rect(0,0,1920,1080)`；`f >= 930` 交给视频自带标题。

`DshAccount.Title()` / `Lines()` / `Name()` 的**日文字面量照抄参考实现**（`dsh-launcher/Panels.cs`），
不要自行改写文案；`signedIn` 时 `Title()=="ローカルアカウント"`、`Lines()` 两行见参考实现、
`Line()[1]` 提到 `DeepSeek Harness`。

> 品牌替换规则：参考实现里所有 `Codex` 字样在新实现里都不存在（它已经替换成 DeepSeek Harness）。
> 新实现里**不得出现** `Codex`。

---

## 9. 验收标准（Lead 与验证者共用）

| # | 标准 | 判定方式 |
|---|---|---|
| AC1 | 三个 .cs + 配置在**干净目录**里用 §1 的命令行一次编译通过，0 error | `build.ps1` 输出 |
| AC2 | `out\` 含 exe（winexe x64）+ `assets\`（4 个文件）+ `launcher.json` + `README.md` | 目录检查 |
| AC3 | 产物**不含** C# 6+ 语法、不含 `Codex`、不含本机路径/坐标默认值 | 源码 + 产物扫描 |
| AC4 | `--check` 写出 `logs\check.txt` 且 `status=signedIn`（有凭据时）/ `signedOut`（无凭据时），**不开窗口** | 运行 + 日志 |
| AC5 | `--bounds` 写出 `logs\bounds.txt`，含客户区矩形 + 16:9 覆盖框，**不开窗口** | 运行 |
| AC6 | 一次真实运行：`begin → dsh-launched → geometry → show → reveal → intro → fade-out → fade-done` 顺序出现，`fade-done` 后进程退出 | `logs\launcher.log` |
| AC7 | 淡出与片尾重叠：`fade-done − intro` ≈ 视频时长（17.43s）±1.2s；`fade-done − fade-out` ≈ 0.7s | 日志算术 |
| AC8 | 不变量：`Prepare()` 无 `return`；无 `Opacity = 0` 隐藏；几何用客户区；面板区间/曲线与 §8 逐值一致 | 静态审计 |
| AC9 | 失败路径：视频缺失/语音缺失/DSH 缺失时窗口仍关闭，日志有对应失败事件 | 隔离副本运行 |
| AC10 | 单实例：第二个实例记 `already-running` 后立即退出，不出现第二个窗口 | 两次启动 |

---

## 10. 自测方法（每个实现者必须做）

因为三个文件同属一次编译，某个文件单独写完后可以在临时目录里**对着参考实现**编译自测：

```powershell
$fx  = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
$tmp = "$env:TEMP\linkstart-<你的名字>"
New-Item -ItemType Directory -Force $tmp | Out-Null
# 例：自测 Launcher.cs → 新 Launcher.cs + 参考 Panels.cs + 参考 Account.cs
& "$fx\csc.exe" /nologo /target:winexe /platform:x64 /out:"$tmp\t.exe" `
   /reference:System.dll /reference:System.Core.dll /reference:System.Xaml.dll `
   /reference:System.Web.Extensions.dll `
   /reference:"$fx\WPF\WindowsBase.dll" /reference:"$fx\WPF\PresentationCore.dll" `
   /reference:"$fx\WPF\PresentationFramework.dll" `
   "E:\Harness&Agent Project\DSH SAO\dsh-linkstart-startup\Launcher.cs" `
   "E:\Harness&Agent Project\DSH SAO\dsh-launcher\Panels.cs" `
   "E:\Harness&Agent Project\DSH SAO\dsh-launcher\Account.cs"
```

要求：**0 error、0 warning 视为合格**（warning 也要报告，说明为什么可以接受）。
不要写 `dsh-linkstart-startup/out/`（那是 Lead 的），临时产物一律放 `%TEMP%`。
