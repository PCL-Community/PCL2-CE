# PCL2-CE 项目知识库

> 本文档为 AI 辅助开发提供项目上下文、编码规范与操作边界。

## 项目概述

**PCL Community Edition (PCL2-CE)** 是基于 PCL 开源代码二次开发的 Minecraft 启动器社区版本。

- **技术栈**: .NET 8.0 WPF + C# 14 + MSTest
- **解决方案格式**: `.slnx`（新版 .NET XML 格式）
- **目标平台**: Windows 10 1809+（主目标）、macOS/Linux（仅交叉编译）
- **SDK 要求**: .NET 10 SDK（构建时）、.NET 8 Desktop Runtime（运行时）

## 解决方案结构

```
PCL2-CE/
├── Plain Craft Launcher 2/     # 主 WPF 应用程序（WinExe）
│   ├── Program.cs              # 入口点 [STAThread] Main()
│   ├── Application.xaml(.cs)   # WPF Application（非标准命名，不用 App.xaml）
│   ├── FormMain.xaml(.cs)      # 主窗口
│   ├── Controls/               # 自定义控件（My* 前缀）
│   ├── Pages/                  # UI 页面（PageDownload/、PageSetup/ 等）
│   ├── Modules/                # 业务逻辑模块（Mod* 前缀）
│   └── Resources/              # 嵌入资源（forge、wrapper 等）
├── PCL.Core/                   # 核心库（类库）
│   ├── App/                    # 应用生命周期、配置、IoC、本地化
│   ├── IO/                     # 文件 I/O、网络、存储
│   ├── Link/                   # 多人游戏/联机子系统
│   ├── Minecraft/              # Minecraft 领域逻辑
│   ├── UI/                     # UI 基础设施、主题、动画
│   ├── Utils/                  # 工具类
│   └── ViewModel/              # MVVM ViewModel
├── PCL.Core.SourceGenerators/  # Roslyn 源码生成器（netstandard2.0）
└── PCL.Core.Test/              # MSTest 单元测试
```

## 启动流程

```
Program.Main() → Lifecycle.OnInitialize()
  → BeforeLoading: StartupService, SingleInstanceService, ApplicationService
  → CurrentApplication.Run() (进入 WPF 消息循环)
  → OnLoading: Loading + WindowCreating 状态服务
  → MainWindowService 创建 FormMain → Show()
  → OnWindowCreated: WindowCreated 状态服务
  → 后台线程: Running + Exiting 状态服务
```

生命周期状态机: `BeforeLoading → Loading → Loaded → WindowCreating → WindowCreated → Running → Closing → Exiting`

## 编码规范

### 命名规则（PCL.Core/.editorconfig 强制执行，warning 级别）

| 标识符类别 | 规范 | 示例 |
|---|---|---|
| 常量 | `PascalCase` | `MaxRetryCount` |
| 局部变量/参数 | `camelCase` | `fileName` |
| 私有实例只读字段 | `_camelCase` | `_logger` |
| 私有静态只读字段 | `_PascalCase` | `_DefaultInstance` |
| 私有实例字段 | `_camelCase` | `_isRunning` |
| 私有方法 | `_PascalCase` | `_ParseConfig` |
| 公共方法/属性 | `PascalCase` | `GetService()` |
| 接口 | `IPascalCase` | `ITask` |
| 类型 | `PascalCase` | `ConfigService` |

### 命名空间

- **主应用**: 所有文件使用 `namespace PCL;`（扁平化，不按文件夹分层）
- **核心库**: 按文件夹映射 `PCL.Core.App`, `PCL.Core.IO`, `PCL.Core.UI` 等
- **测试项目**: `PCL.Core.Test.*`（按子文件夹分层）

### 语言特性

- **LangVersion**: 14.0（C# 14，需 .NET 10 SDK）
- **Nullable**: 全局启用
- **文件作用域命名空间**: 核心库使用 `namespace PCL.Core.App;`，主应用使用传统块作用域
- **ImplicitUsings**: 仅主应用启用
- **全局 using**: 主应用包含 `System.Text.Json` 和 `System.Text.Json.Nodes`

## 构建与 CI

### 构建命令

```bash
# 开发构建（Debug）
dotnet build "Plain Craft Launcher 2/Plain Craft Launcher 2.csproj" -c Debug

# 发布构建
dotnet publish "Plain Craft Launcher 2/Plain Craft Launcher 2.csproj" \
  -p:Configuration=Release -p:Platform=x64 \
  -p:DeleteExistingFiles=true -o ./artifact --no-self-contained
```

### 构建配置

| 配置 | 优化 | 调试符号 | 定义常量 | 用途 |
|---|---|---|---|---|
| Debug | ❌ | full | `DEBUG;TRACE` | 本地开发 |
| CI | ❌ | full | `CI;TRACE` | CI 构建测试 |
| Release | ✅ | none | `RELEASE` | 正式发布 |
| Beta | ✅ | none | `BETA` | 预发布 |

### CI 流程（GitHub Actions）

- **触发**: 推送到 `dev` 分支、PR、手动触发
- **矩阵**: CI/x64 + CI/ARM64 并行构建
- **构建**: `dotnet publish`（不包含自运行时）
- **SDK**: .NET 10.0.x（通过 `actions/setup-dotnet@v5`）
- **注意**: CI **不运行测试**，测试项目被排除在 CI 触发路径之外

### 运行测试

```bash
dotnet test PCL.Core.Test/PCL.Core.Test.csproj
```

## 提交规范

遵循 [Angular 提交规范](https://www.conventionalcommits.org)，详见 [技术规范](https://github.com/PCL-Community/PCL-CE/wiki/技术规范)。

**提交类型**: `feat`, `fix`, `refactor`, `perf`, `improve`, `i18n`, `docs`, `style`, `ci`, `test`, `chore`, `revert`

### AI 生成代码规范

1. **必须在提交信息末尾标注模型**: `[GPT-5.1]`、`[Claude Opus 4.5]` 等
2. **禁止直接提交大型 AI PR**: 200+ 行或 5+ 文件的 AI 生成代码必须拆分
3. **必须人工审查并理解每一处逻辑**: 不得盲目合并 AI 输出

### Git 签名要求

- **所有提交必须附带已验证签名**（GPG 或 SSH）
- PR 中的每个 commit 都必须是 `Verified` 状态

### 分支策略

- **新功能**: `feat/your-feat-name`
- **Bug 修复**: `fix/issue-number-desc`
- **PR 目标**: `dev` 分支
- **同步上游**: 使用 `merge`（不使用 `rebase` 或 `squash merge`）

## 操作边界

### 🚫 禁止事项

1. **不要使用 `[Obsolete]` API**:
   - `AesCbcProvider`（CBC 模式不安全）
   - `IdentifyOld`（使用 `Identify` 替代）
   - `BlockingStream.Read(byte[], int, int)`（编译错误级别）
   - `IsWarn`（使用 `Theme`）
   - `IsSwaped`（使用 `IsSwapped`）
   - `IsDarkMode()`（使用 `ThemeService.IsDarkMode`）
   - `IdentityId`（使用 `AccessToken`/`RefreshToken`）

2. **不要依赖未实现的功能**:
   - `ToastNotification.SendToast()` 和 `UninstallToasts()`（空实现）
   - `IKeyFrameAnimation` 和 `IImplicitAnimation`（空接口）
   - `AnimationExtensions` 动画方法（空方法体）

3. **不要硬编码 UI 字符串**: 使用 `Lang.Text(...)` 或 `sys:String` 资源键进行本地化

4. **不要禁用编译器警告**: 除非有充分理由并在代码中用中文注释说明

5. **不要修改 `.csproj` 的 `EnableDefaultItems=false` 设置**: 这是项目有意为之的设计决策

6. **不要在主应用中引入新的命名空间层级**: 所有文件保持 `namespace PCL;`

7. **禁止使用 `goto` 语句**: 社区维护者会直接拒绝包含 `goto` 的 PR

### ⚠️ 注意事项

1. **手动文件管理**: 添加新 `.cs` 或 `.xaml` 文件后，需手动编辑 `.csproj` 添加包含项
2. **跨平台限制**: 源码可在 macOS/Linux 编译，但只能在 Windows 运行
3. **测试不自动运行**: 测试需要手动执行，CI 不包含测试步骤
4. **依赖注入**: 使用自定义 IoC 容器（`PCL.Core.App.IoC`），非标准 .NET Generic Host
5. **换行符**: 新文件使用 LF 换行符 + UTF-8（不带 BOM）

### ✅ 允许的操作

1. **添加生命周期服务**: 使用 `[LifecycleService(SomeState)]` 属性注解类
2. **添加 MVVM ViewModel**: 在 `PCL.Core/ViewModel/` 创建，使用 CommunityToolkit.Mvvm
3. **添加配置项**: 使用 `PCL.Core.App.Configuration` 框架
4. **添加单元测试**: 在 `PCL.Core.Test/` 使用 MSTest 框架
5. **使用 C# 14 特性**: 主构造函数、集合表达式等

## 关键文件索引

| 文件 | 用途 |
|---|---|
| `Plain Craft Launcher 2.slnx` | 解决方案文件 |
| `Plain Craft Launcher 2/Program.cs` | 应用入口点 |
| `Plain Craft Launcher 2/Application.xaml(.cs)` | WPF Application 定义 |
| `Plain Craft Launcher 2/FormMain.xaml(.cs)` | 主窗口 |
| `PCL.Core/App/IoC/LifecycleFlow.cs` | 生命周期状态机核心 |
| `PCL.Core/App/IoC/LifecycleState.cs` | 生命周期状态枚举 |
| `PCL.Core/App/IoC/Attributes.cs` | `[LifecycleService]` 等属性定义 |
| `PCL.Core/App/Essentials/StartupService.cs` | 启动服务 |
| `PCL.Core/App/Essentials/ApplicationService.cs` | WPF Application 创建 |
| `PCL.Core/App/Essentials/MainWindowService.cs` | 主窗口创建 |
| `PCL.Core/.editorconfig` | 命名规范强制执行 |
| `.github/workflows/build-test.yml` | CI 构建工作流 |
| `.github/workflows/reusable-build.yml` | 可复用构建动作 |
| `cliff.toml` | changelog 生成配置 |

## 环境变量（CI 构建时注入）

| 变量名 | 用途 |
|---|---|
| `PCL_MS_CLIENT_ID` | Microsoft 认证 |
| `PCL_CURSEFORGE_API_KEY` | CurseForge API |
| `PCL_SENTRY_DSN` | 错误遥测 |
| `PCL_NAID_CLIENT_ID/SECRET` | Natayark 认证 |
| `PCL_LINK_SERVER_ROOT` | 联机服务器配置 |
| `PCL_LOBBY_DEFAULT_SECRET` | 大厅默认密钥 |
| `PCL_GITHUB_SHA` | 提交哈希 |

## 相关资源

- [贡献指南](https://github.com/PCL-Community/PCL-CE/wiki/开发指南)
- [技术规范](https://github.com/PCL-Community/PCL-CE/wiki/技术规范)
- [生命周期简介](https://github.com/PCL-Community/PCL-CE/wiki/生命周期简介)
- [EventBus 事件总线](https://github.com/PCL-Community/PCL-CE/wiki/EventBus-事件总线)
- [应用配置系统简介](https://github.com/PCL-Community/PCL-CE/wiki/应用配置系统简介)
- [Tasks 响应式任务系统](https://github.com/PCL-Community/PCL-CE/wiki/Tasks-响应式任务系统)
- [RPC API 说明](https://github.com/PCL-Community/PCL-CE/wiki/RPC-API-说明)
- [身份认证组件文档](https://github.com/PCL-Community/PCL-CE/wiki/身份认证组件文档)
- [GPG 签名配置指南](https://github.com/PCL-Community/PCL-CE/wiki/GPG-签名配置指南)
