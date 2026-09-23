global using Xunit;

// Trading/ 按职责拆成了 Core / Sync / View / Flows 四个子命名空间。
// 主项目靠 Trading/GlobalUsings.cs 统一引入，但 global using **不跨项目**，
// 所以测试项目要自己再列一遍。
global using BetterMultiplayer.Trading.Core;
global using BetterMultiplayer.Trading.Sync;
global using BetterMultiplayer.Trading.View;
global using BetterMultiplayer.Trading.Flows;
global using BetterMultiplayer.Trading.Messages;
