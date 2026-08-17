# Code 与 Chat 模式切换功能需求 Prompt

## 1. 文档信息

- 文档类型：后续设计与开发执行 Prompt
- 适用项目：DeepSeek Harness Desktop
- 编写日期：2026-08-16
- 更新日期：2026-08-17
- 需求状态：待实现
- 默认模式：Code
- Chat 入口：`https://chat.deepseek.com/`

## 2. 使用方式

将本文作为后续设计、实现和验收任务的完整输入。执行者必须先读取当前实现、项目文件、相关测试、`AGENTS.md`、`CLAUDE.md`、`code_rule.md`、开发文档和详细设计，再根据实际类型与接口实施，不得仅依据本文猜测代码结构。

本需求在桌面宿主中增加类似 Codex 的 Code/Chat 模式切换：

1. **Code 模式**继续承载本机 DeepSeek Harness Web UI，是每次应用进程启动后的默认模式。
2. **Chat 模式**承载 DeepSeek 官方免费对话页 `https://chat.deepseek.com/`。
3. 用户可以在两个模式间快速切换，切换只改变当前显示内容，不改变 DSH 进程所有权、生命周期状态或工作目录。
4. Chat 模式必须记住用户已授权保存的登录信息，使用户完全退出并重新启动桌面应用后仍可恢复官方 Chat 登录状态。

本需求不是在 WPF 中重新实现聊天、会话、模型选择、文件上传或登录界面。Code 与 Chat 都应复用各自官方 Web UI。

## 3. 当前实现基线

执行前必须核对最新代码。本文编写时的已知基线如下：

- `MainWindow.xaml` 只有一个 WebView2 控件，是否显示由 `IsRunning` 驱动。
- `MainWindowViewModel` 负责 Harness 状态、工作目录、启动/停止/重启、刷新和日志命令。
- `WebViewNavigationService` 只接受 loopback HTTP/HTTPS 服务 URI，并将已验证 DSH origin 之外的主导航交给系统浏览器。
- `WebViewNavigationService` 已处理 `NavigationStarting`、`NewWindowRequested` 和 `ProcessFailed`，Release 默认关闭 DevTools。
- 当前安全设计明确禁止自动内嵌远程地址。因此实现 Chat 模式属于有意、有限的安全边界变更，不能简单删除 loopback 或同源校验。

实现时应扩展现有 MVVM、DI 和 WebView2 边界，不得把模式选择、远程 URL 判断或进程操作堆入 View code-behind。

## 4. 产品决策

### 4.1 模式语义

定义明确的模式模型，例如 `AppContentMode.Code` 与 `AppContentMode.Chat`，不要使用多个可能互相矛盾的布尔属性表达当前模式。

| 场景 | 预期模式 |
| --- | --- |
| 应用进程首次启动 | Code |
| 从托盘隐藏后再次打开 | 保持隐藏前的当前模式 |
| 第二实例请求激活首个实例 | 保持首个实例的当前模式 |
| 同一进程内来回切换 | 保持用户最后一次选择 |
| 应用完全退出后重新启动 | Code |

本期不把最后选择的模式持久化到 `settings.json`。默认 Code 是确定的启动行为，而不是“首次安装默认、以后记忆”。若未来要持久化，必须另行明确产品语义并补充配置迁移测试。

### 4.2 Code 模式

- 继续复用现有 Harness 生命周期协调器、状态机、健康检查和已验证的 loopback Service URI。
- 应用当前自动启动策略保持不变；进入或离开 Code 模式本身不得触发启动、停止或重启。
- DSH 未运行时显示现有停止、启动、失败或安装引导状态；运行后显示官方 Harness Web UI。
- `RunningExternal` 与 `RunningOwned` 的权限边界保持不变。
- Code 页面的允许导航继续限制为已验证的 DSH origin，不因 Chat 模式而放宽到任意远程地址。

### 4.3 Chat 模式

- 用户首次主动切换到 Chat 后，才初始化或导航 Chat 页面，避免应用启动时无条件访问远程服务。
- 初始地址固定为 `https://chat.deepseek.com/`，不得从配置、命令行、网页消息或用户输入接收任意远程入口。
- Chat 是否可用与 DSH 状态相互独立。DSH 停止、启动失败或正在重启时，用户仍可进入 Chat。
- Chat 页面网络失败、登录失败或渲染失败不得把 `HarnessStateMachine` 迁移为 `Failed`，也不得覆盖 DSH 的错误信息。
- 登录、会话、模型、聊天记录和免费服务规则全部由 DeepSeek 官方页面负责；宿主不得模拟登录、抓取凭据或调用未公开接口。
- Chat 使用稳定、专用的 WebView2 profile 保存官方登录会话。应用完全退出后不得主动登出或清除 Cookie，重新启动后首次进入 Chat 应恢复已有登录状态。
- 密码和表单自动填充只允许使用 WebView2 原生能力，并遵循用户在浏览器原生提示中的明确选择；宿主不得自行保存、填充或解密账号密码。

### 4.4 切换与页面状态

- 切换模式不得重新创建、停止或重启 DSH 进程。
- 切换回 Code 后应恢复原 Harness 页面状态，包括当前会话、滚动位置和未提交输入；切换回 Chat 后也应恢复原页面状态。
- 不得以每次切换都重新导航首页的方式实现。优先使用两个独立 WebView2 控件或等效的可保持页面实例方案，并共享受控的 WebView2 environment/profile 基础设施。
- 两个页面实例的可见性切换必须发生在所属 Dispatcher 上。隐藏页面不得抢夺键盘焦点、覆盖可见页面或接收宿主快捷键的重复处理。
- Chat WebView2 可以懒加载，但首次加载完成后应在当前应用进程内复用。

## 5. UI 与交互要求

### 5.1 模式切换控件

- 在主窗口顶部命令栏中增加稳定尺寸的双选项分段控件，选项为 `Code` 和 `Chat`。
- Code 使用代码语义图标，Chat 使用对话语义图标；沿用当前资源和 Windows/WPF 视觉风格，不引入营销式大标题或装饰卡片。
- 当前模式必须有清晰的选中、悬停、键盘焦点和禁用状态，不能只依靠颜色区分。
- 两个选项均提供 Tooltip 和 `AutomationProperties.Name`。
- 控件支持键盘 Tab 导航，以及方向键或等效标准选择行为。
- 在 820x600 最小窗口和 100%、125%、150%、200% DPI 下，模式控件、工作目录和命令按钮不得重叠或被裁切。

### 5.2 模式相关命令

| 控件或命令 | Code 模式 | Chat 模式 |
| --- | --- | --- |
| 工作目录 | 显示并按现有规则使用 | 隐藏或明确禁用，不影响已保存目录 |
| 启动/停止/重启 DSH | 按现有状态决定可用性 | 隐藏或禁用，避免误解为控制 Chat |
| 刷新 | 刷新当前 Code 页面 | 刷新当前 Chat 页面 |
| 日志、关于 | 可用 | 可用 |
| DeepSeek 账户入口 | 保持当前已定义行为 | 保持当前已定义行为，不读取 Chat 登录态 |
| 清除 Chat 登录信息 | 不影响 Code 页面和 DSH | 二次确认后只清除 Chat profile 的登录数据 |

- 模式切换命令在连续点击时必须幂等，不能并发初始化同一个 WebView2。
- F5 刷新当前可见模式，不得刷新隐藏页面。
- F6 在当前可见 WebView2 与宿主命令栏之间切换焦点。
- 现有重启和日志快捷键保持语义；在 Chat 模式下，DSH 重启快捷键应不执行或先切回 Code 后由用户明确操作，不能在后台静默重启。
- 状态栏应区分当前显示模式。Chat 模式可以显示简明的在线页面状态，但不得把它伪装成 `RunningOwned` 或 `RunningExternal`。

### 5.3 加载与错误状态

- Chat 首次加载提供轻量的加载状态，不覆盖或修改 Harness 本地状态视图。
- Chat 无网络、DNS/TLS 失败、HTTP 导航失败或 WebView2 渲染失败时，显示独立的简明中文错误和“重试”操作。
- 为 Chat 导航失败增加新的稳定错误码或独立页面状态，不复用并改变已有 `DSH-E*`、`WEB-E*` 语义。
- 重试只重新加载 Chat，不启动 DSH、不清除浏览数据、不重建整个主窗口。
- Code 页面错误恢复继续遵循现有健康检查和 WebView2 恢复策略。

## 6. WebView2 安全边界

### 6.1 分离导航策略

Code 和 Chat 必须使用明确分离的导航策略或策略对象，不得把现有 `IsAllowedServiceUri` 改成“loopback 或任意 HTTPS”。

| 模式 | 初始信任 | 主导航规则 |
| --- | --- | --- |
| Code | 健康检查确认的 loopback DSH URI | 仅已确认的 DSH origin；新 loopback origin 仍需重新做身份检查 |
| Chat | `https://chat.deepseek.com/` | 仅已审计的 DeepSeek Chat 官方 HTTPS origin |

Chat 策略必须满足：

1. 精确解析 URI 的 Scheme、Host 和 Port，使用结构化 `Uri` 比较 origin，禁止 `StartsWith`、`Contains` 或字符串后缀判断。
2. 初始 origin 只允许 `https://chat.deepseek.com` 的默认 HTTPS 端口，拒绝 HTTP 降级、用户名/密码、非默认端口以及形似 DeepSeek 的恶意域名。
3. 实现前实际验证登录、验证码和正常会话流程使用的重定向 origin。若确有额外官方 origin 必不可少，必须逐项写入常量白名单、说明用途并覆盖测试；禁止使用 `*.deepseek.com` 或任意 HTTPS 通配。
4. 未列入 Chat 白名单的 `http/https` 链接取消内嵌导航，并交给系统默认浏览器。
5. `file:`、`data:`、`javascript:`、`ms-appx:`、自定义协议以及带用户信息的 URL 默认拒绝，不自动交给系统执行。
6. 重定向后的每一次主导航都执行同一策略，不能只校验首次 URL。
7. `NewWindowRequested` 默认 `Handled=true`。只有经过同一 Chat 白名单判断且产品流程确实需要的窗口，才允许在受控 Chat WebView2 内导航；其他 HTTPS 链接交给系统浏览器。

### 6.2 宿主能力与权限

- 不调用 `AddHostObjectToScript`，不注入任意 JavaScript，不向 Chat 页面暴露进程、文件系统、工作目录、日志、剪贴板或 DSH 生命周期能力。
- 不监听、读取或记录 Chat 页面的消息正文、输入内容、DOM、网络请求、Authorization Header、Cookie、LocalStorage、IndexedDB 或 Token。
- 不把 Chat 登录态转换为 API Key，也不与现有账户余额查询凭据混用。
- Release 构建继续关闭 DevTools。不得为了调试 Chat 永久放宽 Release 权限。
- 对 `PermissionRequested` 采用默认拒绝策略。若某项官方功能确需剪贴板、摄像头、麦克风、位置或通知权限，必须按 origin 和权限类型逐项设计用户确认与测试，不能一揽子允许。
- 文件选择只能由页面中的明确用户操作触发；宿主不得自动上传工作目录或本机文件。
- 下载行为必须有明确策略：只允许用户主动触发的 HTTPS 下载，使用 WebView2 安全文件名与系统下载位置，不静默执行下载内容。可执行文件或危险协议不得自动打开。

### 6.3 登录态与浏览数据

- Chat 必须使用名称和存储位置稳定的专用 WebView2 profile。可使用 `%LOCALAPPDATA%\DeepSeekHarnessDesktop\WebView2\Chat` 或等效的固定 profile 名称，不得使用每次启动变化的临时目录。
- 默认保留 DeepSeek 官方登录流程写入的 Cookie、LocalStorage、IndexedDB 及其他必要站点数据，使登录会话在窗口隐藏、应用退出、系统重启和桌面应用升级后继续可用，直到官方会话失效、用户在网页中退出登录或主动清除登录信息。
- Code 与 Chat 可以共享同一个受控 `CoreWebView2Environment`，但必须使用相互隔离的 profile/数据范围。远程 Chat 不得读取 Code 的 loopback Cookie 或获得 DSH 的非公开宿主能力。
- 可以启用 WebView2 原生密码保存和常规表单自动填充能力，但保存密码必须由 WebView2 的原生交互征得用户同意。宿主不得制作自有密码框、凭据模型、自动登录脚本或账号密码数据库。
- 密码、Cookie 和会话 Token 只能由 WebView2/Windows 当前用户配置提供的存储与保护机制管理；不得以明文或可逆自定义格式写入 `settings.json`、日志、异常、遥测、测试数据或导出文件。
- 宿主不得通过 DevTools、脚本注入、网络拦截、DOM 读取或 WebView2 Cookie API 获取、导出或记录登录信息，也不得将 Chat 登录态转换为 DeepSeek API Key。
- 不读取 Edge、Chrome 或其他系统浏览器的登录数据；Chat 只复用本应用专用 profile 中由用户实际完成的官方登录。
- 用户在 DeepSeek 官方页面执行退出登录后，退出结果应由同一 profile 正常持久化，下次进入 Chat 不得被宿主强制恢复为已登录。
- 必须提供“清除 Chat 登录信息”命令。执行前显示明确的二次确认；确认后关闭或重建 Chat WebView2，并只清除 Chat profile 中的 Cookie、站点存储、缓存、密码保存和自动填充数据，然后回到官方登录页。
- 清除 Chat 登录信息不得清除 Code profile、工作目录、应用设置、日志或 DSH 凭据，也不得停止或重启 DSH。清除过程中禁止重复提交，失败时显示独立错误且不得谎报已清除。
- Chat profile 损坏或无法打开时，不得静默删除登录数据。应显示可恢复错误，允许重试，并仅在用户明确确认后执行清除与重建。

## 7. 生命周期与并发要求

- Harness 生命周期仍只由 `HarnessLifecycleCoordinator` 和 `HarnessStateMachine` 管理。
- 模式切换不产生新的 Harness generation，不取消正在进行的启动/停止操作，也不伪造运行状态。
- 用户在 DSH 启动过程中切到 Chat，启动可以按现有策略继续；完成后只更新 Code 的状态和页面，不强制抢回当前模式或焦点。
- 用户在 Chat 模式退出应用时，Owned DSH 仍按现有退出路径清理整个进程树；Chat WebView2、事件订阅和控制器也必须成对释放。
- 两个 WebView2 的初始化、导航、刷新、失败恢复和销毁各自串行，并支持 `CancellationToken`。过期结果不能覆盖新模式的可见状态。
- WebView2 `ProcessFailed` 的恢复次数应按页面实例管理，不能因一个页面失败无限重载另一个页面。
- 普通窗口关闭到托盘时保持当前模式和两个页面实例；真正退出时再释放资源。

## 8. 建议的实现边界

具体类型名以实际代码评审为准，但职责至少应清晰覆盖：

- 模式模型：表示 `Code`/`Chat`，并提供单一当前模式状态。
- MainWindow ViewModel：暴露当前模式、切换命令、模式相关可见性和当前刷新命令。
- Code 导航策略：保留 loopback、DSH 身份确认和同源限制。
- Chat 导航策略：固定入口、精确官方 origin 白名单、远程重定向和外链处理。
- WebView2 environment/profile 管理：避免两个控件重复创建不一致的用户数据环境。
- Chat 登录信息管理：提供稳定专用 profile、原生密码保存/自动填充配置和范围受控的清除命令。
- Chat 页面状态：加载中、可用、失败和重试，不污染 Harness 快照。
- MainWindow View：只负责控件附着、焦点和必要的 WPF/WebView2 生命周期桥接。

若扩展 `IWebViewNavigationService`，应避免让一个“当前允许 URI”字段同时代表 Code 与 Chat。两个页面实例的允许 origin、恢复计数、加载状态和事件订阅必须彼此隔离。

## 9. 验收标准

### 9.1 模式行为

- `AC-MODE-001`：每次应用进程启动后默认选中 Code，并按现有规则进入或启动 DeepSeek Harness。
- `AC-MODE-002`：用户可从 Code 切换到 Chat，首次切换才加载 `https://chat.deepseek.com/`。
- `AC-MODE-003`：同一进程内切回 Code、再切回 Chat 时保留两个页面的会话、滚动位置和未提交输入，不重复导航首页。
- `AC-MODE-004`：隐藏到托盘再恢复时保持当前模式；完全退出再启动时恢复默认 Code。
- `AC-MODE-005`：连续快速切换不会创建重复 WebView2、抛出跨线程异常或显示两个重叠页面。

### 9.2 生命周期隔离

- `AC-LIFE-001`：Code/Chat 切换前后 DSH PID、所有权、generation 和工作目录不变。
- `AC-LIFE-002`：DSH 启动或重启过程中切到 Chat，不中断操作、不抢回焦点；完成状态可在切回 Code 后正确显示。
- `AC-LIFE-003`：DSH 失败或停止时 Chat 仍可独立使用，Chat 失败也不改变 Harness 状态。
- `AC-LIFE-004`：应用真正退出时仍清理 Owned DSH 进程树，并释放两个 WebView2 的事件和资源。

### 9.3 导航安全

- `AC-NAV-001`：Code 只内嵌已确认的 DSH loopback origin，Chat 功能不会扩大 Code 的允许范围。
- `AC-NAV-002`：Chat 初始页只接受精确的 `https://chat.deepseek.com/`；HTTP、非默认端口、用户信息和相似恶意域名均被拒绝。
- `AC-NAV-003`：Chat 登录所需的每个额外 origin 都有明确用途、精确白名单和自动化测试，不存在 `*.deepseek.com` 或任意 HTTPS 放行。
- `AC-NAV-004`：非白名单 HTTPS 外链在系统浏览器打开；危险协议不会被内嵌或交给系统执行。
- `AC-NAV-005`：Chat 页面无法读取宿主对象、DSH 控制能力、工作目录、日志或现有 API Key。
- `AC-NAV-006`：宿主日志和错误信息不包含 Chat 正文、Cookie、Token、完整敏感 URL 查询或存储内容。

### 9.4 UI 与恢复

- `AC-UI-001`：模式切换控件在最小窗口和 100%-200% DPI 下不与现有命令重叠，键盘和屏幕阅读器可识别当前选项。
- `AC-UI-002`：F5 只刷新当前可见页面；F6 只把焦点切到当前可见 WebView2。
- `AC-UI-003`：Chat 加载失败显示独立错误和重试，重试不影响 DSH。
- `AC-UI-004`：一个 WebView2 渲染进程失败时，不会导致另一个模式无限重载或丢失 Harness 状态。

### 9.5 登录信息记忆

- `AC-AUTH-001`：用户在官方 Chat 页面登录后，完全退出并重新启动桌面应用，首次切换到 Chat 时仍保持有效登录状态；应用启动模式仍为 Code。
- `AC-AUTH-002`：应用升级、窗口隐藏到托盘和 Windows 重启不会主动删除仍有效的 Chat 登录会话。
- `AC-AUTH-003`：密码保存和表单自动填充仅由 WebView2 原生功能在用户明确同意后完成，宿主代码、配置和日志均无法获得明文密码。
- `AC-AUTH-004`：用户在官方页面退出登录后，重新进入或重启应用不会被宿主自动恢复为已登录。
- `AC-AUTH-005`：“清除 Chat 登录信息”经过二次确认后使 Chat 回到未登录状态，并清除该 profile 的密码、Cookie 和站点数据；Code 页面、DSH 状态、工作目录和应用设置保持不变。
- `AC-AUTH-006`：清除失败或 profile 损坏时显示独立错误，不谎报成功、不静默删除数据，也不影响 Harness 状态。
- `AC-AUTH-007`：`settings.json`、UI/文件日志、异常和测试产物中不包含 Chat 账号、密码、Cookie、Token 或可恢复登录会话的内容。

## 10. 测试要求

### 10.1 单元测试

- 模式默认值、切换命令、幂等切换和模式相关命令可用性。
- 隐藏/恢复保持当前模式，重新创建 ViewModel 或应用会话默认 Code。
- Code 与 Chat 的 URI/origin 策略分别测试 Scheme、Host、Port、UserInfo、IDN/大小写、尾点、相似域名和重定向。
- Chat 白名单中的每个额外 origin 都有接受用例，且有相邻恶意域名拒绝用例。
- F5、F6 和刷新路由只作用于当前模式。
- Chat 失败、取消和过期初始化结果不改变 Harness 快照。
- Chat profile 名称/路径稳定性、Code/Chat profile 隔离、清除命令确认、清除范围和失败恢复。
- 原生密码保存与自动填充开关只能作用于 Chat profile，且不会产生宿主可读取的凭据模型。

建议至少覆盖以下恶意或边界地址：

| 地址 | 预期 |
| --- | --- |
| `https://chat.deepseek.com/` | Chat 入口允许 |
| `http://chat.deepseek.com/` | 拒绝降级 |
| `https://chat.deepseek.com:444/` | 拒绝非默认端口 |
| `https://user@chat.deepseek.com/` | 拒绝用户信息 |
| `https://chat.deepseek.com.evil.example/` | 拒绝相似域名 |
| `https://deepseek.com.evil.example/` | 拒绝相似域名 |
| `javascript:alert(1)` | 拒绝且不交给系统 |
| 已确认的 loopback DSH origin | 仅 Code 允许 |

### 10.2 集成与 UI 验证

- 使用可控本地页面或 WebView2 测试替身验证双页面初始化、显示切换、刷新路由、事件释放和渲染失败恢复。
- 使用真实 DSH 验证在包含未提交输入的 Harness 页面切到 Chat 再返回后页面状态不丢失，DSH PID 不变化。
- 在允许联网的人工验证环境中使用专用测试账号检查 DeepSeek Chat 首页、登录、退出、创建会话、切换会话，以及应用完全退出、Windows 重启和应用升级后的登录态恢复。不得在自动化日志或截图中暴露真实账号、密码、聊天正文或 Token。
- 验证用户拒绝和同意 WebView2 原生密码保存的两条路径；拒绝后不得由宿主记住密码，同意后自动填充仍只由 WebView2 管理。
- 验证“清除 Chat 登录信息”只清除 Chat profile，清除后重新启动应用仍为未登录，Code 页面数据和 DSH 进程不受影响。
- 验证 Chat 页面中的外部链接、新窗口、文件选择和下载均遵循安全策略。
- 手工检查 820x600、1280x820 以及 100%、125%、150%、200% DPI 下的布局、焦点、Tooltip 和模式选中状态。
- 自动化测试不得依赖真实 DeepSeek 账号或验证码，也不得绕过官方登录机制。

## 11. 文档与版本要求

实现本功能时必须同步：

1. 更新 `docs/deepseek-harness-desktop-development.md` 的产品范围，说明桌面宿主新增官方 Chat 页面承载能力，但仍不复制聊天业务 UI。
2. 更新 `docs/deepseek-harness-desktop-detailed-design.md` 的主窗口、模式状态、WebView2 双实例/等效方案、导航白名单、权限、错误和测试设计。
3. 更新 `docs/installation.md`，说明 Chat 模式需要互联网、官方站点可用性、登录信息的本机持久化范围和清除方式；Code 模式仍依赖本机 DSH。
4. 同步修改 `AGENTS.md` 与 `CLAUDE.md`，把“禁止远程内嵌”调整为“仅允许经明确评审的 DeepSeek Chat 官方 HTTPS origin 例外”，并在交付前逐字节比较两者。
5. 按兼容新功能递增 `Directory.Build.props` 的 minor 版本，更新 manifest 和 `VERSION_HISTORY.md`。若实施时同一批改动已按更高版本递增，不重复升版。
6. 新增带日期的验证记录，不回写历史阶段验证结论。

## 12. 非目标

- 不在 WPF 中实现自有聊天界面、会话列表、模型选择器、联网搜索、文件解析或消息存储。
- 不调用 DeepSeek Chat 的未公开 API，不抓取网页数据；除用户同意使用 WebView2 原生密码保存与自动填充外，宿主不保存或自动填写账号密码。
- 不把 Chat 登录态当作 DeepSeek API Key，不改变现有 API Key 来源优先级。
- 不支持用户配置任意聊天网址，不提供通用浏览器地址栏。
- 不支持任意远程 DSH，不因 Chat 模式放宽 Code 的 loopback 与身份验证边界。
- 不在模式切换时自动停止 DSH 以节省资源，也不在切回 Code 时无条件重启 DSH。
- 不为 Chat 页面增加宿主文件系统、进程、Shell、剪贴板或工作区桥接能力。
- 不承诺绕过地区、网络、验证码、登录或官方服务限制。

## 13. 执行与交付要求

执行者应先提交安全与设计影响分析，再完成实现、测试和文档同步。交付至少包括：

1. Code/Chat 模式模型、ViewModel 状态与切换命令。
2. 主窗口分段切换控件和两个可保持页面状态的 WebView2 页面实例或等效方案。
3. 相互隔离的 Code/Chat 导航策略、Chat 精确 origin 白名单和权限策略。
4. Chat 稳定专用 profile、跨重启登录记忆、原生密码保存/自动填充和“清除 Chat 登录信息”闭环。
5. Chat 独立加载、失败、重试与渲染恢复状态。
6. 模式、生命周期隔离、登录数据隔离、URI 安全、快捷键和资源释放测试。
7. 与实际行为一致的开发文档、详细设计、安装说明、安全规则与验证记录。
8. 版本号、manifest、版本历史同步，以及 `AGENTS.md`/`CLAUDE.md` 一致性校验。

实现完成后运行与风险匹配的验证，至少包括 restore、Debug build、Release tests 和 Windows IntegrationTests；可见 UI 变化必须完成真实 WPF/WebView2 手工检查，发布前运行 `eng/Verify-Release.ps1`。不得以仅编译成功作为验收结论。
