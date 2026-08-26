# Better Multiplayer 维护者接手手册

本文记录项目结构、已核验环境，以及从源码到 GitHub 和 Steam 创意工坊的完整发布流程。命令以 Windows PowerShell 为准。

## 项目与发布目标

- GitHub：<https://github.com/Isyyyue/sts2-better-multiplayer>
- Steam 创意工坊条目：`3768337454`
- Workshop 作者账号：`76561199758888572`
- BaseLib 条目：`3737335127`
- Mod ID：`BetterMultiplayer`
- 协议版本：`Lobby/RoomSession.cs` 中的 `ProtocolVersion`
- Mod 版本必须同时更新 `BetterMultiplayer.json`、`BetterMultiplayerMod.Version` 和 `BetterMultiplayer.csproj` 的 `Version`。

不要把 Steam 凭据、GitHub Token、游戏程序集或 BaseLib DLL 提交到仓库。发布内容沿用原来的三个文件：`BetterMultiplayer.dll`、`BetterMultiplayer.json` 和 `workshop-branch-support.txt`。

## 代码地图

- `Lobby/`：Steam 公共大厅、房间名、Steam Lobby 补丁和房间生命周期。
- `Diagnostics/`：玩家主动提交的固定结构诊断、多人关联信息、交易/锻牌时序和 Sentry 传输。
- `Security/`：PBKDF2/HMAC 密码证明。
- `Trading/`：Mod 消息与 Steam 中继、房主权威交易状态机、篝火/商店补丁和交易界面。
- `UI/`：Godot 控件创建工具。
- `Localization/`：大厅相关的中英文文本。
- `Trading/GameApiCompatibility.cs`：默认版与 public-beta 的 API 兼容层。
- `tests/`：协议、密码、状态机、结算和兼容层的 xUnit 测试。
- `workshop/`：官方上传器配置、封面与预览图；实际上传工作区由脚本生成到忽略的 `artifacts/workshop-upload/`。

协议的关键约束是房主权威、双方确认、修订号和事务号防重放。修改交易或网络代码时，应同时检查消息兼容性、断线行为、重复包处理和全部测试，不要只验证 UI。

## 2026-08-17 核验基线

- 游戏路径：`D:\Steam\steamapps\common\Slay the Spire 2`
- Steam 分支：`public-beta`
- 游戏版本：`v0.111.0`
- BaseLib：`v3.4.5`
- .NET SDK：`9.0.317`
- 源码基线：`c1083cbdf8c51f84aa6ca0bc21cb178b9c718820`
- Release 构建：0 警告、0 错误。
- 自动测试：72/72 通过。

核验发现旧 Workshop DLL 的 `ProductVersion` 指向提交 `8e6c24a`，没有包含 GitHub `main` 上 `c1083cb` 的兼容层；线上更新说明也仍写 `v0.110.1`。`0.4.5` 的目的就是把已验证的 Beta 兼容代码与实际发布包重新同步。

## 0.5.4 起的双分支兼容门禁

Workshop 同时声明支持 Steam 正式版与 `public-beta`，因此发布证据必须分别绑定两套游戏程序集，不能用一套分支的测试结果替代另一套：

| Steam 分支 | 游戏版本 | 参考 build ID | 发布要求 |
| --- | --- | --- | --- |
| `public` | `v0.107.1` | `23811903` | 完整测试、无增量 Release 构建、真实启动 |
| `public-beta` | `v0.111.0` | `24724944` | 完整测试、无增量 Release 构建、真实启动 |

Steam 一次只保留当前选择的分支。切换前应把每个分支的 `data_sts2_windows_x86_64` 复制到仓库外的只读参考目录，并记录 app manifest build ID、`sts2.dll` SHA-256、BaseLib 版本与 SHA-256。游戏程序集和 BaseLib DLL 不得提交。最终 Workshop DLL 使用最低支持版本 `public v0.107.1` 构建；较新分支必须通过兼容测试和真实启动，避免编译时意外引用测试版独有 API。

## 本机构建

准备 .NET 9 SDK、BaseLib，以及已冻结的正式版和 `public-beta` 两套游戏程序集。先分别执行双分支门禁；以下路径仅为示例：

```powershell
$baseLibPath = 'D:\Steam\steamapps\workshop\content\2868840\3737335127\BaseLib\BaseLib.dll'
$branches = @(
  @{ Name = 'public'; Path = 'E:\Dev\Reference\STS2\public-0.107.1' },
  @{ Name = 'public-beta'; Path = 'E:\Dev\Reference\STS2\public-beta-0.111.0' }
)

foreach ($branch in $branches) {
  dotnet test .\tests\BetterMultiplayer.Tests.csproj -c Release --no-restore `
    -p:STS2Path=$branch.Path -p:BaseLibPath=$baseLibPath
  if ($LASTEXITCODE -ne 0) { throw "$($branch.Name) tests failed" }

  dotnet build .\BetterMultiplayer.csproj -c Release --no-restore --no-incremental `
    -p:STS2Path=$branch.Path -p:BaseLibPath=$baseLibPath
  if ($LASTEXITCODE -ne 0) { throw "$($branch.Name) build failed" }
}
```

双分支通过后，使用最低支持版本重建最终上传工作区：

```powershell
.\tools\prepare-release.ps1 `
  -DotnetPath 'C:\Program Files\dotnet\dotnet.exe' `
  -Sts2Path 'E:\Dev\Reference\STS2\public-0.107.1' `
  -BaseLibPath 'D:\Steam\steamapps\workshop\content\2868840\3737335127\BaseLib\BaseLib.dll'
```

脚本会运行测试和 Release 构建，重建 `artifacts/workshop-upload`，保留原发布包的三个文件格式，并输出 DLL 的 SHA-256。该工作区故意不包含 `previews/`，让官方上传器保留线上 6 张附加预览图。第一次运行发布脚本前先另存当前线上版本的 DLL、JSON 和 SHA-256，因为脚本会重建 staging 与 Workshop 工作区。

## 发布顺序

1. 获取 `origin/main`，确认没有意外的本地修改，并另存当前线上版本的 DLL、JSON 与 SHA-256。
2. 固定正式版和 `public-beta` 两套程序集，记录分支、游戏版本、build ID、`sts2.dll` 与 BaseLib SHA-256。
3. 更新三个版本来源、README 和 `workshop/workshop.json` 的中英文更新说明。
4. 分别对两套程序集运行完整测试、无增量 Release 构建和真实游戏启动；任一分支未验证都不得声明双分支兼容。
5. 只暂存本次发布文件，检查 diff 后提交。沿用仓库现有的 Conventional Commit 格式，例如：`chore(release): publish 0.5.4 stable compatibility update`。
6. 确认 `git status --short` 没有输出，再用正式版程序集和 `-RequireClean` 运行 `tools/prepare-release.ps1`。脚本会拒绝脏工作区，并要求 DLL 的 `ProductVersion` 精确记录当前 `HEAD`。
7. 检查 `artifacts/workshop-upload/content` 仅有 DLL、JSON 和分支说明文本；工作区根目录不得出现 `previews/`。记录版本、提交号和 SHA-256。
8. 推送 GitHub `main`，再用 `git ls-remote origin refs/heads/main` 确认远端哈希与本地发布提交一致。
9. 使用 Mega Crit 官方 `sts2-mod-uploader v0.2.0` 更新现有条目。`workshop/mod_id.txt` 已固定为 `3768337454`，不能删除或改成其他 ID。
10. 只有 Steam API 确认远端 manifest、更新时间和内容大小变化后才归档本次上传日志；超时期间不要重复上传。
11. 通过条目页面和订阅端实际下载继续复核版本、DLL `ProductVersion` 与 SHA-256，并在正式版和 `public-beta` 各启动一次。
12. 最终确认 `git status --short` 没有待提交文件；`artifacts/workshop-upload` 和构建产物被忽略是正常现象。

官方上传器命令：

```powershell
.\ModUploader.exe upload --workspace '<仓库绝对路径>\artifacts\workshop-upload' --id 3768337454
```

也可以使用仓库的包装脚本，它会检查上传器文件、Steam 状态和工作区，并在 Steam API 确认远端 Manifest 与内容大小更新后才归档上传日志：

```powershell
.\tools\publish-workshop.ps1 -UploaderDirectory '<ModUploader-win-x64 完整解压目录>'
```

正式上传前可先加 `-ValidateOnly`，完整检查 Git、版本、DLL、工作区和上传器文件，但不调用 Steam Workshop API。

上传器使用当前 Steam 客户端登录账号。上传时不要运行游戏；日志必须显示当前 persona，并显示条目 `3768337454` 更新成功。若上传器报告超时，先等待包装脚本完成 Steam API 核验，不要立即重试；Steam 可能已经提交内容，重复重试会产生重复更新说明。若核验失败，包装脚本不会归档发布日志。生成的临时工作区没有 `previews/`，因此线上 6 张附加预览保持不变；`workshop.json` 不支持 `preservePreviews` 字段。

## 发布后核验

- `BetterMultiplayer.json`、代码常量、项目版本和 DLL 产品版本均为发布版本。
- Steam 页面标题、描述、更新说明、依赖和 public/public-beta 分支范围正确。
- Steam API 的 `time_updated`、`hcontent_file` 和文件大小已经变化。
- 重新下载的 DLL `ProductVersion` 指向 GitHub 的发布提交。
- 重新下载的 JSON 版本正确，DLL SHA-256 与本地发布包一致。
- 所有联机玩家使用相同游戏分支、Mod 版本和 BaseLib 版本。

遇到坏包时不要覆盖历史源码或强推。修复后发布新的补丁版本；若必须恢复功能，可从已知正常提交重新构建并再次上传同一 Workshop 条目，同时在更新说明中明确回退内容。
