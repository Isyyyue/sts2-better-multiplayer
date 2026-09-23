<!-- 感谢提交。请把下面的模板填完整，尤其是「怎么验证的」一节。 -->

## 改了什么 / What changed

<!-- 一两句话说明。修 issue 的话写 "Fixes #123"。 -->

## 为什么这么改 / Why

<!--
根因是什么、为什么选这个方案。
★ 涉及状态同步的改动必须说明：会不会影响两端一致性？是否会改变已有消息的结构？
   本 Mod 采用锁步模型，各端独立模拟状态，私自改状态会导致不同步甚至掉线。
-->

## 怎么验证的 / How it was verified

<!--
说明实际做了什么，而不是"应该没问题"。
单元测试跑过什么、有没有实机验证、有没有做「撤掉修复看测试变红」的验证。
-->

## 检查清单 / Checklist

- [ ] `dotnet test` 全绿
- [ ] **没有**提交游戏程序集（`sts2.dll` / `GodotSharp.dll` 等）、BaseLib 或其他第三方 DLL
- [ ] 面向玩家的新增文案**同时**提供了中文与英文
- [ ] 若改了版本号：`BetterMultiplayer.csproj`、`BetterMultiplayer.json`、
      `BetterMultiplayerMod.Version`、`workshop/workshop.json` 四处已同步
- [ ] 若改了面向玩家的行为：`CHANGELOG.md` 与 `workshop/workshop.json` 的 `changeNote` 已更新
- [ ] 提交信息沿用 [Conventional Commits](https://www.conventionalcommits.org/zh-hans/)
      （`feat:` / `fix:` / `docs:` / `chore(release):` …）
