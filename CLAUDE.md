# DeepSeek Harness Desktop 仓库维护指南

> **镜像要求**：`AGENTS.md` 与 `CLAUDE.md` 必须保持内容完全一致。修改任一文件时必须同步修改另一份，并在交付前比较两者内容。

## 1. 项目定位

DeepSeek Harness Desktop 是面向 Windows 10/11 x64 的原生桌面宿主。应用负责选择工作目录、启动和管理本机 `dsh web`、探测服务状态，并通过 WebView2 展示官方 DeepSeek Harness Web UI。

本项目不重新实现 Harness 的会话、模型、工具、审批、工作区或配置能力。涉及这些能力时，应优先复用上游 Web UI，不在 WPF 宿主中复制一套业务界面。

## 2. 技术基线

| 范围 | 技术与约束 |
| --- | --- |
| 平台 | Windows 10/11 x64 |
| 运行时 | C# / .NET 8，SDK 版本见 `global.json` |
| UI | WPF + CommunityToolkit.Mvvm |
| 浏览器 | Microsoft Edge WebView2 |
| 依赖注入 | Microsoft.Extensions.DependencyInjection |
| 日志 | Microsoft.Extensions.Logging + Serilog |
| 测试 | xUnit，单元测试与 Windows 集成测试分开 |
| 发布 | `win-x64`、framework-dependent 轻量 ZIP；目标机需安装 .NET 8 Desktop Runtime |

- `Nullable`、隐式 using 和警告即错误由 `Directory.Build.props` 统一启用。
- NuGet 版本统一维护在 `Directory.Packages.props`；项目文件只声明包引用，不重复填写版本。
- 目标框架、RID、平台和版本等公共属性优先维护在仓库级 props 中，不在各项目重复配置。

## 3. 仓库结构与职责

- `src/DeepSeekHarnessDesktop/`：正式应用。
  - `Models/`：配置、状态、错误和跨层数据模型。
  - `Services/Abstractions/`：可替换服务契约。
  - `Services/`：进程、健康检查、生命周期、配置、日志、账户、单实例、托盘和 WebView2 集成。
  - `ViewModels/`：UI 状态与命令编排。
  - `Views/`：WPF XAML、窗口和轻量 UI 桥接代码。
  - `Utilities/`：无状态、可独立测试的解析与格式化逻辑。
- `tests/DeepSeekHarnessDesktop.UnitTests/`：不依赖真实 DSH 的快速行为测试。
- `tests/DeepSeekHarnessDesktop.IntegrationTests/`：真实进程树、HTTP 健康检查等 Windows 集成测试。
- `tests/DeepSeekHarnessDesktop.TestHarness/`：集成测试专用子进程，不能成为生产依赖。
- `eng/`：工程验证、发布和发布门禁脚本。
- `docs/`：开发、详细设计、安装和阶段验证记录。
- `output/`、`artifacts/`、`bin/`、`obj/`、`TestResults/`：生成目录，不手工修改，不作为源码依据；发布产物统一写入 `output/`。

## 4. 维护时的事实来源

1. 先读取当前实现、项目文件和相关测试，再判断行为；禁止根据旧项目经验编造类型、接口或命令。
2. `docs/deepseek-harness-desktop-development.md` 说明产品边界和总体方案；`docs/deepseek-harness-desktop-detailed-design.md` 说明设计细节。
3. `docs/validation/` 是已执行阶段的验证记录。除纠正事实错误外，不回写历史结果；新验证应新增记录或清楚标明新日期。
4. `docs/installation.md` 会被发布脚本复制为发布包 `README.md`，安装要求或发行行为变化时必须同步更新。
5. 代码与文档不一致时，先用测试和实际行为确认，再在同一改动中消除不一致。

## 5. 不得破坏的系统边界

### 5.1 进程所有权

- 仅停止或重启当前应用创建并跟踪的 DSH 进程；绝不因端口号相同而结束外部进程。
- Owned DSH 必须加入带 `KILL_ON_JOB_CLOSE` 的 Windows Job Object，确保宿主退出或异常终止时回收整个子进程树。
- `RunningExternal` 只允许连接和刷新，不得开放停止或重启。
- 启动、停止、重启必须串行；旧进程及端点未确认释放前不得创建新进程。

### 5.2 状态与并发

- 生命周期状态只能通过 `HarnessStateMachine` 的合法事件迁移，不直接拼装或跳过关键状态。
- 保留 `HarnessLifecycleCoordinator` 的单操作门、取消令牌和 generation 校验，防止过期异步结果覆盖新状态。
- 进程立即退出、健康探测完成、用户取消和应用退出之间存在竞态；修改相关代码必须新增竞态或取消测试。
- 所有事件订阅、`CancellationTokenSource`、`SemaphoreSlim`、`Process`、Job Object、托盘图标和 DI 容器都必须成对释放。

### 5.3 命令与工作目录

- 工作目录通过 `ProcessStartInfo.WorkingDirectory` 传递，不能拼进 Shell 命令。
- 参数优先使用 `ProcessStartInfo.ArgumentList`。`.cmd` 仅通过 `CmdCommandLineBuilder` 的受控路径执行，不允许接收未经验证的用户 Shell 文本。
- 自定义启动模式只接受已存在的 `.exe` 或 `.com`；不要在未补齐威胁模型和测试前放宽到 `.cmd`、`.bat` 或任意命令行。
- Auto 启动依次使用 PATH 中可执行的 `dsh.cmd`、当前用户 npm `_npx` 缓存中经固定包名/版本/bin 映射校验的 DSH、PATH 中的 `npx.cmd`。缓存命中时通过 PATH 中的 `node.exe` 直接执行固定 `lib/bin.js`、`web` 和可选纯数字端口参数，不访问 registry。
- 缓存发现只允许枚举标准 `_npx` 根的直接子目录，不硬编码 cache id，不接受 manifest 提供的任意入口，不执行非精确 `@deepseek-ai/dsh@0.1.0-rc.6`。不自动全局安装 Node.js/DSH，不接受用户提供的 npm 包名或 Shell 参数；确认没有可复用安装后，通过 npx 下载固定 DSH 前必须取得用户确认。
- 缺少 WebView2、Node.js 或 npx 时，安装引导一次只展示当前缺失项，并仅打开对应官方安装页面；安装程序由用户确认和执行，返回应用后重新检查系统与用户 PATH。

### 5.4 服务身份与 WebView2

- 默认服务地址必须是 loopback。端口可访问不等于 DSH 可用；加载或认定外部实例前必须验证 HTTP 状态、HTML 内容和 DSH 身份标记。
- Code WebView2 的重定向只能在 loopback 地址之间进行，主导航必须与已确认 DSH 地址同源；禁止加载远程地址、带用户信息的 URL 或身份不明的本机服务。
- Chat WebView2 只允许精确的 `https://chat.deepseek.com:443` origin，使用固定 `Chat` profile 且不与 Code 共享 profile。新增登录、验证码或其他远程 origin 前必须有真实流程证据、逐项常量、相邻恶意域名测试和安全评审，禁止通配符。
- 非内嵌 HTTP(S) 链接交给受控系统浏览器服务；危险协议直接拒绝。Chat 权限默认拒绝、下载默认取消，不读取或复制 Cookie、Token、密码、DOM、消息、站点存储或网络正文。
- 不向任一网页注入宿主对象、本机进程能力或任意 JavaScript。放宽导航、profile、下载或 WebView2 权限属于安全变更，必须有针对性测试和文档说明。

### 5.5 配置、凭据与日志

- 配置位于 `%APPDATA%\DeepSeekHarnessDesktop\settings.json`；写入继续使用临时文件、替换和备份恢复策略，字段变化必须考虑 `SchemaVersion` 和旧配置兼容。
- WebView2 数据和日志位于 `%LOCALAPPDATA%\DeepSeekHarnessDesktop`，不得写入仓库或程序安装目录。
- API Key 不写入 `AppSettings`、日志、错误信息、测试快照或发布产物。修改凭据来源优先级时必须同步测试。
- 进入 UI 日志缓冲区的外部文本必须先规范化并限制长度；写入文件的日志必须统一脱敏。新增可能携带凭据的 UI 输出时，应在进入缓冲区前脱敏。不得记录 Authorization、Cookie、Token、Secret 或完整环境变量。
- 用户提示使用简明中文；技术日志可使用英文，但错误码必须稳定且可检索。不要无迁移地复用或改变已有 `DSH-E*`、`WEB-E*`、`CFG-E*`、`API-E*`、`APP-E*` 语义。

## 6. 编码与 UI 原则

- 保持现有 MVVM 和依赖注入边界：ViewModel 不直接创建进程或网络客户端，View 的 code-behind 只处理控件生命周期和系统 UI 桥接。
- I/O 和进程操作使用异步 API 并传递 `CancellationToken`；禁止在 UI 线程使用 `.Wait()`、`.Result` 或长时间同步工作。
- WPF 控件只能在所属 Dispatcher 上访问。后台事件更新 UI 时必须显式切回 UI 线程。
- 优先小范围、可审查的修改。允许为正确性删除、移动或重构代码，但必须更新所有调用方和测试；禁止用“不能删方法”等绝对规则保留死代码。
- 单个方法原则上控制在约 50 行内；复杂生命周期代码按职责拆分，但不要为满足行数制造无意义包装。
- 用户可见文本、XAML 布局和 DPI 行为变化需检查窗口缩放、托盘交互、键盘焦点及 125%/150% DPI。
- 详细实现约束见 `code_rule.md`。

## 7. 测试与验证

按改动风险选择测试，不得只依赖编译成功：

- 纯模型、解析、格式化、状态迁移：补充或运行 UnitTests。
- 进程启动/停止、进程树、端口、HTTP 探测：补充或运行 IntegrationTests。
- ViewModel、UI 命令或状态展示：至少覆盖命令可用性和状态映射；可见布局变化需手工检查 WPF 窗口。
- 发布、依赖、版本或安装说明变化：运行完整发布门禁。
- 发布脚本变化必须验证 framework-dependent 包不包含 .NET、Node、npm、npx、DSH 缓存或用户数据，并保持发布 ZIP 和主 EXE 的体积门禁。缓存发现变化必须覆盖精确版本/bin 校验、错误候选拒绝、全局 DSH 优先和无缓存 npx 回退。

常用命令（仓库根目录 PowerShell）：

```powershell
dotnet restore DeepSeekHarnessDesktop.sln
dotnet build DeepSeekHarnessDesktop.sln -c Debug --no-restore -m:1
dotnet test DeepSeekHarnessDesktop.sln -c Release --no-restore -m:1
```

发布前执行：

```powershell
.\eng\Verify-Release.ps1
```

该脚本会构建 Debug/Release、运行单元和集成测试、生成 framework-dependent ZIP，并校验版本、包内容和体积上限。它使用 `--no-restore`，执行前必须完成 restore。

## 8. 版本与文档（强制）

每次修改应用代码、配置、构建/发布自动化、测试或用户可见文档时，都必须在同一改动中：

1. 按语义化版本递增 `Directory.Build.props` 的 `AppVersion`：普通兼容修改升 patch，兼容新功能升 minor，不兼容变更升 major。
2. 将 `src/DeepSeekHarnessDesktop/app.manifest` 的 `assemblyIdentity` 同步为 `<AppVersion>.0`。
3. 在 `VERSION_HISTORY.md` 顶部增加带日期的对应版本记录，概述实际变更。
4. 不批量改写设计、验证或发行记录中的历史版本号。
5. 运行与改动相关的构建和测试；发布脚本继续从 `AppVersion` 派生产物名称。

只改内部 AI 协作规则是否需要升版，以 `AGENTS.md` 当时已有的版本政策为准；若同一改动已因其他文件升版，不重复递增。

## 9. 交付检查

- 改动仅覆盖需求及必要依赖，没有修改生成文件或无关模块。
- 关键边界均有测试：进程所有权、合法状态迁移、loopback/同源限制、命令转义和敏感信息脱敏。
- 新增服务已在 `App.xaml.cs` 注册，生命周期与预期一致。
- 用户可见行为、安装方式或维护流程变化已同步相应文档。
- `AGENTS.md` 与 `CLAUDE.md` 内容一致；版本三处一致；相关构建与测试通过。
