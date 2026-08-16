# DeepSeek Harness Desktop 开发计划

## 1. 文档信息

| 项目 | 内容 |
|---|---|
| 文档版本 | 1.1 |
| 编写日期 | 2026-08-14 |
| 目标版本 | MVP 0.1.0 |
| 目标平台 | Windows 10/11 x64 |
| 计划基准 | 单人全职开发 |
| 计划工期 | 16 个工作日实施 + 2 个工作日缓冲 |
| 发布形式 | Windows x64 self-contained ZIP |

相关文档：

- [开发方案](./deepseek-harness-desktop-development.md)
- [详细设计](./deepseek-harness-desktop-detailed-design.md)
- [交互原型](./deepseek-harness-desktop-prototype.html)

## 2. 计划目标

本计划用于指导 DeepSeek Harness Desktop MVP 0.1.0 从空工程到可发布 ZIP 的开发、验证和验收。

计划必须确保：

1. 双击 EXE 后无需终端或隐藏的交互确认即可启动 DSH。
2. 只管理当前桌面宿主创建的 DSH 进程。
3. Owned DSH 固定随桌面宿主退出，不遗留 cmd/node 进程。
4. 只有通过身份确认的 DSH 服务才能进入 `RunningExternal` 或被 WebView2 加载。
5. 启动、停止和重启严格串行，不产生双实例或陈旧回调覆盖。
6. 页面刷新与 DSH 重启保持独立。
7. 错误、日志、配置恢复和发布环境均可验证。

## 3. 计划边界

### 3.1 MVP 范围

- WPF 主窗口和原生状态视图。
- 工作目录选择与持久化。
- DSH 命令解析、启动、停止和重启。
- Windows Job Object 进程树清理。
- stdout/stderr 捕获、URL 解析和日志展示。
- DSH 身份确认、启动探测和外部实例主动监测。
- WebView2 初始化、导航、刷新、链接限制和故障恢复。
- 单实例、配置原子保存、日志滚动与脱敏。
- Windows x64 self-contained ZIP 发布。
- 系统托盘常驻、关闭隐藏、托盘恢复和显式退出。
- DeepSeek API Key 自动解析与单次手工覆盖、API 可用性和账户余额查询。

### 3.2 本期不做

- 多工作区、多 DSH 实例。
- 远程 Harness 地址。
- 自动升级 DSH。
- 自定义 `.cmd` 或 `.bat` 启动脚本。
- 模型、会话、工具和审批界面的原生重做。
- DeepSeek 账号资料和账户级历史 Token 统计；官方公开 API 当前未提供对应端点。
- 正式安装包、自动更新和开机启动。

## 4. 执行原则

1. 先验证 Windows 平台风险，再铺开产品功能。
2. Job Object 与进程管理同时实现，不延后到发布阶段。
3. 健康探测必须先确认 DSH 身份，再允许导航。
4. 核心服务先以单元测试和集成替身验证，再接入 WPF。
5. 每个阶段均有独立完成条件；未通过阶段门禁不得进入下一阶段。
6. 测试随功能实现，不在发布前集中补写。
7. 默认 DSH 回退命令固定为 `npx -y @deepseek-ai/dsh@0.1.0-rc.6 web`。

## 5. 总体进度

| 阶段 | 工作日 | 累计 | 阶段目标 |
|---|---:|---:|---|
| 阶段 0：技术预验证 | 1 | T+1 | 消除 Windows 与 DSH 高风险假设 |
| 阶段 1：工程骨架与核心模型 | 2 | T+3 | 建立可构建、可测试的工程基线 |
| 阶段 2：进程生命周期核心 | 4 | T+7 | 完成受控子进程和并发状态闭环 |
| 阶段 3：探测与真实 DSH 闭环 | 3 | T+10 | 完成身份确认、外部实例和真实 DSH 联调 |
| 阶段 4：WPF 与 WebView2 产品闭环 | 3 | T+13 | 完成用户可操作的桌面体验 |
| 阶段 5：可靠性与发布验收 | 3 | T+16 | 生成发布候选 ZIP |
| 缓冲 | 2 | T+18 | 处理兼容问题和阻断缺陷 |
| 阶段 6：托盘与账户信息 | 扩展阶段 | T+18 后 | 关闭隐藏、余额查询和官方能力边界 |

关键路径：

```mermaid
flowchart LR
    P0[技术预验证] --> P1[工程骨架]
    P1 --> P2[进程生命周期]
    P2 --> P3[探测与真实 DSH]
    P3 --> P4[WPF 与 WebView2]
    P4 --> P5[可靠性与发布]
P5 --> RC[MVP 0.1.0 RC]
    RC --> P6[托盘与账户信息]
```

## 6. 分阶段任务

### 6.1 阶段 0：技术预验证

工期：1 个工作日。

任务：

| 编号 | 任务 | 输出 |
|---|---|---|
| DEV-001 | 验证 WPF `.NET 8` 与锁定版本 WebView2 可构建运行 | 最小窗口和 WebView2 初始化记录 |
| DEV-002 | 验证 `CoreWebView2Controller.AcceleratorKeyPressed` 的快捷键路由 | WebView2 获得焦点时的快捷键实验结果 |
| DEV-003 | 验证 `cmd.exe /d /v:off /s /c` 对带空格、`&`、括号和 Unicode 路径的处理 | 命令行构造测试样例 |
| DEV-004 | 验证 Job Object 能清理 cmd -> node 多层进程树 | 正常关闭和强制退出的进程清理记录 |
| DEV-005 | 验证 DSH 根页面包含标题和 `window.__DSH_BOOT__` 双特征 | 身份特征样本和探测断言 |

完成条件：

- 五项验证均有可复现结果。
- 未发现需要改变技术栈或核心设计的阻断问题。
- 若验证结论与详细设计冲突，先更新设计决策，再进入阶段 1。

### 6.2 阶段 1：工程骨架与核心模型

工期：2 个工作日。

任务：

| 编号 | 任务 | 输出 |
|---|---|---|
| DEV-101 | 创建解决方案、WPF 项目、单元测试和集成测试项目 | `DeepSeekHarnessDesktop.sln` 和项目文件 |
| DEV-102 | 建立 `Directory.Build.props` 与 Central Package Management | 可审计的锁定依赖清单 |
| DEV-103 | 配置 Nullable、x64、WPF、DI 和日志抽象 | 应用启动和依赖注入基线 |
| DEV-104 | 实现运行状态、状态快照、错误和启动选项模型 | `Models` 领域模型 |
| DEV-105 | 定义进程、探测、生命周期、配置和导航接口 | `Services/Abstractions` 接口层 |
| DEV-106 | 实现状态机及合法转换测试 | 状态转换和 generation 单元测试 |
| DEV-107 | 建立主窗口骨架和本地状态占位视图 | 可在模拟状态间切换的 WPF 窗口 |

完成条件：

- Debug 和 Release 均可构建。
- 所有状态机测试通过。
- 应用可启动并展示模拟的 Stopped、Starting、Running 和 Failed 状态。
- 项目文件不包含浮动依赖版本。

### 6.3 阶段 2：进程生命周期核心

工期：4 个工作日。

任务：

| 编号 | 任务 | 输出 |
|---|---|---|
| DEV-201 | 实现 PATH/PATHEXT 解析和自定义原生可执行文件校验 | `DshCommandResolver` |
| DEV-202 | 实现固定 npx 参数和 `.cmd` 双层命令行构造 | `CmdCommandLineBuilder` 及边界测试 |
| DEV-203 | 实现进程启动、stdout/stderr 异步读取和退出事件 | `HarnessProcessManager` |
| DEV-204 | 实现 Windows Job Object，并在进入运行态前完成进程分配 | `WindowsJobObject` |
| DEV-205 | 实现进程树停止、5 秒等待和 Job 关闭兜底 | Stop 集成测试 |
| DEV-206 | 实现 ANSI 清理、日志行截断和 URL Parser | 输出处理流水线及单元测试 |
| DEV-207 | 实现 `SemaphoreSlim`、operation CTS 和 Generation 保护 | `HarnessLifecycleCoordinator` 基础闭环 |
| DEV-208 | 实现停止、重启及 `OldProcessExited`/`OldEndpointReleased` 守卫 | 生命周期并发测试 |
| DEV-209 | 建立无真实 DSH 依赖的子进程测试夹具 | 启动、退出、超时和残留进程测试 |

完成条件：

- 重复点击启动只创建一个进程。
- 启动中停止可取消并完成清理。
- 正常退出和异常退出后测试进程树均不残留。
- 重启只有在旧进程退出且旧地址释放后才能创建新进程。
- 陈旧输出、退出事件和异步结果不能更新新 generation。

阶段阻断门槛：Job Object 清理和生命周期并发测试未通过时，不进入真实 DSH 或 WebView2 联调。

### 6.4 阶段 3：探测与真实 DSH 闭环

工期：3 个工作日。

任务：

| 编号 | 任务 | 输出 |
|---|---|---|
| DEV-301 | 实现结构化 `HealthProbeResult` | DSH、未知服务、不可达、重定向和非法 URI 分类 |
| DEV-302 | 实现手动重定向和 loopback 安全边界 | 最多 5 跳的重定向探测 |
| DEV-303 | 实现 256 KiB 有界 HTML 读取和 DSH 双特征确认 | `HarnessHealthMonitor` |
| DEV-304 | 实现启动等待、输出 URI 优先和 fallback URI | `WaitUntilReadyAsync` |
| DEV-305 | 实现 `RunningExternal` 的 5 秒主动探测和连续 3 次失联策略 | `RuntimeHealthWatcher` |
| DEV-306 | 实现 `FakeHarnessServer` 的多种响应和故障模式 | 集成测试替身 |
| DEV-307 | 联调固定版本真实 DSH 和首次 npx 缓存下载 | 真实启动、停止、重启和日志记录 |

完成条件：

- 已确认 DSH 可进入 `RunningOwned` 或 `RunningExternal`。
- 未知 HTTP 服务返回 `DSH-E205`，不导航、不创建或结束进程。
- loopback 内部重定向可成功，外部重定向返回 `DSH-E204`。
- 外部 DSH 连续 3 次不可达后进入 `Stopped`，不自动启动新实例。
- 无 npx 缓存时不等待不可见的安装确认。

### 6.5 阶段 4：WPF 与 WebView2 产品闭环

工期：3 个工作日。

任务：

| 编号 | 任务 | 输出 |
|---|---|---|
| DEV-401 | 完成主窗口命令栏、状态栏和内容区域布局 | `MainWindow` |
| DEV-402 | 实现工作目录选择、显示、校验和运行中修改限制 | WorkspacePicker |
| DEV-403 | 实现 Starting、Stopped 和 Failed 原生状态视图 | 状态页面及错误操作 |
| DEV-404 | 初始化持久化 WebView2 用户数据目录 | WebView2 Environment |
| DEV-405 | 实现已验证 URI 导航、同源限制和外部链接处理 | `WebViewNavigationService` |
| DEV-406 | 实现页面刷新、导航失败和 WebView2 进程恢复 | WebView2 故障处理 |
| DEV-407 | 实现 F5、Ctrl+Alt+R、Ctrl+Alt+L 和 F6 路由 | `DesktopShortcutRouter` |
| DEV-408 | 连接 ViewModel 命令、CanExecute 和完整状态快照 | 可操作的产品闭环 |
| DEV-409 | 实现日志窗口和最近 1,000 行有界展示 | `LogWindow` |

完成条件：

- 启动 DSH 后自动显示官方 Web UI。
- 刷新页面不会改变 DSH PID。
- WebView2 获得焦点时宿主快捷键仍按设计工作。
- 外部链接不会覆盖当前 Harness 页面。
- 非法状态下的生命周期按钮不可用。

### 6.6 阶段 5：可靠性与发布验收

工期：3 个工作日。

任务：

| 编号 | 任务 | 输出 |
|---|---|---|
| DEV-501 | 实现配置校验、原子保存、`.bak` 恢复和迁移 | `SettingsService` |
| DEV-502 | 实现滚动文件日志、事件分类和敏感信息脱敏 | `LogService` |
| DEV-503 | 实现命名 Mutex、固定命名管道协议和窗口激活 | `SingleInstanceService` |
| DEV-504 | 实现所有状态下的应用退出协调和 8 秒清理上限 | 退出集成测试 |
| DEV-505 | 实现 WebView2 Runtime、Node.js 和 npx 依赖诊断 | 依赖错误状态 |
| DEV-506 | 完成图标、manifest、版本信息和 About 信息 | 发布元数据 |
| DEV-507 | 执行 self-contained win-x64 发布 | 发布候选 ZIP |
| DEV-508 | 执行 DPI、窗口尺寸、长路径和干净环境验收 | 验收记录和已知问题 |

完成条件：

- 全部单元测试和集成测试通过，无跳过的 MVP 用例。
- Windows 10/11 x64、125% 和 150% DPI 完成验收。
- 配置损坏可从 `.bak` 恢复，日志不包含凭据。
- 干净用户环境可完成首次 npx 下载，或显示明确的网络诊断。
- 发布 ZIP 解压后可直接运行。

### 6.7 阶段 6：托盘与账户信息

任务：

| 编号 | 任务 | 输出 |
|---|---|---|
| DEV-601 | 使用 Windows 原生 NotifyIcon 实现托盘常驻 | 应用图标、打开和退出菜单 |
| DEV-602 | 将主窗口关闭改为隐藏，保留 Owned DSH 和 Job Handle | 关闭、恢复及单实例激活闭环 |
| DEV-603 | 托盘显式退出和系统会话结束接入原 8 秒清理 | 无 Owned 进程残留的退出路径 |
| DEV-604 | 按官方 `GET /user/balance` 实现余额客户端 | 固定 HTTPS 端点、Bearer 认证和错误映射 |
| DEV-605 | 实现 DeepSeek 账号窗口 | API Key 自动解析与单次覆盖、掩码、可用性和分币种余额 |
| DEV-606 | 明确账号资料和 Token 统计能力边界 | 仅按 DSH 凭据优先级读取 DeepSeek Key，不读取 WebView2 凭据，不伪造账户级统计 |
| DEV-607 | 补齐服务测试、桌面交互验收和发布复验 | 阶段 6 验收记录及新版 RC |

完成条件：

- 点击主窗口关闭按钮后窗口隐藏、托盘图标可恢复，Owned DSH 不被停止。
- 托盘“退出”执行配置保存、服务释放和 Owned DSH 清理，且操作幂等。
- 余额请求只发送到 `https://api.deepseek.com/user/balance`；API Key 自动取自 DSH 当前凭据来源，支持单次手工覆盖，且不写入桌面配置和日志。
- 401/403、429、超时、服务端错误和非法响应均显示稳定错误码。
- Token 区只说明官方 API 未提供账户级历史统计；单次模型响应的 `usage` 不冒充账户统计。

## 7. 里程碑与门禁

| 里程碑 | 时间 | 必须通过的门禁 |
|---|---:|---|
| M0 技术基线确认 | T+1 | WebView2、命令行、Job Object、DSH 身份特征均验证 |
| M1 工程基线 | T+3 | Debug/Release 构建及状态机测试通过 |
| M2 生命周期基线 | T+7 | 无双实例、无陈旧回调、无测试进程残留 |
| M3 DSH 服务闭环 | T+10 | 真实 DSH、身份确认、外部实例和重定向测试通过 |
| M4 功能完成 | T+13 | 用户主流程和 WebView2 主流程完成 |
| M5 发布候选 | T+16 | 自动化、环境和视觉验收通过 |
| MVP 0.1.0 | T+18 内 | 阻断缺陷清零，发布物和文档齐全 |

缺陷门禁：

- P0：必须为 0。
- P1：必须为 0；确需延期时必须记录影响、规避方式和批准结论。
- P2：允许进入已知问题，但不得影响 MVP 完成定义。

## 8. 测试计划

### 8.1 单元测试

重点覆盖：

- 状态机全部合法转换、非法转换和 generation。
- PATH 查找、固定 npx 参数和自定义原生可执行文件。
- `.cmd` 外层 argv 与内层 `cmd.exe` 转义。
- ANSI、IPv4、localhost、IPv6、非法端口和尾部标点。
- DSH 双特征、未知 HTTP 服务和全部重定向分支。
- 配置默认值、校验、原子保存、备份和迁移。
- 日志中的 Bearer Token、API Key 和 URL Secret。
- ViewModel 命令可用性和状态文案。

### 8.2 集成测试

至少覆盖详细设计 §25.3 的 IT-001 至 IT-016，包括：

- 正常启动、fallback URI、立即退出和启动超时。
- 外部 DSH、未知 HTTP 服务和非 HTTP 端口占用。
- 停止、重启、重复启动和启动中停止。
- 页面刷新不改变 PID。
- loopback 内部重定向和外部重定向拒绝。
- 无 npx 缓存的首次启动。
- 正常和异常退出后的 Job Object 清理。

### 8.3 UI 验证

- 820x600、1280x820、1920x1080。
- 100%、125%、150%、200% DPI；125% 和 150% 为发布阻断项。
- 中文、英文长路径和不可访问目录。
- Initializing、Stopped、Starting、RunningOwned、RunningExternal、Stopping、Restarting 和 Failed。
- 键盘焦点顺序、快捷键、Tooltip 和可访问名称。
- WebView2 Runtime 缺失、导航失败和渲染进程异常。

## 9. 每日质量要求

每个工作日结束前执行：

1. Debug 构建。
2. Release 构建。
3. 当前阶段相关单元测试和集成测试。
4. 检查新增日志是否可能包含凭据。
5. 更新任务状态、阻塞项和已知风险。

每个任务完成必须同时满足：

- 实现符合详细设计中的状态、错误码和所有权边界。
- 正常路径和关键异常路径有自动化测试。
- 没有引入浮动依赖、Shell 文本拼接或跨 generation 状态更新。
- 用户可见错误包含错误码，技术细节进入脱敏日志。

## 10. 风险与缓冲使用

| 风险 | 触发信号 | 处理方式 |
|---|---|---|
| Job Object 受现有父 Job 限制 | `AssignProcessToJobObject` 失败 | 阶段 0 验证目标系统；失败即阻断 Owned 启动，不降级为无保护运行 |
| DSH Developer Preview 行为变化 | 页面特征、输出 URL 或参数变化 | 固定 rc.6；升级必须重新验证身份特征和集成测试 |
| npx 首次下载受网络策略影响 | registry 超时、证书或代理错误 | 保留完整脱敏日志；区分依赖错误和 DSH 启动错误 |
| WebView2 快捷键无法传递 | WebView 获得焦点后宿主无事件 | 阶段 0 验证 Controller 事件，不在 UI 完成后返工 |
| 端口被无关服务占用 | `ReachableUnknown` 或 DSH `EADDRINUSE` | 返回 DSH-E205，不导航、不创建或结束未知进程 |
| 高 DPI 布局溢出 | 125%/150% 出现裁切或重叠 | 阶段 4 开始持续截图验证，缓冲期只处理残余问题 |

两天缓冲仅用于：

- 平台或 DSH 兼容性问题。
- 发布阻断级缺陷。
- 干净环境与目标 Windows 版本差异。

缓冲不用于追加 MVP 范围外功能。

## 11. 发布交付物

MVP 0.1.0 必须交付：

1. Windows x64 self-contained ZIP。
2. 应用版本和锁定依赖清单。
3. 自动化测试结果。
4. Windows 10/11 和 DPI 验收记录。
5. 干净环境首次启动记录。
6. Owned cmd/node 进程清理验证记录。
7. 使用说明、故障诊断和已知问题。

## 12. 变更控制

开发期间出现以下情况时必须先更新设计和计划：

- 修改 Owned/External 进程所有权边界。
- 修改 Job Object 或退出语义。
- 修改 DSH 版本、身份特征或默认启动命令。
- 放宽 WebView2 导航来源。
- 新增远程地址、自定义 Shell 或多实例能力。
- 调整 MVP 完成定义或发布平台。

普通实现细节和不影响里程碑的任务拆分可直接更新本计划的任务状态，无需修改详细设计。
