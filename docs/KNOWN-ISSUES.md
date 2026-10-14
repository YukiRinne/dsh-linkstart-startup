# 已知问题 / Known issues — `dsh-linkstart-startup`

本文件记录**已接受但不修**或**仅在非默认配置下可达**的问题，以及独立验证者发现的、不值得为它再冻结一个修订的边角情况。

冻结修订（两份验证报告与 Lead 终验跑都基于这一版）：

| 文件 | SHA256（前 16） |
|---|---|
| `Launcher.cs` | `43911C0EE00AFAFD` |
| `Panels.cs` | `51289E15348D36E7` |
| `Account.cs` | `8803E8BEBA5C144C` |
| `build.ps1` | `3DBE31DF62AF1B8E` |
| `launcher.json` | `CB66EDB0D2C5361E` |
| `app.manifest` | `E7A0E5A07F289A71` |

> 上表 6 个文件是**行为相关**的冻结修订，必须与验证报告保持一致。
> `README.md`、`LICENSE`、`.gitignore` 属文档/工程文件，验证后还做过文字精简与补充，不逐次记录哈希 —— 以 git 历史为准。

结论来自：`docs/VERIFY-CONFORMANCE.md`（静态）、`docs/VERIFY-RUNTIME.md`（动态/像素）、以及 Lead 指向**真实 DSH** 的终验跑（冷启动 + 缓存两条路径）。

---

## KI-1 `MatchWaitSeconds > MaxHoldSeconds` 时"一定关窗"承诺有破口 — LOW-MEDIUM

**现象**：`OnWatchdogTick` 在窗口尚未上屏（`shownAt < 0`）时把起点回退为 `0`，可能调用 `StartFade()`；而"未上屏窗口的不透明度动画不会推进、`Completed` 永不触发"（验证者用独立探针实测），于是关闭被推迟到窗口真正上屏之后。进程寿命可超过 `MaxHoldSeconds`。

**可达性**：只有在配置文件里把 `MatchWaitSeconds` 设得**大于** `MaxHoldSeconds` 时才可达（`MatchWaitSeconds` 只做非负校验，没有上限）。默认 `6.0 < 25.0`，探针在 6 秒内必然上屏，**默认配置不可达**。

**影响**：延迟关闭（上限约为 `MatchWaitSeconds + FadeOutSeconds`），不是永久卡死；期间屏幕不被占用。

**若你要修**（任选一行）：
1. `OnWatchdogTick` 里 `shownAt < 0` 时直接 `ShutDown()`；
2. 读配置时把 `MatchWaitSeconds` 夹到 `≤ MaxHoldSeconds`；
3. `StartFade()` 开头加 `if (!shown) { ShutDown(); return; }`。

---

## KI-2 淡出开始后仅靠动画 `Completed` 关闭，没有兜底计时器 — LOW

`StartFade()` 之后唯一的关闭路径是 `DoubleAnimation.Completed`。正常流程下它是可靠的（Lead 终验跑两次实测 `fade-done` 都在 `fade-out` 后 0.68–0.69 秒），但理论上"动画不推进"就会停在那里 —— 与 KI-1 是同一个机制。若你按 KI-1 的方案加固，顺手加一个 `FadeOutSeconds + 0.5s` 的兜底 `ShutDown()` 即可。

---

## KI-3 `hold-failed` 事件名被两处复用 — INFO

`BeginHold()` 里"冻结末帧失败"和看门狗"到达上限强制关闭"都记 `hold-failed`。日志读起来要靠 detail 区分。DEF-1 修复时顺手加的 `front-failed "WPF reports visible but Win32 WS_VISIBLE is clear"` 是独立的、只记一次的诊断。

---

## KI-4 视频缺失时是"黑底 hold"，不是立刻退出 — INFO（有意设计）

视频缺失/播放失败时，若 DSH 已启动，启动器会**保持一块全屏黑窗**直到 DSH 窗口出现或到达上限（验证者实测最坏 32.78 秒进程寿命、其中黑窗在屏 26.19 秒；DSH 有窗口时 4.17 秒、DSH 未启动时 10.09 秒）。这是 SPEC §1"盖住启动画面"的延伸：与其露出 DSH 的加载画面，不如黑着等它。它仍受 `ExtraHoldSeconds` / `MaxHoldSeconds` 约束。

**如果你更希望缺片时立刻让开**：把 `MaxHoldSeconds` 调小（例如 `3.0`）即可，不需要改代码。

---

## KI-5 片尾最后约 0.43 秒不显示 —— 平台报值问题，非本实现回归

WPF 的 `MediaElement.NaturalDuration` 对 `intro_dsh.mp4` 报 **17 秒整**（容器真值 17.4333 秒，60fps/1046 帧）。淡出触发条件是 `pos ≥ NaturalDuration − FadeLeadSeconds`，于是实际在片内 **16.30 秒**开始淡出、17.00 秒窗口完全透明，而不是 SPEC §2.1 写的 16.73 → 17.43。

- **不是本次回归**：参考实现 `dsh-launcher` 实测 16.48/17.185，本实现 16.504/17.190，差 5 毫秒。
- **视觉上无关紧要**：那 0.43 秒里爆闪已经收尾（`blueness` 从 31 掉到 1）。SPEC §3.6 的完整 2.3 秒爆闪已被验证**完整保留**（峰值 16.0167s、峰值后仍有 84 帧、末帧 17.4167s）。
- **若要严格对齐 SPEC §2.1**：把 `FadeLeadSeconds` 从 `0.7` 改成 **`0.27`**（= 17.0 − 16.73），淡出就会落在片内 16.73 秒开始、17.43 秒结束。这是**纯配置改动**，不改代码；但它依赖 WPF 对该文件报 17 秒这个实测事实，换片源要重新测。

---

## KI-6 本机 DSH 不在通用探测路径内 — 已按设计解决

`DshPath` 默认为空时按 SPEC §6.10 / §5.7 只探测通用位置（`%DSH_EXE%`、`%LOCALAPPDATA%\Programs\DeepSeek Harness\`、`%ProgramFiles%\DeepSeek Harness\`、`%LOCALAPPDATA%\DeepSeek Harness\`、exe 同目录）。本机 DSH 装在 `C:\Harness&Agent\DeepSeek Harness\`，**刻意不在**默认值里（否则就是把开发者本机数据写进发行包）。

解决办法（已用于终验跑）：

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1 -DshPath "C:\Harness&Agent\DeepSeek Harness\DeepSeek Harness.exe"
```

该参数只把路径写进 **`out\launcher.json`**，仓库里的 `launcher.json` 保持通用。也可以改用环境变量 `DSH_EXE`。

---

## 给后来者：两个"校验器自己会骗人"的实例

1. **小写 `strings` 式的二进制探针会误判**：UTF-16 字符串在 PE 的 `#US` 堆里可能落在**奇数**字节偏移上，把整个文件从偏移 0 当作 UTF-16 解码会漏掉它们。Lead 的终验脚本曾因此把**正确的新构建**判成"旧产物"。要用**原始字节搜索**（`[System.Text.Encoding]::Unicode.GetBytes(needle)` 后逐字节比对），或者用 `-sseof` 这类从另一端入手的方法。
2. **静态扫描器需要反例校准**：两份验证报告的扫描器在自检阶段分别抓出并修掉了自己的 10 个缺陷（最危险的一个会在真实实现上制造 13 处假 FAIL）。SPEC §5.5 的教训是成立的 —— 任何校验脚本都要先证明它**能报错**。
