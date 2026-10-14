# 杂耍计数器+高亮

**Juggling Counter** · Slay the Spire 2

<img src="docs/images/cover.png" alt="杂耍计数器 / Juggling counter" width="160">

显示「杂耍」的触发倒计数，即将复制下一张攻击牌时高亮手牌中的攻击牌。仅修改显示，不改变游戏机制。

Shows a Juggling countdown and highlights Attack cards in your hand when the next attack will be copied. Visual only; no gameplay changes.

**[Steam 创意工坊 / Subscribe on Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3819624503)**

普通玩家直接订阅即可，无需安装 .NET SDK。Mod 使用游戏自带的 .NET 运行时。

Players can subscribe directly; no .NET SDK installation is needed. The mod uses the runtime bundled with the game.

![游戏效果 / In-game preview](docs/images/gameplay.png)

## 从源码构建（仅开发者） / Build from source (developers only)

仅自行编译时需要 .NET 9 SDK 和已安装的 Windows 版游戏。适配版本：`v0.111.0`；游戏更新后可能需要更新 Mod。

Building from source requires the .NET 9 SDK and a local Windows game installation. Tested on `v0.111.0`; game updates may require mod updates.

```powershell
.\build.ps1 -GameDir 'D:\SteamLibrary\steamapps\common\Slay the Spire 2'
```

将路径替换为你的游戏目录。输出位于 `dist/`。测试说明见 [tests/README.md](tests/README.md)。

Replace the path with your game directory. Output: `dist/`. See [tests/README.md](tests/README.md) for tests.
