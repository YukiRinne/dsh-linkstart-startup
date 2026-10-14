# 独立静态一致性验证报告 — `dsh-linkstart-startup`

验证者：**verify-static**（独立验证者 A，task-4）
验证类型：**静态一致性**（不运行启动器、不改任何实现文件）
判定范围：CONTRACT §9 的 **AC1 / AC2 / AC3 / AC8**，以及 **AC10 的静态部分**
不在本报告范围：AC4 / AC5 / AC6 / AC7 / AC9 与 AC10 的运行期确认 → 由 `verify-runtime` 出证据

> **结论：AC1 / AC2 / AC3 / AC8 / AC10（静态）全部 PASS，第三版（最终修订）源码侧无缺陷。**
> 另发现 **1 项交付物缺陷 F-1（HIGH，Lead 范围）**：`dsh-linkstart-startup\out\` 里的 **exe 是 rev2 时代的二进制**
> （不含 DEF-1 修复）、`out\README.md` 是 rev1 版本 —— 见 §7.1，已用二进制字符串取证。
> 代码侧遗留：**D-1（LOW-MEDIUM，配置相关）**、D-2（LOW）、D-3/D-4（INFO）。

---

## 1. 被验证的修订（第三版·最终冻结）

Lead 给出的 SHA256 前 16 位与实测**逐文件一致**；本报告所有结论仅对这组字节成立：

| 文件 | SHA256（前 16） | 字节 | 行 |
|---|---|---|---|
| `Launcher.cs` | `43911C0EE00AFAFD` | 56059 | 1467¹ |
| `Panels.cs` | `51289E15348D36E7` | 16001 | 401 |
| `Account.cs` | `8803E8BEBA5C144C` | 13314 | 340 |
| `build.ps1` | `3DBE31DF62AF1B8E` | 5072 | 109 |
| `launcher.json` | `CB66EDB0D2C5361E` | 807 | 19 |
| `app.manifest` | `E7A0E5A07F289A71` | 969 | 20 |
| `README.md` | `4AA109F9939185E7` | 12661 | 303 |

¹ Lead 通告为 1468 行；差异来自行尾计数口径（文件以换行结尾，`Get-Content` 不计最后的空行；按 `\n` 计数得 1468）。本报告行号引用已逐条对文件复核。

被验证副本：`_verify\conformance\build\`（7 个输入与上表**逐字节相同**，SHA256 校验通过）。所有 `out\` 结论来自**这份副本的构建**，未写 Lead 的 `out\`。
前两版（rev1 `BF3D1FFCEF724513`、rev2 `12DB572D99B82078`）的结论均已作废；第三版**全部扫描器重跑**。

---

## 2. 验证方法与可信度

### 2.1 工具（`_verify\conformance\`）

| 工具 | 覆盖 |
|---|---|
| `scan_cs5.py` | C# 6+ 语法 17 条规则（`?.` `?[` `nameof(` `$"` 表达式体成员、自动属性初始化器、`using static`、`out var`、元组、`default` 字面量、模式匹配、局部函数、`async/await` …） |
| `scan_spec_data.py` | **从 `SPEC.md` 机械解析** §3.3 帧区间 / §3.4 16 点曲线 / §3.5 八矩形·半径·颜色，与 `Panels.cs` 逐值比对（+ Panel() 三件套、字体、交接帧、日文字面量与参考实现集合比对） |
| `scan_contract.py` | CONTRACT 不变量（`Prepare()` 无 `return`、`Visibility.Hidden`、客户区几何、Mutex、DPI、最小覆盖、`Run()` 形态、帧号公式、位置触发淡出、UI 线程回送、Z 序、日志白名单、配置默认值、`Codex`、22 条 frozen 签名、TLS）＋ **rev3 新增 CT-S1…CT-S10（延迟 `Show()` 路径）** |
| `check_compile.py` | AC1：全新干净目录 + CONTRACT §1 原始 csc 命令行，编译器原文落盘 |
| `check_out.py` | AC2/AC3：`out\` 内容、PE 头、assets SHA256、发行配置 |
| `run_all.py` | 按 AC 汇总 + 记录被验证源码 SHA256 |
| `selftest.py` | 反向验证：反例夹具 + 对照组 + 真实 csc 地面真值 |

### 2.2 反向验证（SPEC §5.5「校验脚本自己会骗人」）

`selftest-report.txt`：**81 项断言全部通过**（rev3 新增 9 项 CT-S 反例断言）。

* **反例命中**：`bad_cs6.cs` 全部禁用语法 → 13 条规则报 FAIL；`dir_bad/` 的 SPEC 偏差与契约缺陷全部命中；
  **新增 `gen/show_bad/`** 延迟 `Show()` 反例 → CT-S1/S2/S3/S4/S5/S6/S7/S8/S10 全部报 FAIL。
* **对照组不误报**：合法 C# 5 夹具 → 0 FAIL 且 csc 编译 0 error/0 warning；已知正确的 `dsh-launcher/Panels.cs`
  → 数据类 0 FAIL；`show_bad` 夹具里刻意保留的 `Visibility.Hidden` → **CT-S9 仍 PASS**（证明不是"一律报错"）。
* **地面真值**：13 条语法规则各生成一个最小文件交 `csc.exe v4.0.30319` 编译 → **13/13 被真实编译器拒绝，报错行与扫描器标记一致（±1 行）**；
  1 条政策规则（`async/await`）单独标注：**它是合法 C# 5**，只有 CONTRACT §2 禁止。

### 2.3 本轮（rev3）自检又抓出 3 个扫描器自身缺陷 —— 已修，留证

1. **CT-S1 规则写错了不变量**：原规则要求 `Show()` 字面出现在 `RequestShow()` 体内；实际设计是
   `RequestShow()` 的 1ms tick 调 `ShowWindow()`，`Show()` 在 `ShowWindow()` 里。**这是我的规则错**（会对实现误报 FAIL），
   已改为"全文仅 1 处直接 `Show()`，位于 `ShowWindow()`，且 `ShowWindow()` 的唯一调用点在 `RequestShow()` 内"。
2. **CT-S6 曾"因为错误的原因 PASS"**：原先用"体内任意 `if (!x) x = true`"识别一次性闩锁，结果匹到
   `if (!Topmost) Topmost = true`。已改为**结构性判定**：取守卫 `front-failed` 日志的那个 `if` 条件，
   要求日志位于同一 `{}` 块内、且条件中被取反的标识符在同块内被置 true。现在真实实现报
   `latch='visibilityMismatchLogged'`，而夹具里的诱饵写法报 FAIL。
3. **又一次字面量掩码陷阱**：`body.find("front-failed")` 在"字面量已清空"的文本上永远返回 -1，导致 CT-S6 对真实实现**误报 FAIL**；
   已改为在 `nocomment` 坐标定位再换算偏移。

> 历史：rev1/rev2 期间自检已抓出并修复 **7 个**扫描器缺陷（最危险的是在字面量清空文本里匹配含字符串常量的模式，
> 会对真实实现产生 13 处假 FAIL；以及 `check_compile.py` 漏写 `/reference:` 前缀导致 6 条属于扫描器自己的编译错误）。
> 累计 **10 个**自身缺陷被反例测试挡住。工具局限见 §8。

---

## 3. 第三版新增代码的专项复核（Lead 指定 5 项）

| # | 复核项 | 结论 | 证据 |
|---|---|---|---|
| 1 | `Prepare()` 体内仍 0 个 `return` | **PASS** | `Launcher.cs:882` `public void Prepare()`；body 883–928（1792 字符），**0 个 `return` token**（CT-01b） |
| 2 | 直接 `ShowWindow()` 调用只剩 `RequestShow()` 里 1 处 | **PASS** | 全文直接 `Show()` **1 处**（`:1094`，在 `ShowWindow()` 内）；`ShowWindow()` 调用点 **1 处**（`:1080`，在 `RequestShow()` 的 tick 内）；`Prepare()` 内 0 处直接 `Show()`/`ShowWindow()`（CT-S1/CT-S3） |
| 3 | `RequestShow()` 有 `showRequested \|\| shown \|\| closed` 守卫（幂等） | **PASS** | `:1072`（守卫）、`:1073`（置位）、`:1074–1075`（一次性 1ms `DispatcherTimer`）、`:1078` 停表、`:1079` 置 null、`:1080` `ShowWindow()`；三守卫名与 1ms 间隔全部实测命中（CT-S2） |
| 4 | 新诊断块**非侵入** | **PASS** | `StartFrontGuard()`（`:1112–1145`）块内**无** `Hide(` / `.Show(` / `Visibility =` / `Close(` / `RequestShow(`（CT-S5）；整段诊断在 `try{…}catch{}` 内（`:1132–1142`）；只读 `WindowInteropHelper.Handle` + 一次 `Program.Log`（`:1139`），无 `return`/`throw`/状态改写，不改控制流；`front-failed` ∈ CONTRACT §4 白名单，**事件总数仍为 29 个且全部在白名单内**（CT-L-unknown）；仅记录一次（闩锁 `visibilityMismatchLogged`，`:1135/1138`，CT-S6） |
| 5 | 新代码同样受 C# 5 约束 | **PASS** | `scan_cs5.py` 三个 .cs **51 项 PASS / 0 FAIL**；`?.`/`?[`/`nameof(`/`$"`/表达式体成员/自动属性初始化器/`out var`/元组/`default` 字面量/模式匹配/局部函数**全部 0 命中**；干净目录编译 0 error / 0 warning（AC1） |

**关于 `var` 的更正**：Lead 提示"别放过 `var`"。核实结果：**`var` 是 C# 3 起的合法语法，不属于本契约禁用项**
（CONTRACT §2 禁的是 **`out var`**，C# 7 的解构式声明）。第三版新代码里既无 `out var` 也无 `?.`；
扫描器按契约语义只报 `out var`，不把合法的 `var` 误判为违规 —— 已由对照组夹具（含合法 `var` → 0 FAIL）与 csc 编译通过双重确认。

补充（非 Lead 要求，但属新增代码的风险面）：
* `RequestShow()` 的 4 个调用点与 Lead 声明一致：`Prepare()` 内 2 处（`:905` fixed、`:916` 缓存）、探测 tick 1 处（`:1021`）、
  `UseScreenGeometry()` 1 处（`:1049`）（CT-S4）。
* 延迟 `Show()` 不会让进程提前退出：`ShutdownMode.OnExplicitShutdown`（`:99`）+ `Closed→app.Shutdown()`（`:102`）（CT-S10）；
  若仍是 `OnLastWindowClose`，1ms 延迟窗口期会让 `Run()` 立即结束。
* `OnClosed()`（`:1428–1447`）已停 `showTimer`（`:1445–1446`），不存在"窗口已关、1ms 后仍去 Show"的窗口（CT-S8）。
* `Visibility.Hidden` 按 SPEC §6.3 字面保留（`:823`）（CT-S9）。
* `Win32.IsReallyVisible()` 语义正确：`(GetWindowLong(hWnd, GWL_STYLE) & WS_VISIBLE) != 0`
  （`GWL_STYLE=-16` `:582`、`WS_VISIBLE=0x10000000` `:583`、`GetWindowLong` `:600`、方法 `:606–608`）；
  `GWL_STYLE` 用 32 位 `GetWindowLong` 取值正确，无需 `GetWindowLongPtr`（CT-S7）。

---

## 4. 逐条验收结论（第三版）

### AC1 — 干净目录 + §1 命令行一次编译通过，0 error → **PASS**

* 干净目录 `%TEMP%\linkstart-verify-compile` 只含三个 .cs + `app.manifest` + `assets\`；命令为 CONTRACT §1 **逐字原样**（未用 build.ps1）。
* `evidence/compile-clean-raw.txt`：**exit code 0**，输出仅版本横幅与 C# 5 提示 —— `0 error(s), 0 warning(s)`（CMP-03/04）。
* 产出 `Launcher.exe` 115200 B（CMP-05）；`build.ps1` 对同一组源文件同样 0 error/0 warning，产出
  `DSH LinkStart Startup.exe` 115200 B（`CMP-06`：`$exeName` 单点定义，`/out:` 用其派生变量）。

### AC2 — `out\` 含 exe（winexe x64）+ `assets\`（4 文件）+ `launcher.json` + `README.md` → **PASS（以我自己的副本构建为准）**

在 `_verify\conformance\build\`（与冻结修订逐字节相同）运行 `build.ps1`（**不带 `-DshPath`**，exit 0）：

| 检查 | 结果 |
|---|---|
| exe | `DSH LinkStart Startup.exe` **115200 B** ✅ |
| PE machine / subsystem | `0x8664` **x64** / `2` **Windows GUI（winexe）** ✅ |
| `assets\` | 4 个文件齐全，与项目源 **SHA256 全部一致** ✅ |
| `launcher.json` | 14 个键 = CONTRACT §5 默认值，无本机路径、`WindowBounds=auto` ✅ |
| `README.md` | **12661 B**（= 冻结修订）✅ |
| 冗余产物 | 无 `.pdb`/`.cs`/`.xml` ✅ |

⚠️ Lead 目录下的 `out\` **不满足本修订**（exe 与 README 均为旧版本）→ **F-1（§7.1）**。

### AC3 — 不含 C# 6+ 语法、不含 `Codex`、默认值不含本机路径/坐标 → **PASS**

* C# 6+：51 项 PASS / 0 FAIL（见 §3 第 5 项）。
* `Codex`：7 个文件（三个 .cs + `launcher.json` + `build.ps1` + `app.manifest` + `README.md`）**0 次**，含注释与字符串原文级扫描。
* 默认值：`launcher.json` 14 键全部等于 CONTRACT §5；`CT-C-local`/`CT-C-fixed` PASS；副本构建出的 `out\launcher.json` 同样干净。
* 与 Lead 第 5 条边界一致：**AC3 以仓库默认值为准**；`-DshPath` 是用户显式注入到产物副本的行为，不是默认值（我的构建刻意不带）。
  注：带 `-DshPath` 构建时 `check_out.py OUT-08` 会把注入路径报 FAIL —— 这是工具既定语义，非缺陷。

### AC8 — 不变量静态审计 → **PASS**（`scan_contract` 106 PASS / 0 FAIL / 0 WARN / 0 SKIP；`scan_spec_data` 48 PASS）

| 不变量 | 证据（`文件:行`，第三版行号） |
|---|---|
| `Prepare()` 公开无参、**0 个 `return`** | `:882`（body 883–928，0 token） |
| `Prepare()` 先做全部初始化再分支 | `:884` `begin` → `:890` `TryLaunchDsh()` → `:892/893/894/895` 标题/账号/帧钟/看门狗 |
| 延迟显示（DEF-1 修复） | `RequestShow()` `:1070`、守卫 `:1072`、1ms 定时器 `:1074–1075`、tick `:1076–1081`、`ShowWindow()` `:1080`；`ShowWindow()` `:1088`、唯一 `Show()` `:1094` |
| 隐藏窗口用 `Visibility.Hidden`，**无 `Opacity = 0`** | `:823`；全文件 0 处 `Opacity = 0` |
| 淡出用 `BeginAnimation(Window.OpacityProperty, …)` | `:1403` |
| 几何取**客户区**：`GetClientRect` + `ClientToScreen`；**无 `GetWindowRect`** | `:591`；`GetWindowRect` 全文 **0 次** |
| DPI：`GetSystemMetrics(SM_CXSCREEN) / SystemParameters.PrimaryScreenWidth` | `:581`（另有 `:619`） |
| `aspect` = **最小覆盖** 16:9 | `:739`（`Aspect = 1920.0/1080.0`）、`:749`（`if (width / height < Aspect) width = height * Aspect;`）、`:750` |
| `Run()` 不传窗口参数 / `ShutdownMode` / 事件接线 | `:107` / `:99` / `:103`（Startup）/ `:102`（Closed） |
| 帧号 `f = 90 + 秒数×60`，≈16ms | `:1321`；`frameTick` 16ms `:971` |
| 淡出**由播放位置触发**且判 `HasTimeSpan` | `:1327–1329`（`position > 0.5`、`>= TotalSeconds - FadeLeadSeconds`） |
| 账号回调**回 UI 线程** | `:961` |
| Z 序：layer 在 film 上、`✕` 最上 | `:860`（`SetZIndex(close,100)`）、`:864/866/867`（Children.Add 顺序）、`:865`（`SetZIndex(layer,50)`） |
| 面板与视频**同一条缩放规则** | `:827–832`（aspect→Uniform+"fit"；fill→UniformToFill+"fill"；stretch→Fill+"stretch"；fit→Uniform+"fit"） |
| `MaxHoldSeconds`；进程名由 DshPath 推导 | `:273`；`:64–71/68`（`Path.GetFileNameWithoutExtension`）；`:805` 的 `"DeepSeek Harness"` 是窗口标题、`:539` 的是 §5.1 探测路径 5，均非进程名 |
| `--check`/`--bounds` 在 Mutex/窗口之前 | `:82/:83`（Mutex `:89`、`Application` `:96` 之后才有） |
| 日志：9 个必需事件齐全 | `begin:884` `geometry:904/915/1009/1048` `show:1093` `window:1182` `reveal:1184` `intro:1236` `film-ended:1268` `fade-out:1391/1404` `fade-done:1374/1392` |
| 事件名不越白名单 | **29 个**不同事件全部 ∈ CONTRACT §4（新增诊断复用 `front-failed`，总数与 rev2 相同） |
| `MediaFailed` 用 `ExceptionRoutedEventArgs` | `:838` 订阅 / `:1257` 处理 |
| TLS 数字形式、无 `Tls12` 枚举名 | `Account.cs:45` |
| Account 冻结签名 4/4 + 接口细节 | `Account.cs:57/83/158/282`；`credentials.yaml:69`、`deepseek-account-platform/default:35`、`/auth-api/v0/users/current:223`、`x-dsh-auth-token:233`、`biz_data:265` |
| Panels 冻结签名 **22/22**、`IsHitTestVisible=false`、`Fill` 默认 false | `Panels.cs:21–25/27/41/56/64/80/90/91/92/321/339/365/368/372` |
| SPEC §3.5 八矩形/半径/颜色逐值一致 | `Panels.cs:182/193/257/282/283/301/307/308` |
| SPEC §3.4 曲线 16+16 值逐位一致 | `Panels.cs:317–318` |
| 帧区间 545–621 / 625–713 / 713–823（核对到**正确函数**） | `Panels.cs:174/252/277` |
| 交接帧 930、`WelcomeOpacity`、`Rect(0,0,1920,1080)` | `Panels.cs:337/321/349` |
| Panel() 三件套、字体、`#F7FAF9` | `Panels.cs:125/126/128/131/132/133`；`Yu Gothic`/`Segoe UI Light` 命中 |
| 日文字面量 13 条与参考实现集合一致 | `SD-JP-TEXT` PASS |

### AC10（静态部分）— 单实例 → **PASS（静态）**

* `Launcher.cs:89`：`new Mutex(true, @"Local\DeepSeekHarnessStartup", out first)`，名称逐字符合 CONTRACT §7.5。
* `:90–93`：`!first` → `Log("already-running", …)`（`:92`）→ `return`，位于创建任何窗口之前（`Application` `:96`、窗口 `:101`）。
* 运行期"两次启动"确认属 `verify-runtime`。

---

## 5. 三处申报偏离的独立判定（对第三版重判）

### D1 全局看门狗 → **可接受**（方向正确，但留一处配置相关破口 D-1）

SPEC §2.4/§6.8 要求 25 秒硬上限，看门狗是其实现手段；`hold-failed` ∈ 白名单。
第三版仍为"自窗口上屏起算"（`:1350` `origin = shownAt >= 0 ? shownAt : 0.0`），
上屏后最坏健康路径 = 语音 2.5s + 片长 17.43s + 淡出 0.7s = **20.63s < 25s**（不变量注释见 `:983–985`），算术成立，不会切断正常播放。

### D2 视频缺失/失败走 hold 路径 → **可接受**

`BeginFilm()`（`:1232`）缺失 → `film-missing`（`:1241`）→ `BeginHold()`；`OnFilmFailed`（`:1257`）→ `film-failed`。
`ReadyToClose()`（`:1303`）仅当"DSH 已启动但还没有窗口"时才继续 hold，且仍受 `held >= MaxHoldSeconds` 约束；
否则 `ExtraHoldSeconds`(1.2s) 后淡出；DSH 未启动时 1.2s 内关闭。DSH 在 `Prepare()` 一开始就启动（`:890`），
符合 §2.4"所有失败路径都通向启动 DSH"。副作用（黑底 hold）记为观察项 D-4。

### D3 `--check`/`--bounds` 先读 `launcher.json` → **可接受**

三项禁止逐条核对：**不开窗口**（诊断 `:82/:83` 已 return，`Application` 在 `:96`）、**不占互斥锁**（Mutex `:89`）、
**不启动 DSH**（`TryLaunchDsh` 只在 `:890`/`:113`/`:1416`）。多出的 `options`/`dsh-default` 日志 ∈ 白名单。
关键稳健性：`LauncherOptions.Load()` 自带 try/catch（`:289`、`:299–301`），配置文件损坏**不会**抛到
`Main` 的 catch（`:109–114` 那条会调用 `TryLaunchDsh()`），否则 `--check` 会意外启动 DSH。仅与
CONTRACT §6"Main 里最先处理"的字面措辞有出入（先读配置是 SPEC §6.10 的硬要求），**不构成违规**。

---

## 6. CONTRACT §7.6 与 §5 `MaxHoldSeconds` 语义（第三版重判）

**§5 语义（自窗口上屏起算）→ 判定成立。** `shownAt` 在 `ShowWindow()` 内记录（`:797` 定义、`:1092` 赋值），
未上屏时回退为 0（绝对上限）。正常路径 20.63s < 25s；窗口必然上屏（4 条分支全部走 `RequestShow()`，1ms 后 `ShowWindow()`）。
与 CONTRACT 更新后的语义一致。

**§7.6"窗口一定关闭"→ 意图成立，仍有 1 处配置相关破口。**

* 关闭终点齐全：正常 → `fade-done`（`:1374`）→ `ShutDown()`（`:1420`）；异常 → `fade-failed`（`:1408`）→ `ShutDown()`；
  `FadeOutSeconds<=0.01` → 直接关闭（`:1389–1395`）；看门狗 → `hold-failed`（`:1354`）→ `StartFade()`（`:1380`）；
  `ShutdownMode.OnExplicitShutdown` + `Closed→app.Shutdown()` 保证进程随窗口退出。
* 上屏后最迟 `shownAt + 25 + 0.7s` 关闭。
* **破口 D-1**：若看门狗在窗口**尚未上屏**时触发 `StartFade()`，淡出动画无法完成（实测，见 §7.2），
  关闭被推迟到窗口上屏之后，进程寿命可超 `MaxHoldSeconds`。默认配置（`MatchWaitSeconds=6`）不可达。

---

## 7. 缺陷清单

### 7.1 F-1（HIGH — 交付物，Lead 范围）`dsh-linkstart-startup\out\` 是**旧修订**的产物，不含 DEF-1 修复

| 文件 | Lead `out\` | 冻结修订应为 | 判定 |
|---|---|---|---|
| `DSH LinkStart Startup.exe` | 114176 B，mtime **10:23** | 由 `Launcher.cs 43911C0E…` 构建 → **115200 B** | **旧** |
| `README.md` | 12518 B，SHA16 `5814CFE291C71F27` | 12661 B，`4AA109F9939185E7` | **旧（rev1 版本）** |
| `assets\` 4 文件、`launcher.json` | 与源一致 | 同 | ✅ 新 |

* **二进制取证**（`out\` 的 exe 对比我在副本里用冻结源码构建的 exe，检索 .NET 字符串堆 UTF-16 字面量）：

```
字符串                                 Lead out\exe     我的 rev3 构建
"WS_VISIBLE is clear"（DEF-1 诊断）        缺失             存在
"RequestShow"（DEF-1 修复标识）            缺失             存在
"since show"（rev3 看门狗文案）            缺失             存在
"reached without a hand-over"（rev2）      存在             缺失
```

* 结论：Lead 的 `out\` exe 是 **rev2 时代二进制**，正是 `verify-runtime` 报出
  "缓存/fixed 路径第二次运行屏幕空白"的那一版；若直接分发该目录，**DEF-1 修复不在交付物里**。
* 复现：`(Get-FileHash 'dsh-linkstart-startup\out\README.md').Hash`（得到 rev1 的 `5814CFE2…`）；
  或在两个 exe 里检索上表字符串。
* 建议（Lead 执行，我未改动任何文件）：用冻结源码重建 `out\`（`build.ps1`），
  然后再跑一次 verify-runtime 的**第二次运行**用例确认 DEF-1 确已消失；`assets\`/`launcher.json` 可复用。
* 说明：**这不是源码缺陷** —— AC1/AC2/AC3/AC8/AC10 在冻结源码与我自己的干净构建上全部通过。

### 7.2 D-1（LOW-MEDIUM，配置相关；底层机制已实测）看门狗可在窗口未上屏时启动淡出

* 位置：`Launcher.cs:1350`（`origin` 回退 0）、`:1357`（`StartFade()`）、`:1380`（`StartFade` 无 `shown` 守卫）、
  `:333`（`MatchWaitSeconds` 仅 `NonNegative`，**无上限**）。
* 复现：`launcher.json` 设 `MatchWaitSeconds: 60`（合法用户配置），启动时 DSH 窗口迟迟不出现 →
  t≈25s 看门狗按 `origin=0` 触发 `StartFade()`，此时窗口**从未 `Show()`**。
* 实测（独立小程序 `_verify\conformance\probe_fade.cs`，**未运行启动器**、无可见窗口）：

```
case=hidden  windowShown=no   animationCompleted=NO     opacity=1.0000   ← 未上屏窗口：动画不推进
case=shown   windowShown=yes  animationCompleted=yes    opacity=0.0000   ← 对照：已上屏正常完成
```

  未上屏窗口的 `OpacityProperty` 动画不推进、`Completed` 永不触发，而 `catch`（`:1406–1409`）只覆盖
  `BeginAnimation`（`:1403`）抛异常的情形；`fadeRunning` 已置位（`:1383`）且看门狗被停（`:1384`），
  于是没有任何东西再关闭它 —— 直到探测把窗口显示出来。
* 与既有陈述矛盾：`StartFade` 注释（`:1398–1399`"never before Show()"）与 `StartWatchdog` 注释
  （`:986–988`"the window is guaranteed to be on screen within `MatchWaitSeconds`"）——后者仅在
  `MatchWaitSeconds ≤ MaxHoldSeconds` 时成立，代码未维持该前提。
* 影响：默认配置不可达（6s < 25s），故不阻断验收；用户改一个数字即可触发，违反 §7.6 字面保证。
* 建议（三选一）：`OnWatchdogTick` 中 `shownAt < 0` 时直接 `ShutDown()`；或加载时把 `MatchWaitSeconds`
  夹到 `Math.Min(value, MaxHoldSeconds)`；或 `StartFade()` 开头 `if (!shown) { ShutDown(); return; }`。

### 7.3 D-2（LOW）淡出开始后关闭只依赖动画 `Completed`，无兜底

`:1384` 停掉看门狗、`:1403` 启动动画、`catch`（`:1406–1409`）只覆盖启动异常。若动画静默不完成
（已上屏时对照实验表明会完成，故属理论风险），没有第二个网关闭窗口。
建议加一次性 `DispatcherTimer(FadeOutSeconds + 1s)` → `ShutDown()`（`closed` 已守卫，幂等）。

### 7.4 D-3（INFO）`hold-failed` 事件名被两种原因复用

`:1287`（末帧冻结失败 `"freeze: "`）与 `:1354`（看门狗上限）。两者 ∈ 白名单，不违反契约，
但机械解析无法区分，建议改名或在 detail 前缀区分。

### 7.5 D-4（INFO，取舍）视频缺失/失败时 hold 的是黑底

`:1232–1243` + `Background = Brushes.Black`（`:811`）。有界性没问题（D2 判定），
但用户看到纯黑而非末帧蓝；如需"蓝光散去"观感可加静态兜底帧。

---

## 8. 方法局限与未覆盖范围

1. **纯静态**：本报告不运行启动器。AC4/AC5/AC6/AC7/AC9 与 AC10 运行期部分未经我验证（`verify-runtime` 负责）；
   D-1 的"引擎机制"是我实测的，但"启动器真实运行中的表现"未由我实测。
2. **词法而非语法**：掩码式词法分析不建 AST、不做类型检查；依赖运行期语义的判断（分支可达性、Show 时序）
   只能靠**读源码 + 行号引用**确认；本报告 30+ 处 `文件:行` 引用已逐条机器复核命中。
3. **偏好模式匹配的条目需人工复核**：Z 序、VideoFit→Stretch 映射、`Prepare()` 内初始化、延迟 Show 时序等
   以"模式命中 + INFO"呈现，我已逐个读源码确认（§3、§4）。
4. **数据比对验证数值与常量，不验证像素**；面板动效观感与 §3.5 的实际视觉一致性由运行期/视觉验证负责。
5. **F-1 的取证边界**：字符串取证证明"Lead 的 exe ≠ 冻结源码构建的 exe"，并给出缺失/存在的证据串；
   但它不能逐字节重建 Lead exe 的源码版本——"rev2 时代"是由 rev2/rev3 专有文案的互斥存在推定的。
6. **D-1 的实验边界**：探针复现的是"未 `Show()` 窗口上 `OpacityProperty` 动画不推进"这一 WPF 底层事实，
   不是启动器本身；探针源码与原始输出可复检（§9）。
7. **未覆盖**：性能、非 100% DPI 的实际观感、多显示器、`--check`/`--bounds` 的真实输出内容、失败路径隔离演练（均属运行期）。

---

## 9. 证据文件索引

| 文件 | 内容 |
|---|---|
| `_verify\conformance\evidence\compile-clean-raw.txt` | AC1 原始 csc 命令行与完整输出（exit 0，0 error / 0 warning） |
| `_verify\conformance\evidence\build-ps1-clean-copy.txt` | 副本中 `build.ps1` 完整输出（rev3，exit 0） |
| `_verify\conformance\evidence\AC-summary.txt` | AC1/2/3/8/10 汇总 + 被验证源码 SHA256 |
| `_verify\conformance\evidence\check_out_mybuild.json` | **我的副本构建**的 `out\` 判定（AC2 基准） |
| `_verify\conformance\evidence\{scan_cs5,scan_spec_data,scan_contract,check_compile,check_out}.json` | 逐条判定与证据行（含 CT-S1…CT-S10） |
| `_verify\conformance\evidence\key-evidence.txt` | 全部 PASS 条目的机器证据摘录 |
| `_verify\conformance\selftest-report.txt` | 反向验证 81 项断言 + 13/13 csc 地面真值 |
| `_verify\conformance\probe\probe-fade-{hidden,shown}.txt` | D-1 实测原始输出 |
| `_verify\conformance\README.md` | 工具用法、累计 10 个扫描器自身缺陷、工具局限 |

---

## 附录 A — DEF-1 的发现与修复验证经过（保留记录）

1. **发现（verify-runtime）**：缓存/fixed 几何路径下，`Visibility.Hidden` + 在 `app.Startup` 处理器内**同步** `Show()`
   会让 HWND 停在 `WS_VISIBLE=0`；第二次运行屏幕上什么都没有，而日志照写 `visible=True`（WPF 的 `IsVisible` 为真）。
   这类"日志健康、屏幕空白"的故障静态检查发现不了——它需要真跑，正是运行期验证者的价值。
2. **修复（impl-core，第三版）**：所有上屏路径改走 `RequestShow()`（一次性 1ms `DispatcherTimer`，
   Tick 内先停表置 null 再 `ShowWindow()`），共 4 个调用点；`Visibility.Hidden` 按 SPEC §6.3 字面保留；
   另加 `Win32.GetWindowLong`/`IsReallyVisible()` 与 `front-failed` 一次性诊断，使同类状态今后不易被漏报。
3. **本次静态复核（我）**：§3 五项全部 PASS —— 单一 `Show()` 且只经延迟路径、`RequestShow()` 三重幂等守卫、
   诊断块非侵入（无可见性调用、无控制流改动、全段 try/catch、事件名与总数不变）、新代码仍满足 C# 5、
   延迟上屏不会因 `ShutdownMode` 提前退出、`OnClosed` 已清 `showTimer`。
4. **遗留**：该修复只覆盖"窗口已请求显示"的路径；**看门狗在未上屏时触发淡出**（D-1）是同一类"窗口还没上屏"风险的另一个入口，
   代码未变，仍建议按 §7.2 加固。
5. **交付提醒**：Lead 的 `out\` exe 仍是修复前的二进制（F-1），需重建后才能真正把 DEF-1 修复交出去。

*报告结束。所有 `文件:行` 引用对应 §1 表中第三版冻结修订；本报告未修改任何实现文件。*
