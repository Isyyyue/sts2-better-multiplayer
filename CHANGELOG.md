# 变更日志 / Changelog

本文件记录面向玩家的版本变更。格式参考 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)，
版本号遵循[语义化版本](https://semver.org/lang/zh-CN/)。

创意工坊的更新说明（`changeNote`）由 `workshop/workshop.json` 维护，与这里的版本节点一一对应；
每次发版两处都要更新。仓库自身的规范化文档见 [README](README.md) 与[维护者接手手册](docs/maintainer-handoff.md)。

---

## [0.6.5] - 2026-09-23

### 修复 / Fixed

- 交易报价被拒绝后，「锁定报价」按钮不再永久灰着、界面不再停在等待状态。
- 交易被拒的原因不再被紧随其后的状态更新覆盖，玩家能看到具体提示。
- 交易结算中途出错时，已交出的物品会被回滚，不再永久丢失。
- 休息处交易界面不再在刚打开时就被判定为结束。

### 新增 / Added

- 设置项「允许交易后重复遗物」（默认关闭）。原版规则下一笔交易若会让某一方持有两个同名遗物会被整笔拒绝，打开此项后放行。

> Fixed "Lock Offer" staying disabled and the screen hanging after an offer was rejected.
> Fixed the rejection reason being overwritten before the player could read it.
> Fixed items being lost for good when a trade failed partway through settlement.
> Fixed the Rest Site trade screen being treated as finished the moment it opened.
> Added an "Allow duplicate Relics after trading" setting, off by default.

## [0.6.4] - 2026-09-21

### 修复 / Fixed

- 开启惊喜金币共享后，联机进入游戏不再卡住。根因是 `TradeNetwork.IsHost` 在关卡加载早期裸访问 `NetService`，异常从 `Player.Gold` 的 setter 冒出去打断了加载流程。
- 金币同步现在会等待联机状态与关卡加载完成。
- 退出重进不再让共享金币按人数翻倍（求和不是幂等操作，改为读档时沿用存档值）。
- 客户端改为自己计算共享池，不再被动等待房主广播，消除两端分叉导致的掉线。
- 修正创意工坊 `minBranch` / `maxBranch` 方向写反导致的空区间。

> Fixed a hang when entering a multiplayer run with Surprise Shared Gold enabled.
> Gold synchronization now waits for the network and level loading to be ready.
> Fixed the shared pool multiplying by party size after leaving and reloading a run.
> Clients now compute the shared pool themselves instead of waiting for a host broadcast.
> Fixed an inverted Steam branch range in the Workshop configuration.

## [0.6.3] - 2026-09-19

### 新增 / Added

- 独立的 Mod 设置入口，不再混在 BaseLib 的模组配置列表里。
- 「惊喜模式」：全队金币合成一个池子。
- 设置页新增遗物交易开关，并把 101 个无副作用遗物改为默认可交易。
- 反馈按钮补上结果弹窗（此前点完没有任何可见反馈）。

### 变更 / Changed

- 遗物交易开关不再分类，改回单一总开关。
- 反馈入口从大厅页移到设置页。

## [0.6.2] - 2026-09-19

### 变更 / Changed

- 交易界面改为左右分栏布局。
- 统一仓库行尾为 LF。

## [0.6.0] - 2026-09-14

### 变更 / Changed

- 全面中英双语化。
- 大厅、房间会话与交易模块的代码整理与拆分。

## [0.5.6] - 2026-09-12

### 变更 / Changed

- 交易界面呈现细节调整。

## [0.5.5] - 2026-08-31

### 修复 / Fixed

- 恢复稳定的交易流程与密码房间加入。
- 补充游戏 API 兼容层的回归测试。

## [0.5.4] - 2026-08-26

### 修复 / Fixed

- 正式版与 `public-beta` 双分支兼容性修复。

## [0.5.3] - 2026-08-25

### 修复 / Fixed

- 多人交互相关修复。

## [0.5.2] - 2026-08-24

### 修复 / Fixed

- 交易可靠性修复。
- 恢复商店金币交易按钮的点击响应。

## [0.4.5] - 2026-08-17

### 修复 / Fixed

- 支持正式版与 `public-beta` 两套游戏程序集。
- 发布脚本增加 `-ValidateOnly` 模式，可在不调用 Steam API 的前提下完整校验。

## [初始版本] - 2026-07-25

### 新增 / Added

- 首次开源：密码房间、休息处物品交易、帮助队友锻造、商店金币交易。

---

[未发布]: https://github.com/Isyyyue/sts2-better-multiplayer/compare/v0.6.5...HEAD
[0.6.5]: https://github.com/Isyyyue/sts2-better-multiplayer/releases/tag/v0.6.5
