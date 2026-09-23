# 贡献指南 / Contributing

感谢愿意花时间。本文件说明怎么把环境跑起来、以及提交改动时需要遵守的约定。

> 这份指南面向**改代码**的人。只想反馈问题的话，直接用
> [issue 模板](https://github.com/Isyyyue/sts2-better-multiplayer/issues/new/choose) 就行。

---

## 1. 准备环境

必须自己准备（**这些都不能提交进仓库**）：

| 依赖 | 来源 | 说明 |
|---|---|---|
| Slay the Spire 2 | Steam | 支持正式版 `v0.107.1` 与 `public-beta v0.111.0` |
| BaseLib | [Steam 创意工坊 `3737335127`](https://steamcommunity.com/sharedfiles/filedetails/?id=3737335127) | 最近验证 `v3.4.5` |
| .NET SDK 9 | Microsoft | |

游戏与 BaseLib 的**程序集**是第三方版权文件。仓库里只有本 Mod 的原创代码与素材，
`.gitignore` 已经排除构建产物，但**别手动 `git add` 任何 DLL** —— CI 会拦，但更希望你别试。

---

## 2. 构建与测试

项目默认路径是 `E:/steam/steam/steamapps/common/Slay the Spire 2`，
其他机器需要显式传路径：

```powershell
dotnet test .\tests\BetterMultiplayer.Tests.csproj -c Release `
  -p:STS2Path="D:/Steam/steamapps/common/Slay the Spire 2" `
  -p:BaseLibPath="D:/Steam/steamapps/workshop/content/2868840/3737335127/BaseLib/BaseLib.dll"

dotnet build .\BetterMultiplayer.csproj -c Release `
  -p:STS2Path="D:/Steam/steamapps/common/Slay the Spire 2" `
  -p:BaseLibPath="D:/Steam/steamapps/workshop/content/2868840/3737335127/BaseLib/BaseLib.dll"
```

**提交前必须 `dotnet test` 全绿。**

另外可以跑一次仓库卫生检查（不需要游戏文件，CI 也会跑）：

```bash
python tools/check-repo.py
```

---

## 3. ★ 改同步相关代码前先读这一节

本 Mod 采用**锁步（lockstep）模型**：网络同步的是输入与动作，各端**独立模拟**状态。
由此有两条硬约束：

1. **不能私自改状态。** 任何一端单方面修改 `RunState`（金币、牌组、遗物、药水）
   都会让两端算出不同结果，轻则弹出「数据不同步」，重则直接被踢出联机。
   正确做法是走已有的消息通道，让所有端执行同一段逻辑。
2. **不要给 `Player.Gold` 的 setter 加会抛异常的代码。** 金币在关卡加载早期就会被赋值，
   那时 `RunManager.Instance.NetService` 可能还没就绪。`SharedGoldSync` 当初就是
   因为裸访问它，抛出的异常从 setter 冒出去打断了加载流程，表现为「开了惊喜模式联机进去卡死」。

改动同步逻辑时，请说明**两端会各自执行什么**，并在 PR 描述里写清楚。

---

## 4. 代码与文案约定

- **面向玩家的文案必须中英双语。** 新增 `TextKey` 时同时补 `ModText` 里的中英两条；
  设置页还要补 `ConfigLocalization` 的键（`[ConfigHoverTip]` 要求 `.hover.title`
  和 `.hover.desc` **同时存在**，缺一个整条提示都不显示）。
- **本地化键名写错不会报错**，BaseLib 会静默回落成英文属性名。改完请跑测试 ——
  `ConfigLocalizationTests` 会用真实的 `Slugify` 逐项核对。
- **配置属性必须是 `static`**，否则 BaseLib 静默忽略，设置页会变成空白。
- **无分组的配置项必须声明在第一个 `ConfigSection` 之前**，否则会被塞进那个分组里。
- 注释写「为什么」，不写「是什么」。踩过的坑值得留一条注释说明，
  这个仓库里已经有几处这样的注释，照着来就行。

---

## 5. 提交信息

沿用 [Conventional Commits](https://www.conventionalcommits.org/zh-hans/)：

```
feat(config): add relic trading settings page
fix(trade): stop stalled confirmations and item loss on failed trades
docs: clarify bilingual mod support
chore(release): publish 0.6.5 trade reliability update
```

常用类型：`feat` / `fix` / `docs` / `chore` / `test` / `ui` / `release`。

**正文写清根因**，不要只写「修了 bug」。半年后回看，只有根因是有用的。

---

## 6. 发版

发版流程比较长（双分支验证、版本号四处同步、创意工坊更新说明、上传后核验），
完整步骤见[维护者接手手册](docs/maintainer-handoff.md)。简要顺序：

1. 更新版本号（**四处**）：`BetterMultiplayer.csproj`、`BetterMultiplayer.json`、
   `BetterMultiplayerMod.Version`、`workshop/workshop.json` 的 `changeNote`
2. 更新 `CHANGELOG.md`
3. 提交（`chore(release): publish X.Y.Z ...`）
4. 构建 —— **提交之后必须重新构建**，因为 DLL 的产品版本里含 HEAD commit
5. 上传创意工坊，用 Steam 公开 API 核实 `time_updated`
6. 推送 GitHub

`python tools/check-repo.py` 可以帮你在第 3 步之前抓出版本号漏改、CHANGELOG 缺条目、
分支区间写反这类问题。
