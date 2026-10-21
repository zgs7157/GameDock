# GameDock · 游戏坞

> Minecraft 版本与加载器「下载即导入」工具 —— 多镜像自动回退，专治启动器下载源被“封杀”。
> 一键把原版 / Forge / Fabric / NeoForge / OptiFine 下载进 PCL2、HMCL、FCL 的目录，启动器打开就能玩。

![C#](https://img.shields.io/badge/C%23-.NET%208-blue) ![License](https://img.shields.io/badge/License-MIT-green) ![Platform](https://img.shields.io/badge/Platform-Windows-lightgrey)

---

## 为什么要做它

MC 启动器的下载源经常被墙、被阻断、被“封杀”：官方源在境内慢，镜像源今天能用明天可能就 404。
GameDock 的思路很简单：

1. **下载**：从 Mojang 官方 / BMCLAPI / 自定义镜像拉取版本文件（客户端、库、资源），全部带 **SHA1 校验**；
2. **导入**：按启动器通用目录布局（`versions / libraries / assets`）直接写进 `.minecraft`，PCL2、HMCL、FCL 启动即识别；
3. **防封杀**：一个地址失败，自动按镜像列表回退下一个；任何新镜像，一条命令即可加入。

支持 **命令行（CLI）** 与 **图形界面（GUI）** 两种用法。

## 特性

- ✅ 原版版本下载：正式版 / 快照 / 旧版本，客户端、服务端、库、资源（assets）、natives 全覆盖
- ✅ 加载器下载：
  - **Fabric**：meta API 直接生成完整版本并导入，**无需本机 Java**
  - **Forge / NeoForge**：下载安装器，可选自动运行 `--installClient`（需 Java 8+）
  - **OptiFine**：BMCLAPI 列表下载安装器
- ✅ 多镜像自动回退：内置官方源 + BMCLAPI，失败自动切换，支持自定义镜像
- ✅ 并发下载 + 断点跳过（已存在且校验通过的文件直接跳过）+ 失败重试
- ✅ 导入目录兼容：PCL2 / HMCL / FCL 与任意使用标准 `.minecraft` 布局的启动器
- ✅ 启动器目录自动探测（PCL2/HMCL 默认目录、自定义目录等）

## 快速开始

### 图形界面（推荐）

```powershell
# 直接运行
src\GameDock.App\bin\Debug\net8.0-windows\GameDock.exe
```

1. 顶部选择镜像源（可选「测速」），确认启动器目录；
2. 左侧筛选 / 搜索版本 → 勾选要下的内容 → 「下载并导入」；
3. 切到「加载器」页，选 MC 版本 → 类型 → 「获取加载器版本」→ 选择 → 「下载加载器」。

### 命令行

```powershell
# 列出正式版（--contains 关键字过滤）
gamedock list --type release --contains 1.21

# 下载并导入最新正式版
gamedock download latest:release --mc-dir "D:\MC\.minecraft"

# 下载并导入指定版本（含服务端，不下载资源）
gamedock download 1.21.4 --server --no-assets --mc-dir "D:\MC\.minecraft"

# 安装 Fabric（自动先导入原版基础版本）
gamedock fabric 1.21.4

# 下载 Forge 安装器并自动运行安装
gamedock forge 1.20.1 --version 47.2.0 --run

# 管理镜像（测速 / 新增自定义镜像）
gamedock mirror health
gamedock mirror add mymirror 我的镜像 https://mc.example.com

# 查看配置 / 修改启动器目录
gamedock config
gamedock config --mc-dir "D:\MC\.minecraft"
```

完整命令说明见 [docs/使用说明.md](docs/使用说明.md)。

## 从源码构建

需要 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)（本机若无 SDK 可用下方脚本装一个用户级 SDK）：

```powershell
# 用户级安装 SDK（无需管理员）
Invoke-WebRequest https://dot.net/v1/dotnet-install.ps1 -OutFile $env:TEMP\dotnet-install.ps1
& $env:TEMP\dotnet-install.ps1 -Channel 8.0 -InstallDir "$env:LOCALAPPDATA\Microsoft\dotnet"
# 之后把该目录加入 PATH，或直接用完整路径调用 dotnet

dotnet build GameDock.sln          # 编译全部项目
dotnet test tests/GameDock.Core.Tests  # 跑单元测试
dotnet run --project src/GameDock.Cli -- list --contains 1.21   # 直接跑 CLI
```

发布单文件：

```powershell
dotnet publish src/GameDock.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

## 目录结构

```
GameDock/
├── GameDock.sln                 # 解决方案
├── global.json                  # 固定 SDK 版本范围
├── src/
│   ├── GameDock.Core/           # 核心库（跨平台）
│   │   ├── Models/              # Mojang/Fabric/Forge 等 JSON 模型
│   │   ├── Mirrors/             # 镜像源抽象与注册表（官方/BMCLAPI/自定义）
│   │   ├── Services/            # 下载引擎、版本编排、导入、规则评估
│   │   ├── Loaders/             # Forge/Fabric/NeoForge/OptiFine 加载器
│   │   ├── Config/              # 配置读写（%APPDATA%\GameDock）
│   │   └── Util/                # 日志、哈希、Java 定位等工具
│   ├── GameDock.Cli/            # 命令行工具（gamedock）
│   └── GameDock.App/            # WinForms 图形界面
├── tests/
│   └── GameDock.Core.Tests/     # xUnit 单元测试
└── docs/
    ├── 使用说明.md               # CLI/GUI 全命令详解
    ├── 镜像源配置.md             # 镜像机制与自定义镜像指南
    └── 开发指南.md               # 代码结构与扩展新加载器
```

## 镜像源机制（防封杀核心）

下载时对每个文件生成候选 URL 列表：**原始地址 → 各镜像的重写地址**，逐个尝试、失败重试后切换。
内置：

| key | 名称 | 说明 |
|---|---|---|
| `official` | Mojang 官方源 | 版本/资源/库全部指向官方端点 |
| `bmclapi` | BMCLAPI 镜像 | 国内常用，路径与官方兼容 |

自定义镜像（如自建反代、其他镜像站）：
`gamedock mirror add <key> <名称> <BaseUrl>`，随后 `--mirror <key>` 或 `gamedock config --mirror <key>` 选用。
详见 [docs/镜像源配置.md](docs/镜像源配置.md)。

## 配置

配置文件位于 `%APPDATA%\GameDock\config.json`（CLI 可用 `--config <path>` 覆盖）：

| 字段 | 默认 | 说明 |
|---|---|---|
| `McDir` | `%APPDATA%\.minecraft` | 启动器 .minecraft 目录 |
| `Mirror` | `bmclapi` | 首选镜像 |
| `Threads` | `8` | 并发线程数 |
| `RetryCount` | `3` | 单文件重试次数 |
| `DownloadAssets` | `true` | 是否下载资源文件 |
| `DownloadServer` | `false` | 是否下载服务端 |
| `AutoImport` | `true` | 下载后写入启动器目录 |
| `AutoRunInstaller` | `false` | 加载器安装器下载后自动运行 |

## 常见问题

**Q：下载到一半断了怎么办？** 重新执行同一命令即可，已校验通过的文件会自动跳过，只补缺失部分。

**Q：PCL2 / HMCL 里看不到下载的版本？** 确认导入目录就是启动器的 .minecraft 目录；Fabric 版本依赖同目录下的原版基础版本（工具会自动导入）。

**Q：镜像源 404 / 超时？** `gamedock mirror health` 测速，切到可用镜像；或添加自定义镜像。

**Q：Forge/OptiFine 提示“未找到 Java”？** 安装 [Java 8+](https://www.oracle.com/java/technologies/downloads/) 并设置 `JAVA_HOME`，或手动运行下载的安装器 jar。

## 许可

[MIT](LICENSE) © zgs7157

> 免责声明：本工具仅用于下载 Mojang 官方分发的游戏文件及公开的加载器元数据，请遵守你所在地区的法律法规与《Minecraft 最终用户许可协议》。下载的内容仅供个人合法使用。
