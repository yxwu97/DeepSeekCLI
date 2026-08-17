# DeepSeek Harness 安装引导、服务地址与更新能力开发计划

## 1. 计划信息

- 来源需求：[`prompt-20260816-01-DeepSeekHarness安装服务地址与更新需求.md`](../prompt/prompt-20260816-01-DeepSeekHarness安装服务地址与更新需求.md)
- 计划日期：2026-08-16
- 计划状态：已执行（2026-08-17，Desktop 0.2.0）
- 当前基线：Desktop `0.1.6`、固定 DSH `0.1.0-rc.6`
- 目标功能版本：`0.2.0`（兼容新增功能，实际实施时从届时仓库版本统一递增）
- 预计工期：12 至 15 个工作日，不包含等待官方资料或真实环境验证的阻塞时间

本计划只制定实施步骤，不在本次改动中实现产品功能。后续执行必须同时遵守 `AGENTS.md`、`CLAUDE.md`、`code_rule.md` 以及当前代码事实。

## 2. 目标与范围

本轮实现三个闭环：

1. 在 Node.js、npx 或 DSH 启动条件不满足时，展示可取消、可重试的安装引导；用户确认后复用现有 owned DSH 启动链路下载并启动固定版本。
2. 提供本机服务地址设置、连接测试、原子保存、外部实例切换和 owned 实例非默认端口重启。
3. 在关于窗口提供手动 DSH 更新检查，展示当前验证版本和 npm `latest`，但不下载、不安装、不切换版本。

明确不包含：

- 自动安装 Node.js、调用 `winget`/Chocolatey、提升权限或运行下载的安装程序。
- 全局安装 DSH、静默升级、启动时自动检查更新或一键切换到 npm `latest`。
- 局域网、远程或公网 DSH 地址。
- DeepSeek Harness Desktop 自身自动更新。
- 在 WPF 中复制官方 Web UI 的模型、会话、工具、审批或工作区配置能力。

## 3. 当前实现基线与差距

| 范围 | 当前事实 | 主要差距 |
| --- | --- | --- |
| 依赖诊断 | `DependencyDiagnosticsService` 在 `App.OnStartup` 中手工创建一次，检查 WebView2、Node.js 和 npx | 没有接口、不能在向导中重新检查、未识别全局 `dsh.cmd`，结果是不可变启动快照 |
| 启动命令 | `DshCommandResolver` 优先 `dsh.cmd`，否则固定 npx `0.1.0-rc.6` | 不会根据配置端口生成 Web 参数；固定包名和版本分散在多个文件 |
| `.cmd` 安全 | `CmdCommandLineBuilder` 只允许两组完全固定的参数 | 需要在不接收 Shell 文本的前提下增加受控数值端口模板 |
| 生命周期 | `HarnessLifecycleCoordinator` 已有单操作门、operation CTS、generation、owned/external 区分 | 没有运行中应用新服务地址或切换 external watcher 的契约 |
| 地址安全 | `SettingsService`、`HarnessHealthMonitor`、`WebViewNavigationService` 都限制 loopback HTTP(S) | 校验逻辑重复；配置已有 `ServiceUri`，但没有 UI、测试连接和立即应用流程 |
| 主窗口 | 内容区域由 `HarnessRuntimeState` 选择 Stopped/Starting/Failed/WebView | 没有安装引导展示状态，也没有设置入口 |
| 关于窗口 | 直接绑定启动时的 `DependencyDiagnosticsResult` | 无法刷新诊断或手动查询 npm 最新版本 |
| 日志与进程 | owned 进程进入 Job Object，输出经过限长并进入最近日志 | 需要把向导取消和下载失败继续纳入同一进程所有权链路 |
| 测试 | 已有命令转义、状态机、配置、健康探测和真实子进程测试 | 缺少安装向导 ViewModel、非默认端口、地址切换、npm 响应和 SemVer 测试 |

## 4. 总体设计决策

### 4.1 安装引导不是新生命周期状态

安装引导属于宿主展示状态，不向 `HarnessRuntimeState` 增加 `Installing`。用户点击“下载并启动”后仍调用 `IHarnessLifecycleCoordinator.StartAsync`，由现有 `Starting`、Job Object、取消令牌和 generation 保护完整管理 npx 下载与 DSH 启动。

主窗口增加独立的内容模式，优先级如下：

```text
已确认 DSH 运行 -> WebView2
安装引导已激活 -> InstallationGuideView
其他情况       -> 现有生命周期状态视图
```

向导由 `DSH-E101` 自动触发，也可从失败页或停止页手动打开。向导发起启动后保持可见，显示阶段、有限日志和取消操作；进入 `RunningOwned`/`RunningExternal` 后自动关闭。

### 4.2 服务地址只表示本机 DSH origin

本期只接受绝对、无用户信息的 loopback `http/https` URI。实现一个无状态的统一 URI 校验/规范化工具，供设置保存、健康探测、输出解析和 WebView2 导航共同使用，避免不同层出现边界漂移。

设置值用于：

- 初始化和启动前探测的目标地址。
- owned DSH 的 fallback 地址。
- 非默认端口的受控 `--port` 参数。
- 已确认 DSH 的同源 WebView2 导航。

配置不接受查询字符串、片段或用户信息；路径规范化为 `/`。这使保存值与 DSH Web origin、端口启动参数保持一一对应。若实施前发现官方支持并要求子路径部署，应先更新威胁模型和本计划，不能临时放宽。

### 4.3 地址切换由生命周期协调器串行完成

不得在 Settings ViewModel 中直接替换 external watcher 或拼装状态快照。扩展 `IHarnessLifecycleCoordinator` 的地址应用操作，内部继续使用现有生命周期门和 generation：

- `Stopped`/`Failed`：保存合法地址，下一次启动使用新地址。
- `RunningExternal`：先确认新地址为 DSH，再替换 watcher，并通过新增合法事件在 `RunningExternal` 内更新快照；失败时保持原地址、原 watcher 和原配置。
- `RunningOwned`：UI 先要求用户确认，再保存新地址并调用现有串行重启；旧进程退出和旧端点释放前不得创建新进程。
- `Starting`/`Stopping`/`Restarting`/`Initializing`：禁止应用地址，命令 `CanExecute=false`。

### 4.4 更新检查与运行版本解耦

手动检查只访问固定的 npm 官方注册表端点，解析 `@deepseek-ai/dsh` 的 `latest` 版本并用 `NuGet.Versioning` 比较 prerelease。检查结果不写入 `AppSettings`，不修改启动命令，也不改变 Harness 状态机。

### 4.5 固定 DSH 元数据集中维护

把包名、验证版本和默认地址集中到单一代码事实来源。`DshCommandResolver`、`CmdCommandLineBuilder`、依赖诊断、关于窗口和更新检查都引用该来源，避免以后只修改其中一处。

## 5. 目标组件与文件影响

名称以当前仓库命名风格为准；实施时若已有更合适的类型，应复用而不是重复创建。

### 5.1 新增组件

| 层 | 计划组件 | 职责 |
| --- | --- | --- |
| Models | 依赖诊断状态模型 | 分别表达 WebView2、全局 dsh、Node.js、npx 的 Available/Missing/Unusable，不用空字符串推断状态 |
| Models | `DshUpdateCheckResult` | 当前验证版本、latest、是否有新版、检查时间和可展示错误 |
| Services/Abstractions | `IDependencyDiagnosticsService` | 可取消地重新诊断依赖，供启动与安装引导复用 |
| Services/Abstractions | `IDshReleaseService` | 手动查询并解析 npm `latest`，不承担下载或安装 |
| Services/Abstractions | `IExternalLinkLauncher` | 只允许打开固定或已验证的 HTTP(S) 官方链接，隔离 `Process.Start` |
| Services | `DshReleaseService` | 固定端点、有限响应、JSON 解析、超时和错误映射 |
| Services | `ExternalLinkLauncher` | 使用系统默认浏览器打开 Node.js、DSH 文档和官方仓库 |
| Utilities | DSH 包元数据常量 | 统一包名、验证版本和默认地址 |
| Utilities | 服务 URI 校验器 | 结构化解析、loopback 校验、规范化和端口提取 |
| ViewModels | `InstallationGuideViewModel` | 步骤、重新检查、打开下载页、启动、取消、日志和命令状态 |
| ViewModels | `SettingsViewModel` | 地址编辑、规范化、测试、保存、恢复默认和运行中应用 |
| ViewModels | `AboutViewModel` | 最新诊断信息和手动更新检查状态 |
| Views | `InstallationGuideView` | 主内容区内的四步安装引导 |
| Views | `SettingsWindow` | 本机服务地址设置窗口 |

### 5.2 重点修改文件

| 文件/区域 | 计划修改 |
| --- | --- |
| `App.xaml.cs` | 把诊断、更新、外部链接和新增 ViewModel 注册到 DI，明确 Singleton 生命周期 |
| `DependencyDiagnosticsService.cs` | 实现抽象，识别全局 dsh 与 Node/npx 两条可用路径，确保版本探测进程可取消和释放 |
| `DependencyDiagnosticsResult.cs` | 从松散可空字段扩展为可绑定的分类结果，同时保留关于窗口需要的版本信息 |
| `DshCommandResolver.cs` | 使用集中版本元数据；从 `ServiceUri` 提取端口并生成受控 Web 参数 |
| `CmdCommandLineBuilder.cs` | 接受固定模板加合法端口值，继续拒绝其他参数和 Shell 元字符 |
| `HarnessLifecycleCoordinator.cs`、接口和状态事件 | 增加串行地址应用/外部切换操作，不破坏已有 Start/Stop/Restart 竞态保护 |
| `HarnessHealthMonitor.cs`、`WebViewNavigationService.cs`、`SettingsService.cs` | 复用统一 URI 规则，保留 DSH 身份检查、loopback 重定向和同源限制 |
| `MainWindowViewModel.cs` | 编排子 ViewModel、安装引导可见性、设置入口事件和所有命令刷新 |
| `MainWindow.xaml/.xaml.cs` | 增加设置按钮、安装引导内容模式和窗口桥接，不放入业务判断 |
| `FailedView.xaml`、`StoppedView.xaml`、`StartingView.xaml` | 增加符合状态的引导/重新检查/取消入口，避免重复业务逻辑 |
| `AboutWindow.xaml/.xaml.cs` | 改绑 `AboutViewModel`，增加重新诊断、检查更新和官方资料入口 |
| `Directory.Packages.props`、应用 csproj | 集中声明并引用 `NuGet.Versioning`，不在项目文件写版本 |

### 5.3 配置兼容策略

本期可直接复用已有 `AppSettings.ServiceUri`，更新检查与引导状态不持久化，因此原则上不修改配置结构，也不递增 `SchemaVersion`。

如果实施中新增连接模式、更新时间或其他持久字段，必须先：

1. 把 `SchemaVersion` 从 1 递增到 2。
2. 实现显式的 v1 到 v2 迁移，而不是把 v1 当损坏配置回退默认值。
3. 覆盖主配置、备份恢复、迁移失败和原子写入测试。

## 6. 分阶段实施计划

### 6.1 阶段 0：事实验证与设计冻结

工期：1 个工作日。

| 编号 | 任务 | 输出 |
| --- | --- | --- |
| P01-001 | 在固定 `0.1.0-rc.6` 上验证 `dsh web --port` 与官方示例 `dsh --profile web --port` 的实际行为 | 选定唯一的内置参数模板和真实启动记录 |
| P01-002 | 验证全局 dsh 是否稳定支持 `--version`；不支持时只显示路径和“版本未知” | 诊断探测契约 |
| P01-003 | 冻结新错误策略并更新详细设计错误表 | 不复用旧语义的错误码清单 |
| P01-004 | 记录当前 72 个 UnitTests、18 个 IntegrationTests 基线并复跑关键生命周期测试 | 基线验证记录 |
| P01-005 | 更新详细设计中的安装引导展示状态、地址应用序列和更新检查边界 | 经评审的设计增量 |

计划错误策略：

- 保留 `DSH-E101` 表示缺少或无法使用 DSH 启动依赖。
- 保留 `DSH-E201` 表示 owned 进程意外退出，不把所有 stderr 猜测成网络错误。
- 仅当 npx 输出包含经过验证的稳定 npm 网络/registry 错误标识时，新增独立的“固定版本准备失败”错误；未知退出仍使用 `DSH-E201` 并提供脱敏日志。
- 更新检查错误只存在于更新检查结果，不把 Harness 生命周期推进到 `Failed`。

完成条件：实际 CLI 行为、错误码和设计文档一致；如果固定版本不支持端口参数，则先解决版本/方案阻断，不进入阶段 3。

### 6.2 阶段 1：共享安全原语与依赖基线

工期：1.5 个工作日。

| 编号 | 任务 | 输出 |
| --- | --- | --- |
| P01-101 | 集中 DSH 包名、验证版本和默认服务地址 | 单一元数据来源及引用测试 |
| P01-102 | 实现 URI 解析、规范化、loopback 和端口边界工具 | 参数化 URI 单元测试 |
| P01-103 | 让设置、健康检查和导航复用统一 URI 判定 | 无安全边界漂移的回归测试 |
| P01-104 | 引入 `NuGet.Versioning` 并建立 rc 版本比较测试 | SemVer prerelease 测试 |
| P01-105 | 扩展依赖诊断模型和接口，保留取消令牌与有限超时 | 可替换诊断服务 |
| P01-106 | 更新 DI 注册和构造调用方 | `validateScopes:true` 构建通过 |

完成条件：现有默认启动、健康探测、WebView2 同源和设置恢复测试全部通过；非法 URI 与 prerelease 比较的新测试通过。

### 6.3 阶段 2：依赖诊断与安装引导

工期：3 个工作日。

| 编号 | 任务 | 输出 |
| --- | --- | --- |
| P01-201 | 诊断全局 dsh 与 Node+npx 两条可运行路径，避免“无全局 dsh”误判 | 依赖分类与单元测试 |
| P01-202 | 修正版本探测子进程的超时、取消、退出和 Dispose 行为 | 无残留诊断进程测试 |
| P01-203 | 实现安装引导 ViewModel 的步骤、命令、单操作门和取消 | `InstallationGuideViewModel` 测试 |
| P01-204 | 实现官方 Node.js 下载链接启动服务 | 固定域名/协议校验及替身测试 |
| P01-205 | 将“下载并启动”连接到协调器，不新增旁路进程管理 | owned 启动、取消和失败回归测试 |
| P01-206 | 在向导中订阅有界脱敏日志，处理重入、关闭和过期回调 | 日志容量与取消测试 |
| P01-207 | 实现安装引导 WPF 视图及 DSH-E101/停止页入口 | 可操作的四步 UI |

完成条件：覆盖 `AC-INSTALL-001` 至 `AC-INSTALL-006`；用户未确认前不执行 npx；取消后 owned 进程树和订阅均释放。

### 6.4 阶段 3：服务地址设置与端口闭环

工期：3 个工作日。

| 编号 | 任务 | 输出 |
| --- | --- | --- |
| P01-301 | 实现 Settings ViewModel 的编辑、规范化、恢复默认和命令状态 | ViewModel 单元测试 |
| P01-302 | 实现不保存的“测试连接”，映射 Invalid/Unreachable/Unknown/Redirect/Confirmed | 健康结果到中文提示测试 |
| P01-303 | 扩展 resolver 与 `.cmd` 固定模板，安全传递 1-65535 端口 | 端口参数和注入边界测试 |
| P01-304 | 扩展协调器的 external 地址切换事件与 watcher 替换 | generation、失败回滚和无旧 watcher 回调测试 |
| P01-305 | 实现 owned 地址保存确认与串行重启 | 旧进程退出、旧地址释放、新端口启动测试 |
| P01-306 | 保证 external 切换失败时原设置、快照、导航和 watcher 不变 | 事务回滚测试 |
| P01-307 | 实现 SettingsWindow 和主工具栏设置入口 | 键盘与可访问性完整的设置 UI |
| P01-308 | 增强 TestHarness/FakeHarnessServer 支持指定端口 | 非默认端口集成测试夹具 |

`.cmd` 安全测试必须覆盖空格、引号、`& | < > ^ % ! ( )`、中文路径、端口 0/65536、非数字和附加参数。测试只能证明程序生成的数值端口模板可用，不能放宽为用户参数列表。

完成条件：覆盖 `AC-URI-001` 至 `AC-URI-006`；未知服务不导航、不被结束；`RunningExternal` 仍禁止停止和重启。

### 6.5 阶段 4：手动更新检查

工期：2 个工作日。

| 编号 | 任务 | 输出 |
| --- | --- | --- |
| P01-401 | 实现固定 npm endpoint、15 秒内超时和 64 KiB 有界响应 | `DshReleaseService` |
| P01-402 | 只解析 `version` 字段并使用 SemVer 比较 | 正常、rc 顺序、缺字段和畸形 JSON 测试 |
| P01-403 | 实现取消、重复点击抑制和失败隔离 | 更新检查并发测试 |
| P01-404 | 实现 AboutViewModel，支持重新诊断与手动检查更新 | 关于窗口命令与状态测试 |
| P01-405 | 更新 AboutWindow，显示检查时间、latest、预览风险和官方入口 | 手动更新检查 UI |
| P01-406 | 使用替身 handler 和本地假 registry 覆盖 HTTP 状态、超时、过大响应 | 网络边界测试 |

生产端点必须固定为 npm 官方注册表；测试可通过内部构造参数或替身 handler 注入本地地址，不能把 endpoint 暴露成用户配置。

完成条件：覆盖 `AC-UPDATE-001` 至 `AC-UPDATE-005`；检查失败不修改设置、固定版本、Harness 状态或现有连接。

### 6.6 阶段 5：UI 集成、竞态与安全回归

工期：2 个工作日。

| 编号 | 任务 | 输出 |
| --- | --- | --- |
| P01-501 | 整合主内容显示优先级和所有命令 `CanExecute` | 展示状态映射测试 |
| P01-502 | 覆盖启动、引导重试、设置切换、停止和退出之间的竞态 | 确定性 `TaskCompletionSource` 测试 |
| P01-503 | 验证所有事件订阅、CTS、Semaphore、HTTP 响应和窗口关闭释放 | 资源生命周期检查 |
| P01-504 | 扩展外部文本脱敏测试，覆盖 npm registry、代理和认证错误样本 | UI/文件日志无敏感信息 |
| P01-505 | 手工检查 100%、125%、150% DPI、最小窗口、最大化、键盘焦点和托盘恢复 | 带日期的 UI 验证记录 |
| P01-506 | 验证 WebView2 仍只加载已确认同源 DSH，官方链接走系统浏览器 | 导航安全回归记录 |

完成条件：安装、设置、更新三个 UI 流程不重叠、不遮挡，长路径/地址/错误文本可换行或截断，状态变化不只依赖颜色。

### 6.7 阶段 6：文档、版本与发布门禁

工期：1 至 2 个工作日。

| 编号 | 任务 | 输出 |
| --- | --- | --- |
| P01-601 | 更新开发文档和详细设计 | 实际组件、流程、错误码、配置和安全边界 |
| P01-602 | 更新 `docs/installation.md` | Node.js 引导、服务地址、更新检查和故障处理 |
| P01-603 | 在 `docs/validation/` 新增本阶段验证记录 | 命令、结果、环境和未验证项 |
| P01-604 | 按兼容新功能递增 AppVersion，并同步 manifest 与版本历史 | 三处版本一致 |
| P01-605 | 比较 `AGENTS.md` 与 `CLAUDE.md` | 内容完全一致 |
| P01-606 | 运行完整发布门禁并检查 ZIP 内容 | `output/` 中的发布产物和报告 |

完成条件：文档描述与实际行为一致，历史验证记录不被重写，发布 ZIP 不包含日志、缓存、配置、测试文件或凭据。

## 7. 测试矩阵

| 测试层 | 新增/扩展测试 | 关键断言 |
| --- | --- | --- |
| Unit | `DependencyDiagnosticsServiceTests` | 全局 dsh 可用时不要求 Node；Node+npx 可用时可进入准备步骤；取消无残留 |
| Unit | `CommandAndOutputTests` | 默认/自定义端口参数准确，所有非模板参数和元字符拒绝 |
| Unit | URI validator tests | IPv4、localhost、IPv6、HTTPS、规范化、用户信息、远程地址、query/fragment 边界 |
| Unit | `SettingsServiceTests` | 地址保存、备份、原子替换、无 schema 变化；若变更 schema 则覆盖 v1 迁移 |
| Unit | lifecycle tests | external 切换、watcher 替换、owned 重启、取消、generation 过期结果丢弃 |
| Unit | installation ViewModel tests | 步骤迁移、命令状态、用户确认前无 I/O、重试、取消、日志限长 |
| Unit | settings ViewModel tests | 测试不保存、失败不覆盖、运行中禁用、确认后的重启调用 |
| Unit | release/about tests | rc SemVer、HTTP/JSON 错误、超时、取消、重复点击、无状态副作用 |
| Integration | process manager/lifecycle | 首次 npx 模拟下载期间取消，Job Object 回收完整子进程树 |
| Integration | health/fake server | 非默认端口、DSH 身份、未知服务、loopback/remote redirect |
| Integration | settings apply | external 地址成功切换与失败回滚；owned 端口切换等待旧地址释放 |
| Manual WPF | 主窗口/向导/设置/关于 | DPI、最小尺寸、键盘焦点、托盘、长文本和进度状态 |

测试不得依赖用户真实 `%APPDATA%`、`%LOCALAPPDATA%`、npm 缓存、`.dsh` 或 API Key。所有进程、端口、文件和临时目录必须由测试创建并在 `finally`/Dispose 中清理。

## 8. 验收追踪

| 需求验收项 | 主要任务 | 验证方式 |
| --- | --- | --- |
| `AC-INSTALL-001`、`002` | P01-201、P01-207 | 诊断服务与安装视图测试 |
| `AC-INSTALL-003`、`004` | P01-203、P01-205 | ViewModel 无副作用断言、owned 启动集成测试 |
| `AC-INSTALL-005`、`006` | P01-202、P01-206、P01-504 | 取消进程树与脱敏测试 |
| `AC-URI-001`、`002` | P01-102、P01-301、P01-303 | URI/命令参数单元测试 |
| `AC-URI-003`、`004` | P01-302、P01-304 | Fake server 与状态机测试 |
| `AC-URI-005`、`006` | P01-305、P01-306、P01-308 | owned 端口与 external 回滚集成测试 |
| `AC-UPDATE-001`、`002` | P01-401、P01-402、P01-405 | registry 与 SemVer 测试、About UI |
| `AC-UPDATE-003` 至 `005` | P01-403、P01-404、P01-406 | 失败隔离、取消和并发测试 |

## 9. 风险与控制

| 风险 | 影响 | 控制措施 |
| --- | --- | --- |
| 固定 DSH 的端口参数与官方 README 示例不一致 | owned 非默认端口无法启动 | 阶段 0 先对 `0.1.0-rc.6` 做真实验证，参数模板经测试后才进入 allowlist |
| 把 npx 缓存误当全局安装状态 | 向导误判或扫描不稳定目录 | 只判断公开命令入口和受控启动结果，不扫描 `_npx` |
| npm stderr 格式不稳定 | 错误码误分类 | 仅识别验证过的稳定标识，其他失败保留 DSH-E201 和脱敏日志 |
| 设置地址时 watcher/进程发生竞态 | 旧结果覆盖新配置 | 地址应用纳入协调器单操作门、operation CTS 和 generation |
| 新端口重启失败 | owned 服务暂时不可用 | 保存前确认，保留旧地址值用于回滚提示，不在未确认时创建第二个进程 |
| URI 校验在多层不一致 | 远程加载或合法地址被拒绝 | 使用单一结构化 validator，健康与导航继续独立执行身份/同源检查 |
| 更新检查返回恶意或超大内容 | 内存、UI 或日志风险 | 固定 endpoint、有限响应、只解析 version、不呈现原始响应 |
| developer preview 破坏兼容性 | 新版本无法启动或身份识别失败 | 本期不切换版本；未来必须先建立兼容清单和回退机制 |
| UI 新增窗口和订阅未释放 | 关闭后回调或内存泄漏 | 明确 Singleton/Transient 生命周期，关闭/Dispose 时解除订阅并取消任务 |

## 10. 验证命令与门禁

各阶段至少运行其相关测试；阶段 6 从仓库根目录执行：

```powershell
dotnet restore DeepSeekHarnessDesktop.sln
dotnet build DeepSeekHarnessDesktop.sln -c Debug --no-restore -m:1
dotnet test DeepSeekHarnessDesktop.sln -c Release --no-restore -m:1
.\eng\Verify-Release.ps1
```

若运行中的 Desktop 锁住 `bin/Release`，不得擅自结束用户进程。可以先请求用户关闭，或把验证输出定向到 `output/` 下的独立目录，并在验证记录中说明。

发布门禁必须同时确认：

- 0 warning、0 error，UnitTests 与 IntegrationTests 全部通过。
- 真实固定 DSH 的默认端口、非默认端口、首次无缓存启动和取消均有记录。
- external DSH 不出现停止/重启入口，未知服务不导航、不结束。
- npm 更新检查失败不影响启动，且没有后台自动请求。
- 日志、错误、测试结果和发布 ZIP 不含 Token、Cookie、代理凭据或用户配置。
- `AGENTS.md` 与 `CLAUDE.md` 完全一致，版本三处一致。

## 11. 完成定义

只有同时满足以下条件，计划才可标记完成：

1. 三组验收标准全部有自动化测试或明确的手工验证证据。
2. 安装引导没有创建第二套生命周期或绕过 Job Object。
3. 自定义地址仍限定 loopback，所有导航都经过身份和同源校验。
4. 更新功能只检查，不安装、不切换、不修改固定版本。
5. 所有异步操作可取消，过期结果不能更新新状态，事件和资源成对释放。
6. 开发文档、详细设计、安装说明、验证记录、版本和发布产物全部同步。
7. 未修改或提交 `bin/`、`obj/`、`TestResults/`、日志、npm 缓存或用户数据。
