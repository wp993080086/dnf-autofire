# 配置与数据文件

本文说明 `config.json` 的字段、兼容策略，以及轨迹方案文件的存放方式。使用层面的说明见 [README](../README.md)，模块原理见 [ARCHITECTURE](ARCHITECTURE.md)。

## 数据文件

| 文件 | 位置 | 说明 |
| --- | --- | --- |
| `config.json` | EXE 同目录（无写权限时 `%APPDATA%\KeyMouseFactory\`） | 按键、鼠标、热键、模式、回放模式等全部设置 |
| `recordings.json` | 同上 | 录制方案（含轨迹点），最多 10 个方案；与配置文件分开，避免改设置时重写大文件 |
| `error.log` | 同上 | 异常与关键操作日志，只保留最近 200 行；托盘菜单「打开日志」可直接用系统默认程序打开 |

- 发生未处理异常时会弹窗显示摘要，并提供「打开日志」「复制详情」两个按钮；异常详情同时写入 `error.log`，连环触发只弹一次。
- 两个 JSON 都可以手工编辑，程序下次启动读取；`recordings.json` 解析失败时**只回退空列表、不会覆盖原文件**。
- 想清空方案直接删 `recordings.json`；想备份就把两个文件一起拷走。
- 配置默认与 EXE 同目录；若 EXE 所在目录无写入权限，则使用 `%APPDATA%\KeyMouseFactory\config.json`。
- 文件完全无法解析时以默认配置运行（文件保留原样以便排查），并记录到 `error.log`；下次保存配置时覆盖。

## 配置文件示例（config.json）

```json
{
  "ConfigVersion": "1.3.0",
  "Keys": [
    { "Key": "X", "IntervalMs": 50, "StopMode": "hold", "StopValue": 0, "Enabled": true },
    { "Key": "A", "IntervalMs": 100, "StopMode": "auto", "StopValue": 200, "Enabled": false }
  ],
  "MouseClicks": [
    { "Button": "L", "IntervalMs": 300, "StopMode": "infinite", "StopValue": 1, "Enabled": false },
    { "Button": "R", "IntervalMs": 300, "StopMode": "auto", "StopValue": 50, "Enabled": true },
    { "Button": "M", "IntervalMs": 100, "StopMode": "infinite", "StopValue": 1, "Enabled": false },
    { "Button": "X1", "IntervalMs": 300, "StopMode": "infinite", "StopValue": 1, "Enabled": false },
    { "Button": "X2", "IntervalMs": 300, "StopMode": "infinite", "StopValue": 1, "Enabled": false }
  ],
  "StartHotkey": "F6",
  "MouseStartHotkey": "F8",
  "RecordHotkey": "F9",
  "PlaybackHotkey": "F10",
  "PlayMode": "infinite",
  "PlayValue": 1,
  "PlayGapSec": 1,
  "Mode": "B",
  "AntiTyping": true,
  "ChatKey": "ENTER"
}
```

### 字段说明

- `Keys.Key`：键名，取值见 [README 的支持键位](../README.md#支持的键位)（`A`–`Z`、`0`–`9`、`N0`–`N9`、`SPC` 与符号键；小键盘数字与主键盘数字分开算）。手改成不支持的值时，下次启动会被清空，等用户在界面重填。
- `Keys.StopMode`：`hold`=松手停止（默认）/ `auto`=自动连按；`StopValue` 为自动连按的次数（`0` = 无限）。
- `MouseClicks.Button`：`L`=左键 / `R`=右键 / `M`=滚轮（按下）/ `X1`=侧键1 / `X2`=侧键2。旧配置只有三行时，加载会自动补出关闭状态的 X1/X2 两行。
- 侧键走的是原生 `WM_XBUTTONDOWN/UP`，不做键盘映射。部分鼠标驱动会把侧键改成键盘按键（如 PageUp/PageDown），此时钩子收不到侧键消息，需要在驱动里恢复成默认鼠标键。
- `MouseClicks.StopMode`：`infinite`=松手停止（默认）/ `auto`=自动点击；`StopValue` 为自动点击的次数（`0` = 一直点，旧版 `count` 读入后按自动点击处理）。
- `PlayMode`：`count`=回放 N 次 / `infinite`=一直放 / `duration`=定时 N 分钟；`PlayValue` 为对应的次数或分钟数。
- `PlayGapSec`：两轮回放之间的等待秒数（`0` = 不等待，默认 `1`，上限 999999）。
- `Mode`：`B`=拦截原生 / `A`=保留原生。
- `AntiTyping`：防打字干扰开关（默认 `true`）。开启后焦点离开游戏进程、或按下聊天键进入打字态时，连按自动暂停并放行物理键，切回游戏 / 再按聊天键恢复。关闭则回到全局拦截的原行为。
- `ChatKey`：聊天呼出键的规范名（默认 `ENTER`），可用 `ENTER` / `ESC` / `TAB`，或 `A`–`Z`、`0`–`9`、`SPC`、小键盘 `N0`–`N9` 与符号键。与连按目标键表相互独立——这三个编辑键绝不会变成连按目标。非法值加载时回落到 `ENTER`。

### 版本兼容

`ConfigVersion`：配置结构版本，等于写盘时的程序版本。加载时**不做整份重置**，而是逐字段合并：

- 认识的字段、值也合法 → 原样保留。
- 文件里缺的字段（新增项）→ 用默认值补上。
- 已废弃的字段（如早先的 `PlayGapMs`）→ 丢弃，回写后从文件里消失；`PlayGapMs` 会先换算成秒写进 `PlayGapSec`。
- 越界的值（如间隔 99999、热键写成 `F13`）→ 钳到允许范围或回落默认值。
- 每次启动都会把合并结果写回文件，并把 `ConfigVersion` 更新为当前程序版本。

## 相关文档

- [功能方案与落地记录](PLAN.md)
- [构建、测试与调试参数](DEVELOPMENT.md)
