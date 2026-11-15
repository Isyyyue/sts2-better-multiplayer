# 更好的联机 / Better Multiplayer

[![Steam 创意工坊](https://img.shields.io/badge/Steam-创意工坊-1b2838?logo=steam&logoColor=white)](https://steamcommunity.com/sharedfiles/filedetails/?id=3768337454)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![Game](https://img.shields.io/badge/Slay%20the%20Spire%202-v0.113.1%20%7C%20public--beta-blue)](docs/maintainer-handoff.md)
[![Version](https://img.shields.io/badge/version-0.7.3-informational)](CHANGELOG.md)

保留游戏原生好友联机，增加三项玩法：

> Keeps native friends multiplayer and adds three gameplay features:

- 篝火交易：交换卡牌、遗物和药水。
- 协助锻造：消耗自己的篝火行动，为队友升级一张牌。
- 商店金币交易：与队友交换金币。

## 构建

```powershell
dotnet build .\BetterMultiplayer.csproj -c Release
dotnet test .\tests\BetterMultiplayer.Tests.csproj -c Release
```

如果游戏或 BaseLib 不在默认路径，可传入 `STS2Path` 和 `BaseLibPath`。

## 发布

- GitHub：[Isyyyue/sts2-better-multiplayer](https://github.com/Isyyyue/sts2-better-multiplayer)
- Steam Workshop：[更好的联机 / Better Multiplayer](https://steamcommunity.com/sharedfiles/filedetails/?id=3768337454)

## 许可证

原创源代码与 Mod 素材使用 [MIT License](LICENSE)。
