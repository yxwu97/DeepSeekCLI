# DeepSeek Harness Desktop 启动性能优化开发计划

## 1. 计划信息

- 编写日期：2026-08-18
- 当前基线：`0.7.2` 托管运行时实现
- 本计划记录版本：`0.7.4`
- 目标功能版本：`0.8.0`
- 需求输入：`prompt/prompt-20260818-06-Desktop启动性能优化需求.md`
- 规则输入：`AGENTS.md`、`CLAUDE.md`、`code_rule.md`
- 计划状态：待执行
- 预计工期：15 至 24 个工作日；各阶段工期顺序相加为 15.0 至 24.0 日

本文只制定后续开发顺序和验收门禁，不代表功能已实现。开发时必须基于届时真实代码和测试更新落点；接口名可在阶段 0 细化，但不得改变 Prompt 冻结的安全、性能和恢复语义。

## 2. 实施目标

1. 将主窗口显示从完整依赖诊断和运行时 hash 校验之前释放出来，正常启动尽快提供可见 Shell。
2. 建立 Fast/Full 两级校验和稳定证明，正常复用不再打开约 33,000 个文件读取约 366 MB。
3. 在一个 Desktop 进程内共享同一运行时校验任务，消除诊断和 Provisioner 的第二次完整遍历。
4. 保持首次安装、升级、修复、回退、异常退出和疑似篡改的阻塞式 Full，不用时间戳缓存替代完整性。
5. 在 `Usable` 后执行有界、低优先级的周期 Full；失败时安全停止匹配 Owned DSH 并进入既有修复预算。
6. 建立稳定启动 EventId、单调计时和 Release 性能基准，以 P50/P95 而不是单次主观观察验收。
7. 保留无 npm/npx、进程所有权、生命周期串行、运行租约、loopback 身份、WebView2 同源和日志脱敏边界。

## 3. 当前基线与性能模型

### 3.1 当前实现

| 组件 | 当前行为 | 性能影响 |
| --- | --- | --- |
| `App.xaml.cs` | `MainWindow.Show()` 前加载设置并等待完整依赖诊断 | 用户在诊断完成前看不到窗口 |
| `DependencyDiagnosticsService` | 读取 WebView2、PATH，探测全局 DSH/Node，并调用 `InspectActiveAsync` | 可选诊断进入关键路径；活动运行时执行第一次 Full |
| `ManagedRuntimeStore` | `ValidateInstallationAsync` 按 manifest 逐文件计算 SHA-256，并枚举目录 | 约 33,003 次文件 hash、366 MB 读取 |
| `ManagedRuntimeProvisioner` | `EnsureReadyAsync` 启动时再次检查/获取活动运行时 | 同一进程对同一 runtime 再执行一次 Full |
| `HarnessLifecycleCoordinator` | Provisioner 完成后才创建 Owned DSH，随后等待 HTTP 身份 | DSH 自身约 2 至 3 秒，受前置 Full 放大 |
| `CodeWebViewService` | App 在 lifecycle 初始化前等待 WebView2 初始化 | 可独立步骤尚未明确并行，需阶段 0 画出依赖图 |
| `InstallationGuideViewModel` | 已使用 `TimeProvider` 显示运行阶段时间 | 尚无从进程入口到 Shell/Usable 的完整结构化时序 |

### 3.2 实测根因

五次代表样本总耗时为 36.6、46.6、47.6、48.2、56.9 秒；初始诊断约 19.7 至 30.6 秒，第二次完整校验约 14.6 至 23.5 秒，DSH/HTTP 约 2.4 至 2.9 秒。

当前受测运行时样本的每次 Full 约打开 33,000 个文件并读取 366 MB；两次 Full 约打开 66,000 个文件并读取 732 MB。这些是 Prompt 留存的样本数据，不是未来 runtime 的固定常量，P06-001 必须从受测 Release manifest 和实际 I/O 重新输出文件数与字节数。小文件 I/O 被 Defender/企业杀毒实时扫描放大。优化重点应是缩短关键路径和复用可信结果，不是增大 DSH 超时。

### 3.3 性能口径

- `ShellVisible`：ProcessEntry 到主窗口首次 `ContentRendered`。
- `Usable`：ProcessEntry 到 DSH HTTP 身份确认且 Code WebView2 具备导航条件。
- 正常复用样本：活动运行时已 Full、无失效触发、未发生解包/修复/回退。
- 首次准备、强制 Full、冷缓存和恢复样本单独报告，不能混入正常复用 P50/P95。
- 持续时间全部来自单调时钟；结构化日志的墙钟只用于关联。

## 4. 冻结设计决策

### 4.1 Shell 优先

保留日志、异常边界和单实例判定在窗口之前；只加载构造 Shell 必需的设置与服务。托管运行时完整诊断、全局 Node/DSH 版本探测、上游版本查询和 DSH HTTP 等待不得阻塞 `MainWindow.Show()`。

窗口显示后由单一异步初始化编排器启动剩余工作并拥有应用级 CTS。窗口先显示真实“正在初始化”状态；失败进入绑定状态，不从后台线程直接弹静态 MessageBox。关闭到托盘不取消运行中的正常 DSH；明确退出才取消并等待后台校验和 Owned 清理。

### 4.2 Fast/Full 与稳定证明

新增显式 `ManagedRuntimeValidationLevel.Fast/Full` 或语义等价模型。Full 仍执行现有 manifest 全文件长度/hash、额外文件和兼容检查；成功后原子写入稳定证明。Fast 读取并验证证明、manifest/完成标记、兼容范围、运行时根身份、关键入口摘要和可用文件系统变更证据。

证明只能缓存此前 Full 的结论，不能自己产生可信结论。时间戳、文件数、目录存在、DPAPI 或 HMAC 单独使用均不是无变化证明。阶段 0 必须冻结：

- schema 与策略版本。
- runtime id、manifest digest、Desktop compatibility、volume/root file identity。
- `.complete` 与关键 Node/DSH 入口摘要。
- Full 完成时间、周期到期时间、异常退出标记和文件系统变更检查点。
- 在现有 `runtime/versions`、`staging`、`active.json` 边界内的存放位置和原子更新方式。
- NTFS 增量证据可用/断档、非 NTFS 和同用户主动攻击的明确威胁模型。

关键证据缺失或不一致时转阻塞式 Full，绝不回退为“假定可用”。

P06-004 ADR 和 P06-702 用户安全说明必须写死这一结论：Fast 是可用性优化，不是同一 Windows 用户权限下的主动防篡改控制；若可信增量证据不可用，非关键文件的同用户主动篡改最迟可能在下一次周期 Full 才被发现，初定残余风险窗口最长 7 天。任何产品文案不得将 Fast 描述为“本次启动已完成全部文件校验”。

### 4.3 进程内共享

引入 `IManagedRuntimeValidationCoordinator` 或与现有 Store/Provisioner 边界一致的等价服务。以 `(runtime id, manifest digest, policy version)` 共享进行中和成功结果；Full 可满足 Fast，Fast 不可满足 Full。等待方取消与共享 operation 取消分离，应用退出才统一停止共享工作。

协调器只共享校验，不替代 Provisioner、运行租约或 lifecycle gate。runtime key、generation、修复事务或策略变化后立即失效。

### 4.4 Full 触发与后台恢复

首次安装、新 runtime、证明缺失/损坏、Fast 不符、异常退出、修复、回退和疑似篡改均在 DSH 执行前 Full。仅 7 天周期到期且 Fast 全通过时允许先到 Usable，再低优先级后台 Full。

后台失败先作废证明，再经 lifecycle gate 和 generation/runtime 匹配检查停止 Owned 树，按每 operation 一次重建、一次兼容回退恢复。External 只更新诊断。Stop、Restart、退出或更新 generation 已胜出时，旧结果不得重新启动进程。

### 4.5 独立工作并行

阶段 0 依据调用图冻结并行段。预期在 ShellVisible 后并行启动：

- Code WebView2 环境初始化。
- 托管运行时 Fast/必要 Full、Owned DSH 创建和 HTTP 身份等待。

只有二者都成功才导航。若 WebView 服务内部依赖生命周期状态，则保持必要串行并优先通过延迟非关键 WebView 工作优化，不能为追求指标制造双导航、跨线程控件访问或取消竞态。

### 4.6 计时和性能门禁

使用稳定 EventId、启动 operation id 和单调时钟记录 ProcessEntry、SettingsReady、ShellVisible、RuntimeValidation、WebViewReady、DshProcessCreated、DshHttpReady、Usable/Failed。正常复用至少运行 20 次，报告原始样本、P50、P95、最大值和失败数。

目标门禁：ShellVisible P95 <= 2.0 秒；正常复用 Usable P50 <= 8 秒、P95 <= 15 秒；DSH create-to-ready P95 <= 5 秒；Fast P95 <= 1.5 秒。只有 DSH 指标可由现有 2.4 至 2.9 秒基线直接支撑；其余三项是优化后新指标，当前属于工程估算。阶段 0 必须用最小 Shell 原型和拟定 Fast 检查微基准测出可达下界，再在生产实现前冻结或调整一次；阶段 4/6 负责正式实现确认，不能在实现后为通过测试下调。

## 5. 预计改动落点

实际名称以阶段 0 读取后的代码为准。不得为了匹配本表编造接口。

| 文件/区域 | 计划职责 |
| --- | --- |
| `src/DeepSeekHarnessDesktop/App.xaml.cs` | 最小同步启动、ShellVisible 计时、异步初始化所有权和退出等待 |
| `Models/ManagedRuntimeModels.cs` 或新模型 | Fast/Full、校验 key/result、证明、失效原因和后台状态 |
| `Models/DependencyDiagnosticsResult.cs` | 区分共享托管运行时快照与可选外部诊断状态 |
| `Services/Abstractions/IManagedRuntimeStore.cs` | 显式 Fast/Full、证明读写/失效和变更检查契约 |
| 新校验协调器抽象/实现 | 同 key single-flight、提升、取消隔离、结果失效和 Dispose |
| `Services/ManagedRuntimeStore.cs` | Full 重用现有严格 hash；实现 Fast、原子证明和稳定扫描判定；把 Shell 前同步目录创建/枚举/清理移入异步初始化或延迟解析 |
| `Services/ManagedRuntimeProvisioner.cs` | 消费共享结果，删除无条件重复 Full，保持重建预算 |
| `Services/DependencyDiagnosticsService.cs` | 从窗口关键路径移除，托管诊断消费共享结果，外部版本延迟加载 |
| `Services/HarnessLifecycleCoordinator.cs` | 接入共享校验、后台失败、gate/generation 和 Owned 恢复 |
| `Services/DshCommandResolver.cs` | 只消费已确认租约，不自行检查或发现运行时 |
| Code WebView2 服务与宿主 View | 可并行初始化、一次导航和 Dispatcher 约束 |
| `ViewModels/InstallationGuideViewModel.cs` | 真实启动/后台验证状态、耗时、失败和命令可用性 |
| `Views/MainWindow.xaml*` / 安装引导 | Shell 优先状态与 DPI/焦点检查；仅必要的小范围布局调整 |
| UnitTests | 策略、共享、证明、状态、EventId、取消和竞态 |
| IntegrationTests/TestHarness | 真实文件/进程、异常退出、后台失败和一次遍历证据 |
| `eng/Measure-StartupPerformance.ps1` | 多样本启动、结构化完成信号、P50/P95 和环境报告 |
| `eng/Verify-Release.ps1` | 性能报告及阈值门禁，不削弱现有运行时供应链门禁 |
| 开发/详细设计/安装/验证文档 | 威胁模型、启动口径、用户预期和带日期证据 |

不得修改 `output/`、`artifacts/`、`bin/`、`obj/`、`TestResults/` 作为源码。测试不得读取用户真实 LocalAppData 运行时或结束非测试创建的进程。

## 6. 分阶段开发计划

### 6.1 阶段 0：基线、调用图与 ADR 硬门禁

工期：2.5 至 4 个工作日。

| 编号 | 任务 | 输出 |
| --- | --- | --- |
| P06-001 | 在完整 0.7.2 Release、从受测 manifest 独立输出实际文件/字节数的运行时和 Defender 开启条件下采集至少 20 次正常启动 | 原始样本、P50/P95、磁盘 I/O |
| P06-002 | 分别记录 ProcessEntry、设置、诊断第 1 次 Full、WebView2、Provisioner 第 2 次 Full、进程、HTTP 和 Usable | 火焰/时序基线 |
| P06-003 | 画出 App、诊断、WebView、Provisioner、Lifecycle 的调用和取消依赖图 | 可审查时序图 |
| P06-004 | 冻结 Fast 证明 schema、存储、原子性、ACL、NTFS/非 NTFS 和同用户威胁模型 | ADR-Startup-Validation |
| P06-005 | 实测 USN/目录身份等增量证据在 Win10/11 普通用户权限下的可用性；不可用时冻结 fail-closed fallback | 平台证据与负向结果 |
| P06-006 | 冻结阻塞 Full/后台 Full 触发表、7 天策略、异常退出语义和后台失败动作 | 状态/恢复 ADR |
| P06-007 | 冻结 EventId、operation id、性能测量完成信号和基准机；确认门禁阈值 | 计时契约 |
| P06-008 | 确认 WebView2 与 DSH 初始化可并行边界、导航条件和 Dispatcher 所有权 | 并发 ADR |
| P06-009 | 记录当前 Unit/Integration/Release 门禁、AGENTS/CLAUDE 一致性与 0.7.2 行为基线 | 阶段 0 记录 |
| P06-010 | 建立不改变生产默认路径的最小 Shell 测量原型：旁路诊断等待、延迟有 I/O 的 Store 构造，测 ShellVisible 下界 | 原型样本与 P95 |
| P06-011 | 对拟定 Fast 检查集合建立只读微基准，记录冷/热缓存、Defender 开启时的下界；不得接成正式绕过 Full 的启动路径 | Fast 可行性报告 |
| P06-012 | 将后台 Full 结果提交到 lifecycle 的契约冻结到方法签名级：runtime key、generation、Owned/External、operation token、结果与失效语义 | 接口草案与顺序图 |
| P06-013 | 冻结逐交付批次版本策略：0.7.x 内部兼容准备、首次完整功能收口为 0.8.0、后续修复从 0.8.1 递增 | 版本阶梯 |

硬门禁：P06-004 至 P06-008、P06-010 至 P06-013 未评审通过不得实现跨启动 Fast 缓存或异步窗口重排。若无法在不接受过度完整性风险的前提下建立跨启动 Fast 证明，只暂停并重新规划阶段 2 的持久证明部分及依赖它的阶段 5 周期后台 Full；阶段 3 仍实施同进程 Full single-flight，阶段 4 仍实施 Shell 优先和独立初始化并行。不得因 Fast 不可行而放弃这两项独立收益，也不得直接信任时间戳。

### 6.2 阶段 1：启动计时模型与可观测性

工期：1 至 2 个工作日。

| 编号 | 任务 | 输出 |
| --- | --- | --- |
| P06-101 | 先写阶段事件顺序、单调耗时、operation id 和失败字段测试 | 计时契约测试 |
| P06-102 | 实现窄职责启动计时上下文，使用 `TimeProvider`/单调时间 | 可注入模型 |
| P06-103 | 在 App、校验、WebView、进程和 HTTP 边界写稳定 EventId | 结构化日志 |
| P06-104 | 记录验证模式、是否共享、files/bytes、runtime id 和恢复次数 | 可定位性能日志 |
| P06-105 | 扩展脱敏/限长测试，证明 operation id 和性能字段不携带路径或凭据 | 隐私证据 |
| P06-106 | 保证日志失败不阻止窗口显示或改变 lifecycle 结果 | 故障注入测试 |

完成条件：不改变当前启动语义时，日志已经能准确还原每个阶段和两次 Full，为后续改动提供同口径对比。

### 6.3 阶段 2：校验级别、证明与 Store

工期：2.5 至 4 个工作日。

| 编号 | 任务 | 输出 |
| --- | --- | --- |
| P06-201 | 定义 validation level/key/result/attestation/invalidation reason，未知 schema fail closed | Models 与解析测试 |
| P06-202 | 把现有完整遍历明确收口为 Full，保持 manifest 全文件、额外项和安全属性行为 | Full 回归测试 |
| P06-203 | 先写证明缺失、截断、旧策略、错 runtime/manifest/compat/root identity/关键入口的失败测试 | Fast 负向矩阵 |
| P06-204 | 实现 Fast 检查，不遍历或读取全部 manifest 文件正文 | Fast Store 路径 |
| P06-205 | Full 成功后用临时文件 + replace 原子提交证明；取消/失败/扫描变化不得提交 | 原子性测试 |
| P06-206 | 实现阻塞 Full 触发表和 7 天周期边界，使用可注入 `TimeProvider` | 策略测试 |
| P06-207 | 在启动开始写未清洁标记、正常显式退出原子提交清洁状态；崩溃样本触发下次 Full | 异常退出测试 |
| P06-208 | 对 Full 扫描前后运行时变化进行检测和一次有界重试 | 稳定扫描测试 |
| P06-209 | 保持 private root/path/reparse/lease/compat 边界；非 NTFS 或增量证据断档按 ADR 转 Full | Windows 集成测试 |

完成条件：`AC-STARTUP-VAL-002` 至 `007` 通过；Fast 不以任何单个弱元数据替代此前 Full，现有托管运行时完整性测试无回归。

### 6.4 阶段 3：进程内 single-flight 与调用方去重

工期：1.5 至 2.5 个工作日。

| 编号 | 任务 | 输出 |
| --- | --- | --- |
| P06-301 | 先写诊断/Provisioner 并发、Fast/Full 提升、等待方取消和 key 变化测试 | single-flight 测试 |
| P06-302 | 实现校验协调器，以 runtime/manifest/policy key 共享进行中任务 | 共享服务 |
| P06-303 | Full 成功满足 Fast；Fast 进行中收到 Full 时按 ADR 提升或串行一次 Full，不重复 Full | 提升语义测试 |
| P06-304 | 调用方取消只停止等待，共享 operation 由应用 CTS 管理；Dispose 取消并等待 worker | 资源/取消测试 |
| P06-305 | DependencyDiagnostics 改为消费共享快照，外部 Node/DSH 版本按需/后台加载 | 无关键路径外部探测 |
| P06-306 | Provisioner 消费共享校验并删除同 key 无条件第二次遍历，仍返回运行租约 | 生命周期契约测试 |
| P06-307 | 修复、回退、runtime/key/generation 变化时精确失效，不把失败永久缓存 | 失效测试 |

完成条件：同一进程同一 key 的 Full 遍历计数为 1，取消和失败不会污染下一 operation，`ManagedRuntimeLease` 行为不变。

### 6.5 阶段 4：Shell 优先与异步初始化

工期：2 至 3 个工作日。

| 编号 | 任务 | 输出 |
| --- | --- | --- |
| P06-401 | 为 App 启动抽取可测试初始化编排边界，保留 `async void OnStartup` 的顶层异常处理 | 启动编排测试 |
| P06-402 | 将完整诊断移到 `Show()` 后，主窗口首次 ContentRendered 提交 ShellVisible | 可见时序测试 |
| P06-403 | 确保最小设置加载失败有默认/错误策略，不让部分构造的 DI 容器进入运行态 | 设置失败测试 |
| P06-404 | 根据 P06-008 并行 WebView 环境和 DSH 分支，二者成功后只导航一次 | TCS 顺序矩阵 |
| P06-405 | Window/ViewModel 从第一帧显示真实初始化状态，后台错误通过绑定状态处理 | UI 状态测试 |
| P06-406 | 关闭到托盘、再次激活、次实例通知和明确退出保持现有语义 | 单实例/托盘回归 |
| P06-407 | 检查 Dispatcher、焦点、最小窗口、100/125/150% DPI 和 WebView2 覆盖 | 带日期人工记录 |
| P06-408 | 审计 ShellVisible 前解析的全部服务构造器；延迟 `ManagedRuntimeStore` 的目录创建、安全检查和 staging 枚举等同步 I/O | 构造器 I/O 清单与测试 |

完成条件：ShellVisible 不等待运行时 Full、全局版本探测或 DSH；两个初始化分支以任意顺序完成均无跨线程访问、重复导航或未观察异常。

### 6.6 阶段 5：后台 Full、失效与有界恢复

工期：2.5 至 4 个工作日。

| 编号 | 任务 | 输出 |
| --- | --- | --- |
| P06-501 | 建立周期 Full 调度器或协调器内等价职责，Usable 后、I/O 并发 1、可取消/Dispose | 后台 worker 测试 |
| P06-502 | 周期到期仅在 Fast 全通过时后台 Full，其他触发仍在进程前阻塞 Full | 触发表测试 |
| P06-503 | 后台失败原子作废证明，并携带 runtime key/generation 提交 lifecycle | 失败事件模型 |
| P06-504 | coordinator gate 下停止匹配 Owned 树并执行一次重建/一次回退，不递归 Start | Owned 恢复集成测试 |
| P06-505 | RunningExternal、过期 generation、用户 Stop/Restart/退出不被旧失败结果干扰 | 所有权/竞态测试 |
| P06-506 | 后台 Full 与解包、清理、回退和运行租约协调，不删除或替换在用版本 | lease/cleanup 测试 |
| P06-507 | 失败、取消、应用退出等待和日志异常下释放 CTS、事件、流和 worker | 资源测试 |

完成条件：`AC-STARTUP-REC-*` 全部通过；后台验证不形成无限循环，不扩大 External 控制范围。

### 6.7 阶段 6：性能工具、回归与发布门禁

工期：2 至 3 个工作日。

| 编号 | 任务 | 输出 |
| --- | --- | --- |
| P06-601 | 实现 `Measure-StartupPerformance.ps1`，检查 `$LASTEXITCODE`、使用 `-LiteralPath` 和受控 output 子目录 | 基准脚本 |
| P06-602 | 用结构化 Usable/Failed 信号和有界等待收集样本，不用固定 sleep；每次样本正常退出 | 可重复运行器 |
| P06-603 | 输出环境、版本、包 hash、原始样本、P50/P95/max/失败数和阶段分解 | 机器可读 + Markdown 报告 |
| P06-604 | 分组执行正常热启动、重启后冷缓存、首次准备、强制 Full、后台 Full 和损坏恢复 | 场景报告 |
| P06-605 | 加入遍历/读取计数断言：正常 Fast 不读取全部正文，同进程 Full 至多一次 | 性能回归测试 |
| P06-606 | 运行 Unit/Integration，覆盖立即退出、HTTP 身份、Job、generation、WebView 和脱敏回归 | 完整测试证据 |
| P06-607 | 将正常复用硬阈值加入 `Verify-Release.ps1` 或独立必跑门禁；保留现有确定性运行时验证 | Release 门禁 |
| P06-608 | 若采用基准专用控制信号，限制在不分发的工程构建，并断言正式 Release 默认无该信号/入口；优先使用结构化日志和现有正常退出 | 生产面负向测试 |

脚本不得删除用户 LocalAppData、关闭安全软件、结束非脚本创建的进程或使用不受控 glob。若基准需要专用退出信号，阶段 0 必须选择无任意路径/命令输入、仅用于工程测量且能正常执行应用退出清理的方案。

完成条件：参考机达到所有性能阈值；任何丢失事件、启动失败或无法正常退出的样本计为失败，不能从 P95 中删除。

### 6.8 阶段 7：文档、新设备与发布验收

工期：1 至 1.5 个工作日。

| 编号 | 任务 | 输出 |
| --- | --- | --- |
| P06-701 | 更新开发、详细设计和安装说明，区分 ShellVisible、正常复用和首次准备 | 同步文档 |
| P06-702 | 记录 Fast 证明能力和限制、阻塞/后台 Full 触发表、恢复预算 | 安全说明 |
| P06-703 | 在至少一台无 Node/npm 的非开发 Win10/11 x64 新设备验证 Release | 新设备记录 |
| P06-704 | Defender 开启执行 20 次正常复用，另记冷缓存/首次准备/周期 Full | 最终性能报告 |
| P06-705 | 检查 ZIP 内容、NOTICE、runtime hash、真实 DSH HTTP smoke 和端口释放 | 发布证据 |
| P06-706 | 复核每个已落地交付批次均即时递增 AppVersion/manifest/VERSION_HISTORY，完成 0.8.0 收口三处一致，并比较 AGENTS/CLAUDE | 全程版本审计 |
| P06-707 | 新增带日期 validation 记录，不改写历史验证结果 | 阶段验收记录 |

完成条件：新设备满足安全与性能验收，完整发布门禁通过，用户文档不承诺首次准备也在 8 秒内完成。

## 7. 阶段依赖与实施策略

```text
阶段 0 ADR/基线/原型
  -> 阶段 1 计时
       |-> 轨道 A：阶段 2 Fast/Full Store ----|
       |                                      |-> 阶段 5 后台 Full/恢复
       |-> 轨道 B：阶段 3 single-flight       |
                         -> 阶段 4 Shell/并行初始化

轨道 A+B 完整方案 -> 阶段 6 性能与发布门禁 -> 阶段 7 新设备验收
轨道 A 不可行   -> 保留轨道 B -> 重新冻结阶段 6 的“无跨启动 Fast”指标与验收
```

- 阶段 1 可在 P06-004 至 P06-008、P06-010 至 P06-012 冻结后与阶段 2/3 的测试准备部分重叠，但总工期不依赖该压缩。
- 阶段 3 的 Full single-flight 不依赖跨启动 attestation；阶段 4 的 Shell 优先也不依赖 Fast。轨道 A 被安全评审否决时，两者继续交付，并重新制定不虚构 Fast 的性能目标。
- 每阶段先提交能失败的测试，再实现最小行为，再运行相关回归；不要一次同时重写 Store、App 和 Lifecycle。
- 性能优化提交必须同时给出同口径前后数据；没有结构化计时证据的“感觉更快”不接受。
- 每个独立交付批次在同一改动中递增版本三处并追加真实历史；不允许等阶段 7 才集中升版。0.8.0 只用于完整兼容新功能首次收口，不能提前标记未完成阶段为已交付。
- 任何阶段发现安全边界需要放宽，停止该阶段并单独评审，不以性能指标授权网络、Shell、进程或文件权限扩张。
- 不提交或推送 Git，除非用户在实施阶段明确授权。

## 8. 测试矩阵

| 场景 | 期望 | 主要层级 |
| --- | --- | --- |
| 无证明首次启动 | 阻塞 Full，成功后原子证明 | Unit + Integration |
| 正常清洁退出后重启 | Fast，正常关键路径不全量读取 | Integration + Perf |
| runtime/manifest/policy 改变 | 证明失效，阻塞 Full | Unit |
| 关键入口或普通文件改变 | Fast 发现或变更证据转 Full，最终拒绝/修复 | Windows Integration |
| 文件系统变更检查点断档 | fail closed 到 Full | Windows Integration |
| 非 NTFS 私有根 | 按 ADR Full/fail，不假装增量可信 | Windows Integration |
| 上次异常退出 | 下次进程前 Full | Integration |
| 周期恰好 7 天 | 后台 Full，不重置到期 | Unit |
| 诊断 + Provisioner 并发 Full | 一次遍历、两个结果 | Unit |
| Fast 进行中收到 Full | 按冻结提升语义，仅一个 Full | Unit |
| 单个等待方取消 | 其他等待方完成 | Unit |
| Shell 后 DSH/WebView 任意顺序完成 | 只导航一次 | Unit/WPF |
| 后台 Full 失败 + Owned Running | 作废、停止 Job、一次恢复 | Integration |
| 后台 Full 失败 + External Running | 不停止外部进程 | Integration |
| 后台失败 + Stop/Restart/Exit | 新 generation 胜出 | Unit + Integration |
| Full 扫描中修改文件 | 不提交成功证明，最多重试一次 | Integration |
| Defender 开启 20 次正常复用 | 达到 P50/P95，零静默剔除 | Release Perf |
| 冷缓存/首次准备 | 单独报告，不污染正常样本 | Manual/Perf |
| 正式 Release 接收基准专用控制信号 | 默认无入口或明确拒绝，不改变退出/运行状态 | Release negative |

## 9. 验收追踪

| Prompt AC | 计划任务 |
| --- | --- |
| `AC-STARTUP-PERF-001` | P06-001/002/010、P06-401/402/408、P06-601 至 607 |
| `AC-STARTUP-PERF-002` | P06-001/002/010/011、P06-401/404、P06-601 至 607 |
| `AC-STARTUP-PERF-003` | P06-002、P06-103、P06-603/604/607 |
| `AC-STARTUP-PERF-004` | P06-011、P06-204、P06-603 至 607 |
| `AC-STARTUP-PERF-005` | P06-001/002、P06-604、P06-704 |
| `AC-STARTUP-PERF-006` | P06-006、P06-501/502、P06-604、P06-704 |
| `AC-STARTUP-VAL-001` | P06-301 至 306、P06-605 |
| `AC-STARTUP-VAL-002` | P06-004/005、P06-201/203/204、P06-209 |
| `AC-STARTUP-VAL-003` | P06-004/005、P06-203/204、P06-209 |
| `AC-STARTUP-VAL-004` | P06-006、P06-206/207、P06-502 |
| `AC-STARTUP-VAL-005` | P06-006、P06-206、P06-501/502 |
| `AC-STARTUP-VAL-006` | P06-205、P06-208 |
| `AC-STARTUP-VAL-007` | P06-004/005、P06-203/204、P06-209 |
| `AC-STARTUP-REC-001` | P06-012、P06-503/504/506 |
| `AC-STARTUP-REC-002` | P06-505、P06-606 |
| `AC-STARTUP-REC-003` | P06-307、P06-503/505、P06-606 |
| `AC-STARTUP-REC-004` | P06-304、P06-505/507、P06-606 |
| `AC-STARTUP-SEC-001` | P06-209、P06-504 至 506、P06-606、P06-705 |
| `AC-STARTUP-SEC-002` | P06-004/005、P06-608、P06-702 |
| `AC-STARTUP-UI-001` | P06-401 至 406、P06-408 |
| `AC-STARTUP-UI-002` | P06-405/407/408 |
| `AC-STARTUP-LOG-001` | P06-101 至 104、P06-603 |
| `AC-STARTUP-LOG-002` | P06-104 至 106、P06-603 |
| `AC-STARTUP-BENCH-001` | P06-007/010/011、P06-601 至 608、P06-704 |
| `AC-STARTUP-BENCH-002` | P06-007、P06-603/604/608、P06-704 |

## 10. 必测竞态与故障注入点

1. App 退出发生在 ShellVisible 前、后和两条异步初始化分支之间。
2. 单实例通知与主窗口首次 Show/托盘隐藏同时发生。
3. Fast 完成、Full 提升和等待方取消以全部顺序交错。
4. Full 最后一个文件完成与 runtime key/manifest 改变同时发生。
5. 证明临时文件 flush 后、replace 前取消或模拟崩溃。
6. 正常退出清洁标记与后台 Full 失败同时提交。
7. WebView Ready、DSH HTTP Ready、Owned 立即退出以全部顺序完成。
8. 后台 Full 失败与 Stop、Restart、退出、健康丢失同时发生。
9. 后台失败属于旧 runtime/generation，新启动已经成功。
10. Full 扫描文件时文件被截断、替换、锁定或变为 reparse point。
11. 清理旧 runtime 与仍持有 ManagedRuntimeLease 的 Owned 进程并发。
12. 日志写入失败或性能事件订阅者抛异常时主启动继续受控失败/成功。

所有竞态使用 `TaskCompletionSource`、fake stream/store、`TimeProvider` 和有限超时；禁止用长时间 `Thread.Sleep` 或生产 7 天墙钟等待。

## 11. 风险与控制

| 风险 | 控制 |
| --- | --- |
| Fast 证明被误解为与每次 Full 同等强度 | ADR 明确威胁模型；关键不符 fail closed；周期 Full；不宣传超出能力的防篡改 |
| 时间戳/目录 identity 漏掉子文件内容变化 | 不单独信任；优先增量变更证据 + 关键 hash + 周期 Full；证据断档转 Full |
| 异步 Show 导致未构造服务或错误闪烁 | 最小 Shell 依赖清单、单一初始化编排器、第一帧真实状态 |
| WebView 与 DSH 并行引入双导航/跨线程 | 双完成 gate、generation、Dispatcher 和顺序矩阵 |
| single-flight 被某个页面取消 | 等待 token 与共享 operation token 分离，应用 CTS 统一拥有 |
| 后台 Full 抢占磁盘拖慢 UI | Usable 后调度、并发 1、低优先级/节流、记录 I/O 与交互验证 |
| 后台损坏结果误杀外部或新进程 | runtime key + generation + Owned 检查，Lifecycle gate 内提交 |
| 异常退出导致频繁阻塞 Full | 精确原子清洁标记、基准正常退出；保留安全优先，不静默跳过 |
| Defender/企业杀毒导致基准波动 | 记录安全软件、20 次 P50/P95、失败样本、冷/热分组 |
| 性能脚本用强杀污染下次样本 | 结构化完成信号和受控正常退出；强杀样本只用于异常退出测试 |
| 旧路径数据无法证明新 Shell/Fast 阈值 | 阶段 0 最小 Shell 原型和 Fast 微基准先测下界；只在生产实现前冻结一次 |
| lifecycle 后台失败接口到阶段 5 才发现不适配 | P06-012 在阶段 0 冻结方法签名、token、generation、Owned/External 和失效语义 |
| 基准控制信号意外进入产品面 | 优先外部日志；专用信号仅工程构建；P06-608 断言正式 Release 无入口 |
| 证明 schema 升级与旧 runtime 不兼容 | policy version 进入 key，旧证明转 Full 后重建 |
| Full 扫描期间文件变化产生错误成功 | 前后变更检查点/稳定句柄，一次有界重试，失败不写证明 |
| 只优化开发输出，正式包仍慢 | 基准只接受完整 Release ZIP 和真实托管运行时 |

## 12. 验证命令与发布门禁

阶段开发常用命令：

```powershell
dotnet restore DeepSeekHarnessDesktop.sln
dotnet build DeepSeekHarnessDesktop.sln -c Debug --no-restore -m:1
dotnet test tests/DeepSeekHarnessDesktop.UnitTests/DeepSeekHarnessDesktop.UnitTests.csproj -c Release --no-restore -m:1
dotnet test tests/DeepSeekHarnessDesktop.IntegrationTests/DeepSeekHarnessDesktop.IntegrationTests.csproj -c Release --no-restore -m:1
```

性能与最终发布命令在实施后以脚本真实参数为准，目标形态：

```powershell
.\eng\Measure-StartupPerformance.ps1
.\eng\Verify-Release.ps1
```

最终门禁同时要求：

- Debug/Release 警告即错误构建通过。
- Unit/Integration 全部通过，进程、端口、临时目录清理完成。
- 两次空工作目录托管运行时 ZIP 字节一致，Node/DSH/NOTICE/lock/hash 正确。
- 正常复用 20 次达到 ShellVisible、Usable、Fast 和 DSH Ready 阈值。
- 首次准备、冷缓存、后台 Full、损坏恢复有独立报告。
- 正式 Release 默认不包含或拒绝基准专用控制/退出信号。
- Release ZIP 内容、版本元数据和安装说明一致。
- `git diff --check` 通过；`AGENTS.md` 与 `CLAUDE.md` 字节一致。

## 13. 完成定义

1. 阶段 0 ADR、旧路径基线、最小 Shell 原型和 Fast 微基准证据完整，阈值在生产实现前冻结且未在实现后放宽。
2. Prompt 中全部 `AC-STARTUP-*` 可追溯且通过。
3. 主窗口优先显示，正常复用不执行两次或每次完整文件遍历。
4. Fast 只复用已成功 Full 的证明，所有冻结失效条件均 fail closed。
5. 后台 Full 失败对 Owned/External、generation、恢复预算和退出行为正确。
6. 性能日志可回答每次启动慢在哪一阶段并保持脱敏。
7. Release 基准达到 P50/P95，首次准备和冷缓存数据没有被混入。
8. 无 npm/npx、Job Object、运行租约、loopback 身份、WebView2 同源和供应链门禁无回归。
9. 新设备、Defender、DPI/焦点和托盘人工验证有带日期记录。
10. 每个交付批次的版本三处、开发/设计/安装文档和 AGENTS/CLAUDE 一致，未提交生成物。
