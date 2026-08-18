# DeepSeek Harness Desktop 启动性能优化需求 Prompt

## 1. 文档信息

- 文档类型：后续设计、开发与验收执行 Prompt
- 适用项目：DeepSeek Harness Desktop
- 编写日期：2026-08-18
- 需求状态：待实现
- 文档基线版本：`0.7.2`
- 本文档记录版本：`0.7.4`
- 目标功能版本：`0.8.0`
- 需求主题：以启动速度为优先，消除托管运行时重复完整校验和窗口显示前的非必要阻塞
- 关联计划：`plan/plan-20260818-06-Desktop启动性能优化开发计划.md`

## 2. 使用方式与优先级

将本文作为 Desktop 启动性能设计、开发、测试和发布验收的权威输入。执行者必须先读取届时当前实现、相关测试、`AGENTS.md`、`CLAUDE.md`、`code_rule.md`、托管运行时 Prompt/Plan、开发文档、详细设计和最新验证记录，再依据真实接口实施。

“启动速度优先”的含义是：在不扩大进程、文件、网络、WebView2 和供应链权限边界的前提下，优先缩短用户看到窗口和已有运行时进入可用状态的关键路径。它不表示永久跳过完整性校验，也不允许仅信任时间戳、文件数量、目录存在或未经保护的缓存结论。

本文与 `prompt-20260817-05` 冲突时，只在“已完整验证并成功提交的同一托管运行时如何在后续正常启动中复用校验结果”这一点上以本文为准。首次安装、升级、重建、回退、原子提交、完整文件 hash、运行租约、Job Object、loopback 身份和有界恢复仍遵循 05 号需求。

## 3. 已确认基线与根因

### 3.1 实测启动时间

在当前开发设备上，对已安装且可正常工作的托管运行时重复启动，记录到以下代表性数据：

| 样本 | 窗口前初始诊断 | 生命周期内第二次运行时校验 | DSH 创建至 HTTP 就绪 | 总耗时 |
| --- | ---: | ---: | ---: | ---: |
| 1 | 25.3 秒 | 18.8 秒 | 2.5 秒 | 46.6 秒 |
| 2 | 24.1 秒 | 21.7 秒 | 2.4 秒 | 48.2 秒 |
| 3 | 19.7 秒 | 14.6 秒 | 2.4 秒 | 36.6 秒 |
| 4 | 30.6 秒 | 23.5 秒 | 2.9 秒 | 56.9 秒 |
| 5 | 24.2 秒 | 21.1 秒 | 2.4 秒 | 47.6 秒 |

当前正常启动约为 37 至 57 秒，中位数约 48 秒。DSH 自身从进程创建到 HTTP 身份确认只需约 2 至 3 秒，不是主要瓶颈。

### 3.2 已确认代码路径

- `App.OnStartup` 在 `MainWindow.Show()` 前同步等待 `DependencyDiagnosticsService.DiagnoseAsync`。
- 诊断服务调用 `IManagedRuntimeStore.InspectActiveAsync`，触发已安装运行时的完整校验。
- `ManagedRuntimeStore.ValidateInstallationAsync` 对 manifest 中约 33,003 个文件逐一打开并计算 SHA-256，同时枚举安装目录检查额外文件。该数量和下述 366 MB 来自当前运行时样本，不是代码或未来发布包的固定常量；阶段 0 必须从受测 Release manifest 重新记录实际文件数与字节数。
- 随后 `ManagedRuntimeProvisioner.EnsureReadyAsync` 在生命周期启动路径再次验证同一活动运行时。
- 单次完整校验约读取 366 MB、打开约 33,000 个文件；重复两次约打开 66,000 个文件并读取 732 MB。
- Windows Defender 或企业杀毒软件会放大小文件打开与读取成本，因此同一代码在新设备上可能更慢。

根因是“窗口显示前的完整校验 + 同一进程内重复完整校验”，不是 DSH 服务启动慢，也不能靠延长 `StartupTimeoutSeconds` 解决。

## 4. 术语与计时口径

1. `ProcessEntry`：应用进程进入 `App.OnStartup` 后记录的第一个单调时钟时间点。
2. `ShellVisible`：主窗口首次完成 `ContentRendered`，用户可看到并操作宿主控制区；不能用调用 `Show()` 的时刻替代。
3. `Usable`：DSH 已通过 HTTP 身份确认，Code WebView2 已具备导航条件，主工作流可交互。
4. `ReuseStart`：活动运行时此前已完整验证和成功提交，runtime id、manifest 和兼容范围未变化，且没有强制完整校验触发条件的启动。
5. `ProvisioningStart`：首次安装、Desktop 携带新 runtime id、运行时重建或回退，需要解包或阻塞式完整校验的启动。
6. `FastValidation`：复用既有完整校验证明前执行的有界结构、身份、关键入口和变更证据检查。
7. `FullValidation`：按 manifest 对全部文件执行长度、SHA-256、额外文件、完成标记和兼容性校验。

所有耗时使用 `TimeProvider.GetTimestamp()` / `GetElapsedTime()` 或 `Stopwatch` 的单调时间，不使用 `DateTime.Now` 计算持续时间。日志时间戳只用于关联，不作为性能统计来源。

## 5. 目标与非目标

### 5.1 目标

1. 主窗口不再等待托管运行时完整校验、全局 Node/DSH 版本探测或 DSH HTTP 就绪后才显示。
2. 同一 Desktop 进程对同一 runtime id 与 manifest 摘要最多执行一次完整校验，并让诊断、准备和生命周期共享该结果。
3. 已完整验证且没有变化证据的活动运行时走快速复用路径，避免每次启动读取数百 MB 和打开数万个文件。
4. 首次安装、升级、异常或疑似篡改仍执行完整校验；周期完整复核在可用后低优先级执行。
5. 记录结构化启动阶段、耗时、验证模式、文件/字节量和恢复结果，使慢启动能够用日志定位。
6. 建立可重复的 Release 性能基准，区分窗口可见、正常复用、首次准备、冷/热文件缓存和安全软件场景。
7. 保留生命周期 gate、generation、取消、Owned/External 边界、运行租约、Job Object、HTTP 身份和 WebView2 同源限制。

### 5.2 非目标

- 不通过关闭 Windows Defender、建议用户添加排除项或降低企业安全策略获得性能。
- 不删除 manifest 文件级 hash，不把文件存在、大小、最后写入时间或目录时间单独当作完整性证明。
- 不在客户端重新引入 npm/npx、PATH 中的 `dsh.cmd`、用户 npm cache 或在线依赖解析。
- 不为性能并行执行 Stop/Restart/Start，不放弃生命周期串行、取消和 generation 校验。
- 不在 WPF UI 线程做文件枚举、hash、WebView2 环境创建或进程等待。
- 不承诺首次解包、强制修复和完整复核与正常复用启动具有相同的可用时间。
- 不在本需求中改变 DSH、Node 或 Desktop 自动更新策略。

## 6. 冻结产品与架构决策

### 6.1 启动关键路径

启动按以下顺序组织：

1. 记录进程入口，建立日志和未处理异常边界。
2. 完成单实例判定；次实例只通知主实例，不创建第二套运行时任务。
3. 加载显示主窗口所需的最小设置和依赖，构造 DI 容器并尽快显示 Shell。ShellVisible 前创建的服务构造器必须无同步文件枚举、清理、hash 或网络 I/O；当前 `ManagedRuntimeStore` 构造阶段的目录创建、安全检查和 staging 清理必须延迟到窗口显示后的异步初始化，或由阶段 0 证明其不影响目标后再保留。
4. 窗口显示后启动异步初始化，执行快速运行时检查、DSH 生命周期和必要的 WebView2 初始化。
5. 只有存在明确依赖关系的步骤才串行；Code WebView2 环境初始化与托管运行时快速检查/Owned DSH 启动可并行时并行，导航必须等待二者均成功。
6. 全局 Node/DSH 版本、上游版本和其他只读诊断不进入 `ShellVisible` 或 `Usable` 关键路径，按需或后台获取。

窗口提前显示后必须展示真实状态，不能短暂显示“已就绪”后回退为“正在检查”。初始化异常进入现有可恢复状态和错误页，不从后台任务形成未观察异常或静态 MessageBox 竞态。

### 6.2 快速校验与完整校验

引入显式校验级别，禁止用布尔参数隐藏语义：

| 级别 | 用途 | 最低检查 |
| --- | --- | --- |
| `Fast` | 正常复用启动 | `active.json` 原子状态、manifest schema/hash、`.complete`、Desktop 兼容范围、runtime id、平台、运行时根身份、私有 Node 与 DSH 入口存在/类型/关键摘要、稳定校验证明和变更证据 |
| `Full` | 建立或重新建立可信结论 | Fast 的全部内容，加 manifest 中所有文件长度/hash、文件总数、展开字节、额外文件与安全属性检查 |

Fast 结论只能复用此前成功 Full 校验产生的稳定证明。证明至少绑定：schema、runtime id、manifest SHA-256、Desktop 兼容范围、运行时根的卷/文件身份、完成标记摘要、关键入口摘要、最近完整校验时间、校验策略版本和可用的文件系统变更检查点。证明通过临时文件 + replace 原子更新，并限制在现有 `%LOCALAPPDATA%\DeepSeekHarnessDesktop\runtime` 存储边界内。

阶段 0 必须冻结证明字段、存放位置、ACL/只读策略和威胁模型。文件时间戳、目录时间、文件数量或 DPAPI/HMAC 单独使用都不足以证明相同用户上下文中的文件未被篡改，不得在设计或文案中作超出能力的安全承诺。若文件系统无法提供可信的增量变更证据，Fast 仍必须校验 manifest、完成标记和关键可执行入口，并按下述触发策略定期 Full；任何关键字段缺失或不一致均 fail closed 到 Full，而不是继续启动。

阶段 0 ADR 和面向用户的安全说明必须逐字表达以下结论的等价语义：**Fast 证明是正常启动的可用性优化，不是同一 Windows 用户权限下的主动防篡改控制；当可信增量变更证据不可用时，非关键文件的同用户主动篡改最迟可能在下一次周期 Full 才被发现，初定最长窗口为 7 天。** 这是显式接受并受周期 Full 限制的残余风险，不得被安装、关于、日志或发行文案描述成“每次启动均已完整校验”。

### 6.3 完整校验触发策略

以下情况必须在执行私有 Node/DSH 前完成阻塞式 Full：

- 首次安装、首次建立校验证明或新 runtime id 激活。
- 随 Desktop 发布的新 manifest、校验策略 schema 或 Desktop 兼容范围发生变化。
- 强制重建、自动修复、上一版本回退或活动指针/完成标记恢复。
- Fast 任一检查失败，证明缺失、损坏、身份不匹配或文件系统变更证据不连续。
- 上次后台 Full 失败、上次事务未完成、上次 Desktop/Owned DSH 非正常退出或检测到疑似篡改。

仅“周期复核到期”且 Fast 全部通过时，允许先完成正常启动，再在 `Usable` 后低优先级执行后台 Full。周期上限初定为 7 天；阶段 0 可基于安全评审与实测缩短，但不得无限延后或通过每次重启重置。

### 6.4 单进程校验去重

增加进程级校验协调器或等价机制，以 `(runtime id, manifest digest, validation policy version)` 为 key 共享进行中任务和已完成结果：

- Full 成功可满足同 key 的 Fast 请求；Fast 结果不能冒充 Full。
- 诊断、Provisioner 和 Lifecycle 不得分别重复遍历同一安装。
- 单个等待方取消只取消自己的等待；共享校验由应用生命周期或校验 operation token 管理，不能被无关页面关闭误杀。
- 失败结果只在当前明确 operation 内共享，用户修复、runtime id 变化或 generation 变化后必须重新判断。
- 所有返回启动选项的路径仍需获取 `ManagedRuntimeLease`；校验结果缓存不能替代运行租约。

### 6.5 后台完整校验与失败闭环

后台 Full 只在 Shell 和 DSH 已可用后调度，I/O 并发为 1，并避免与解包、修复、回退或关闭流程并发。后台任务必须可取消、可等待、可释放，不得在应用退出后继续访问运行时。

后台 Full 发现损坏时：

1. 原子作废 Fast 校验证明，保证下一次启动不能复用。
2. 通过现有生命周期 gate 提交结果，并验证 runtime id、generation 和 Owned 进程仍匹配。
3. 对匹配的 Owned DSH 停止整个 Job Object 进程树，阻止继续使用已判定损坏的运行时。
4. 按现有每 operation 最多一次重建、一次兼容回退预算恢复；不递归调用 Start。
5. 若当前是 `RunningExternal`，只更新托管运行时诊断，不停止或重启外部进程。
6. 用户退出、Stop、Restart 或新 generation 已胜出时，过期后台结果不得覆盖新状态或重新创建进程。

完整校验过程中如文件变化，结果不得写成成功证明。实现必须用可测试的前后变更检查点或稳定文件句柄识别扫描期间变化，最多进行一次有界重试；仍不稳定时按完整性失败处理。

### 6.6 启动计时与日志

新增稳定 EventId 和每次启动唯一但不含设备身份的 `StartupOperationId`。阶段 0 冻结最终编号，至少覆盖：

| 建议 EventId | 事件 | 必要字段 |
| --- | --- | --- |
| `1300` | ProcessEntry | operation id、Desktop version |
| `1301` | SettingsReady | elapsed ms、是否默认回退 |
| `1302` | ShellVisible | elapsed ms |
| `1310` | RuntimeValidationCompleted | Fast/Full、elapsed ms、runtime id、files/bytes、共享/新执行 |
| `1320` | WebViewEnvironmentReady | elapsed ms、成功/错误码 |
| `1330` | DshProcessCreated | elapsed ms、Owned PID、runtime id |
| `1331` | DshHttpReady | process-to-ready ms、确认 URI |
| `1332` | StartupUsable | total elapsed ms、启动类型、恢复次数 |
| `1390` | StartupFailed | stage、elapsed ms、稳定错误码 |

日志不得记录 API Key、Cookie、Token、完整环境变量、文件正文或运行时全部文件名。工作目录和本机路径继续按现有脱敏策略处理。对启动失败仍保留原始异常作为内部 cause，但 UI 只显示简明中文和稳定错误码。

### 6.7 性能基准方法

发布性能报告必须记录 Windows 版本、CPU 核数、内存、磁盘类型/文件系统、Defender/第三方安全软件状态、Desktop/runtime/Node/DSH 版本和包 SHA-256。基准至少区分：

- 已验证运行时、OS 文件缓存较热的连续正常启动。
- 已验证运行时、重启设备后的冷缓存启动。
- 首次安装/新 runtime id 的准备启动。
- 周期后台 Full、强制 Full 和完整性失败恢复。
- Defender 实时保护开启；可取得企业杀毒设备时另记一组，不要求关闭安全软件对比来通过门禁。

自动基准至少运行 20 次正常复用启动，使用结构化完成信号和有界等待，不用固定 `Thread.Sleep` 猜测就绪。结果报告原始样本、P50、P95、最大值、失败数和验证模式；丢失阶段事件的样本计为失败，不能静默剔除。

阶段 0 除旧代码基线外，必须执行两个不改变生产默认安全行为的测量原型：一是最小 Shell 原型，临时移出诊断等待并延迟有 I/O 的服务构造，测量真实 ShellVisible 下界；二是只针对受测运行时执行拟定 Fast 检查集合的微基准，测量 Fast 下界。原型不得作为绕过完整校验的正式启动路径交付。

性能工具优先从结构化日志等外部可观察信号判断 Usable 并调用现有正常退出路径。若确需专用控制信号，它只能存在于不分发的工程测量构建，生产 Release 默认必须禁用或完全不包含，并增加发布包负向测试；不得新增任意路径、任意命令或可被外部滥用的产品控制面。

## 7. 状态、数据与职责边界

- `HarnessStateMachine` 继续只表达 DSH 生命周期，不加入每个性能阶段。
- 运行时 Fast/Full、进行中、共享、失效和后台复核状态放在托管运行时模型或独立校验模型中。
- `IManagedRuntimeStore` 负责执行具体 Fast/Full 检查和原子持久化证明，不负责启动 DSH。
- 新的校验协调器负责进程内去重、共享和失效，不拥有 WPF 控件或进程。
- `IManagedRuntimeProvisioner` 负责缺失/损坏运行时的准备与有界重建，消费校验结果，不再无条件重复 Full。
- `DependencyDiagnosticsService` 只消费共享快照；全局 Node/DSH 探测为可选诊断，不阻塞 Shell 或正常 Auto 启动。
- `HarnessLifecycleCoordinator` 保持 gate/token/generation，负责把后台失败安全映射为 Owned 停止与恢复。
- ViewModel 只通过 Dispatcher 绑定启动阶段、耗时和诊断，不直接执行文件 hash、计时脚本或进程操作。

持有共享任务、CTS、事件、计时器、文件流、运行租约或后台 worker 的服务必须实现成对释放。不得在锁内执行 I/O、触发事件或 `await`。

## 8. 失败、取消与竞态要求

必须覆盖以下行为：

1. 诊断和 Provisioner 同时请求同一 Full，只运行一次文件遍历。
2. Fast 与 Full 同时请求时，Full 可提升并满足 Fast，不能产生两个冲突证明。
3. 一个等待方取消，其他等待方仍能得到共享结果。
4. runtime id、manifest 或策略版本在校验期间变化，旧结果不得提交。
5. Shell 已显示但初始化失败，UI 保持可操作并显示稳定错误，不崩溃或永久忙碌。
6. WebView2 初始化与 DSH 启动先后任意完成，只在二者条件满足后导航。
7. 后台 Full 与 Stop/Restart/退出同时完成，generation 较新的操作胜出。
8. 后台 Full 失败时 Owned 被停止，External 不受影响。
9. 校验过程中运行时文件变化，不产生成功证明。
10. 异常退出标记写入、正常退出提交和下次启动读取均为原子操作。
11. 周期任务、窗口关闭到托盘和显式退出之间不泄漏任务、句柄或事件。
12. 多次快速重试不绕过一次重建/一次回退预算。

测试使用 `TaskCompletionSource`、fake store/stream、`TimeProvider` 和有限超时控制顺序，禁止用长时间 `Thread.Sleep` 制造竞态。

## 9. 验收标准

### 9.1 启动性能

参考门禁环境为 Windows 11 x64、至少 4 核/8 GB、受支持的本地 NTFS SSD、Release 包完整解压、Defender 实时保护开启且不在调试器内。阶段 0 必须记录实际硬件并冻结基准机。DSH create-to-ready 阈值由现有 2.4 至 2.9 秒数据直接支撑；ShellVisible、Usable 和 Fast 是优化后新指标，当前数值属于工程目标，不能宣称由旧路径基线推出。它们必须经阶段 0 最小 Shell/Fast 原型实测确认；若需调整，只能在原型证据形成后、生产实现前调整一次并写入 ADR，阶段 4/6 再以正式实现确认，不能在实现后为通过测试下调。

- `AC-STARTUP-PERF-001`：正常复用启动的 `ShellVisible` P95 不超过 2.0 秒。
- `AC-STARTUP-PERF-002`：正常复用启动的 `Usable` P50 不超过 8 秒，P95 不超过 15 秒，至少 20 个有效样本。
- `AC-STARTUP-PERF-003`：Owned DSH 从进程创建到 HTTP 身份确认 P95 不超过 5 秒。
- `AC-STARTUP-PERF-004`：Fast 校验 P95 不超过 1.5 秒，且不读取全部 manifest 文件正文。
- `AC-STARTUP-PERF-005`：首次准备、强制 Full 和冷缓存场景分别报告，不混入正常复用样本；无硬阈值的场景必须保留阶段分解和回归基线。
- `AC-STARTUP-PERF-006`：后台 Full 不阻塞 ShellVisible；启动可用后执行时，UI 输入、状态刷新和 WebView2 导航保持可用。

### 9.2 校验正确性

- `AC-STARTUP-VAL-001`：同一进程、同一 key 的 Full 文件遍历最多一次，诊断和生命周期共享结果。
- `AC-STARTUP-VAL-002`：没有先前成功 Full 证明时不得仅以 Fast 启动。
- `AC-STARTUP-VAL-003`：Fast 至少验证 manifest/完成标记/兼容范围/运行时根身份/关键入口摘要和证明 schema，任一不符转 Full。
- `AC-STARTUP-VAL-004`：首次安装、升级、修复、回退、异常退出、证明失效和疑似篡改在执行 DSH 前完成 Full。
- `AC-STARTUP-VAL-005`：周期复核最多延后 7 天；到期后仅在 Fast 全通过时可后台执行。
- `AC-STARTUP-VAL-006`：Full 成功证明原子写入；扫描期间文件变化或取消不得产生可复用证明。
- `AC-STARTUP-VAL-007`：证明不能仅依赖时间戳、文件数或目录存在，也不能替代 `ManagedRuntimeLease`。

### 9.3 恢复、并发与安全

- `AC-STARTUP-REC-001`：后台 Full 失败立即作废证明，并通过 lifecycle gate 对匹配 Owned 运行时执行停止和有界修复。
- `AC-STARTUP-REC-002`：后台失败不停止、重启或清理 `RunningExternal`。
- `AC-STARTUP-REC-003`：过期 runtime id/generation 的校验结果不能覆盖当前状态或启动进程。
- `AC-STARTUP-REC-004`：用户取消、Stop、Restart 和退出均能有界等待或取消后台任务，无未观察异常与资源泄漏。
- `AC-STARTUP-SEC-001`：优化不改变客户端无 npm/npx、受控路径、Job Object、loopback 身份和 WebView2 同源边界。
- `AC-STARTUP-SEC-002`：Fast 证明与威胁模型在详细设计中明确，不能宣称阻止同一用户权限下超出实际能力的主动攻击。

### 9.4 UI、日志与基准

- `AC-STARTUP-UI-001`：主窗口先显示真实初始化状态，不能闪现错误的 Ready；错误可重试且命令状态与生命周期一致。
- `AC-STARTUP-UI-002`：100%、125%、150% DPI 和最小窗口下，启动阶段、耗时和操作不重叠 WebView2 或相邻控件。
- `AC-STARTUP-LOG-001`：每次启动可由 operation id 关联到 ProcessEntry、ShellVisible、验证、进程创建、HTTP Ready、Usable/Failed。
- `AC-STARTUP-LOG-002`：启动日志和报告经过限长、规范化和脱敏，不包含凭据、文件正文或完整环境。
- `AC-STARTUP-BENCH-001`：基准使用单调计时、结构化完成信号、至少 20 次样本和 P50/P95；不靠固定 sleep 判定成功。
- `AC-STARTUP-BENCH-002`：发布报告记录环境、版本、包 hash、每个原始样本、失败样本和冷/热/首次准备分类。

## 10. 测试要求

### 10.1 单元测试

- Fast/Full 选择、证明 schema、触发策略、7 天边界和单调计时。
- 同 key 任务共享、Full 满足 Fast、取消隔离、失败失效和 key 变化。
- App/ViewModel 的初始化状态映射、Dispatcher 切换、命令状态和后台异常处理。
- EventId、operation id、阶段顺序、耗时字段和敏感信息脱敏。

### 10.2 Windows 集成测试

- 真实小型运行时目录证明 Full 后正常重启只走 Fast，修改关键/普通文件均触发 Full 或失败。
- 诊断与生命周期并发请求只遍历一次；使用计数 fake stream/store，不用墙钟猜测。
- WebView2 与 DSH 两条异步分支以两种顺序完成均只导航一次。
- 后台校验失败时 Owned 进程树回收、External 保留、运行租约和目录清理正确。
- 异常终止后下次启动阻塞 Full；正常退出后可 Fast。

### 10.3 性能与人工验证

- 在完整 Release 包和真实 33,000 文件运行时上执行热缓存、重启后冷缓存和 Defender 开启基准。
- 记录首次安装/升级、正常复用、周期 Full、损坏修复的阶段时间和磁盘读取量。
- 检查窗口首次显示、托盘恢复、键盘焦点、最小窗口和 100%-150% DPI。
- 在至少一台非开发新设备上复核日志、P50/P95 和无 Node/npm 环境启动。
- 验证正式 Release 默认不存在或拒绝任何基准专用退出/控制信号，性能测量不能扩大生产控制面。

## 11. 文档、版本与发布

- 实施时同步 `docs/deepseek-harness-desktop-development.md`、详细设计和 `docs/installation.md`，说明正常复用与首次准备的时间差异。
- 新增带日期的性能验证记录，不回写历史验证结果。
- 代码、配置、测试、发布脚本或用户文档的每个独立交付批次都按 `AGENTS.md` 立即同步 `AppVersion`、manifest 和 `VERSION_HISTORY.md`，不得把所有阶段的升版集中到最终验收。内部兼容准备批次按 patch 递增；首次收口并交付本兼容新功能时使用目标 minor `0.8.0`，其后的修复从 `0.8.1` 继续。
- `Verify-Release.ps1` 必须继续验证完整托管运行时包内容、两次确定性构建、真实 DSH smoke，并增加启动性能报告存在性与阈值断言；性能优化不能删减供应链门禁。
- 不提交基准临时目录、日志、TRX、`bin/obj` 或用户 LocalAppData 内容。

## 12. 完成定义

1. 所有 `AC-STARTUP-*` 均有自动测试、发布报告或明确人工证据，且能追溯到开发任务。
2. 正常复用启动达到 ShellVisible、Usable、Fast 和 DSH Ready 阈值。
3. 同一进程不重复完整遍历同一运行时，首次安装和所有失效触发仍完整校验。
4. 后台 Full 失败能够停止匹配 Owned 运行时并按既有预算恢复，External 不受影响。
5. 启动日志能准确回答慢在设置、窗口、校验、WebView2、进程还是 HTTP，不泄露敏感信息。
6. 生命周期、generation、取消、运行租约、Job Object、HTTP 身份和 WebView2 安全测试无回归。
7. 完整 Release 门禁、性能基准和新设备人工验证通过。
8. 文档、版本三处和 `AGENTS.md`/`CLAUDE.md` 一致性检查完成。
