# Windows 全局热键冲突诊断工具 · 实现方案

> 版本 v1.0 ｜ 2026-10-20 ｜ 对应设计文档 v1.0 ｜ 范围：P0 / MVP
> 本文档面向开发实施。核心链路已在 Windows 11 (22631.7652) 实机跑通，代码见 `src/`。

---

## 1. 实现目标（P0 验收口径）

**一句话**：用户按下一个没反应的组合键，10 秒内得到"谁占了、什么机理、怎么解"的确定结论。

| # | 可交付能力 | 验收方式 |
|---|---|---|
| 1 | 判定任意组合键是否被系统级占用 | 与 `RegisterHotKey` 返回值一致，误判率 0 |
| 2 | 对内置知识库覆盖的软件给出进程级归因 | 20 款软件矩阵测试 ≥ 90% 准确 |
| 3 | 区分「注册型 / 系统保留 / 未知」并给差异化处置 | 判定与实际机理一致 |
| 4 | 处置动作可逆，支持一键恢复 | 恢复成功率 100% |
| 5 | 全程零注入、零驱动 | 静态检查 + 进程行为审计 |
| 6 | 单文件免安装，双击即用 | 冷启动 < 1.5 秒 |

**P0 明确不做**：钩子取证（L2）、游戏模式编排、众包知识库、内核热键表读取。

---

## 2. 技术选型（对设计文档的技术栈修正）

| 项 | 选型 | 理由 |
|---|---|---|
| 语言 / 框架 | **C# / .NET 8 + WinForms** | Win32 互操作原生；启动快；UI 需求简单（结果卡片，非动画） |
| 发布形态 | `PublishSingleFile` + `EnableCompressionInSingleFile` | 单文件约 40–50 MB，免运行时依赖 |
| 权限 | `asInvoker` + 按需提权 | P0 全部能力**不需要管理员**（探测与进程枚举均可在用户会话完成） |
| 数据存储 | 本地 JSON（知识库）+ SQLite（操作日志，可选） | 知识库需可热更新；日志量小 |
| 打包 | 代码签名证书 | 降低 SmartScreen / 杀软误报 |

> **修正说明**：设计文档 v1.0 §5 建议 WPF，此处改为 WinForms。原因：① WPF self-contained 体积 ~130 MB，对一个"3 分钟急救工具"过重；② 结果页是信息卡片列表，不需要 WPF 的矢量能力；③ 冷启动 WPF 明显慢于 WinForms，而"急救"场景对启动时间敏感。

**为什么不选 Python 直接发布**：PyInstaller 打包物对杀软误报率高（枚举进程 + 全局热键探测的组合极易触发启发式），且启动需解压，1–3 秒。Python 仅用于原型验证。

---

## 3. 工程结构

```
HotkeyTriage/
├─ src/
│  ├─ HotkeyTriage.sln
│  ├─ HotkeyTriage.Core/              # 无 UI 依赖，可单元测试
│  │  ├─ Interop/
│  │  │  ├─ NativeMethods.cs          # P/Invoke 声明集中管理
│  │  │  └─ Win32Error.cs             # 1409 等错误码常量
│  │  ├─ Probe/
│  │  │  ├─ HotkeyProbe.cs            # 试注册探测
│  │  │  ├─ ProbeGrid.cs              # 候选组合网格生成
│  │  │  └─ KeyCombo.cs               # 组合键 ↔ 字符串互转
│  │  ├─ Processes/
│  │  │  ├─ ProcessScanner.cs         # 枚举 PID / exe / 路径
│  │  │  └─ ProcessGuard.cs           # 黑白名单，红线拦截
│  │  ├─ Knowledge/
│  │  │  ├─ KnowledgeBase.cs          # 加载 + 反查索引
│  │  │  └─ kb.json                   # 内置知识库
│  │  ├─ Attribute/
│  │  │  ├─ AttributionEngine.cs      # 归因主流程
│  │  │  ├─ SystemHotkeyRules.cs      # 系统保留 / 系统可关
│  │  │  └─ BisectSolver.cs           # 二分排除（L3）
│  │  └─ Act/
│  │     ├─ DispositionPlanner.cs     # 机理 → 动作映射
│  │     ├─ ProcessTerminator.cs      # 结束 + 恢复
│  │     └─ SnapshotStore.cs          # 处置前快照
│  ├─ HotkeyTriage.App/               # WinForms UI
│  │  ├─ MainForm.cs                  # 主界面
│  │  ├─ ResultCard.cs                # 结果卡片控件
│  │  └─ TrayIcon.cs
│  └─ HotkeyTriage.Tests/
└─ build/
   └─ publish.ps1
```

**分层原则**：`Core` 不引用任何 `System.Windows.Forms`，所有 Win32 调用走 `Interop`。这样归因逻辑可以完全用单测覆盖，UI 层只负责呈现。

---

## 4. 模块规格

### 4.1 Probe 层 `HotkeyProbe`

**职责**：判定某组合键在系统层面是否已被注册。

```csharp
public enum ProbeResult { Free, Taken, Error }

public sealed class HotkeyProbe
{
    private const int HotkeyId = 0xBEEF;

    public ProbeResult Probe(uint modifiers, uint vk, out int errorCode)
    {
        NativeMethods.SetLastError(0);
        bool ok = NativeMethods.RegisterHotKey(IntPtr.Zero, HotkeyId, modifiers, vk);
        if (ok)
        {
            NativeMethods.UnregisterHotKey(IntPtr.Zero, HotkeyId);
            errorCode = 0;
            return ProbeResult.Free;
        }
        errorCode = Marshal.GetLastWin32Error();
        return errorCode == Win32Error.HotkeyAlreadyRegistered   // 1409
            ? ProbeResult.Taken
            : ProbeResult.Error;
    }
}
```

**P/Invoke 声明**

```csharp
[DllImport("user32.dll", SetLastError = true)]
internal static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

[DllImport("user32.dll", SetLastError = true)]
internal static extern bool UnregisterHotKey(IntPtr hWnd, int id);
```

**实现约束（踩坑点）**

| 陷阱 | 处理 |
|---|---|
| `RegisterHotKey` 与**调用线程**绑定 | 所有探测必须在同一 STA 线程串行执行，禁止并发 |
| `hWnd = IntPtr.Zero` 时注册到线程队列 | 无需消息泵即可保持注册，探测完成立即注销，不残留 |
| 同一 `id` 重复使用 | 必须先 `UnregisterHotKey`；异常路径用 `try/finally` 保证注销 |
| 探测期间可能被他人抢注 | 概率极低，忽略；但探测结果需在同一毫秒窗口内使用 |
| 非 1409 的失败（权限/保留） | 归为 `Error` 并**不参与判定**，避免假阳性 |

### 4.2 ProbeGrid 候选网格

与原型一致：13 组修饰键 × 63 个主键 = 828 个组合，覆盖游戏与日常软件冲突区。

```csharp
static readonly uint[] ModifierSets = {
    0, MOD_CONTROL, MOD_ALT, MOD_SHIFT, MOD_WIN,
    MOD_CONTROL|MOD_ALT, MOD_CONTROL|MOD_SHIFT, MOD_ALT|MOD_SHIFT,
    MOD_CONTROL|MOD_WIN, MOD_ALT|MOD_WIN, MOD_SHIFT|MOD_WIN,
    MOD_CONTROL|MOD_ALT|MOD_SHIFT, MOD_CONTROL|MOD_ALT|MOD_WIN
};
```

裸字母/数字键不属于全局热键范畴，仅 `F1–F12` 允许无修饰。**828 次探测实测耗时 < 1 秒**，可全量跑，不需要增量优化。

### 4.3 ProcessScanner

```csharp
public record ProcInfo(int Pid, string ExeName, string FullPath);

public IReadOnlyList<ProcInfo> Scan()   // CreateToolhelp32Snapshot + QueryFullProcessImageNameW
```

- 用 `PROCESS_QUERY_LIMITED_INFORMATION` 打开进程（避免对受保护进程请求过高权限而失败）
- 路径获取失败时不中断，降级为只用 exe 名
- **不做**签名校验（`WinVerifyTrust` 单次 10–50 ms × 200 进程 = 太慢，移到 P1 异步补充）

### 4.4 ProcessGuard（安全边界的代码级实现）

这是**唯一允许决定"能否动手"的地方**，其他模块不得绕过。

```csharp
public enum RiskLevel { Safe, Caution, Forbidden }

public RiskLevel Evaluate(ProcInfo p)
{
    if (AntiCheatBlacklist.IsMatch(p.ExeName)) return RiskLevel.Forbidden;   // 反作弊
    if (SystemCritical.IsMatch(p.ExeName))     return RiskLevel.Forbidden;   // 会话关键
    var kb = _kb.Find(p.ExeName);
    if (kb is null)                            return RiskLevel.Caution;
    return kb.SafeToKill ? RiskLevel.Safe : RiskLevel.Caution;
}
```

```csharp
static readonly string[] AntiCheatBlacklist = {
    "easyanticheat", "eac_launcher", "start_protected_game",
    "becservice", "beclient", "beservice", "_be",
    "vgc", "vgk", "vgtray",                              // Vanguard
    "ace-base", "sgguard", "ace-game",                    // 腾讯 ACE
    "mhyprot", "mhypbase",                                // 米哈游
    "nprotect", "gamemon", "xigncode", "x3.xem",
    "javelin", "ricochet", "pb", "pbsvc", "punkbuster"
};

static readonly string[] SystemCritical = {
    "system", "smss", "csrss", "wininit", "winlogon", "services",
    "lsass", "dwm", "explorer", "svchost", "fontdrvhost", "ctfmon"
};
```

> `explorer` 列入 Forbidden 的原因：它是大量 `Win+*` 热键的注册者，但结束它会摧毁整个桌面。**必须拦住**——这是最容易犯的错。

### 4.5 KnowledgeBase

```csharp
public sealed class KnowledgeBase
{
    Dictionary<string, KbEntry> _byExe;              // 小写 exe 名 → 条目
    Dictionary<string, List<string>> _byCombo;       // "ALT+A" → [exe...]

    public IReadOnlyList<KbEntry> LookupByCombo(string combo);
    public KbEntry? Find(string exeName);
    public static KnowledgeBase Load(Stream json);
}
```

**条目字段**

```jsonc
{
  "weixin.exe": {
    "vendor": "腾讯",
    "hotkeys": ["Alt+A"],                    // "*" 表示钩子型，任何键都可能
    "category": "social",
    "disableHint": "设置 → 快捷按键 → 截屏快捷键",
    "safeToKill": true,
    "restartAfterGame": true
  }
}
```

**反查索引构建**：加载时把 `hotkeys` 展开成 `combo → exe` 倒排表，归因时 O(1) 查询。

**热更新策略**（P1）：启动时异步拉取远端 `kb.json`，比对 `version` 字段，成功后原子替换本地文件；失败静默降级用内置版本。内置版本必须永远可用。

### 4.6 AttributionEngine

归因主流程，伪代码即规格：

```
function Attribute(combo):
    m, vk = Parse(combo)

    # 1. 系统保留键：铁定不能动，直接返回
    if (m, vk) in SystemReservedRules:
        return SystemReserved(说明)

    # 2. 系统可关功能（Win+G / Win+V ...）
    if combo in SystemDisableableMap:
        return SystemDisableable(关闭路径)

    # 3. 知识库反查
    cands = KB.LookupByCombo(combo)
    running = cands.Filter(c => processMap.Contains(c.Exe))

    if running.Count == 1:
        return ThirdParty(running[0], confidence: 0.90, mechanism: Register)
    if running.Count > 1:
        return Ambiguous(running, confidence: 0.60)   # 列全部候选，让用户选

    if cands.Count > 0:
        return KnownButIdle("该软件装了但没在跑，可能是它的服务或另一软件")

    # 4. Win+ 前缀兜底归类
    if m & MOD_WIN:
        return SystemShell("Windows 壳热键（Explorer/DWM 注册）")

    return Unknown("已注册但无法归因")
```

**置信度模型**

| 场景 | 置信度 | 依据 |
|---|---|---|
| 知识库声明 + 进程运行 + 唯一候选 | 0.90 | 强证据 |
| 知识库声明 + 多候选 | 0.60 | 需用户裁决 |
| 仅 exe 名模糊匹配 | 0.50 | 弱证据，标注"可能" |
| 无匹配 | 0.00 | 转 L3 |

### 4.7 BisectSolver（L3 兜底）

**关键实现约束（由实验直接推出）**：二分排除**必须用「结束进程」，不能用「挂起」**。

原因：实验已证明挂起进程不释放热键（`taken → suspend → taken`）。若用挂起做二分，每轮测试结果都是"按键仍无效"，算法会永远判定"元凶不在这一组"，最终收敛到错误答案。

```csharp
public async Task<ProcInfo?> SolveAsync(string combo, IProgress<int> progress)
{
    var suspects = _procs
        .Where(p => _guard.Evaluate(p) != RiskLevel.Forbidden)
        .Where(p => p.Pid != Environment.ProcessId)
        .Where(p => p.Pid != _foregroundGamePid)      // 不碰前台游戏
        .Where(p => _kb.Find(p.ExeName)?.SafeToKill != false)
        .ToList();

    var terminated = new List<(ProcInfo, ProcessStartInfo)>();
    try
    {
        while (suspects.Count > 1)
        {
            var half = suspects.Take(suspects.Count / 2).ToList();
            foreach (var p in half) terminated.Add((p, Capture(p)));
            TerminateAll(half);

            // 人工确认：此时按一次目标键，用户回答"有没有反应"
            bool stillBlocked = await AskUserAsync(combo);

            if (stillBlocked) suspects = suspects.Except(half).ToList();
            else              suspects = half;
        }
        return suspects.FirstOrDefault();
    }
    finally
    {
        RestoreAll(terminated);                        // 无论成败都恢复
    }
}
```

**代价评估**：n 个候选需 ⌈log₂n⌉ 轮，每轮结束约 n/2 个进程。实际场景 `SafeToKill=true` 的用户态进程通常 < 20 个，4–5 轮收敛，耗时 60–120 秒。**这就是它只能当兜底的原因**。

### 4.8 DispositionPlanner

机理 → 动作的映射表，是"人话结论"的生成器。

| 机理 | 首选动作 | 备选 | 禁用动作 |
|---|---|---|---|
| 注册型 + 有配置路径 | `GuideSettings`（DeepLink 到设置页） | 结束进程 | 挂起 |
| 注册型 + 无配置路径 | 结束进程 + 记录恢复 | — | 挂起 |
| 系统可关功能 | `GuideSettings` | — | 结束进程 |
| 系统保留 | `RemapSuggestion`（建议换键） | — | 一切 |
| 未知 | 启动 BisectSolver | — | — |

> 注意 **首选永远是"引导改设置"而非"结束进程"**——结束进程是可逆的，但会打断用户正在做的事（比如正在传文件）。这是产品判断，不是技术判断。

---

## 5. 诊断流程状态机

```
        ┌──────────┐
        │  Idle    │  等待用户按键
        └────┬─────┘
             │ 捕获组合键（GetAsyncKeyState 轮询）
             ▼
        ┌──────────┐
        │ Probing  │  RegisterHotKey 试注册（< 10ms）
        └────┬─────┘
        ┌────┴─────┐
    Free│          │Taken
        ▼          ▼
  ┌──────────┐  ┌─────────────┐
  │ NoConflict│  │ Attributing │  KB 反查 + 系统规则
  │ 引导检查IME│ └──────┬──────┘
  └──────────┘    ┌────┴────┬─────────┐
              ThirdParty  System   Unknown
                  │         │         │
                  ▼         ▼         ▼
            ┌──────────┐ ┌───────┐ ┌─────────┐
            │ Proposed │ │Advise │ │Bisecting│
            └────┬─────┘ └───────┘ └────┬────┘
                 │ 用户确认              │
                 ▼                      ▼
            ┌──────────┐          ┌──────────┐
            │ Executing│          │ Resolved │
            └────┬─────┘          └──────────┘
                 ▼
            ┌──────────┐
            │ Reversible│ 快照已落盘，可一键恢复
            └──────────┘
```

**按键捕获的实现选择**：用 `GetAsyncKeyState` 每 10 ms 轮询，**不用** `WH_KEYBOARD_LL` 钩子。原因：

1. 全局热键被占用时，本进程窗口根本收不到 `WM_KEYDOWN`（键被系统吃掉），所以普通消息捕获无效；
2. 低级钩子虽能抢在热键匹配之前看到按键，但会引入一个全局钩子——这是反作弊敏感特征，与"零注入零驱动"的定位冲突；
3. 轮询无任何系统副作用，代价只是 10 ms 精度，对"按一下组合键"完全够用。

---

## 6. UI 规格

### 6.1 主界面（单页，无导航）

```
┌────────────────────────────────────────────┐
│  HotkeyTriage                              │
│                                            │
│      按下那个没反应的快捷键                  │
│      ┌──────────────────────────────┐      │
│      │   Ctrl + Alt + A             │      │  ← 实时显示当前按下的组合
│      └──────────────────────────────┘      │
│                                            │
│      松开后自动诊断 · 不修改任何系统设置      │
└────────────────────────────────────────────┘
```

设计约束：
- **只有一个交互入口**，没有"选择软件""高级选项"
- 组合键显示区在按下时实时变化，给即时反馈
- 底部常驻一行免责：不修改系统设置 / 可随时撤销

### 6.2 结果卡片（三种形态）

**形态 A：第三方占用**

```
┌────────────────────────────────────────────┐
│  Alt + A  已被占用                          │
│                                            │
│  微信  （腾讯）                             │
│  F:\SoftWare\WeChat\Weixin\Weixin.exe      │
│                                            │
│  ⓘ 这是「注册型」占用：挂起它没有用，        │
│    必须结束进程或改它的设置                  │
│                                            │
│  [ 去微信里关掉 ]  [ 结束微信 ]  [ 复制报告 ] │
└────────────────────────────────────────────┘
```

**形态 B：系统功能**

```
│  Win + G  被 Windows 自身占用               │
│  Xbox Game Bar 录制栏                       │
│  ⓘ 这是系统功能，可以关闭，但不能"杀进程"     │
│  [ 打开设置 ]                              │
```

**形态 C：无冲突（价值最高的一种）**

```
│  Ctrl + Shift + A  系统层面没有被占用        │
│                                            │
│  也就是说，问题不在系统热键。按可能性排序：   │
│  1. 输入法截获（中文输入法激活时最常见）      │
│  2. 低级键盘钩子型占用（AHK / 录屏 / 宏）    │
│  3. 游戏内键位未绑定，或 Raw Input 独占       │
│                                            │
│  [ 检查输入法状态 ]  [ 开始二分定位 ]        │
```

> 形态 C 是竞品全部缺失的能力。用户拿到这个结论，至少知道"别再折腾系统了，去查游戏设置"。

---

## 7. 数据契约

### 7.1 诊断结果

```jsonc
{
  "combo": "Alt+A",
  "probe": { "result": "taken", "errorCode": 1409, "elapsedMs": 2 },
  "attribution": {
    "kind": "third_party",          // third_party | system | system_disableable | unknown | free
    "mechanism": "register",        // register | hook | driver | system
    "confidence": 0.90,
    "evidence": "knowledge_base",   // knowledge_base | rule | bisect | none
    "owner": {
      "pid": 17816,
      "exe": "weixin.exe",
      "path": "F:\\SoftWare\\WeChat\\Weixin\\Weixin.exe",
      "vendor": "腾讯"
    }
  },
  "disposition": {
    "primary": "guide_settings",
    "hint": "设置 → 快捷按键 → 截屏快捷键",
    "fallback": "terminate",
    "risk": "safe"
  },
  "scan": { "gridSize": 828, "taken": 129, "noiseFiltered": 124 }
}
```

### 7.2 处置快照（恢复依据）

```jsonc
{
  "id": "20261020-122500-001",
  "createdAt": "2026-10-20T12:25:00+08:00",
  "action": "terminate",
  "target": {
    "pid": 17816,
    "exe": "weixin.exe",
    "path": "F:\\SoftWare\\WeChat\\Weixin\\Weixin.exe",
    "args": null,
    "cwd": "F:\\SoftWare\\WeChat\\Weixin"
  },
  "restored": false
}
```

落盘位置：`%LOCALAPPDATA%\HotkeyTriage\snapshots\`。启动时扫描未恢复快照，提示"上次结束的微信还没恢复，要现在启动吗"。

---

## 8. 构建与发布

```powershell
# build/publish.ps1
dotnet publish src/HotkeyTriage.App/HotkeyTriage.App.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:EnableCompressionInSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:DebugType=None -p:DebugSymbols=false `
  -o dist
```

| 事项 | 做法 |
|---|---|
| 清单权限 | `asInvoker`（P0 不需管理员，降低 UAC 摩擦与信任成本） |
| 代码签名 | EV 或 OV 证书，`signtool sign /fd SHA256` |
| SmartScreen | 签名后仍需累积声誉；提供 SHA256 校验值供用户自行核对 |
| 杀软误报 | 避免"枚举全部进程 + 试注册热键 + 结束进程"在短时间内连续发生；向主流厂商提交样本白名单 |
| 体积 | 预计 40–50 MB（压缩单文件） |

---

## 9. 测试方案

### 9.1 单元测试（Core 层，无需 UI）

| 用例 | 断言 |
|---|---|
| `Probe` 对已知空闲键 | 返回 `Free` |
| `Probe` 对自我注册的键 | 返回 `Taken`，`errorCode == 1409`（本仓库原型已验证） |
| `Parse("Ctrl+Alt+A")` → `Format` 往返 | 字符串一致 |
| `Parse("Ctrl++A")` / 空串 | 抛 `ArgumentException` |
| `Attribute` 对 `Alt+Tab` | 返回 `system`，且 `disposition.primary == "remap"` |
| `Attribute` 对 `Win+G` | 返回 `system_disableable` |
| `Attribute` 对 `Alt+A` + 微信运行 | 返回 `third_party`，`confidence >= 0.9` |
| `ProcessGuard.Evaluate(explorer.exe)` | 返回 `Forbidden` |
| `ProcessGuard.Evaluate(vgc.exe)` | 返回 `Forbidden` |
| 知识库反查索引 | 覆盖率 = 所有 hotkeys 条目数，无遗漏 |

### 9.2 集成测试（软件矩阵，手工 + 脚本辅助）

准备 20 款真实抢键软件，逐项验证：

| 软件 | 默认热键 | 期望判定 |
|---|---|---|
| 微信 | Alt+A | third_party / register |
| QQ | Ctrl+Alt+A | third_party / register |
| 钉钉 | Ctrl+Shift+A | third_party / register |
| Steam | Shift+Tab | third_party / register |
| NVIDIA App | Alt+Z | third_party / register |
| Xbox Game Bar | Win+G | system_disableable |
| Snipaste | F1 / F3 | third_party / register |
| AutoHotkey 脚本 | 任意 | **unknown**（钩子型，探测不到）← 关键负例 |
| 中文输入法激活 | Shift / Ctrl+Space | Probe 可能为 Free → 必须走形态 C |

> 最后两行是**必测负例**：它们验证"探测看不到的东西"，是产品是否诚实的分水岭。

### 9.3 回归基线

以本机实测数据为基线（可用 `src/hotkey_diag.py scan` 复现）：

```
扫描 828 个组合 → 命中 129 个
  ├─ 第三方已定位      1  (Alt+A → 微信)
  ├─ 系统可关功能      4  (Win+G/H/P/V)
  ├─ 未知需二分        9
  └─ 系统保留/壳热键  115  (已过滤)
对用户有用的结论：5 条（噪声 124 条，滤除率 96.1%）
```

**滤除率必须 ≥ 90%**，否则说明系统规则覆盖不足，用户会被噪声淹没。

---

## 10. 排期（15 人日 / 3 周）

| 天 | 任务 | 产出 |
|---|---|---|
| D1 | 工程骨架、P/Invoke 层、CI | 可编译空壳 |
| D2 | `HotkeyProbe` + `ProbeGrid` + 单测 | 探测可用，828 组合 < 1s |
| D3 | `ProcessScanner`（含路径降级） | 进程列表准确 |
| D4 | `KnowledgeBase` + 内置 40 条 + 反查索引 | 归因数据基础 |
| D5 | `SystemHotkeyRules`（保留键 / 可关功能） | 噪声滤除 ≥ 90% |
| D6 | `AttributionEngine` + 置信度模型 | 归因可用 |
| D7 | `ProcessGuard` 黑白名单 | 安全边界就位 |
| D8 | `MainForm` + 按键轮询捕获 | 主流程可跑 |
| D9 | `ResultCard` 三种形态 | 结果呈现完整 |
| D10 | `ProcessTerminator` + `SnapshotStore` | 处置与恢复可用 |
| D11 | 一键恢复 + 未恢复快照提示 | 可逆闭环 |
| D12 | `BisectSolver`（L3 兜底） | 未知场景可解 |
| D13 | 20 款软件矩阵联调 | 准确率达标 |
| D14 | 缺陷修复、异常路径加固 | 稳定 |
| D15 | 签名、打包、文档、发布 | 可分发 |

**关键路径**：D5 → D6 是决定成败的两天（噪声滤除与归因），不可压缩。D12 可延后到 P1。

---

## 11. 交付物清单

| 文件 | 说明 |
|---|---|
| `src/hotkey_diag.py` | **P0 参照实现**（Python，已跑通，可复现全部判定逻辑） |
| `src/hotkey_kb.json` | 知识库初版（40 条，可直接转为 C# 资源） |
| `probe/hotkey_lab.py` | 机理验证实验脚本（探测法 / 挂起有效性） |
| `Windows全局热键冲突诊断-方案设计.md` | 需求分析与方案取舍 |
| `Windows全局热键冲突诊断-实现方案.md` | 本文档 |

**Python 原型与 C# 实现的对应关系**：原型中的 `probe_hotkey` / `classify` / `KnowledgeBase` / `cmd_scan` 与本文档 §4.1 / §4.6 / §4.5 / §6.2 一一对应，C# 侧照此翻译即可，归因规则可直接复用。

---

## 12. 已知未决项

| ID | 问题 | 影响 | 计划 |
|---|---|---|---|
| H1 | 钩子型占用下探测返回 `free` 但按键无效，该假设尚未实测 | 决定形态 C 的文案准确性 | P1 前补实验 |
| H2 | 挂起钩子型占用者是否能让按键透传（~300ms 延迟） | 若成立，钩子型多一种处置手段 | P1 |
| H3 | 知识库 40 条对目标用户覆盖率未知 | 决定 L1 是否会频繁落到 L3 | 试用埋点 |
| — | 多用户会话 / 远程桌面下的热键隔离 | P0 假设单用户单会话 | P2 |
| — | 部分 OEM 工具热键由驱动层接管，用户态不可见 | 无法给出结论 | 在 UI 明示"可能为硬件驱动" |
