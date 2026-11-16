# v1.2.0 功能方案：自动执行 + 鼠标轨迹录制回放

> 状态：待评审　|　基线：`v1.1.0`（master `181c37d`）　|　文档位置：`docs/PLAN-v1.2.0.md`
>
> 本文只描述实现方案，不含代码落地。实施按第 10 节分阶段推进，每个阶段可独立编译、独立验收。

---

## 1. 需求拆解

| 编号 | 需求原文要点 | 关键行为 | 与现有功能的关系 |
| --- | --- | --- | --- |
| R1 | 键盘自动按键：设置 1 个或多个按键 + 间隔毫秒；启动后按间隔按个不停，或按间隔按 N 次 | **定时触发**，不需要按住任何键 | 与"按键连发"同用 `SendInput` 扫描码路径，但触发模型不同 |
| R2 | 鼠标自动点击：左键 / 右键 / **中键** + 间隔毫秒；一直点或点 N 次 | **定时触发**，不需要按住鼠标 | 与"鼠标连点"同一个注入函数，需补中键常量 |
| R3 | 鼠标轨迹 + 点击录制，可存多个方案（方案A/方案B…）；回放可选一个方案，回放 N 次 / 一直 / 定时 N 分钟；开始与停止都支持快捷键 | **录制**（全局鼠标采样）→ **持久化** → **时间轴回放** + 循环/定时 | 全新能力，复用鼠标钩子与 `SendInput` |

三项都属于"全局钩子 + 定时注入"的一个家族，与现有两个引擎同源，架构上不需要推倒重来。

---

## 2. 可行性结论

**三项都可行，工作量集中在 R3。** 真正的难点不是"功能做不出来"，而是下面三件事，方案里必须正面处理：

1. **触发模型不一样，不能复用现有引擎。** 现有 `AutoFireEngine` / `MouseClickEngine` 的语义是"物理按住 → 引擎接管重复"。R1/R2 是"没人按键也要自己按"，是一个纯粹的定时器 + 注入器。硬塞进现有引擎会污染"按住"状态机，回归风险高。**结论：新增一个通用定时引擎，老引擎一行不动。**
2. **录制是高频事件源，低级钩子扛不住"重活"。** `WH_MOUSE_LL` 回调跑在安装钩子的线程（UI 线程），系统对回调有超时限制（`LowLevelHooksTimeout`，默认 300ms），超时会被**静默摘钩**。鼠标移动可达每秒数百次，回调里不能分配大对象、不能写文件、不能碰 UI。方案：回调里只做"距离/时间过滤 + 追加到预分配数组"，落盘放到停止录制时一次性做。
3. **回放的"坐标"有两层坑。** 一是 DPI 虚化（进程 SystemAware 时挂钩坐标与 `SendInput` 绝对坐标都是物理像素，必须保持一致）；二是多显示器虚拟桌面（含负坐标，必须用 `MOUSEEVENTF_VIRTUALDESK` + `SM_*VIRTUALSCREEN` 归一化）。

另外一个必须提前说清的预期管理：**轨迹回放能否在游戏内"生效"，取决于游戏怎么读鼠标。** DNF 的攻击靠键盘边沿触发（已有 40ms 下限结论），鼠标主要用于 UI 点击与少量拖拽；如果某个场景游戏走 Raw Input / DirectInput 读相对位移，注入的绝对坐标移动可能不被采信。这部分**只能真机验证，不能靠设计保证**，方案里会给降级路径。

---

## 3. 现状盘点与改造面

### 3.1 可直接复用的资产

| 资产 | 位置 | 复用方式 |
| --- | --- | --- |
| 扫描码注入 | `NativeInput.SendKey` / `TapKey` | 直接复用 |
| 鼠标键注入 | `NativeInput.SendMouseButton(bool left, bool up)` | 扩展为 `SendMouseButton(int button, bool up)` |
| 低级钩子骨架 | `KeyboardHook` / `MouseHook` | `MouseHook` 增加 Move 事件与中键 |
| 高精度调度 | `timeBeginPeriod(1)` + `Stopwatch` + `WorkerLoop` + `Sleep(1)` | 新引擎照抄这套骨架 |
| 自绘控件 | `AntButton / AntCheckBox / AntInput / AntNumber / AntSelect / FlatScrollBar` | 新页面全部复用，不新增控件类型 |
| 滚动容器 | `pnlContent + FlatScrollBar + WheelFilter` | 方案列表直接套用 |
| 配置 / 日志 / 托盘 / 热键 | `ConfigManager` / `Logger` / `NotifyIcon` / `RegisterHotKey` | 增量扩展 |

### 3.2 必须改的三处硬编码（P0 地基）

`MainForm.cs` 目前把"只有两个 Tab"写死了，加页面必须先拆：

| 锚点 | 现状 | 改造 |
| --- | --- | --- |
| `MainForm.cs:26-31` | `_tabIndex`、`keyPage`、`mousePage` 两个字段 | 改为 `_tabIndex` + `Control[] _pages` |
| `MainForm.cs:38` | `_tabWidths = new int[2]` | `_tabWidths = new int[TabTitles.Length]` |
| `MainForm.cs:640-653` | `for (int i = 0; i < 2; i++)` + `i == 0 ? "按键" : "鼠标"` | 遍历 `static readonly string[] TabTitles` |
| `MainForm.cs:687-691` | `keyPage.Visible = idx == 0; mousePage.Visible = idx == 1;` | 循环切 `_pages[i].Visible` |
| `MainForm.cs:319-344` | `RefreshRunCard` / `RefreshStatus` 按 `_tabIndex == 1` 二分 | 改为按 Tab 查表（引擎名、按钮文案、状态文案） |
| `MainForm.cs:694-708` | `SyncHotkeyUI` 只处理按键/鼠标 | 按 Tab 查表（热键标签、第二行控件显隐、提示文案） |
| `MainForm.cs:353-363` | 关闭判断只看两个引擎 | 扩到"任一引擎运行 / 录制中 / 回放中" |

窗口高度也需要调整（见 4.4）。

### 3.3 为什么 R1/R2 不复用老引擎

| 维度 | 老引擎（连发/连点） | 新需求（自动执行） |
| --- | --- | --- |
| 触发源 | 物理按下（钩子事件） | 启动按钮 / 热键 |
| 停止条件 | 松手 / 点满 N 次（每次按压重置） | 一直 / 累计 N 次 / 手动 |
| 状态量 | `Held` + `IsDown` + `NextDue` | `Remaining` + `IsDown` + `NextDue` |
| 结束时 | 回到待触发 | 引擎自动停止并通知 UI |

语义差别足够大，硬合并会让已稳定的 v1.1.0 行为产生回归。**新增 `AutoActionEngine`，两个老文件零改动。**

---

## 4. 总体设计

### 4.1 功能 → Tab → 引擎 → 热键

| Tab | 引擎 | 触发方式 | 主按钮 | 热键（默认） | 运行中锁定范围 |
| --- | --- | --- | --- | --- | --- |
| 按键（沿用） | `AutoFireEngine` | 按住目标键 | 开启/关闭连发 | `F6` | 按键页配置 |
| 鼠标（沿用） | `MouseClickEngine` | 按住左/右键 | 开启/关闭连点 | `F8` | 鼠标页配置 |
| **自动**（新） | `AutoActionEngine` | 开始按钮/热键，**无需按住** | 开启/关闭自动 | `F7` | 自动页配置 |
| **录制**（新） | `MacroRecorder` + `MacroPlayer` | 录制按钮/`F9`；回放按钮/`F10` | 开始/停止回放 | 录制 `F9`、回放 `F10` | 方案列表 + 回放参数 |

Tab 标题固定为 `按键 / 鼠标 / 自动 / 录制`（11pt 粗体，间距 24，估算总宽 ≈ 392px，右侧版本号从 580 向左排，不重叠）。

### 4.2 分层与数据流

```mermaid
flowchart LR
    UI[MainForm 四个页面] -->|Start/Stop| E1[AutoFireEngine]
    UI --> E2[MouseClickEngine]
    UI --> E3[AutoActionEngine]
    UI --> R[MacroRecorder]
    UI --> P[MacroPlayer]
    R -->|采样| H[MouseHook / WH_MOUSE_LL]
    E1 --> NI[NativeInput: SendInput 扫描码]
    E2 --> NI
    E3 --> NI
    P --> NI2[NativeInput: SendInput 绝对坐标+键]
    H -->|Move/Down/Up| R
    R -->|Stop: RecordProfile| S[RecordStore: recordings.json]
    S -->|Load| UI
    S -->|Play| P
    UI --> C[ConfigManager: config.json]
```

职责边界：
- **UI 只负责收集参数、开关引擎、轮询状态**（不参与实时调度）。
- **引擎/播放器只做定时注入**，不碰控件、不写文件。
- **存储层只做 JSON 读写与容错**，不认识 UI。

### 4.3 运行时状态机与互斥

```mermaid
stateDiagram-v2
    [*] --> Idle
    Idle --> KeyRun: 开启连发
    Idle --> MouseRun: 开启连点
    Idle --> AutoRun: 开启自动
    Idle --> Recording: 开始录制
    Idle --> Playing: 开始回放
    KeyRun --> Idle: 关闭
    MouseRun --> Idle: 关闭
    AutoRun --> Idle: 关闭/任务做完
    Recording --> Idle: F9/托盘停止
    Playing --> Idle: F10/定时到/轮次到
```

互斥矩阵（开始前校验，不满足则弹提示并拒绝）：

| 操作 \ 当前状态 | 连发中 | 连点中 | 自动中 | 录制中 | 回放中 |
| --- | --- | --- | --- | --- | --- |
| 开始连发 | - | 允许 | 键冲突时拒绝 | 允许 | 允许 |
| 开始连点 | 允许 | - | 允许 | 允许 | 允许 |
| 开始自动 | 键冲突时拒绝 | 允许 | - | 允许 | 允许 |
| **开始录制** | **拒绝** | **拒绝** | **拒绝** | - | **拒绝** |
| **开始回放** | 允许 | 允许 | 允许 | **拒绝** | - |

录制拒绝的理由：其他引擎会拦截物理点击/按键、或注入额外的移动与点击，录进去的方案是脏的，且用户无法分辨。

### 4.4 窗口尺寸与布局

高度从 400 → **470**，为新增页面留出垂直空间（宽度保持 600，横向行宽 528 复用现有排版）：

```
ClientSize = 600 × 470
┌────────────────────────────────────────────────────────────┐ y=2
│ 按键    鼠标    自动    录制              v1.2.0 · 鹏多多    │ 44
├────────────────────────────────────────────────────────────┤
│                                                            │
│   页面内容区 596 × 328（四个页面叠放，只显示当前页）           │
│                                                            │
├────────────────────────────────────────────────────────────┤ y=374 (2px 分隔)
│ [开启连发]                    │ 连发热键   [F6   ▾]        │
│ 当前控制：按键连发             │ 连发模式   [B 拦截原生 ▾]   │
│ 未开启                        │ B 模式全局拦截目标键…       │
└────────────────────────────────────────────────────────────┘ y=470
   ↑ 294 宽                         ↑ 300 宽（x=298 起，2px 竖分隔）
```

同步改动：`ClientSize`、`pnlMain.Size`、`page.Size`、两个底部面板 `Location.Y`、`OnPaint` 两条分隔线的坐标。原有按键/鼠标页内容坐标不动（页面变高只是下方留白变多，可在 P0 顺便把按键页行区域从 210 拉到 268，多显示 1-2 行）。

### 4.5 文件规划

| 文件 | 动作 | 内容 |
| --- | --- | --- |
| `AutoActionEngine.cs` | 新增 | 定时自动执行引擎（键盘按键组 / 鼠标键），一直 / N 次，全部完成后自动停 |
| `MacroRecorder.cs` | 新增 | 录制器：钩子采样、降噪、容量上限、宽限期、生成 `RecordProfile` |
| `MacroPlayer.cs` | 新增 | 回放器：时间轴调度、循环、定时、插值、停止时释放 |
| `RecordStore.cs` | 新增 | `recordings.json` 读写 + 模型 + 版本兼容 |
| `MainForm.Auto.cs` | 新增 | `partial class MainForm`：自动页构建与开关逻辑 |
| `MainForm.Record.cs` | 新增 | `partial class MainForm`：录制页构建、方案列表、录制/回放开关 |
| `NativeMethods.cs` | 修改 | 中键常量、`WM_MOUSEMOVE`、`MOUSEEVENTF_*` 绝对值/虚拟桌面、`GetSystemMetrics`、`SendInput` 批量封装 |
| `MouseHook.cs` | 修改 | 支持中键；新增 `Move` 事件（仅在订阅时回调，避免连点场景开销） |
| `AppConfig.cs` | 修改 | `AutoKeys` / `AutoClicks` / 三个新热键字段 |
| `MainForm.cs` | 修改 | Tab 化重构（3.2）、状态栏、热键注册与冲突校验、托盘菜单、关闭逻辑、调试入口 |
| `Program.cs` | 修改 | 调试参数扩展（`--screenshot <path> auto\|record`、`--autotest`/`--recordtest`/`--playtest`） |
| `README.md` / `CHANGELOG.md` | 修改 | 功能说明、配置示例、1.2.0 条目 |
| `dnf-autofire.csproj` | 修改 | `<Version>1.2.0</Version>`（发版时改） |

`MainForm.cs` 现在已经 1240 行，再加两个页面会破 2000 行。**新页面用 partial 类分文件**（`MainForm.Auto.cs` / `MainForm.Record.cs`），既有页面暂不拆分（避免无谓 diff），后续可另行拆成 `MainForm.Keys.cs` / `MainForm.Mouse.cs`。

---

## 5. R1：自动按键

### 5.1 交互（自动页上半区）

```
自动执行
[ ] 启用  按键  [ X A      ]  间隔  [ 100 ] 毫秒   [ 一直按 ▾ ]  [ 100 ]
[ ] 启用  按钮  [ 左键   ▾ ]  间隔  [ 100 ] 毫秒   [ 一直点 ▾ ]  [ 100 ]
提示：开启后无需按住，按设定间隔自动执行；任务全部做完自动停止。
```

- 键名输入框：允许 `A-Z`、`0-9`，**最多 5 个键**，支持 `XA`、`X,A`、`X A` 三种写法，解析时去重、转大写。非法字符在 `KeyPress` 里直接吞掉（复用现有 `KeyBox_KeyPress` 风格，但允许多字符）。
- **多个按键在同一 tick 一起按下、一起抬起**（一个 R1 任务的"1 次"= 这一组键各按一次）。
- 间隔：50–5000ms，步进 10，默认 100（与连发页一致）。
- 停止条件：`一直按` / `按 N 次`（N: 1–9999，步进 10，默认 100）；选"一直按"时隐藏次数框（复用 `ApplyMouseStopMode` 的显隐套路）。
- 运行中锁定：两行配置 + 热键下拉。

### 5.2 引擎设计（`AutoActionEngine`）

一个引擎、一个线程、一张任务表，同时承载键盘组与鼠标键：

```csharp
internal sealed class AutoActionEngine
{
    public enum Kind { Keys, Mouse }
    public enum StopMode { Infinite, Count }

    public readonly record struct AutoTask(
        Kind Kind, int[] Vks, int Button, int IntervalMs, StopMode Mode, int StopValue);

    public void Start(IReadOnlyList<AutoTask> tasks);
    public void Stop();
    public bool Running { get; }
    public int DoneCount(int index);   // 已执行次数，供 UI 轮询
    public event Action? Completed;    // 所有任务完成 → UI 恢复停止态
}
```

调度与现有引擎保持同一套数字（这是 DNF 帧检测的经验值，不要另起一套）：

```
period = max(IntervalMs, 40) * freq / 1000      // 有效间隔下限 40ms
hold   = clamp(IntervalMs / 2, 20, 30) * freq/1000  // 按下保持 20~30ms
按下（键盘：一组键用一次 SendInput 批量提交；鼠标：单键）
  → 等 hold → 抬起 → 若 Count 模式则 Remaining-- → 排下一次
```

任务级完成：`Count` 且 `Remaining == 0` → 该任务置 `Finished`，不再排期；**所有任务都 `Finished` 时引擎自动 `Stop()` 并触发 `Completed`**（UI 用 200ms 定时器轮询 `Running`，或 `Completed` 里 `BeginInvoke` 恢复按钮，二者取其一，推荐前者，简单不跨线程）。

### 5.3 边界处理

| 场景 | 处理 |
| --- | --- |
| 两行都没勾启用 | 弹提示"没有启用的自动任务"，不开引擎 |
| 键名输入为空/全非法 | 该任务视为未配置 → 未启用处理 |
| 自动键与正在跑的连发键重复 | 启动前校验，提示"键 X 已被连发占用，请先关闭连发或改用其他键"并拒绝 |
| 键盘组里含空格/中文 | 输入阶段就拦掉 |
| 停止时正处在"按下"状态 | `Stop()` 统一补发抬起（照抄现有 `Stop` 的释放逻辑），避免卡键 |
| 一直按模式 | 只有手动/热键能停；托盘气泡提示"按 F7 停止" |
| 自动键注入到别的窗口 | 全局注入无法限定窗口 → 提示文案写明"会作用于当前活动窗口"，并在 README 说明 |

---

## 6. R2：自动点击（含中键）

与 R1 同一个引擎（`Kind.Mouse` 任务），差别只在参数与常量补齐。

### 6.1 常量补齐（`NativeMethods.cs`）

| 常量 | 值 | 用途 |
| --- | --- | --- |
| `WM_MBUTTONDOWN` / `WM_MBUTTONUP` | `0x0207` / `0x0208` | 钩子识别中键 |
| `VK_MBUTTON` | `0x04` | 键码 |
| `MOUSEEVENTF_MIDDLEDOWN` / `MIDDLEUP` | `0x0020` / `0x0040` | 注入中键 |
| `WM_MOUSEMOVE` | `0x0200` | 录制采样 |
| `MOUSEEVENTF_MOVE` / `ABSOLUTE` / `VIRTUALDESK` | `0x0001` / `0x8000` / `0x4000` | 回放定位 |

`NativeInput.SendMouseButton(bool left, bool up)` 改为 `SendMouseButton(int button, bool up)`（`1=左 2=右 4=中`），内部用字典映射 flag。**注意：`MouseClickEngine` 现有两处调用要同步改参数，这是本方案唯一会碰到老引擎文件的地方，改完必须回归连点功能。**

### 6.2 参数

- 按钮：左键 / 右键 / 中键（下拉）。
- 间隔：50–10000ms，步进 10，默认 100（比连点页的 100 下限更低，因为自动点击不依赖"按住"判定，40ms 引擎下限仍是硬底）。
- 停止：一直点 / 点 N 次（1–9999，默认 100）。
- 与 R1 的键盘任务可**同时勾选**，各自独立间隔，同一线程调度，互不阻塞（每个任务独立 `NextDue`）。

---

## 7. R3：鼠标轨迹录制与回放

这是本期最重的一块，拆成"采—存—放"三段。

### 7.1 录制链路

```
物理鼠标 → WH_MOUSE_LL（UI 线程，极轻）
        → 过滤（时间/距离/宽限期/自窗口）
        → 追加到预分配 List<RecPoint>（lock，仅 Add）
        → 停止录制
        → 一次性生成 RecordProfile → RecordStore.Save() → 刷新列表
```

**钩子回调内只做三件事**（这是防"静默摘钩"的关键）：

1. 取 `MSLLHOOKSTRUCT.pt / time`；
2. 与上一个采样点比较：`dt < 8ms && dist < 2px` → 丢弃；`dt < 40ms && dist < 1px` → 丢弃；
3. `lock (_sync) { _points.Add(...) }`（`List` 预分配 capacity = 60000，避免扩容抖动）。

**不做的事**：不写文件、不更新 UI、不发消息、不 LINQ、不 ToString、不分配引用类型对象。

采样参数（可作为常量，v1.2.0 不暴露）：

| 参数 | 值 | 说明 |
| --- | --- | --- |
| 最小采样间隔 | 8ms | 约 125 点/秒上限，够还原手速 |
| 最小移动距离 | 1–2px | 抖动噪声过滤 |
| 启动宽限期 | 300ms | 忽略"点击开始录制"那一下 |
| 单次时长上限 | 10 分钟 | 到点自动停并提示 |
| 单次点数上限 | 60000 点 | 到点自动停并提示 |
| 事件类型 | Move / LDown / LUp / RDown / RUp / MDown / MUp | 滚轮本期不录 |

**注入事件过滤**：现有 `MouseHook` 已按 `LLMHF_ANY_INJECTED` 过滤，自己的注入不会被录进去——这条要继续保证（回放时若录制钩子还挂着，也不会自激）。

**为什么录制时窗口要隐藏**：录制中用户必须能点"停止"，如果窗口在最上层，这几下点击就被录进方案里了。所以：**开始录制 → 立即 `Hide()` 到托盘 + 300ms 宽限期**，停止只能靠 `F9` 热键或托盘菜单；停止后自动 `ShowWindow()`。这样既不用做"忽略本窗口区域"的复杂判断（会漏录用户真想点那块屏幕的情况），交互也干净。

### 7.2 存储

新增 `recordings.json`（与 `config.json` 同目录，走 `AppPaths.DataDir`）。**不塞进 config.json**：方案可能上万点，会拖慢每次热键/勾选变更时的配置落盘。

```jsonc
{
  "version": 1,
  "profiles": [
    {
      "id": "8f2c1e7a",
      "name": "方案A",
      "createdUtc": "2026-11-16T10:20:00Z",
      "durationMs": 12480,
      "screen": { "x": 0, "y": 0, "w": 1920, "h": 1080 },   // 录制时虚拟桌面，用于分辨率变化提示
      "points": [
        { "t": 0,    "k": 0, "x": 960, "y": 540 },   // k: 0=Move 1=LDown 2=LUp 3=RDown 4=RUp 5=MDown 6=MUp
        { "t": 120,  "k": 1, "x": 1210, "y": 380 }
      ]
    }
  ]
}
```

- 字段名压到 1–2 字符，60k 点约 2MB，可接受；`WriteIndented = false` 只为这个文件。
- 读取容错对齐现有 `ConfigManager.Load`：解析失败 → 返回空库 + `Logger.Error`，**不覆盖原文件**（避免用户方案被静默清空）。
- `version` 预留：将来加滚轮/键盘事件时按版本升级，旧文件照读。
- 上限 20 个方案，超出时开始录制前提示"请先删除旧方案"。

**方案命名**：录制结束自动取"第一个未占用的 `方案A…方案Z`，用尽后 `方案27`"。名称可在列表行内直接改（`AntInput`，失焦即存）。

### 7.3 回放链路

```
MacroPlayer.Start(profile, repeatMode, value)
  → 线程：Stopwatch 时间轴
      for each loop:
        for each point:
          等到 t（Sleep(1) 轮询，timeBeginPeriod(1)）
          Move  → SendInput(MOVE|ABSOLUTE|VIRTUALDESK)
          点击  → SendInput(按下) / SendInput(抬起)
        loop 间隔 300ms（常量 LoopGapMs）
        Count 模式：轮次 -1，为 0 结束
        Duration 模式：now - start >= N*60000 → 立即结束（允许截断在半轮）
```

细节约定：

| 项 | 规则 |
| --- | --- |
| 定位方式 | 用 `MOUSEEVENTF_MOVE` + `ABSOLUTE` + `VIRTUALDESK` 三标志组合，不用 `SetCursorPos`（不产生标准输入事件、且不同 DPI 感知下语义不一致） |
| 归一化 | `dx = round((x - vx) * 65535 / (vw - 1))`，`dy` 同理；`vx/vy/vw/vh` 取自 `SM_XVIRTUALSCREEN / Y / CXVIRTUALSCREEN / CYVIRTUALSCREEN`（多屏含负坐标，`VIRTUALDESK` 必带，误差 ±1px） |
| 平滑插值 | 相邻两个 Move 的 `dt > 32ms` 且距离 > 4px 时，按 ≤16ms 步长线性插点（对齐 60fps 轮询节奏）。可用常量开关，默认开 |
| 点击保持 | 录制的 down/up 间隔 < 20ms 时抬到 20ms（游戏帧边沿检测下限，与现有引擎一致）；不改变点击次数与相对顺序 |
| 停止 | 每个点检查 `_stop`；`Stop()` 时补发所有处于按下状态的键/鼠标键 |
| 轮次/进度 | 暴露 `Loop`、`ElapsedMs`、`ProfileName`，UI 200ms 轮询显示"第 2 轮 · 00:41" |
| 结束 | `Finished` 事件 + `Running=false`，UI 恢复按钮并弹托盘气泡 |

### 7.4 坐标、DPI、多屏

| 风险 | 说明 | 对策 |
| --- | --- | --- |
| DPI 虚化 | 进程 SystemAware 且系统缩放 125% 时，若一处用逻辑坐标一处用物理坐标就会偏移 | 全程物理像素：钩子 `pt` 本来就是物理像素；`SendInput` 绝对值在 DPI-aware 进程下也按物理像素解释。**不要引入 `Screen.PrimaryScreen.Bounds`（那是逻辑坐标）** |
| 多显示器 | 副屏在主屏左侧时 X 为负，普通 `SM_CXSCREEN` 归一化会算错 | 用 `SM_*VIRTUALSCREEN` + `MOUSEEVENTF_VIRTUALDESK` |
| 录制后改了分辨率/缩放 | 回放位置整体偏移 | 已保存 `screen` 快照；回放前若与当前虚拟桌面不一致，状态栏提示"录制分辨率 1920×1080，当前 2560×1440，回放位置可能有偏移"（仅提示，不阻止） |

### 7.5 交互（录制页）

```
鼠标录制回放
┌ 方案列表（528×180，超过 5 行滚动）────────────────────────────┐
│ [✓] 方案A   12.4s · 386 点                        删除      │
│ [ ] 方案B   03.1s ·  92 点                        删除      │
└──────────────────────────────────────────────────────────────┘
[ ● 开始录制 ]   未录制 / 录制中 00:03.2 · 128 点

回放   一直放 ▾   [   1 ] 次      已选：方案A · 12.4s

提示：录制中窗口隐藏到托盘，按 F9 结束；F10 开始/停止回放，回放中窗口同样隐藏。
```

- 方案选择：`AntCheckBox` 实现**单选**（勾一个自动取消其他），不加新控件类型。
- 名字：行内 `AntInput`，失焦或回车即存。
- 删除：行尾 `AntButton(Text, Danger)`，点击后即时删除（方案可重录，不做二次确认弹窗，但状态栏提示"已删除 方案B"）。
- 回放设置：`[模式下拉] [数值框] [单位]`，模式 = `回放 N 次` / `一直放` / `定时 N 分钟`；"一直放"隐藏数值框；N 次范围 1–9999；分钟范围 1–600，步进 1。
- 主按钮（左下）跟随 Tab：`开始回放 / 停止回放`；`lblTarget` = "当前控制：轨迹回放"。
- 未选方案点开始 → 提示"请先勾选一个方案"。
- 回放中锁定：列表（名称、勾选、删除）+ 模式/数值 + 录制按钮。
- 录制中锁定：列表全部 + 回放设置 + 主按钮置灰。

### 7.6 失败模式与降级

| 场景 | 表现 | 处理 |
| --- | --- | --- |
| 游戏走 Raw Input 读相对位移 | 回放时光标动了，游戏内视角没动 | 无解，界面提示"若游戏内无响应，说明该场景使用原始输入，回放仅在系统层生效" |
| 独占全屏 + 高权限游戏 | 注入被 UIPI 丢弃 | 程序已 `requireAdministrator`，与连发同理；仍失败则提示以管理员运行 |
| 方案被删时正在回放 | 已开始的回放持有快照 | 回放期间禁止删除（列表锁定），从根上避免 |
| 录制中切换了分辨率/显示器 | 采样坐标跳跃 | 记录 `screen` 快照，不做特殊处理，回放时给提示 |
| 托盘退出时正在录制 | 数据丢失 | `ExitApp` 前先 `StopRecording(save: true)` |
| 内存/文件异常 | 大方案写盘失败 | `try/catch` + `Logger.Error` + 状态栏红字提示，内存中的方案保留（可重试删除/重录） |

---

## 8. 热键与全局开关

| 热键 | 默认 | 行为 | 配置位置 |
| --- | --- | --- | --- |
| 连发热键 | `F6` | 开关按键连发 | 按键页 → 设置卡第一行 |
| 连点热键 | `F8` | 开关鼠标连点 | 鼠标页 → 设置卡第一行 |
| 自动热键 | `F7` | 开关自动执行 | 自动页 → 设置卡第一行 |
| 录制热键 | `F9` | 开始 / 结束录制 | 录制页 → 设置卡第一行 |
| 回放热键 | `F10` | 开始 / 停止回放 | 录制页 → 设置卡**第二行**（该 Tab 下第二行原本是"连发模式"，改为回放热键；模式行仅按键页显示） |

设置卡第二行复用策略（`SyncHotkeyUI` 按 Tab 查表）：

| Tab | 第一行 | 第二行 | 底部提示 |
| --- | --- | --- | --- |
| 按键 | 连发热键 | 连发模式 | B 模式全局拦截目标键；间隔 50-5000ms |
| 鼠标 | 连点热键 | 隐藏 | 连点热键全局开关鼠标连点… |
| 自动 | 自动热键 | 隐藏 | 开启后无需按住，按间隔自动执行 |
| 录制 | 录制热键 | 回放热键 | 录制/回放均可用热键一键开始与停止 |

**热键冲突校验（新增，必须做）**：5 个热键共用一个 F1–F12 池，现在可能重复（比如用户把自动也设成 F6）。`SaveConfig` 前检测重复 → 拒绝保存 + 状态栏提示"热键 F6 已被连发占用，请更换"；`RegisterHotkeys` 对重复项跳过并提示。

---

## 9. 配置与兼容

### 9.1 `config.json` 增量（向后兼容，旧文件缺字段走默认值）

```jsonc
{
  "Keys": [ ... ],
  "MouseClicks": [ ... ],
  "AutoKeys":   { "Keys": "XA", "IntervalMs": 100, "StopMode": "infinite", "StopValue": 100, "Enabled": false },
  "AutoClicks": { "Button": "L", "IntervalMs": 100, "StopMode": "infinite", "StopValue": 100, "Enabled": false },
  "StartHotkey": "F6",
  "MouseStartHotkey": "F8",
  "AutoStartHotkey": "F7",
  "RecordHotkey": "F9",
  "PlaybackHotkey": "F10",
  "Mode": "B"
}
```

- 老配置（1.0/1.1）加载后 `AutoKeys/AutoClicks` 为 `null` → `LoadConfigToUi` 里按默认值初始化（沿用现有 `MouseClicks` 缺失时的兜底写法，`MainForm.cs:450-464` 是正面样板）。
- 不做版本号字段，不写迁移代码，靠"缺省即默认"。

### 9.2 三个状态面同步

进程内所有入口必须同时更新 UI（历史坑：`DebugShowMouseTab` 曾漏 `RefreshRunCard`）：

| 入口 | 必做 |
| --- | --- |
| 主按钮 | `RefreshRunCard()` + `SetXxxUiLocked()` + `RefreshStatus()` |
| 热键 `WM_HOTKEY` | 同上（不依赖当前 Tab） |
| 托盘菜单（新增录制/回放项） | 同上 + `Hide/ShowWindow` |
| 引擎自动结束（任务做完/定时到） | 200ms 定时器发现 `Running` 变 `false` → 复原 UI + 托盘气泡 |
| 关闭窗口 / 退出 | 依次 `Stop` 所有引擎 + `StopRecording(save:true)` + `StopPlayback` |

托盘菜单建议：

```
显示窗口
────────────────
开始录制 / 停止录制      （文案随状态变）
开始回放 / 停止回放      （未选方案时置灰）
────────────────
退出程序
```

---

## 10. 实施计划

| 阶段 | 内容 | 产物 | 验收标准 |
| --- | --- | --- | --- |
| **P0 地基** | Tab 数组化重构、窗口 470、状态栏/RefreshRunCard/SyncHotkeyUI 查表化、关闭逻辑、常量补齐、`SendMouseButton` 改签名 | 4 Tab 壳子（后两个空页） | 三个旧功能行为不变；`--screenshot … mouse` 仍正常；连点回归通过 |
| **P1 自动执行** | `AutoActionEngine` + 自动页 + `F7` + 任务完成后自动复原 | 可用的 R1 + R2 | 一直按/按 N 次都准；N 次到点自动停；停止不卡键；与连发键冲突有提示 |
| **P2 录制** | `MouseHook` 扩展（Move/中键）、`MacroRecorder`、`RecordStore`、录制页列表、`F9`、托盘项 | 可录制、可管理方案 | 录 3 个方案、重启后还在；高频移动不掉点、不被摘钩；录制中窗口隐藏 |
| **P3 回放** | `MacroPlayer`（循环/定时/插值/停止释放）、`F10`、`RefreshRunCard` 联动 | 完整 R3 | N 次/一直/定时三种模式正确；停止立即生效；多屏与 125% 缩放位置准确 |
| **P4 打磨发版** | 真机复测、文案/README/CHANGELOG、截图、`Version=1.2.0`、tag | v1.2.0 | 五项功能同时可开不互相干扰；托盘/热键/配置全通 |

每阶段的"验收标准"就是提测清单，P0 必须先做，否则后面两个页面要塞进 596×258 会挤爆。

---

## 11. 测试计划

**自动按键**：单键间歇 / 多键同按（`XA`）/ 按 3 次数到即停 / 一直按手动停 / 间隔 50 与 5000 / 停止时正处按下态（不卡键）/ 空配置拦截 / 与连发同键冲突提示 / 输入非法字符被拦 / 配置重启回显 / 热键 F7 开关 / 运行中配置锁定。

**自动点击**：左/右/中三键各测 / 一直点 / 点 5 次即停 / 与键盘任务同时开 / 键盘先完成、鼠标继续 / 中键在浏览器（新标签页）与游戏内验证 / 停止释放不卡键。

**录制**：`F9` 起停 / 按钮起停 / 窗口自动隐藏与恢复 / 快速画圈（点数与降噪是否符合预期）/ 含左键、右键、中键各一次 / 10 分钟上限自动停 / 60000 点上限自动停 / 录制中按 `F9` 停止不丢点 / 其它引擎运行时拒绝录制 / 方案列表滚动、改名、删除、单选互斥 / 重启后方案仍在 / 损坏的 `recordings.json` 不覆盖原文件。

**回放**：N 次（2 次）/ 一直放后手动停 / 定时 1 分钟自动停（记录实际时长误差 ≤1s）/ 停止后光标位置与按下状态正常 / 回放中列表锁定 / 未选方案点开始被拒 / 多屏（副屏在左，负坐标）/ 125% 与 150% 缩放 / 4K 与 1080p 混用 / 录制后改分辨率有提示 / 回放中按热键立即停 / 连续启停 10 次不泄漏线程。

**并发与稳定性**：四功能全开跑 10 分钟 / 托盘退出时录制中能保存 / 关闭窗口时运行中隐藏到托盘 / 单实例保护仍有效 / 全程 `error.log` 无新增异常。

**调试入口扩展**（沿用现有 `--screenshot` / `--enginetest` 风格）：

| 参数 | 用途 |
| --- | --- |
| `--screenshot <path> [n] auto\|record` | 新页面离屏截图 |
| `--autotest <keys> <interval> <count>` | 开自动按键 N 次后自动退出，验证调度精度 |
| `--playtest <profileName> <loops>` | 回放当前第一个（或指定）方案 N 次后退出 |
| `--recordtest <ms>` | 不需要手也能验证：内置一段合成轨迹（代码里生成点）走一遍录制→存储→回放，无需人工操作 |

---

## 12. 本期不做（明确范围边界）

- 滚轮录制/回放、键盘事件录制（本期只按需求做"鼠标轨迹 + 点击"）
- 多方案同时回放、方案拼接、片段剪辑
- 变速回放（0.5x/2x）、单步调试、轨迹可视化编辑器
- 相对窗口坐标（窗口移动后仍能对齐）——需要额外记录窗口句柄与偏移，留到后续
- 方案云端同步/导入导出
- 把两个老引擎合并进统一调度器（收益低、回归风险高）

---

## 13. 待确认（需要你拍板）

1. **自动按键的键集合语义**：本方案是"一组键共享一个间隔、同 tick 一起按"。如果你想要"每个键独立间隔"（像连发页那样多行），自动页就要改成多行表格 + 共享停止条件。哪个更贴合你的用法？
2. **回放方案是否要支持多选连播**（勾 A 和 C，按顺序播）？本方案按需求取单选。
3. **录制时窗口隐藏到托盘**这个处理能接受吗？（好处：不会把"停止录制"的点击录进去；代价：录制中看不到计时，只能靠托盘气泡/结束后的提示。）
4. 热键默认值：自动 `F7`、录制 `F9`、回放 `F10`。你游戏里这几个键有没有冲突？
5. 窗口高度 400 → 470 是否可以接受？（不加高的话录制页的列表只能显示 3 行。）
6. 方案数量上限 20 个、单次录制 10 分钟 / 6 万点，够用吗？

---

## 附：关键改动锚点速查

| 位置 | 要做的事 |
| --- | --- |
| `MainForm.cs:71` | `ClientSize` 600×470 |
| `MainForm.cs:505` | `pnlMain.Size` 596×372 |
| `MainForm.cs:528-530` | 页面尺寸 596×328，新增 autoPage / recordPage |
| `MainForm.cs:540,571` | 底部两面板 Y=376 |
| `MainForm.cs:615-617` | `OnPaint` 分隔线 Y=374 / `Height-376` |
| `MainForm.cs:640-665` | Tab 自绘循环化 |
| `MainForm.cs:672-692` | Tab 点击：循环切页 + 两个 Refresh |
| `MainForm.cs:320-344` | `RefreshRunCard` / `RefreshStatus` 查表化，状态栏只列运行项 |
| `MainForm.cs:374-387` | `WM_HOTKEY` 增 3 个 ID（3=自动 4=录制 5=回放） |
| `MainForm.cs:391-425` | `SaveConfig` 增字段 + 热键重复校验 |
| `MainForm.cs:427-470` | `LoadConfigToUi` 增默认值兜底 |
| `MainForm.cs:472-498` | 注册/注销 5 个热键 |
| `MainForm.cs:917-932` | 托盘菜单增录制/回放项 |
| `MainForm.cs:1222-1239` | `WheelFilter` 改为作用于当前 Tab 的滚动区 |
| `MouseClickEngine.cs:86,109,115,134,140` | `SendMouseButton(bool,bool)` → `(int,bool)` 参数调用 |
| `Program.cs:15-63` | 新增调试参数分支 |
