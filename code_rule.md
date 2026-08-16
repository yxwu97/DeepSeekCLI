# DeepSeek Harness Desktop 编码规则

本文补充 `AGENTS.md` / `CLAUDE.md` 的仓库级要求，适用于 `src/`、`tests/` 和 `eng/`。发生冲突时，优先遵循仓库级指南和用户当前需求。

## 1. 通用规则

- 先读取相关接口、实现、调用方和测试，再修改代码；禁止编造 API、类型、配置项或 DSH 行为。
- 修改范围应与需求和必要依赖一致，不顺手重写整个文件或重构无关模块。
- 允许删除、重命名和拆分已有代码，但必须有明确收益，并同步所有调用方、DI 注册、文档和测试。
- 使用仓库现有命名、错误模型、日志方式和依赖注入模式。引入新抽象前先确认它能隔离 I/O、提高可测试性或消除真实重复。
- 保持 `Nullable` 干净，禁止用无依据的 `!`、宽泛 catch 或关闭编译警告掩盖问题。
- 方法原则上不超过约 50 行，类保持单一职责；生命周期编排可以较长，但应把探测、转换、资源处理等独立逻辑提取为可测试单元。
- 源码、XAML、JSON、XML、Markdown 和 PowerShell 使用 UTF-8；用户可见中文、日志和异常信息不得乱码。
- 注释解释约束和原因，不复述代码。公开接口只有在用途不明显时添加简洁 XML 文档。

## 2. 分层规则

### Models

- 仅承载配置、状态、错误和跨层数据；不得直接访问文件、网络、进程或 WPF 控件。
- 新增配置字段时明确默认值、空值语义、验证规则及旧 `SchemaVersion` 的兼容方式。
- 金额等精确数值使用 `decimal`，时间优先使用 `DateTimeOffset`，地址使用已验证的 `Uri`，不要用松散字符串替代领域类型。

### Services

- 外部 I/O 能力先定义在 `Services/Abstractions/`，便于 ViewModel 和协调器测试；纯内部辅助类不强制增加空接口。
- `HarnessLifecycleCoordinator` 只负责编排生命周期；`HarnessStateMachine` 只决定合法状态迁移；`HarnessProcessManager` 只管理当前 Owned 进程；不要把这些职责重新混合。
- 新服务统一在 `App.xaml.cs` 注册，并明确 Singleton/Transient 生命周期。持有状态、事件或系统句柄的 Singleton 必须可释放。
- HTTP 响应、请求、流、进程和原生句柄使用 `using` / `await using` / SafeHandle 管理。

### ViewModels 与 Views

- ViewModel 暴露可绑定状态和命令，不直接操作 `Process`、文件选择器、WebView2 或静态 `MessageBox`；通过服务抽象访问系统能力。
- code-behind 只处理控件初始化、窗口生命周期、Dispatcher 和无法合理绑定的 UI 事件，不承载 DSH 业务规则。
- 命令的 `CanExecute` 必须与状态机一致：`RunningExternal` 不允许停止/重启，冲突生命周期操作执行期间禁用相关命令。
- 集合和属性必须在 UI Dispatcher 上更新；不要假定进程、计时器或 HTTP 回调位于 UI 线程。

### Utilities

- 保持无状态、确定性和边界清晰。解析外部文本时限制长度、拒绝无效输入，并覆盖 Unicode、空输入和边界值测试。

## 3. 异步、并发与资源

- I/O API 采用 `async Task` / `ValueTask`，完整传递调用方的 `CancellationToken`。仅事件处理器和 WPF 生命周期入口允许 `async void`，且必须捕获并上报预期异常。
- 禁止在 UI 线程调用 `.Wait()`、`.Result` 或同步等待异步任务。应用最终退出阶段若必须同步释放，应保持在已有集中退出路径内并说明原因。
- `SemaphoreSlim.WaitAsync` 后必须在 `finally` 中释放。共享可变状态使用现有锁保护，不在持锁期间执行 I/O、触发外部事件或 `await`。
- 生命周期操作必须保留 generation/operation token 防护；任何异步完成结果在提交状态前检查其是否仍属于当前操作。
- 事件订阅和后台 watcher 必须在停止、重启或 Dispose 时取消并等待结束，防止窗口关闭后回调和对象泄漏。
- 测试异步竞态使用 `TaskCompletionSource`、有限超时和确定性信号，禁止依赖长时间 `Thread.Sleep`。

## 4. DSH 进程与命令安全

- 启动前验证工作目录和可执行文件。路径与参数使用结构化字段，不接受一整段可执行 Shell 文本。
- 原生 `.exe/.com` 参数使用 `ProcessStartInfo.ArgumentList`；受控 `.cmd` 入口只通过 `CmdCommandLineBuilder` 生成 `cmd.exe /d /v:off /s /c` 命令。
- 扩展允许的字符或参数前，必须补充空格、引号、`& | < > ^ % ! ( )` 和中文路径测试，证明不存在命令注入或转义回归。
- Owned 进程启动后立即加入 Job Object 并异步读取 stdout/stderr；必须处理“启动后立即退出”和“订阅前退出”的竞态。
- 停止操作针对已跟踪的 Owned 进程树，带有限超时并确保最终释放；不得扫描端口后结束不明 PID。
- DSH 包版本固定值发生变化时，同步解析器测试、开发文档和版本记录，并验证首次无缓存下载和已有缓存启动。

## 5. 健康检查与导航安全

- 只接受绝对、无用户信息的 `http/https` loopback URI（`127.0.0.1`、`localhost`、`::1`）。
- 健康检查必须区分：不可达、已确认 DSH、可达但身份未知、外部重定向和无效 URI；不得把任意 2xx 服务当作 DSH。
- DSH 身份确认继续验证 HTML 和稳定标记；修改标记时保留兼容策略并覆盖误判测试。
- 限制响应体大小、单次探测时间和重定向次数。重定向每一步都重新验证 loopback，禁止凭最终地址补验。
- WebView2 仅加载已确认服务的同源页面。新窗口和外部链接仅允许合法 `http/https`，交由系统默认浏览器。
- 不启用远程内容、本机宿主对象、任意脚本注入、浏览器加速键或 Release DevTools，除非需求明确且完成安全评审与测试。
- 账户请求只访问已定义的 DeepSeek API 端点；Authorization 值不得进入日志、异常技术信息或测试输出。

## 6. 配置、凭据与日志

- `SettingsService` 保持原子保存、备份恢复、默认值回退和边界验证。写文件失败转换为稳定的 `HarnessError`，同时保留可诊断但已脱敏的技术信息。
- API Key 的现有查找顺序是：进程环境变量 `DEEPSEEK_API_KEY`、`DSH_HOME/.credentials.yaml`、工作目录 `.env`、`DSH_HOME/.env`。调整顺序或解析规则必须补测试，不把密钥复制到设置文件。
- 凭据解析必须保守：格式含糊、非法字符或损坏内容时返回不可用，不猜测或回显原文。`.credentials.yaml` 的重复顶级 Key 视为无效，`.env` 沿用最后一项生效语义。
- 所有 DSH 输出先经 `OutputLineProcessor` 规范化和限长，再进入缓冲区；写入 Serilog 文件的内容最终经 `SensitiveDataRedactor` 处理。新增可能携带凭据的 UI 输出时，应在进入缓冲区前脱敏。
- 新增敏感字段名时同步扩展脱敏规则和测试。异常对象、HTTP Header、查询参数及环境变量字典同样视为泄露入口。
- 日志事件使用稳定 EventId；用户提示与技术诊断分离。保留原始异常作为内部 cause，但向 UI 展示可操作且不泄密的中文信息。

## 7. 错误处理

- 可预期的 DSH/配置/WebView2 问题使用 `HarnessException(HarnessError)`；账户 API 使用现有 `DeepSeekAccountException`，不要用 `InvalidOperationException` 代替领域错误跨越 UI 边界。
- 错误码前缀保持职责稳定：`DSH-E*` 生命周期与服务，`WEB-E*` WebView2，`CFG-E*` 配置，`API-E*` DeepSeek API，`APP-E*` 应用级故障。
- `UserMessage` 简短、可操作且不含实现细节；`TechnicalMessage` 可诊断但必须脱敏；`IsRetryable` 必须与 UI 可执行操作一致。
- 只捕获能处理、转换或补充上下文的异常。不得使用空 catch；取消异常通常继续传播或映射为明确的取消状态。

## 8. WPF 与可访问性

- 沿用现有资源、字号、间距和控件风格，避免在单个窗口内引入孤立样式。
- 固定格式控件使用稳定尺寸和合理的最小宽高；长工作目录、错误文本和金额必须支持截断、换行或滚动，不能遮挡相邻控件。
- 图标按钮需要可访问名称或 ToolTip；主要操作支持键盘焦点，状态变化不能只依赖颜色表达。
- 窗口关闭默认隐藏到托盘，只有明确“退出”才清理 Owned DSH 并结束进程；修改该语义必须同步托盘和单实例测试。
- UI 变化至少检查 100%、125%、150% DPI，以及最小窗口尺寸和最大化状态；WebView2 不得覆盖宿主控制区。

## 9. 测试规则

- 修复缺陷时先增加能复现问题的测试，再修改实现；无法自动化的 UI 或系统行为需在交付说明中列出手工验证。
- 单元测试覆盖：状态迁移、命令解析/转义、URL 解析、输出限长、脱敏、配置兼容、错误映射和 ViewModel 命令状态。
- 集成测试覆盖：真实子进程输出、立即退出、进程树回收、HTTP 身份识别、重定向、端口占用和运行期健康丢失。
- 测试不依赖用户真实 `%APPDATA%`、`%LOCALAPPDATA%`、`.dsh`、API Key 或 npm 缓存；使用唯一临时目录、Fake server 和 TestHarness。
- 测试创建的进程、端口、文件和目录必须在 `finally`/Dispose 中清理。超时要有限且失败信息包含待观察对象。
- 不为通过测试降低生产安全约束，不使用真实密钥，不调用真实余额 API，不结束测试未创建的进程。

## 10. PowerShell 与发布脚本

- 脚本设置 `$ErrorActionPreference = 'Stop'`，外部命令后检查 `$LASTEXITCODE`。
- 文件操作使用 `-LiteralPath`；删除或覆盖前把路径解析为绝对路径并确认位于预期目录，尤其是 `output/` 清理。
- 发布产物名称和目录从 `Directory.Build.props` 的 `AppVersion` 派生，不在脚本中维护第二份版本。
- 发布脚本保持可重复执行；发布生成物只能进入 `output/`，不得提交 `bin/`、`obj/`、TRX、日志或用户配置。

## 11. 完成定义

1. 代码能够在警告即错误条件下构建。
2. 与风险对应的单元/集成测试已补充并通过；必要的 WPF 手工检查已完成。
3. 没有泄露密钥，没有扩大进程终止、Shell、网络或 WebView2 权限边界。
4. 配置、用户界面、安装或运行行为变化已同步相关文档。
5. `AppVersion`、manifest 和 `VERSION_HISTORY.md` 已按仓库规则同步。
6. 未修改生成目录；交付说明列出实际验证命令及任何未验证风险。
