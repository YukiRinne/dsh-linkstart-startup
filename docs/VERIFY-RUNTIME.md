# 独立动态运行与像素验证报告 — `dsh-linkstart-startup`

- 验证者：**verify-runtime（独立验证 B）**，任务 `task-5`（第三版／最终修订全量重跑）
- 验证方式：**自建隔离副本、自行编译、自行运行、自行测量**；不采信实现者结论，不修改任何实现文件（仓库 `out\` 也未触碰）
- 验证对象（最终冻结修订，SHA256 前 16 位；**运行前后各核对一次，无漂移**）：

| 文件 | SHA256(16) | 字节 |
|---|---|---|
| `Launcher.cs` | **43911C0EE00AFAFD** | 56059 |
| `Panels.cs` | **51289E15348D36E7** | 16001 |
| `Account.cs` | **8803E8BEBA5C144C** | 13314 |
| `build.ps1` | 3DBE31DF62AF1B8E | 5072 |
| `launcher.json` | CB66EDB0D2C5361E | 807 |
| `app.manifest` | E7A0E5A07F289A71 | 969 |
| `README.md` | 4AA109F9939185E7 | 12661 |

本报告对应的构建产物：`_verify\runtime\run\out\DSH LinkStart Startup.exe`，115200 B，SHA256(16) = `3019A4F3EF4ED70F`。
四个素材与参考实现 `dsh-launcher\assets\` **逐字节相同**（`intro_dsh.mp4` 98BA25C3C66C49B3、`link-start.wav` 4B7038275B229646、`title.png` 4877875E69F732F9、`dsh.ico` 2367917C907F98EA）。
所有运行都在 `_verify\runtime\` 的副本里，`DshPath` 指向 `notepad.exe` 或我专门构造的替身；**从未启动真实 DSH**。

> 本报告是**第三版重跑**的结果。第二版里发现的 `DEF-1`（缓存/fixed 路径窗口不上屏）已由 Lead 修复，修复的回归验证与"诊断反证"见 §2；旧修订的结论已作废，下文数字全部来自最终修订的实测。

---

## 1. 结论表（最终修订）

| # | 标准 | 判定 | 实测 |
|---|---|---|---|
| AC1 | 干净目录里一次编译通过，0 error | **PASS** | 副本内 `build.ps1`：0 error / **0 warning**；产物 PE machine=0x8664(x64)、subsystem=2(WINDOWS_GUI/winexe)、115200 B |
| AC2 | `out\` 含 exe + 4 assets + `launcher.json` + `README.md` | **PASS** | 7 项齐全（exe、launcher.json、README.md、assets\{intro_dsh.mp4, link-start.wav, title.png, dsh.ico}） |
| AC3 | 无 C#6+ 语法 / 无 `Codex` / 默认值不含本机路径或坐标 | **PASS**（本报告覆盖后半） | 源码与构建输入 `Codex` 命中 0；仓库 `launcher.json` 的 `DshPath=""`、`WindowLeft/Top/Width/Height` 全 0；语法扫描见 verify-static |
| AC4 | `--check` 写 `check.txt`，`status=signedIn`（有凭据）/`signedOut`（无凭据），不开窗口 | **PASS** | 有凭据：**0.676 s** 退出、exit 0、**独立测得可见窗口 0 个**、日志仅 `options`+`account`（无 show/reveal/window/geometry）；`status=signedIn`/`nameSource=platform-profile`/`displayName=YukiRinne`/`Title()=ローカルアカウント`/两行 `Lines()` 正确。无凭据（`DSH_HOME`→空目录）：0.455 s、0 窗口、`signedOut`/`no-local-grant`/`(none)`/`ローカル未ログイン`/`未ログイン`，日志仅 `dsh-default`+`options`+`account` |
| AC5 | `--bounds` 写 `bounds.txt`（客户区 + 16:9 覆盖框），不开窗口 | **PASS** | 0.247 s、exit 0、0 窗口。文件给出客户区 `240,234 1264x747`（scale 1.00）+ fixed 四行 + 覆盖框 `208,234 1328x747`。**独立复核**：我用 `GetClientRect`+`ClientToScreen` 量同一 notepad 窗口 = 客户区 `240,234 1264x747`（窗口矩形 `234,234 1276x753`）逐值一致；覆盖框复算 `747×16/9=1328`、中心 x=872 → 左 208 ✅ |
| AC6 | 一次真实运行的事件顺序 + `fade-done` 后退出 | **PASS** | 行号 `begin..fade-done = [2,3,4,6,8,11,12,13]` 严格递增；`fade-done` 后 **0.344 s** 退出（exit 0，wall 19.228 s）。独立测量：**可见窗口峰值 = 1**，矩形 `104,130 1328x747` 与 `show` 行逐字一致 |
| AC7 | `fade-done − intro ≈ 17.43 ± 1.2`；`fade-done − fade-out ≈ 0.7` | **PASS** | `fade-done − intro = `**17.187 s**（Δ −0.246，区间内）；`fade-done − fade-out = `**0.687 s**（Δ −0.013）。根因（`NaturalDuration` 只报 17 整秒）见 §4 |
| AC9 | 视频缺失 / 语音缺失 / DSH 缺失时窗口仍关闭 + 对应失败事件 | **PASS** | 5 个用例全部 exit 0 且关窗（4.14 / 4.39 / 10.16 / 6.11 s），见 §3 |
| AC10 | 第二个实例记 `already-running` 立即退出，不出第二个窗口 | **PASS** | 第一实例运行中 t=0.8 s 启动第二个：**0.043 s** 退出（exit 0），日志 `already-running another launcher owns the screen; exiting`，第二进程**可见窗口峰值 = 0**；第一实例可见窗口峰值 = 1（**走的是缓存路径**，即 DEF-1 修复点） |
| — | **DEF-1 修复回归**（探测/缓存/fixed 三条路径都上屏） | **PASS** | 三条路径 `WS_VISIBLE=1` + `IsWindowVisible=1` + 像素证明在屏；且**无**新诊断行。见 §2 |
| — | **诊断反证**（诊断不是摆设） | **PASS** | 临时副本退回同步 `Show()` 后：缓存/fixed 重新不可见 **且** `front-failed WPF reports visible but Win32 WS_VISIBLE is clear` **确实打出**；探测路径不受影响。见 §2.3 |
| — | 素材审计（SPEC §3.6 / §5.5） | **PASS** | 见 §5 |
| — | 面板像素 + 缩放（SPEC §3.4/§3.5） | **PASS** | 55/55 像素检查 + 3/3 缩放检查，见 §6 |
| — | 正常路径不出现 `hold-failed` / `front-failed` | **PASS** | 完整运行事件表：`options, begin, dsh-launched, geometry, geometry-cache, show, window, reveal, account, account-ready, intro, fade-out, fade-done` —— 两个安全网事件都没有；三条可见性路径的日志也无 `front-failed` |
| — | `account-ready` 是否出现 | **PASS** | 每个真实运行都有 `account-ready signedIn / platform-profile`；完整运行中在 `begin` 后 **0.368 s** 落盘，远早于 `intro`（1.52 s）→ 确认面板（10.38–12.22 s）必然已拿到名字 |

**一句话结论**：最终修订上 **AC1–AC10 全部 PASS**；第二版的 `DEF-1` 已修复并经"三条路径 + 像素 + 可证伪反证"验证；新加的可见性诊断在健康构建上不触发、在退回旧写法的构建上必然触发，二者互相印证。

---

## 2. DEF-1 修复的回归验证（本轮核心）

### 2.1 修复内容（供对照；代码未被我改动）

- `Launcher.cs:1070-1083` 新增 `RequestShow()`：一次性 `DispatcherTimer`（1 ms），Tick 内先 `StopTimer` 置 null 再调 `ShowWindow()`。
- 4 个上屏点全部改走它：`Launcher.cs:905`（fixed 分支）、`:916`（缓存分支）、`:1021`（探测命中）、`:1049`（屏幕回落）；`OnClosed` 停表。
- 诊断：`StartFrontGuard` 的 500 ms tick（`Launcher.cs:1117-1143`）用 `GetWindowLong(GWL_STYLE)&WS_VISIBLE` 与 WPF `IsVisible` 对照，不一致时**只记一次** `front-failed WPF reports visible but Win32 WS_VISIBLE is clear`；只读状态、不改控制流。

### 2.2 三条几何路径实测

每次运行用测试用短配置（临时 `FadeLeadSeconds: 16.5`，压到约 3.3 s/次；**项目 config 与代码默认值未改**）。窗口句柄/样式与屏幕像素独立采集：

| 路径 | 构造 | `WS_VISIBLE` | `IsWindowVisible` | 窗口矩形 | 窗口区域像素（mean BGR / 暗像素占比） | 右上 ✕ | 日志 `show` | 新诊断 |
|---|---|---|---|---|---|---|---|---|
| 探测（无 `geometry.json`） | `WindowBounds=auto` | **1** | True | `78,104 1328x747` | **0.0 / 1.000** | max 228 | 有 | 无 |
| 缓存（先跑一次生成） | `geometry.json` 存在 | **1** | True | `182,208 1328x747` | **0.0 / 1.000** | max 228 | 有 | 无 |
| fixed | `WindowBounds=fixed` + 四值 | **1** | True | `0,26 1328x747` | **0.0 / 1.000** | max 228 | 有 | 无 |

像素判据：启动器窗口是黑底 + 右上角 ✕，"窗口矩形内 ≈100% 暗像素 + 该角落出现亮像素"= 窗口真的在屏；若不在屏，该区域会透出下层内容。对照**修复前**（第二版缓存路径）同位置采样：`WS_VISIBLE=0`、窗口区域 mean BGR **41.4**、暗像素占比 **0.000**（下层内容透出）。

### 2.3 诊断反证（可证伪性，SPEC §5.5 精神）

在**我自己的临时副本** `_verify\runtime\diagcopy\`（仓库源码未动）里，用 `make_diagcopy.py` 把 `RequestShow()` 的定时器体精确替换回"直接 `ShowWindow()`"（即退回 `Application.Startup` 内同步 `Show()` 的旧写法），重新编译后再跑三条路径：

| 路径 | `WS_VISIBLE` | 窗口区域 mean BGR / 暗占比 | 诊断行 |
|---|---|---|---|
| 探测 | 1（可见） | 0.0 / 1.000 | 无（该路径本来就没坏，符合预期） |
| 缓存 | **0（不可见）** | **49.3 / 0.000** | **有**：`2026-10-14T11:02:03.6968621+08:00 front-failed WPF reports visible but Win32 WS_VISIBLE is clear` |
| fixed | **0（不可见）** | **92.0 / 0.000** | **有**：`2026-10-14T11:02:08.0403480+08:00 front-failed WPF reports visible but Win32 WS_VISIBLE is clear` |

结论：①三条路径的可见性**确实由 `RequestShow()` 决定**；②新诊断在"WPF 说可见、Win32 说不可见"状态下**必然触发**、在健康构建上不触发 → 它是有效探测器，不是装饰。

### 2.4 方法连续性

第二版定位根因时用的三件工具本轮沿用同一口径：`probe_onscreen.ps1`（Win32 判据）+ 截图像素 + 与实现无关的 3 用例 WPF 探针（`smoke_showtest.cs`：`Visibility.Hidden`+`Startup` 内同步 `Show()` → 不可见；同窗口改由 timer tick 显示 → 可见；不设 `Hidden` → 可见）。因此第二版与第三版的"不可见/可见"结论可以直接比较。

---

## 3. 失败路径（AC9）与 D2 实测（最终修订）

| 用例 | 构造 | 失败事件 | 结果 | 关闭耗时（wall） | 独立测量的窗口 |
|---|---|---|---|---|---|
| PF1b | `assets\intro_dsh.mp4` 改名 | `film-missing` | exit 0 | **4.137 s** | 可见 1 个 `26,52 1328x747` |
| PF3b | `assets\link-start.wav` 改名（片在） | `voice-missing` | exit 0 | **4.386 s**（我按 Esc 短路，`skip t=3.77`） | 可见 1 个 `104,130 1328x747`；`voice-missing` 后 **0.47 s** 即 `intro`，片子照常播 |
| PF2b | `DshPath` → `C:\nonexistent\NoSuchDsh.exe` + 缺片 | `dsh-missing` + `film-missing` | exit 0 | **10.162 s**（含 6 s 探测等待） | 可见 1 个全屏 `0,0 1920x1080` |
| PC1b | `DshPath` → `quiet.exe`（进程在、**永不拥有窗口**）+ 缺片；测试用 `MaxHoldSeconds=3`、`MatchWaitSeconds=1` | `film-missing` → `hold-failed` | exit 0 | **6.110 s** | 可见 1 个全屏 `0,0 1920x1080` |
| PF4b | `DSH_HOME` → 空目录 + `--check` | —（`account`） | exit 0 | 0.455 s、**0 窗口** | — |

**D2（缺片走 hold 路径）判定：接受，但"关闭时机"取决于 DSH 窗口是否存在。** 本轮实测：

1. DSH 窗口存在（PF1b）：`intro`(`t=1.62`) 后 **1.31 s** 开始淡出，全程 4.14 s 关闭 —— 体验正常。
2. DSH 未启动（PF2b，`dshStarted=false`）：`ExtraHoldSeconds` 生效，`intro`(`t=7.56`) 后 **1.23 s** 开始淡出。
3. DSH 进程在但无窗口（PC1b）：`ReadyToClose()` 持续返回 false → 一直到安全网 `hold-failed MaxHoldSeconds=3.00s since show (t=4.68)` → 淡出 → 关闭。

**看门狗起点复核（D1，第二版修订的关键点）**：PC1b 中 `show` 在 `11:04:13.7274`、`hold-failed` 在 `11:04:17.1180` → 相差 **3.391 s**（= 3.00 s 上限 + ≤0.5 s tick 粒度）；而 `begin`(`11:04:12.5719`) → `hold-failed` = **4.55 s**。若起点是 `Prepare`/`begin`，差值应≈3.0 s；实测明确指向 **`show`**。第二版曾用默认 25 s 上限实测同一路径：`hold-failed` 出现在 `show` 后 25.00 s，全程 32.78 s、全屏黑窗在屏 26.19 s；本轮为省屏改用 3 s 上限复验同一逻辑。

---

## 4. 时序与 AC7 根因

完整运行（本轮唯一一次完整动画，探测路径，片/语音/账号齐全）：

```
+0.000  begin            screen=1920x1080 bounds=auto fit=aspect lead=0.70 hold=1.20 max=25.00
+0.042  dsh-launched     C:\Windows\System32\notepad.exe
+0.182  geometry         matched DSH client area 136,130 1264x747 -> 104,130 1328x747
+0.191  show             104,130 1328x747
+0.314  window / reveal  visible=True opacity=1 topmost=True state=Normal
+0.368  account / account-ready  YukiRinne / signedIn / platform-profile
+1.519  intro            t=1.65
+18.018 fade-out         0.70s at t=18.15
+18.706 fade-done        t=18.84    → 进程退出（exit 0），wall 19.228 s
```

| 指标 | 实测 | 期望/参考 |
|---|---|---|
| `reveal − begin` | 0.314 s | — |
| `intro − reveal`（语音 1.170 s） | 1.205 s | 1.17 + 起播开销 |
| `fade-out − intro` | 16.500 s | 16.73（=17.43−0.7） |
| `fade-done − fade-out` | **0.687 s** | 0.700 ✅ |
| `fade-done − intro` | **17.187 s** | 17.43 ± 1.2 ✅（Δ −0.246） |
| `fade-done → 退出` | 0.344 s | — |
| 进程总时长 | 19.228 s | < MaxHold 25 s ✅ |
| 可见窗口峰值 | 1 | 1 ✅ |

**根因（判据与根因分开陈述）**：AC7 **判据通过**；−0.246 s 的缺口不是触发逻辑错，而是 WPF 媒体栈的时长报值：

- 独立小工具 `media_probe.exe`（`MediaPlayer.Open` → `MediaOpened` → 打印 `NaturalDuration`）实测：`HasTimeSpan=True`、**`TotalSeconds=17`**（整秒）；容器真值 = **17.43333 s / 1046 帧 @60fps**。
- 因此 `OnFrameTick` 的 `position >= NaturalDuration − 0.7` 实际在**片内 16.30 s** 触发淡出（不是 SPEC §2.1 的 16.73 s），淡出在片内 ≈17.00 s 结束，**片尾最后 ≈0.43 s 未显示**（blueness 31→4.6 的收拢尾）。
- **非本次重写的回归**：参考实现同口径 `fade-out − intro = 16.48 s`、`fade-done − intro = 17.185 s`，本实现 16.500 / 17.187 s，相差 2 ms（第二版实测 16.504/17.190，同样一致）。若要严格回到 SPEC 的 16.73 淡出点，需要一个不依赖 `NaturalDuration` 的片长来源（配置项或容器时长）。

---

## 5. 素材审计（SPEC §3.6 / §5.5）

`assets\intro_dsh.mp4`：**1920x1080 / 60 fps / 1046 帧 / 17.4333 s**，h264 High、yuv420p、bt709；与参考实现素材**逐字节相同**。

blueness = `mean(B−(R+G)/2)`（全片逐帧解码，帧号 = 秒×60）：

| t(s) | 15.10 | 15.50 | 16.00 | 16.40 | 16.70 | 17.00 | 17.40 |
|---|---|---|---|---|---|---|---|
| 实测 | 16.25 | 65.73 | 96.27 | 95.67 | 67.97 | 32.34 | 4.60 |
| SPEC §3.6 | 21.5 | 80.1 | 106.1 | 95.6 | 70.0 | 31.0 | 1.0 |
| Δ | −5.3 | −14.4 | −9.8 | +0.1 | −2.0 | +1.3 | +3.6 |

**末帧爆闪是否被裁：没有被裁。** 峰值在**第 961 帧 = 16.0167 s**（96.36）；峰值后仍有 **84 帧 = 1.40 s**；**末帧 = 第 1045 帧 = 17.4167 s**，blueness 4.60（已回落）。用 `ffmpeg -sseof -0.05` 独立抽尾帧交叉验证：与 cv2 读到的最后一帧 mean abs diff = **0.0155**（确认读到文件真末帧），尾帧 blueness 4.62 与逐帧解码一致 → 不存在 SPEC §5.5 的"在峰值切断"。剖面绝对高度比 SPEC 表格低约 10，但形态与结束位置一致；素材与参考实现逐字节相同，判为 SPEC 表格的测量口径差异。

`assets\link-start.wav`：**1.1700 s**，48000 Hz / 单声道 / 16 bit ✓。

---

## 6. 面板像素审计（SPEC §3.4 / §3.5）

方法：自写最小 WPF 宿主（`panel_host.cs` + `render_panels.ps1`，csc 编译，引用被测 `Panels.cs`），`RenderTargetBitmap` 在 1920×1080 渲染指定帧为带 alpha 的 PNG，再用 Python 检查真实像素。宿主先对参考实现 `dsh-launcher\Panels.cs` 自测（55/55 PASS）以确保"失败≠harness bug"。

**最终修订结果：55/55 PASS**（`render_final\`、`check_panels.txt`）、缩放 **3/3 PASS**。代表性采样：

| 元素 | 帧 | 实测填充（band 众数） | 期望 | 边界实测 |
|---|---|---|---|---|
| 语言主面板 | 585 | `#0482FD` | `#0482FD` | 填充行左沿 x=337 → rect.x≈335.0（期望 334） |
| 语言次面板 | 585 | `#0086EE`（select=0） | `#0086EE` | 857..1455（期望 856.0..1456.0） |
| 登录面板 | 665 | 上 `#0784B8` / 下 `#037FB3` | 同（渐变方向正确） | 488..1431 |
| 确认标题 | 780 | `#0083B7` | `#0083B7` | 621..1303 |
| 确认主体 | 780 | 上 `#0083B7` / 下 `#007FB3` | 同 | 315..1610 |
| 名字条 | 780 | `#00CBDE` | `#00CBDE` | 737..1183（Δ0.0） |
| YES / NO | 780 | `#00EBE7` / `#05ACCD` | 同 | 615..814 / 1106..1308 |

其他像素级检查全部 PASS：圆角处不是自身填充色、方形元素（名字条 r=0）角落是填充色、面板帧门控正确（f585 时登录/确认区域 alpha=0；f665 时语言/确认 alpha=0；f780 时语言区域 alpha=0）。

标题渐入（探针 = title.png 中 alpha=255 的 128958 个像素，渲染 alpha/255 = 不透明度）：

| 帧 | 852 | 858 | 866 | 872 | 878 | 884 |
|---|---|---|---|---|---|---|
| 实测 | 0.0000 | 0.1176 | 0.3490 | 0.5451 | 0.7529 | 0.9608 |
| SPEC §3.4 | 0 | 0.115770 | 0.350558 | 0.543375 | 0.752496 | 0.961197 |
| \|Δ\| | 0.0000 | 0.0019 | 0.0015 | 0.0017 | 0.0004 | 0.0004 |

全部 ≤0.03（实际 ≤0.002，仅受 8 bit alpha 量化限制）；`WelcomeOpacity(f)` 与我在 Python 里独立实现的同一条 16 点曲线逐帧对比 f=845..945：**max\|Δ\| = 5e-7**（JSON 六位小数舍入）。f=890（不透明度 1）整帧与 `title.png` 逐像素比对：max Δ=1、mean Δ=0.00074（`Rect(0,0,1920,1080)` 1:1 落位）；f=930 与 f=1000 整帧 max alpha = 0（f≥930 交给视频自带标题）。

面板缩放与视频同规则：`fit` 1457×820 → (560,476,898,526) vs 预测 (559.3,476.0,898.5,526.1)；`fill` 1280×820 → (471,477,810,526) vs (470.7,476.1,810.1,526.2)；`stretch` 1280×820 → (492,477,789,526) vs (491.3,476.1,789.3,526.2)：max Δ ≤ 0.9 px。

---

## 7. 申报偏离 D1/D2/D3 的独立判定（最终修订）

| 偏离 | 判定 | 实测依据 |
|---|---|---|
| **D1** 全局看门狗：自 `show` 起 25 s 未开始淡出 → `hold-failed` 强制淡出关闭（未上屏时退回绝对上限） | **接受** | 完整运行**无** `hold-failed`；PC1b：`show`→`hold-failed` = **3.391 s**（3.00 s 上限 + ≤0.5 s tick），`begin`→`hold-failed` = 4.55 s → 起点确为**上屏时刻**而非 `Prepare`。正常播放最坏 20.63 s < 25 s，不会打断正常播放 |
| **D2** 缺片/失败走 hold 路径，受 `ExtraHoldSeconds`/`MaxHoldSeconds` 约束 | **接受，需知后果** | 见 §3：DSH 有窗口 4.14 s 关闭；DSH 未启动 10.16 s（6 s 探测等待 + 1.2 s hold）；DSH 进程在但无窗口则一直 hold 到上限（默认 25 s 时为 32.78 s／全屏黑窗在屏 26.19 s，第二版实测） |
| **D3** `--check`/`--bounds` 也先读 `launcher.json`（多 `options`/`dsh-default` 两行；不得出现 show/reveal） | **接受** | `DshPath` 有值时仅多 `options`；`DshPath=""` 时多 `options` + `dsh-default nothing in the standard locations...`。两模式**均无** show/reveal/window/geometry，独立测得**可见窗口 0 个**、0.676 s / 0.247 s 退出；都在单实例互斥锁之前处理，运行中也能用 |
| **新增诊断**（`front-failed ... WS_VISIBLE is clear`） | **接受（有效）** | 健康构建三条路径均不触发；退回同步 `Show()` 的副本上缓存/fixed 必然触发（§2.3）。语义准确、只读状态、不影响控制流 |

---

## 8. 方法局限

1. **替身窗口**：几何仍以 notepad 窗口代替 DSH 窗口。本机 `notepad.exe` 是 Store 应用 stub（启动的进程立刻退出，窗口属于另一个 `Notepad` 进程），几何规则据此验证有效（`GetClientRect` 实测与 `--bounds` 输出逐值一致），但**真实 DSH 窗口的尺寸/DPI 组合未测**（本机 1920×1080 / DPI 100%）。
2. **完整动画只跑一次**（19.2 s）；三条可见性路径用测试用短配置（`FadeLeadSeconds: 16.5`）压到约 3.3 s/次，PC1b 的 `MaxHoldSeconds`/`MatchWaitSeconds` 临时改为 3 s / 1 s。这些只改我副本里的 `launcher.json`，**仓库 config 与代码默认值未动**。
3. **诊断反证只在临时副本做**（`_verify\runtime\diagcopy\`，由 `make_diagcopy.py` 精确替换 `RequestShow()` 的定时器体），仓库 `Launcher.cs` 始终是冻结修订（哈希前后一致）。
4. **无凭据分支**是用 `DSH_HOME` 指向空目录模拟的，未在真实未登录环境运行。
5. PC1b 用 3 s 上限复验 D2 的"暂停到上限"行为；默认 25 s 的墙钟时长（32.78 s / 屏幕占用 26.19 s）沿用第二版实测——该路径逻辑在本轮修订中未变，且看门狗**起点**已单独用 3 s 用例验证为 `show`。
6. `blueness` 与 SPEC §3.6 表格的绝对差最高 14.4：我的量法是整帧均值（cv2 解码、未重采样），SPEC 表格未记录量法；素材与参考实现逐字节相同，故不改判。

---

## 9. 复现命令（全部在 `_verify\runtime\` 内，不触碰实现文件）

```powershell
$py   = "C:\Users\Administrator\AppData\Local\Programs\Python\Python312\python.exe"
$rt   = "E:\Harness&Agent Project\DSH SAO\_verify\runtime"
$impl = "E:\Harness&Agent Project\DSH SAO\dsh-linkstart-startup"

# 素材 / 面板
& $py "$rt\audit_assets.py"
pwsh -File "$rt\render_panels.ps1" -Panels "$impl\Panels.cs" -OutDir "$rt\render_final"
& $py "$rt\check_panels.py" "$rt\render_final"
& $py "$rt\check_scale.py" "$impl\Panels.cs"

# 构建 + 诊断模式 + 完整运行 + 失败路径
pwsh -File "$rt\mkrun.ps1" -Variant normal
pwsh -File "$rt\run_case.ps1" -Case check -LaunchArgs "--check"
pwsh -File "$rt\run_case.ps1" -Case bounds -OpenNotepad -LaunchArgs "--bounds"
pwsh -File "$rt\run_case.ps1" -Case e2e3 -OpenNotepad          # 唯一一次完整动画，约 19 s
pwsh -File "$rt\mkrun.ps1" -Variant novideo   ; pwsh -File "$rt\run_case.ps1" -Case pf1b-novideo -OpenNotepad
pwsh -File "$rt\mkrun.ps1" -Variant novoice   ; pwsh -File "$rt\run_case.ps1" -Case pf3b-novoice -OpenNotepad -SkipAfterSeconds 4
pwsh -File "$rt\mkrun.ps1" -Variant novideo -DshPath "C:\nonexistent\NoSuchDsh.exe" ; pwsh -File "$rt\run_case.ps1" -Case pf2b-nodsh
pwsh -File "$rt\mkrun.ps1" -Variant nocreds -DshPath "" ; pwsh -File "$rt\run_case.ps1" -Case pf4b-nocreds -LaunchArgs "--check" -EnvDSHHome "$rt\run\emptyhome"

# DEF-1 回归（三条路径）+ 诊断反证
pwsh -File "$rt\visibility_check.ps1" -Tag probe -Mode normal -GrabAt 1.0
pwsh -File "$rt\visibility_check.ps1" -Tag cache -Mode cache  -GrabAt 1.0
pwsh -File "$rt\visibility_check.ps1" -Tag fixed -Mode fixed  -GrabAt 1.0
& $py "$rt\make_diagcopy.py"                                  # 副本里退回同步 Show()
pwsh -File "$rt\diagcopy\build.ps1"
pwsh -File "$rt\visibility_check.ps1" -Tag cache -Mode cache -RunDir "$rt\diagcopy" -OutPrefix diag
pwsh -File "$rt\visibility_check.ps1" -Tag fixed -Mode fixed -RunDir "$rt\diagcopy" -OutPrefix diag
```

产物与原始数据：`_verify\runtime\`（`assets_audit.txt`、`check_panels.txt`、`check_scale.txt`、`render_final\`、`vis_*.json` / `grab_vis_*.png`、`diag_*.json` / `grab_diag_*.png`、`meta_*.json`、`analyze_e2e3.json`、`pf1b.log`）。
