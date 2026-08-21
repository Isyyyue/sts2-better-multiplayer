# 更好的联机 / Better Multiplayer

《杀戮尖塔 2》中英双语联机增强 Mod。它保留官方好友联机，同时新增：

> A bilingual Slay the Spire 2 mod that keeps official friend multiplayer and
> adds password rooms, Rest Site item trades, teammate smithing, and merchant
> gold trading over Steam lobbies and relay.

- 通过准确的房间名称和必填密码创建、加入公开房间，不显示公开房间列表。
- 使用 Steam Lobby 与 Steam Datagram Relay，不需要端口映射、樱花穿透或自建服务器。
- 篝火可交换卡牌、遗物和药水；每名玩家每个篝火只能成功交易一次，成交后仍可休息或锻造。
- 篝火可选择一名队友并使用官方锻造界面为其升级一张牌；消耗发起者自己的篝火行动，队友不消耗行动。
- 商店可反复交换金币，每笔由双方同时确认。
- 多人菜单可一键提交本 Mod 的脱敏诊断反馈，便于定位玩家端无法复现的问题。
- 双方确认后由房主程序自动校验并结算，不需要房主手动批准；消息带修订号与事务号，防止延迟和重复包复制物品。
- 房主掉线时沿用官方行为结束房间；本版不实现房主迁移。

## 环境

- Slay the Spire 2（最低版本 `0.107.1`；最近验证 `public-beta v0.111.0`）
- .NET SDK 9
- BaseLib（Steam 创意工坊物品 `3737335127`；最近验证 `v3.4.5`）

项目默认使用本机路径 `E:/steam/steam/steamapps/common/Slay the Spire 2`。其他电脑可在构建时传入 `STS2Path` 和 `BaseLibPath`。

## 构建

```powershell
dotnet build .\BetterMultiplayer.csproj -c Release
```

发布内容会生成在 `artifacts/staging/BetterMultiplayer`。测试安装时，把该目录复制到游戏的 `mods` 目录中。

如果游戏或 BaseLib 不在项目默认路径，可显式传入：

```powershell
dotnet build .\BetterMultiplayer.csproj -c Release `
  -p:STS2Path="D:/Games/Slay the Spire 2" `
  -p:BaseLibPath="D:/Games/BaseLib/BaseLib.dll"
```

运行测试：

```powershell
dotnet test .\tests\BetterMultiplayer.Tests.csproj -c Release
```

维护者可以使用 `tools/prepare-release.ps1` 一次完成测试、Release 构建、发布目录重建和 SHA-256 输出。完整的接手、核验与发布流程见[维护者接手手册](docs/maintainer-handoff.md)。

仓库不包含游戏、BaseLib 或其他第三方 DLL。构建者需要从官方来源自行安装这些依赖。更多设计细节见[架构说明](docs/architecture.md)。

## 诊断反馈与隐私

“发送反馈”只在玩家主动点击时向本项目的 Sentry 提交一次诊断事件，不会后台自动上传。事件仅包含本 Mod、游戏和 BaseLib 版本，本 Mod DLL 的 SHA-256，运行时与窗口尺寸，以及最近 30 分钟内的商店按钮输入阶段和脱敏 UI 状态。

反馈不包含截图、存档、原始日志、Steam ID 或昵称、大厅与房间信息、密码、IP 字段、本机路径、其他 Mod 列表或玩家输入的文本；不会写入磁盘等待以后重传。Sentry 的网络基础设施仍会像其他互联网服务一样在建立连接时看到来源 IP，但事件要求服务端不要推断或保存 IP。只有收到 Sentry 的 `2xx` 响应后，游戏内才显示“反馈已提交”和报告编号。

## 创意工坊

`workshop` 目录遵循 Mega Crit 官方 `sts2-mod-uploader` 格式。当前公开条目为 [Steam 创意工坊物品 3768337454](https://steamcommunity.com/sharedfiles/filedetails/?id=3768337454)。第一次上传前请确认 `BetterMultiplayer.json` 中的作者名，并替换或确认预览图，然后使用官方 `ModUploader.exe` 上传。

## 已知限制

- 官方后端已有重连消息与完整状态快照，但 `0.107.1` 客户端恢复流程仍标记为 `NotImplementedException`。本 Mod 不冒险实现中途自动重连。
- 房主掉线后由所有人退出，房主输入新的房间名称和密码重新创建房间，再选择“读档多人游戏”加载官方存档。本 Mod 不额外强制保存交易状态，恢复位置与原版房间节点存档规则一致。
- 密码不会明文写入 Steam 大厅或日志。弱密码仍可能被离线猜测，建议至少使用 8 位混合字符。
- 所有参与者必须安装相同版本的本 Mod 和 BaseLib，官方 Mod 一致性检查会负责拦截不匹配版本。
- 帮助队友锻造会校验队友牌组序号、卡牌 ID 和升级等级；若选择期间牌组因其他操作改变，本次选择取消且不消耗篝火行动，避免升级错牌。

## 开源许可 / License

原创源代码与 `Assets/Trading` 下的原创 Mod 素材使用 [MIT License](LICENSE)。创意工坊宣传图中出现的游戏画面、商标和其他第三方内容不属于 MIT 授权范围，详见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。
