# 0.12.1 发布 EXE 启动修复验证

## 原因与修复

0.12.0 正式应用同时注册了 `HttpClient` 和 `IDshBrowserSession`，但使用自动构造的 `HarnessHealthMonitor` 有两个可满足的单参数公开构造函数。容器解析 `MainWindow` 时抛出 `InvalidOperationException`，主窗口尚未显示就以 `APP-E599` 崩溃。

0.12.1 在正式注册处显式选择 `IDshBrowserSession` 构造函数，继续创建专用 loopback HTTP 客户端，保留禁用代理、自动重定向和默认 Cookie 的限制。

## 回归验证

- 新增 `eng/Verify-Startup.ps1`，从发布 ZIP 解压到独立生成目录，运行正式 EXE 的 `--startup-smoke`，限制 30 秒并检查退出码。超时时只结束本脚本创建的进程。
- 此模式使用正式容器、WPF 资源、主窗口和托盘；主窗口完成 `ContentRendered` 后走正式退出和资源清理流程。使用内存默认设置，不加载或保存用户配置，不启动后台诊断、WebView2 或 DSH；如已有实例则以非零码拒绝，避免把第二实例正常退出误判为启动成功。
- 应用 DI 修复前实际运行该检查，得到退出码 `-532462766`，日志确认两构造函数冲突。修复后同一检查通过。
- 执行机记录：2026-09-10 23:37:17 主窗口渲染，23:37:18 正常退出；门禁报告生成于 `2026-09-10T23:37:20+08:00`。

## 完整门禁结果

执行 `dotnet restore DeepSeekHarnessDesktop.sln` 和 `eng/Verify-Release.ps1`，最终结果：

- Debug / Release 构建均无警告、无错误。
- 244 项单元测试、36 项 Windows 集成测试通过。
- 交互式 Code / Chat WebView2 双 profile 验证通过。
- DSH `0.1.5-rc.1` 真实私有安装及二次免下载复用通过。
- 真实 Code WebView2 认证、干净根地址导航和 Cookie 刷新通过。
- 正式 ZIP EXE 主窗口渲染与正常退出检查通过。
- 版本、完整 DSH 锁图、资源 hash、包内容及体积门禁通过；AGENTS / CLAUDE SHA-256 一致。

产物：`output/DeepSeekHarnessDesktop-0.12.1-win-x64.zip`，1,603,051 字节。

SHA-256：`22D32ED59E34DD5E6508BD958A6A2D98D4A87FA69FE8B6A58F05D8E4AF1FB502`。

机器可读报告：`output/validation/release-gate-0.12.1-win-x64.json`。

本次启动 smoke 覆盖窗口显示前的实际生产路径；窗口显示后的真实 DSH / WebView2 由独立门禁覆盖。全新 Windows 用户、不同 Windows 版本及 125% / 150% DPI 仍属于报告列明的外部验证范围。
