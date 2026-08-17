# Code 与 Chat 模式切换功能开发计划

## 1. 计划信息

- 来源需求：[`prompt-20260816-02-Code与Chat模式切换需求.md`](../prompt/prompt-20260816-02-Code与Chat模式切换需求.md)
- 计划日期：2026-08-17
- 计划状态：待执行（依赖计划 01 完成并冻结）
- 初稿 Desktop 基线：`0.1.8`
- 本次修订基线：`0.1.9` 工作区；计划 01 组件已部分落地但尚未完成发布验收
- 本计划文档版本：`0.1.10`
- 目标功能版本：`0.2.0`（兼容新增功能；实际实施时从届时版本统一递增 minor）
- 预计工期：14 至 17 个工作日，不包含官方登录流程、验证码或受限网络环境造成的等待时间

本计划只制定设计、实施和验证步骤，不在本次改动中实现 Code/Chat 功能。后续执行必须以届时的代码事实为准，并同时遵守 `AGENTS.md`、`CLAUDE.md`、`code_rule.md`。

仓库另有 [`plan-20260816-01-DeepSeekHarness安装服务地址与更新需求.md`](plan-20260816-01-DeepSeekHarness安装服务地址与更新需求.md)。执行顺序冻结为：先完成计划 01 的实现、测试、文档和发布门禁，再以其最终代码作为计划 02 的阶段 0 基线。当前工作区已经部分落地 `ServiceUriValidator`、`IExternalLinkLauncher`、`IUserConfirmationService`、`ApplyServiceUriAsync` 和相关 ViewModel，但这些组件的现有契约不能直接满足 Chat；计划 02 必须扩展并复用它们，不重复创建同义抽象。除非另有经过评审的合并方案，不并行实施两份计划中对 `App.xaml.cs`、`MainWindowViewModel`、`MainWindow` 和导航服务的修改。

## 2. 目标、范围与非目标

本轮实现五个闭环：

1. 应用每次进程启动默认进入 Code；同一进程内可切换 Code/Chat，隐藏到托盘和第二实例激活不重置当前模式。
2. Code 继续显示本机已确认的 Harness Web UI；Chat 首次被用户选择时才加载 `https://chat.deepseek.com/`。
3. 两个页面实例在进程内持续存在，切换不重新导航，保留会话、滚动位置和未提交输入。
4. Code 与 Chat 使用隔离的导航策略和 WebView2 profile；Chat 登录态由稳定专用 profile 跨应用重启保留。
5. Chat 具有独立的加载、错误、重试、权限、下载和清除登录信息流程，不改变 DSH 生命周期状态。

明确不包含：

- 在 WPF 中实现聊天、会话、模型、登录、验证码、文件解析或消息存储。
- 调用 DeepSeek Chat 未公开 API，读取 DOM、消息、Cookie、Token、LocalStorage、IndexedDB 或网络正文。
- 将 Chat 登录态转换为 API Key，或改变现有 DeepSeek API Key 来源优先级。
- 用户配置任意 Chat 地址、通用地址栏、任意 HTTPS 白名单或远程 DSH。
- 切换模式时停止、启动或重启 DSH，或持久化最后选择的模式。
- 向网页注入宿主对象、任意 JavaScript，或暴露工作目录、进程、日志、Shell、剪贴板等宿主能力。
- 绕过地区、网络、登录、验证码和官方服务限制。

## 3. 当前实现基线与差距

| 范围 | 当前事实 | 主要差距 |
| --- | --- | --- |
| 主窗口 | `MainWindow.xaml` 只有一个 `Browser`，由 `IsRunning` 控制显示 | 没有模式模型、分段控件、第二页面、Chat 状态或模式化工具栏 |
| ViewModel | `MainWindowViewModel` 直接依赖 `IWebViewNavigationService`，刷新只针对 Code | 没有当前模式、懒加载、当前页面刷新、Chat 重试/清除和模式相关命令状态 |
| Code 导航 | `WebViewNavigationService` 只接受 loopback HTTP(S)，主导航只允许已确认 DSH 同源 | 单例字段只支持一个控件/一个 origin，初始化、恢复计数和订阅不能承载两个页面 |
| WebView2 数据 | 使用固定 `%LOCALAPPDATA%\DeepSeekHarnessDesktop\WebView2` 用户数据目录，未显式命名 profile | 需要保留现有 Code 数据，同时为 Chat 建立稳定、隔离、非 InPrivate 的命名 profile |
| Chat 安全 | 当前所有远程主导航都会取消并尝试交给系统浏览器 | 需要仅对经过审计的官方 Chat origin 建立有限例外，且不放宽 Code 策略 |
| 错误状态 | WebView2 初始化失败使用 `WEB-E301`，单次 `ProcessFailed` 后直接 Reload | 没有 Chat 专用错误、导航完成映射、重试状态或按页面隔离的恢复预算 |
| 权限与下载 | 未处理 `PermissionRequested`、`DownloadStarting` 等 Chat 风险事件 | 需要默认拒绝权限、明确下载策略并覆盖外链/危险协议 |
| 登录信息 | 宿主不管理 WebView2 profile，也没有清除命令 | 需要原生密码保存提示、跨重启登录态和只清除 Chat profile 的闭环 |
| 生命周期 | App 启动时先初始化 Code WebView2，再初始化 Harness；退出时统一释放 DI | Chat 必须懒加载；隐藏到托盘不能释放页面；真正退出时两个页面都要解除订阅并释放 |
| 快捷键 | F5 刷新 Code；F6 在 `Browser` 和工作目录间切换；重启快捷键不识别模式 | F5/F6 必须路由到可见页面，Chat 下不得后台重启 DSH |
| 计划 01 共享能力 | 当前工作区已部分实现 `ServiceUriValidator`、固定枚举驱动的 `IExternalLinkLauncher.Open(OfficialResource)`、两个确认方法和 `ApplyServiceUriAsync` | 计划 01 尚未完成发布验收；外链服务不能打开 Chat 策略批准的动态 URI，确认服务也缺少清除 Chat 数据确认 |
| 测试 | `PresentationServiceTests` 只覆盖 Code 同源和刷新不影响 PID | 缺少模式、策略、profile、Chat 失败、清除、双页面竞态及真实 WebView2 验证 |

当前 NuGet 基线 `Microsoft.Web.WebView2 1.0.3537.50` 已提供命名 `ProfileName`、`CoreWebView2Profile.ClearBrowsingDataAsync`、`AllProfile`、`IsPasswordAutosaveEnabled`、`IsGeneralAutofillEnabled` 和带 controller options 的 WPF 初始化重载。实施前仍必须在目标 Windows 10/11 Runtime 上做真实验证，不能只以编译期 API 存在作为可用结论。

## 4. 总体设计决策

### 4.1 模式是进程内展示状态

新增单一枚举 `AppContentMode.Code` / `AppContentMode.Chat`，由 `MainWindowViewModel` 持有当前值。默认值始终为 Code，不写入 `AppSettings`，不提升 `SchemaVersion`。

模式切换只改变主窗口内容和命令路由：

```text
应用启动              -> Code
Code <-> Chat         -> 保持两个页面实例，不操作 DSH
隐藏到托盘/再次激活   -> 保持当前模式
第二实例激活首实例    -> 保持首实例当前模式
完全退出/重新启动     -> Code
```

切换到 Chat 时只发起一次幂等初始化。连续点击或快速往返使用 Chat 页面自己的操作门、取消令牌和 generation；过期完成结果只能更新该页面内部资源，不能覆盖当前可见模式或 Harness 快照。

### 4.2 两个 WebView2 控件保持页面实例

主窗口同时创建 Code 和 Chat 两个 WebView2 控件，通过 `Visibility=Collapsed/Visible` 切换，不在普通模式切换时 Dispose 或重新 Navigate。Chat 控件首次切换前不创建 CoreWebView2 controller、不访问网络；首次初始化后在该应用进程内复用。

WPF WebView2 存在 HWND/airspace 限制。加载或错误状态不覆盖在可见 WebView2 上方，而是先折叠对应 WebView2，再显示同一内容区域中的原生状态视图，避免网页覆盖宿主按钮或错误提示。

Code 的 Harness 状态视图继续由 `HarnessStateSnapshot` 决定。Chat 使用独立的 `ChatPageState`（建议至少包含 `NotInitialized`、`Initializing`、`Ready`、`Failed`、`ClearingData`），不得向 `HarnessRuntimeState` 增加 Chat 状态。

### 4.3 共享 Environment，隔离 Profile

引入单例 WebView2 environment provider，只在固定用户数据根目录创建一个 `CoreWebView2Environment`，两个页面通过不同 controller options 创建：

| 页面 | Profile 决策 | 原因 |
| --- | --- | --- |
| Code | 保留当前默认 profile | 避免升级后丢失现有 Harness Web UI Cookie、缓存和页面设置 |
| Chat | 固定命名 profile `Chat`，`IsInPrivateModeEnabled=false` | 与 Code 隔离，并跨进程、Windows 重启和应用升级保存官方登录态 |

Chat profile 名称必须是代码常量，不进入 `settings.json`。初始化后断言 `ProfileName`/`ProfilePath` 属于预期 environment；测试使用唯一临时用户数据根目录，不访问用户真实 `%LOCALAPPDATA%`。

只对 Chat profile 启用 `IsPasswordAutosaveEnabled` 和 `IsGeneralAutofillEnabled`。密码保存只能由 WebView2 原生 Save/Update Password 提示征得用户选择；宿主不提供密码设置项、不读取密码数据，也不记录 profile 真实路径中的用户信息。

原生密码提示是否出现受目标 WebView2 Runtime、Windows/Edge 企业策略和实际登录表单行为影响，宿主不能保证该能力始终可用，也不得绕过被禁用的策略。阶段 0 必须分别验证“提示出现并接受”“提示出现并拒绝”“环境或策略使提示不可用”三种结果，并在安装文档中说明环境依赖。密码自动保存不可用不应影响由 Chat profile Cookie/站点存储提供的登录会话持久化，两者必须分开验收。

### 4.4 Code 与 Chat 导航策略完全分离

命名决策冻结为：将现有 `IWebViewNavigationService` / `WebViewNavigationService` 重命名为 `ICodeWebViewService` / `CodeWebViewService`，与新增的 `IChatWebViewService` / `ChatWebViewService` 对齐。Code 服务不得把现有 `IsAllowedServiceUri` 扩成“loopback 或 DeepSeek”。新增纯策略 `ChatNavigationPolicy`，使用结构化 `Uri` 比较 scheme、ASCII host、有效端口和 user info。

Chat 初始规则：

- 只允许 `https://chat.deepseek.com/` 默认 443 端口作为固定入口。
- 拒绝 HTTP、非默认端口、UserInfo、尾点主机、IDN/Unicode 混淆、相似后缀域名和所有非 HTTP(S) 协议。
- 每次顶层导航和重定向均重新判定，不以首次入口校验代替后续校验。
- 额外官方登录/验证码 origin 必须在阶段 0 通过真实流程确认，按“精确 origin + 用途”逐项列入常量和测试；禁止 `*.deepseek.com`。
- 非白名单 HTTP(S) 外链取消内嵌导航，由受控外部链接服务打开；危险协议只拒绝，不交给系统执行。
- `NewWindowRequested` 默认 `Handled=true`；只有经同一策略确认且官方流程确需的目标才在 Chat 控件内导航。

Code 仍只允许健康检查确认后的 loopback DSH origin，新 loopback origin 仍需先做 DSH 身份检查。两种策略的单元测试独立维护，防止 Chat 例外污染 Code。

现有 `IExternalLinkLauncher` 只支持固定 `OfficialResource` 枚举，不能直接承载 Chat 外链。计划 02 扩展该接口，保留 `Open(OfficialResource)`，新增 `Open(Uri)`（或语义等价的明确命名）供导航策略判定为“外部打开”的绝对 HTTP(S) URI 使用。实现继续做 scheme、UserInfo 和绝对 URI 的防御性校验，但不再维护第二份 Chat origin allowlist；是否内嵌、外开或拒绝的唯一业务决策仍由 Code/Chat 导航策略给出。现有 Code 服务私有的 `Process.Start` 外链逻辑同步删除并改走该服务。

#### 4.4.1 登录跨 origin 决策门禁

阶段 0 必须从以下三种结果中冻结一种，不得把普通系统浏览器登录描述成应用内登录的可靠降级：

1. 登录、验证码和会话流程的所有顶层 origin 均可被有限枚举：逐项加入精确 allowlist 后继续内嵌流程。
2. 官方提供受支持的外部浏览器授权/回调，且验证能把授权结果安全返回当前应用的 Chat profile：为该固定回调单独设计状态校验、取消和测试后实施。
3. origin 无法安全闭合，或外部浏览器登录不能把会话带回应用专用 profile：`AC-AUTH-*` 视为阻塞，停止功能发布并请求产品决策；可以提供“在系统浏览器打开 Chat”作为独立外部使用入口，但不得声称刷新内嵌 Chat 后会登录，也不得用 wildcard、Cookie 复制或脚本绕过。

系统默认浏览器与应用命名 Chat profile 默认不共享 Cookie 和站点存储，因此“在系统浏览器登录后返回并刷新 Chat”本身不满足登录记忆验收。

### 4.5 页面服务各自持有状态和资源

拆分为 Code 页面服务和 Chat 页面服务，共享 environment provider，但分别持有：

- WebView2 控件引用和 Dispatcher 访问边界。
- 初始化任务、操作门、CTS、generation 和已初始化标记。
- 当前允许 origin、导航状态和页面错误。
- `NavigationStarting`、`NavigationCompleted`、`NewWindowRequested`、`ProcessFailed` 等事件订阅。
- 独立的渲染进程恢复次数和重试操作。

`MainWindow` code-behind 只负责把两个 WPF 控件附着到对应服务、处理 F6 焦点和窗口 UI 桥接。URL 判定、profile 选择、错误映射和清除数据不得放入 code-behind。

### 4.6 Chat 失败不污染 Harness

Chat 页面通过 `NavigationCompleted.IsSuccess`、`WebErrorStatus` 和 `HttpStatusCode` 映射独立错误，至少区分初始化/profile、网络/DNS/TLS/HTTP 导航和清除数据失败。计划采用新的 `WEB-E31x` 号段，阶段 0 在详细设计中冻结具体编号与语义；不得复用或改变 `WEB-E301`、`DSH-E*`。

Chat 错误只更新 `ChatPageState` 和 Chat 状态栏。重试只重新初始化或导航 Chat 固定入口，不启动 DSH、不清除 profile、不重建主窗口。因宿主取消、快速重定向或切换 generation 产生的预期取消不能误报为网络失败。

### 4.7 权限、文件与下载采取最小能力

- `PermissionRequested` 默认设置为拒绝；若真实官方流程需要某项权限，必须新增 origin + 权限类型 + 用户确认的窄化设计后再放行。
- 不注册宿主对象、WebMessage、脚本注入、网络拦截、Cookie API 或 DOM 读取。
- 文件选择保留 WebView2 原生、由网页明确用户操作触发的流程；宿主不预选工作目录、不自动上传。
- `DownloadStarting` 必须有显式处理。默认取消无法确认安全上下文的下载；若验证官方 Chat 的用户下载功能为必要能力，只允许白名单 HTTPS 页面触发、展示系统保存确认、采用 WebView2 安全文件名，完成后绝不自动打开。
- Release 继续关闭 DevTools 和浏览器加速键；不得为 Chat 放宽全局设置。

### 4.8 清除 Chat 登录信息使用 Profile API

“清除 Chat 登录信息”只在 Chat profile 初始化后可执行，流程为：

```text
用户点击 -> 宿主二次确认 -> 禁止重复提交 -> 停止 Chat 导航
         -> ClearBrowsingDataAsync(AllProfile)
         -> 成功后重新导航固定入口 -> 显示未登录页
```

使用 `AllProfile` 覆盖 Cookie、DOM 存储、缓存、密码保存和自动填充。不得删除整个 WebView2 用户数据根目录，也不得触碰 Code 默认 profile。失败时保留失败状态并允许重试，不显示成功提示。

`CoreWebView2Profile.Delete` 会产生 deletion-pending，可能无法在同一浏览器进程中立刻用同名 profile 重建，因此不作为普通清除路径。profile 损坏恢复必须先在阶段 0 验证受支持行为；若只能在下次进程启动完成重建，UI 必须明确提示并在用户确认后安排，不能静默删除目录。

### 4.9 工具栏、状态栏与快捷键按当前模式路由

- 顶部使用固定尺寸双选项分段控件，选中状态同时通过形状/边框/标记表达，不只依赖颜色。
- 820x600 下采用稳定的两行命令栏：第一行放模式和公共命令，第二行放当前模式上下文命令。Code 行显示工作目录和 DSH 生命周期；Chat 行显示 Chat 重试/清除等操作，避免横向挤压。
- F5 只刷新当前可见页面；Code 未运行时不可用，Chat 未初始化/清除中不可用。
- F6 在命令栏与当前可见 WebView2 间切换；当前页面处于原生错误状态时聚焦其主要操作。
- `Ctrl+Alt+R` 在 Chat 模式不执行；不自动切回 Code，也不在后台重启。
- 日志、关于和账户入口两种模式都保留；账户入口不读取 Chat profile。
- Chat 状态栏使用“Chat · 加载中/已就绪/加载失败”等页面语义，不伪装成 `RunningOwned`/`RunningExternal`。

## 5. 目标组件与文件影响

具体名称以实施时仓库事实为准；表中名称表示职责边界，不要求机械照搬。

### 5.1 计划新增与调整组件

| 层 | 计划组件 | 职责 |
| --- | --- | --- |
| Models | `AppContentMode` | Code/Chat 单一模式值，默认 Code，不持久化 |
| Models | `ChatPageState`、`ChatPageSnapshot` | Chat 加载、可用、失败、清除状态和独立错误 |
| Services/Abstractions | `IWebViewEnvironmentProvider` | 在固定根目录幂等创建共享 environment |
| Services/Abstractions | `IChatWebViewService` | Chat 懒初始化、导航、刷新、重试、清除和状态事件 |
| Services/Abstractions | `ICodeWebViewService` | 由现有接口重命名，专门管理 Code WebView2、已确认 DSH origin 和 Code 页面操作 |
| Services/Abstractions | `IExternalLinkLauncher` | 扩展现有接口：保留固定资源入口，新增经导航策略批准的结构化 HTTP(S) URI 外开能力 |
| Services/Abstractions | `IUserConfirmationService` | 扩展现有接口，新增 `ConfirmClearChatData()`，不让 ViewModel 直接调用静态 `MessageBox` |
| Services | `WebViewEnvironmentProvider` | 缓存 environment 初始化任务，管理取消、失败重试和释放 |
| Services | `ChatWebViewService` | 持有 Chat controller/profile、事件、操作门和恢复预算 |
| Utilities/Services | `CodeNavigationPolicy`、`ChatNavigationPolicy` | 可独立测试的结构化 origin 决策，不执行 UI 或网络 I/O |
| Views/States | Chat loading/error view | 中文错误、稳定错误码、重试操作，不覆盖 Code 状态页 |

### 5.2 重点修改区域

| 文件/区域 | 计划修改 |
| --- | --- |
| `App.xaml.cs` | 在计划 01 最终 DI 基线上注册 environment、Code/Chat 服务；只预初始化 Code，Chat 保持懒加载；退出时按序释放两个页面 |
| `WebViewNavigationService.cs`、接口及调用方 | 重命名为 `CodeWebViewService` / `ICodeWebViewService`，复用共享 environment，增加成对解绑/Dispose，保持 loopback/同源限制 |
| `ExternalLinkLauncher.cs`、接口 | 保留 `Open(OfficialResource)`，增加受结构校验的 `Open(Uri)`；Code 与 Chat 外链统一通过该服务 |
| `UserConfirmationService.cs`、接口 | 增加 `ConfirmClearChatData()` 及明确中文二次确认，原有计划 01 确认语义不变 |
| `MainWindowViewModel.cs` | 增加模式、Chat 快照、切换/重试/清除命令、当前刷新路由和模式相关 CanExecute |
| `MainWindow.xaml` | 增加分段控件、稳定两行命令栏、第二个 WebView2 和 Chat 原生状态视图 |
| `MainWindow.xaml.cs` | 附着两个控件，F6 聚焦可见页面，Chat 下屏蔽重启快捷键，关闭时解除窗口事件 |
| `App.xaml` | 增加复用现有色彩和尺寸的分段控件/状态样式，不引入孤立主题 |
| `PresentationServiceTests.cs` | 拆分/扩展 Code 导航、模式路由和 ViewModel 命令测试 |
| 新增 WebView2/Chat 测试文件 | profile、导航策略、状态、清除、失败恢复和资源释放测试 |
| `docs/` 与维护规则 | 同步产品边界、双页面设计、安装/隐私说明、远程 Chat 例外和验证记录 |

不计划向 `AppSettings` 增加模式、Chat URL、profile 路径、登录状态或密码字段，因此正常实施不应修改 `SchemaVersion`。若实现中发现必须新增持久配置，应先补充迁移设计和 v1 兼容测试，不得临时写入字段。

## 6. 分阶段实施计划

### 6.1 阶段 0：安全与可行性门禁

工期：2 至 2.5 个工作日。

| 编号 | 任务 | 输出 |
| --- | --- | --- |
| P02-001 | 在允许联网的隔离环境中验证 Chat 首页、登录、验证码、退出、会话和新窗口流程使用的顶层 origin，并冻结 4.4.1 的三分支决策 | 脱敏后的“精确 origin + 用途”清单和可发布/受支持回调/阻塞结论；不记录 URL query、账号、正文或 Token |
| P02-002 | 在目标 Windows 10/11 与当前 WebView2 SDK/Runtime 验证命名 profile、跨重启 Cookie、原生密码提示的接受/拒绝/不可用结果、`AllProfile` 清除和 profile 损坏行为 | API/Runtime/企业策略兼容结论、密码能力声明和恢复约束 |
| P02-003 | 用本地可控页面验证两个 WPF WebView2 在 `Collapsed/Visible` 切换后保持输入、滚动、焦点和页面实例 | 双控件方案可行性记录 |
| P02-004 | 验证 `PermissionRequested`、文件选择、下载、新窗口和 `ProcessFailed` 的事件顺序及可控范围 | 权限/下载/恢复策略定稿 |
| P02-005 | 冻结 `WEB-E31x` 错误语义、日志字段和不记录项，更新详细设计增量 | 可检索且不泄密的错误表 |
| P02-006 | 先完成计划 01 的实现、测试、文档和发布门禁，再记录计划 02 的 Unit/Integration 基线和最终共享契约 | 已冻结的执行基线与复用清单 |
| P02-007 | 将确定性测试与真实 WebView2 交互测试分层，验证发布环境具备 Runtime 和交互式桌面会话 | 专用 WebView2 测试通道、显式前置检查和不可静默跳过规则 |

完成条件：计划 01 已完成并通过门禁；4.4.1 已形成明确可执行结论；除 `https://chat.deepseek.com:443` 外，任何额外内嵌 origin 都有真实流程证据、明确用途和相邻恶意域名测试；密码提示不可用时的产品文案和测试预期已经冻结；真实 WebView2 测试环境可用且不会静默跳过。任一安全门禁未满足时不得进入阶段 1。

### 6.2 阶段 1：模式模型与纯导航策略

工期：1.5 至 2 个工作日。

| 编号 | 任务 | 输出 |
| --- | --- | --- |
| P02-101 | 新增 `AppContentMode`、Chat 状态/快照和默认值 | 无 I/O 的领域模型及测试 |
| P02-102 | 提取 Code origin 判定并保持现有 loopback、UserInfo 和同源规则 | Code 安全回归测试 |
| P02-103 | 实现 Chat 精确 origin allowlist 和导航决策（内嵌/外部/拒绝） | 参数化 URI 策略测试 |
| P02-104 | 覆盖大小写、默认/显式 443、UserInfo、尾点、IDN、相似域名、危险协议和重定向 | 恶意地址测试矩阵 |
| P02-105 | 定义 Chat 错误映射，区分预期取消与真实 DNS/TLS/HTTP 失败 | 错误映射测试 |

完成条件：Code 与 Chat 策略没有共享“任意允许 URI”可变字段；Chat 的加入不改变任何现有 Code 接受/拒绝结果。

### 6.3 阶段 2：共享 Environment 与双页面服务

工期：2.5 至 3 个工作日。

| 编号 | 任务 | 输出 |
| --- | --- | --- |
| P02-201 | 实现共享 environment provider，固定用户数据根目录并确保并发初始化幂等 | 可替换 provider 与并发测试 |
| P02-202 | 将现有导航服务及调用方重命名为 `ICodeWebViewService` / `CodeWebViewService`，复用 environment、保留默认 profile，并补齐事件解绑和异步释放 | 命名一致的 Code 服务及行为/数据兼容回归 |
| P02-203 | 实现 Chat 服务附着、固定 `Chat` profile、非 InPrivate 和首次切换懒初始化 | profile 稳定性与隔离测试 |
| P02-204 | 仅对 Chat profile 请求启用原生密码保存和常规自动填充，并正确处理 Runtime/策略不可用 | 设置范围与不可用环境测试，不产生宿主凭据模型 |
| P02-205 | 为两个服务分别实现操作门、CTS、generation 和单页恢复预算 | 快速重复初始化、取消和过期结果测试 |
| P02-206 | 在真正退出时按序取消任务、解除全部 CoreWebView2 事件并释放控制器；托盘隐藏不释放 | 生命周期/事件泄漏测试 |

完成条件：应用启动不访问 Chat；第一次切换只创建一个 Chat controller；两个页面使用不同 profile；一个页面失败或恢复不会 Reload 另一个页面。

### 6.4 阶段 3：ViewModel 编排与命令路由

工期：2 个工作日。

| 编号 | 任务 | 输出 |
| --- | --- | --- |
| P02-301 | 在 `MainWindowViewModel` 增加当前模式和幂等切换命令 | 默认 Code、同值无操作和快速切换测试 |
| P02-302 | 订阅 Chat 独立快照并在 UI Dispatcher 更新，防止覆盖 Harness 快照 | 双状态隔离测试 |
| P02-303 | 按模式计算工作目录、生命周期按钮、状态栏和 Chat 操作可见性/可用性 | 属性映射与 CanExecute 测试 |
| P02-304 | 将刷新命令路由到当前页面，Chat 下屏蔽 DSH 重启入口/快捷键 | F5 和重启无后台副作用测试 |
| P02-305 | 实现 Chat 重试和清除命令的重复提交、取消和错误呈现 | 命令并发测试 |
| P02-306 | 在 ViewModel Dispose 时解除 coordinator、日志和 Chat 状态订阅 | 关闭后无回调测试 |

完成条件：切换前后 DSH PID、ownership、generation、工作目录和操作 CTS 均不变化；DSH 启动完成只更新 Code 状态，不强制切回模式或抢焦点。

### 6.5 阶段 4：Chat 导航、权限、下载与失败恢复

工期：2 至 2.5 个工作日。

| 编号 | 任务 | 输出 |
| --- | --- | --- |
| P02-401 | 接入 `NavigationStarting/Completed`，对每次重定向应用 Chat 策略 | 顶层导航与失败状态测试 |
| P02-402 | 扩展 `IExternalLinkLauncher.Open(Uri)`，让 Code/Chat 的 `NewWindowRequested` 和外链统一走受控服务；未知 HTTP(S) 外开、危险协议拒绝 | 接口兼容、Code 收敛、外链和新窗口测试 |
| P02-403 | 对 `PermissionRequested` 默认拒绝，并验证官方基本聊天无需宿主权限 | 权限类型参数化测试 |
| P02-404 | 实现阶段 0 定稿的文件选择/下载策略，禁止静默保存和自动打开 | 下载 URI、文件名、取消和确认测试 |
| P02-405 | 映射网络、DNS、TLS、HTTP 和渲染进程失败，提供单页有限恢复与显式重试 | `WEB-E31x` 恢复测试 |
| P02-406 | 确认日志不包含 Chat URL query、正文、Cookie、Token、profile 数据或账号 | 脱敏/不记录测试 |

完成条件：Chat 的任何错误都不调用 lifecycle coordinator、不修改 Harness 状态、不重新创建主窗口；Code 导航仍只接受已确认 DSH origin。

### 6.6 阶段 5：登录态持久化与范围受控清除

工期：1.5 至 2 个工作日。

| 编号 | 任务 | 输出 |
| --- | --- | --- |
| P02-501 | 验证并固定 Chat profile 名称/路径规则，禁止进入 AppSettings 和日志 | 跨重启稳定性测试 |
| P02-502 | 为现有 `IUserConfirmationService` 增加 `ConfirmClearChatData()`，并实现“清除 Chat 登录信息”的二次确认、单操作门和取消边界 | 原有确认契约不变、确认前无副作用、重复提交测试 |
| P02-503 | 调用 Chat profile `ClearBrowsingDataAsync(AllProfile)`，成功后回固定入口 | Cookie/存储/缓存/密码/自动填充清除验证 |
| P02-504 | 验证清除前后 Code profile、工作目录、设置、日志和 DSH PID 均不变化 | profile 隔离集成测试 |
| P02-505 | 实现清除失败和 profile 损坏的可恢复提示，不静默删目录 | 失败不谎报与明确确认测试 |
| P02-506 | 使用专用测试账号手工验证登录、网页退出、应用退出/升级/Windows 重启和清除后的状态 | 不含凭据/正文截图的验证记录 |

完成条件：有效官方会话可跨应用重启恢复，但应用仍默认 Code；网页主动退出后宿主不会恢复登录；清除失败不会影响 Code 或 Harness。

### 6.7 阶段 6：WPF UI、焦点与托盘集成

工期：2 个工作日。

| 编号 | 任务 | 输出 |
| --- | --- | --- |
| P02-601 | 实现 Code/Chat 分段控件，包含图标、Tooltip、Automation Name、选中/悬停/焦点/禁用状态 | 可访问模式控件 |
| P02-602 | 把命令栏改为稳定两行响应式布局，按模式显示上下文命令 | 820x600 无重叠布局 |
| P02-603 | 集成两个 WebView2 与各自原生状态视图，确保隐藏页面不遮挡、不抢焦点 | 双页面可见性切换 |
| P02-604 | 完成 F5、F6、Tab/方向键、重启快捷键和错误页主要操作焦点 | 键盘交互验证 |
| P02-605 | 验证托盘隐藏/恢复、第二实例激活、最小化/最大化均保持当前模式 | 窗口生命周期测试 |
| P02-606 | 检查 100%、125%、150%、200% DPI 和 820x600、1280x820 | 带日期的 WPF 验证记录 |

完成条件：任意时刻只显示一个模式内容；WebView2 不覆盖命令栏/状态栏；长工作目录、错误文本和状态文字不裁切或挤压相邻操作。

### 6.8 阶段 7：文档、版本与发布门禁

工期：1 至 2 个工作日。

| 编号 | 任务 | 输出 |
| --- | --- | --- |
| P02-701 | 更新开发文档的产品范围、主窗口、浏览器数据和安全边界 | Code/Chat 实际行为说明 |
| P02-702 | 更新详细设计的模式、双 controller/profile、导航、权限、错误和序列 | 与实现一致的设计文档 |
| P02-703 | 更新 `docs/installation.md` 的联网、官方服务、登录保存、密码提示环境依赖和清除说明 | 发布包 README 同步 |
| P02-704 | 同步修改 `AGENTS.md`/`CLAUDE.md` 的远程 Chat 精确例外并逐字节比较 | 两份维护规则完全一致 |
| P02-705 | 在 `docs/validation/` 新增本功能验证记录，不修改历史结论 | 命令、环境、结果和未验证项 |
| P02-706 | 按兼容新功能递增 minor，并同步 manifest 与 `VERSION_HISTORY.md` | 三处版本一致 |
| P02-707 | 运行完整发布门禁并检查 ZIP 不含 profile、缓存、凭据或测试数据 | 可发布 single-file ZIP |

完成条件：文档不再笼统写“禁止所有远程内嵌”，而是明确 Code 仍限本机 DSH、Chat 只允许审计后的官方精确 origin；发布包不包含任何用户浏览数据。

## 7. 测试矩阵

| 测试层 | 新增/扩展测试 | 关键断言 |
| --- | --- | --- |
| Unit | 模式/ViewModel 测试 | 默认 Code、同值幂等、快速切换、托盘恢复、全退出后新 VM 仍默认 Code |
| Unit | Code 策略测试 | loopback、身份确认后的动态 origin、scheme/host/port/UserInfo，不受 Chat 白名单影响 |
| Unit | Chat 策略测试 | 固定入口、额外精确 allowlist、显式 443、HTTP、UserInfo、尾点、IDN、相似恶意域名、危险协议 |
| Unit | 命令路由测试 | F5 只刷新可见页；Chat 下 Start/Stop/Restart 和重启快捷键无调用 |
| Unit | Chat 状态测试 | 取消/过期初始化不覆盖新状态，失败不修改 Harness snapshot |
| Unit | profile 测试 | Code 默认 profile、Chat 固定命名 profile、非 InPrivate、密码/自动填充只作用于 Chat |
| Unit | 清除测试 | 必须二次确认，`AllProfile` 范围准确，重复提交抑制，失败不报成功 |
| Unit | 外链/确认契约测试 | 固定 `OfficialResource` 行为不变；`Open(Uri)` 只接受结构合法的 HTTP(S)；`ConfirmClearChatData()` 拒绝时零副作用 |
| Unit | 权限/下载/日志测试 | 权限默认拒绝、下载不自动执行、敏感 URL/Token/正文不进入日志 |
| Windows Integration | 确定性非 UI 集成 | lifecycle、profile 服务替身和本地 HTTP 页面，不要求真实账号或交互桌面 |
| Windows WebView2 Interactive | 双 WebView2 + 本地页面 | 专用交互式 Windows 通道运行懒初始化、输入/滚动保持、刷新路由、显示切换、事件释放和单页进程失败恢复 |
| Windows WebView2 Interactive | profile 隔离 | 临时 user-data 根目录内 Code/Chat 数据隔离，清除 Chat 后 Code 数据保留 |
| Windows Integration | Harness 生命周期 | DSH 启动/重启中切 Chat 不取消、不抢焦点；切换前后 PID/generation 不变 |
| Manual WPF | 真实 DSH 与 Chat | 页面状态保持、官方登录/退出/清除、外链、新窗口、文件选择、下载、焦点、DPI、托盘 |

自动化测试不得使用真实 DeepSeek 账号、验证码、用户 `%LOCALAPPDATA%`、系统浏览器 profile、API Key 或聊天正文。真实登录验证只在隔离的人工环境使用专用测试账号；记录不得包含凭据、Cookie、Token、完整敏感 URL 或消息截图。

真实 WebView2 测试需要已安装 Runtime 和交互式桌面会话，不能假定普通无头 CI 稳定支持。发布流程必须提供专用 Windows 交互测试通道并在运行前显式检查环境；环境缺失应使该门禁失败或标记发布验证未完成，不能以 `Skip` 伪装通过。普通 `dotnet test` 仍需覆盖所有纯策略、ViewModel、并发和非 UI 集成测试，手工验证也不能替代其中可确定性自动化的部分。

## 8. 验收追踪

| 需求验收项 | 主要任务 | 验证方式 |
| --- | --- | --- |
| `AC-MODE-001`、`002` | P02-201、P02-203、P02-301 | 默认/懒加载 ViewModel 与 WebView2 集成测试 |
| `AC-MODE-003` 至 `005` | P02-205、P02-601、P02-603、P02-605 | 双页面状态保持、快速切换、托盘测试 |
| `AC-LIFE-001` 至 `004` | P02-206、P02-302、P02-304、P02-504 | lifecycle 替身、真实 DSH 和退出释放测试 |
| `AC-NAV-001`、`002` | P02-102 至 P02-104 | Code/Chat 独立 URI 策略测试 |
| `AC-NAV-003`、`004` | P02-001、P02-401、P02-402 | 真实 origin 清单、重定向/外链测试 |
| `AC-NAV-005`、`006` | P02-403、P02-406 | 宿主能力缺失断言、日志隐私测试 |
| `AC-UI-001`、`002` | P02-601、P02-602、P02-604、P02-606 | 可访问性、快捷键和 DPI 验证 |
| `AC-UI-003`、`004` | P02-405、P02-603 | Chat 错误/重试和单页恢复测试 |
| `AC-AUTH-001` 至 `004` | P02-202 至 P02-204、P02-501、P02-506 | profile 稳定性、原生密码和真实重启验证 |
| `AC-AUTH-005`、`006` | P02-502 至 P02-505 | 清除范围、失败恢复和隔离测试 |
| `AC-AUTH-007` | P02-406、P02-501、P02-707 | 配置、日志、测试结果和 ZIP 内容检查 |

## 9. 关键竞态与资源验证

以下场景必须使用 `TaskCompletionSource`、可控替身和有限超时建立确定性测试，不依赖长时间 Sleep：

1. 第一次切 Chat 初始化未完成时连续点击 Code/Chat，只产生一个 Chat controller。
2. Chat 初始化完成前用户退出，过期回调不访问已释放 Dispatcher/控件。
3. DSH 正在 Starting/Restarting 时切到 Chat，Harness operation CTS 和 generation 不变化。
4. Chat 正在导航时切回 Code，完成/失败只更新 Chat 快照，不抢回可见模式。
5. Chat `ProcessFailed` 与用户重试同时发生，恢复预算只消耗一次且不 Reload Code。
6. 清除 profile 与刷新/重试/退出同时发生，只有清除操作持有单页门，失败不报告成功。
7. 托盘隐藏时两个 controller 和模式保留；真正退出时订阅、CTS、Semaphore 和 controller 成对释放。
8. Code Service URI 变化或 DSH 失联时只影响 Code；Chat 的允许 origin 和页面状态不变化。

## 10. 风险与控制

| 风险 | 影响 | 控制措施 |
| --- | --- | --- |
| 官方登录/验证码使用未预期或第三方 origin | 登录流程被拦截，普通系统浏览器登录又无法回写 Chat profile | 阶段 0 冻结 4.4.1 三分支决策；无受支持回调则阻塞发布，绝不 wildcard、复制 Cookie 或注入脚本 |
| Chat 网站前端频繁变化 | 导航、下载或新窗口策略回归 | 不依赖 DOM/脚本；以顶层 origin 和 WebView2 事件为边界；建立手工回归清单 |
| Code 改为命名 profile 导致现有数据丢失 | Harness 页面设置/登录态回退 | 保留现有默认 profile，只给 Chat 新建稳定命名 profile |
| `AllProfile`/profile 删除行为受 Runtime 版本影响 | 清除不完整或无法同进程重建 | 阶段 0 验证；普通清除只用受支持 API；不直接删用户目录 |
| Runtime/企业策略禁用原生密码提示 | 用户无法保存密码，宿主又不能安全补偿 | 真实验证接受/拒绝/不可用三种结果；文档声明不保证；Cookie 会话持久化独立验收；不实现宿主凭据库 |
| WPF WebView2 airspace/focus | 错误视图被网页覆盖、隐藏页抢焦点 | 状态页显示时折叠 WebView2；F6 按当前模式路由；多 DPI 手工检查 |
| 两个 WebView2 增加内存/进程占用 | 长时间运行资源上升 | Chat 懒初始化；进程内复用；记录空闲/双页内存基线，不用切换时重建 |
| 下载事件无法可靠证明用户手势 | 静默或恶意下载 | 默认取消；仅在阶段 0 证明可安全控制后增加显式保存确认，永不自动打开 |
| Chat 网络错误被误判为 Harness 失败 | DSH 状态、按钮和提示混乱 | 独立快照、错误号段和事件；测试 coordinator 零调用 |
| 两份开发计划先后落地造成重复服务 | DI 冲突和维护成本 | 冻结计划 01 先完成并通过门禁；计划 02 只扩展其最终 URI/外链/确认契约 |
| 普通 CI 无交互桌面或 WebView2 Runtime | 真实 controller/profile 测试被跳过，发布结论失真 | 建立专用 Windows 交互通道和显式前置检查；缺失即门禁未完成，不静默 Skip |

## 11. 验证命令与发布门禁

各阶段运行相关测试。完成功能和文档同步后，在仓库根目录执行：

```powershell
dotnet restore DeepSeekHarnessDesktop.sln
dotnet build DeepSeekHarnessDesktop.sln -c Debug --no-restore -m:1
dotnet test DeepSeekHarnessDesktop.sln -c Release --no-restore -m:1
.\eng\Verify-Release.ps1
```

发布门禁还必须确认：

- 0 warning、0 error，UnitTests、Windows IntegrationTests 和专用 Windows WebView2 Interactive 测试全部通过；环境缺失不得算通过。
- 应用冷启动不访问 Chat，且每次启动模式均为 Code。
- Code/Chat 切换不改变 DSH PID、ownership、generation、工作目录或运行操作。
- Chat 只内嵌经审计的精确 HTTPS origin，Code 仍只加载已确认的 loopback DSH。
- Chat 登录态在有效期内跨应用重启保留，清除后只影响 Chat profile。
- 原生密码提示的接受、拒绝和策略不可用结果均有记录；不可用时宿主不绕过策略、不谎报已启用。
- Release DevTools 关闭；权限默认拒绝；下载不静默保存或自动执行。
- `settings.json`、日志、异常、验证材料、发布 ZIP 不含账号、密码、Cookie、Token、聊天正文、浏览器 profile 或用户缓存。
- 820x600 与 100%-200% DPI 下无控件重叠、裁切、网页覆盖或错误焦点。
- `AGENTS.md` 与 `CLAUDE.md` 内容完全一致；`AppVersion`、manifest、版本历史三处一致。

若真实 Chat 验证因网络、地区、验证码或官方服务状态无法完成，不得将 `AC-AUTH-*`、额外 origin 或登录流程标记为通过；应在新验证记录中列为明确阻塞和残余风险。

## 12. 完成定义

只有同时满足以下条件，计划才可标记完成：

1. Code/Chat 模式、双页面保持、懒加载和模式化命令均有自动化与 WPF 验证证据。
2. 计划 01 已先完成发布门禁，计划 02 没有复制其 URI、外链、确认或 DI 抽象。
3. 模式切换没有新增 Harness 状态、generation 或旁路生命周期操作。
4. Code 与 Chat 的导航策略、profile、错误和恢复预算彼此隔离。
5. Chat 登录态仅由 WebView2/Windows 当前用户 profile 管理，宿主无法读取或导出凭据。
6. 登录跨 origin 方案通过 4.4.1 门禁；普通系统浏览器登录未被误当作内嵌 profile 登录。
7. 清除命令经二次确认，只清除 Chat profile，失败不谎报且不影响 Code/DSH。
8. 所有异步操作可取消，过期结果不覆盖新状态，事件和资源成对释放。
9. 开发文档、详细设计、安装说明、维护规则、验证记录、版本和发布产物全部同步。
10. 未修改或提交 `bin/`、`obj/`、`TestResults/`、WebView2 用户数据、日志、缓存或凭据。
