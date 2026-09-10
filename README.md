# Oopz Talking — FF14 小队列表显示 oopz 谁在说话

**改编自 [WhosTalking](https://codeberg.org/keysmashes/WhosTalking)（by keysmashes），把数据源从 Discord 换成 oopz**，游戏内绘制层（绿/蓝/黄框、未匹配名单、跨服/团队列表支持）原样保留。

数据不经过任何服务器：直连本机 oopz 客户端在 `127.0.0.1:10274` 暴露的 WebSocket，读 `{"cmd":"members","voice":true,"members":[{"name":"...","talking":true,"muted":false}]}` 成员流，每 1~2 秒刷新一次。只需 oopz 昵称包含游戏角色名（或手动绑定），就能在小队列表看到：

- 🟢 绿框 = 正在说话
- 🔵 蓝框 = 静音（oopz 成员流无"聋"状态，红色 Deafened 永不出现）
- 🟡 黄框 = 名字没对上（可在设置里关掉）
- 也可在列表下方显示"不在小队里但正在说话的人"

## 安装（卫月 dev 插件方式）

在游戏里 **卫月设置 → 研发/开发选项**：

1. 勾选 **启用开发模式**（Development Mode）
2. 添加**开发插件路径**，选择本文件夹下的 **`OopzTalking.dll`**（目录里必须同时存在 `OopzTalking.json` 清单）
3. 在 **已安装插件** 里启用 **Oopz Talking**（InternalName: `OopzTalking`，API 15，匹配国服卫月 15.0.3.x / 国际服 15.0.3.x）

启用后：进 oopz 语音房间 + FF14 小队，队友昵称与角色名一致即可立刻看到说话框。

## 使用

- `/oopztalking` —— 开关设置窗口
- `/oopztalking port 10274` —— 改 oopz 端口（默认已 10274）
- oopz 昵称包含角色名（第一个或最后一个字）即自动匹配；重名/花名可在设置「Advanced Individual Assignments」手动绑定

## 文件结构

```
OopzTalking.sln / OopzTalking\OopzTalking.csproj   工程
OopzTalking\OopzConnection.cs                       数据源：直连 oopz 本地 ws（替代原 DiscordConnection）
OopzTalking\Plugin.cs                               绘制/匹配逻辑（XivToOopz，原 XivToDiscord 改名）
OopzTalking\Configuration.cs                        配置（mute/speak/deafen 颜色、端口、绑定）
OopzTalking\Windows\ConfigWindow.cs                 设置窗口
OopzTalking\Windows\MainWindow.cs                   Debug 窗口
OopzTalking\OopzTalking.yaml / .json                插件清单（API 15）
release\                                            编译产物（DLL + 清单 + 图标）
```

## 怎么编译

需要 .NET SDK 和卫月 dev 目录（`%APPDATA%\XIVLauncher\addon\Hooks\dev` 或 `XIVLauncherCN` 的对应目录，内含 `Dalamud.dll`）：

```powershell
$env:DalamudLibPath = "$env:APPDATA\XIVLauncherCN\addon\Hooks\dev"
dotnet build OopzTalking.sln -c Release -p:DalamudLibPath="$env:DalamudLibPath"
# 产物：OopzTalking\bin\x64\Release\OopzTalking.dll + .json
```

## 与原版 WhosTalking 的差异

| 项 | WhosTalking | OopzTalking |
|---|---|---|
| 数据源 | Discord RPC WebSocket（6463，需 OAuth token + 订阅事件） | oopz 本地 ws（10274，免认证，2 秒推全量） |
| 匹配 | Discord 昵称 / 手动绑定 ID | oopz 昵称包含角色名 / 手动绑定成员名 |
| 命令 | `/whostalking` | `/oopztalking` |
| 依赖 | Websocket.Client | 无（System.Net.WebSockets） |
| 自己身份 | Discord `Self` | oopz 流无标记，按角色名匹配 |

## 注意

- oopz 成员流是**全量快照**（每 1~2 秒整体替换），不像 Discord 有单独 speaking 事件，所以说话框最多延迟 1~2 秒。
- oopz 自己没有"服务器/频道"概念，`Connection.Self` 恒为 null，自己的指示按角色名匹配。
- 小队列表被 HUD 布局隐藏时（DelvUI 等替换列表的插件场景）需在设置里关掉指示灯，与原版行为一致。

## 开源协议 / License

本项目以 **GNU Affero General Public License v3.0（AGPL-3.0）** 开源，见 [LICENSE](LICENSE)。

## 鸣谢 / Credits

- **keysmashes** —— 原版 [WhosTalking](https://codeberg.org/keysmashes/WhosTalking) 的作者。本插件是其改编：把 Discord RPC 数据源替换为 oopz 本地 WebSocket，游戏内绘制层（说话/静音/未匹配方框、跨服与团队列表支持）沿用其实现。
- **Google Fonts Material Design icon library** —— 插件图标源自该图标库，依 [Apache License 2.0](https://www.apache.org/licenses/LICENSE-2.0) 使用。
- **oopz** —— 语音数据源（本机 `ws://127.0.0.1:10274`），非本项目开发内容。