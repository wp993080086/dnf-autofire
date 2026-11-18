# 构建、测试与调试

面向开发者：如何从源码构建、跑测试，以及只在 Debug 构建里存在的调试参数。使用方式见 [README](../README.md)。

## 环境要求

- Windows 10 / 11（x64）
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

## 从源码构建

```bash
dotnet publish -c Release -r win-x64 ^
  -p:PublishSingleFile=true ^
  -p:SelfContained=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=true
```

产物位于 `bin/Release/net8.0-windows/win-x64/publish/key-mouse-factory.exe`，为自包含单文件，目标机器无需安装 .NET 运行时。

## 单元测试

`tests/KeyMouseFactory.Tests` 是 xUnit 项目，覆盖键名映射、热键回退、鼠标侧键消息与注入标志、配置逐字段合并与越界钳制、录制方案脏数据钳制、轨迹坐标归一化、回放进度文本等纯逻辑（79 项）：

```bash
dotnet test tests/KeyMouseFactory.Tests/KeyMouseFactory.Tests.csproj
```

CI 在发布前会先以 Release 配置跑一遍测试，测试不通过就不会出包。

## 调试参数

只在 `Debug` 构建里存在（源码在 `Program.Debug.cs`，整段用 `#if DEBUG` 包住）；`Release` 构建里这些入口连字符串都不会进入程序集，正式发布的 exe 不响应任何调试参数，传入也只会正常启动主界面。

| 参数 | 说明 |
| --- | --- |
| `--screenshot <path.png> [n] [mouse\|record]` | 离屏渲染主界面并保存 PNG（可选拦截参数：额外新增 n 行；`mouse`/`record` 切到对应 Tab） |
| `--abouttest <path.png>` | 离屏渲染关于弹窗，并验证点击版本/作者文案会弹出关于窗口 |
| `--uitest <prefix>` | 窗口可见状态下切换按钮运行态/停止态，用 PrintWindow 抓图验证自绘控件重绘 |
| `--mouselayout` | 打印鼠标页每行控件的实际坐标与尺寸，核对行内排布 |
| `--mousetest <秒>` | 按当前配置开启鼠标连点，数秒后关闭退出（配 `KEYMOUSE_DRYRUN=1` 只跑调度、不注入点击） |
| `--recordtest <秒> <png>` | 自动开始录制、抓一张悬浮条截图、到时停止并退出（验证录制链路） |
| `--playtest <方案名> <值> [count\|infinite\|duration] [最长秒数] [png]` | 按指定模式开启回放，跑完/到点即退出（配 `KEYMOUSE_DRYRUN=1` 只跑时间轴、不注入鼠标；给 `png` 则 3 秒后抓一张回放状态条） |
| `--playoverlaytest <png> [第几次] [总次数]` | 离屏渲染回放状态条并保存 PNG，同时打印窗口扩展样式（确认鼠标穿透确实生效） |
| `--seltest` | 模拟点击第二个方案，验证单选互斥不会无限递归（需要 `recordings.json` 里至少 2 个方案） |
| `--hotkeytest` | 打印四个热键在当前页的置灰项，并硬选一个被占用的键，确认会被退回原值 |
| `--hotkeyfallbacktest` | 校验备用热键挑选规则（默认四键、大小写、全占满、无占用四种情况） |
| `--hotkeyoccupy <F键> <秒>` | 在独立进程里强占一个热键若干秒，用来验证本程序的自动换键 |
| `--instancetest listen\|notify` | `listen` 持单实例锁并隐藏窗口；`notify` 从另一个进程发广播，验证窗口会被叫到前台 |
| `--enginetest <A\|B> <秒>` | 按当前配置开启连按，数秒后关闭（配 `KEYMOUSE_DRYRUN=1` 只跑调度） |
| `--keytest` | 打印配置里收集到的连发目标键名与 VK |
| `--keynametest` | 校验键名 ↔ VK 映射的往返一致性、唯一性与非法值拒绝 |
| `--keycaptest` | 喂物理按键给键名框，验证捕获与不拦截规则 |
| `--xbuttontest` | 校验 X1/X2 侧键的消息解析、注入标志、配置映射与默认行 |
| `--blockedtest` | 模拟注入被系统拦截，验证托盘气泡与状态栏告警链路 |
| `--focustest` | 打印各控件的当前焦点与光标状态 |
| `--traytest` | 打印托盘菜单项，确认「打开日志」等出口都在 |
| `--tabstyletest` | 读 Tab 头实际样式位，确认 hover 双缓冲已开启（不闪） |
| `--logpathtest` | 确认日志路径，以及文件不存在时会自动创建 |
| `--crashshot <png>` | 离屏渲染异常提示窗并保存 PNG（用模拟异常，不真的让程序崩溃） |
| `--cursortest` | 把光标依次移到按钮/复选框/输入框/下拉/删除/禁用控件上，打印当前光标形状（Hand / IBeam / No / Default） |
| `--movetest <x> <y>` | 注入一次绝对定位并回报光标实际落点与 `SendInput` 返回值，用于确认当前环境允许注入 |
| `--closetest <png> [busy]` | 离屏渲染「关闭还是最小化到托盘」询问框并保存 PNG（`busy` 展示有任务在运行时的文案） |
| `--xclosetest <前缀>` | 自动模拟点 X：询问框出现 → 选最小化 → 再点 X → 选退出，日志记录各步结果 |
| `--savetest <间隔秒> [等待毫秒]` | 连改 5 次配置后立刻读文件（应仍是旧值）、等防抖到期再读（应已落盘）、最后设值后立即关窗（验证退出兜底） |

配 `KEYMOUSE_DRYRUN=1` 空跑时，`--enginetest` / `--mousetest` 结束会多打一行 `[probe]` 统计（次数、周期均值、最大间隔、标准差、迟到次数、按下保持时长），用来核对调度精度有没有退化。

## 相关文档

- [技术实现](ARCHITECTURE.md)
- [配置与数据文件](CONFIG.md)
- [功能方案与落地记录](PLAN.md)
