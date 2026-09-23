// 交易模块内部的命名空间统一在这里引入。
//
// 为什么要 global using：Trading/ 原本是一个扁平命名空间，所有类互相直接引用、
// 一个 using 都不需要。按职责拆成 Core / Sync / View / Flows 之后，跨组引用会非常多，
// 逐个文件补 using 既啰嗦又容易漏。这个模块本身就是高度内聚的一体
// （校验要读模型、事务要读校验、界面要读状态），用 global using 保持
// "模块内部互相可见"的语义最省事。
//
// ★ 注意 Messages 的特殊地位：它列在这里只是为了方便引用，
//   但那个目录下**消息类的命名空间不能改** ——
//   BaseLib 的 CustomMessageWrapper 用 `FullName` 排序来决定消息 ID，
//   改命名空间会打乱 ID 分配。详见 Trading/Messages/README.md。

global using BetterMultiplayer.Trading.Core;
global using BetterMultiplayer.Trading.Sync;
global using BetterMultiplayer.Trading.View;
global using BetterMultiplayer.Trading.Flows;
global using BetterMultiplayer.Trading.Messages;
