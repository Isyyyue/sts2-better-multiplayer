using Xunit;

// 模组配置的属性是 static 的（BaseLib 只收集静态属性），所以任何触碰
// BetterMultiplayerConfig 的测试都会共享同一份状态。并行执行时，
// 一个测试改了开关、另一个测试正在断言，结果取决于调度顺序——
// 表现为"偶尔挂一次"。
//
// 测试套件很小（约 150 项、半秒跑完），关掉并行没有代价。
[assembly: CollectionBehavior(DisableTestParallelization = true)]
