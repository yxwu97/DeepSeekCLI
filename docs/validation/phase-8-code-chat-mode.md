# 阶段 8：Code/Chat 模式切换验证记录

## 验证信息

- 验证日期：2026-08-17
- 应用版本：Desktop `0.3.0`
- 平台：Windows 10.0.26200 x64
- WebView2 Runtime：`151.0.4129.86`
- 范围：模式与命令路由、Code/Chat 导航策略、双 controller/profile、状态保持、清除 API、构建测试和发布包

## 自动化验证

在计划 01 的 `0.2.0` 冻结基线上执行：

```powershell
dotnet build DeepSeekHarnessDesktop.sln -c Debug --no-restore -m:1
dotnet build DeepSeekHarnessDesktop.sln -c Release --no-restore -m:1
dotnet test DeepSeekHarnessDesktop.sln -c Release --no-restore -m:1
dotnet eng/Phase0Validation/bin/Release/net8.0-windows/win-x64/DeepSeekHarnessDesktop.Phase0Validation.dll --chat-webview-smoke
```

结果：

- Debug/Release 构建为 0 warning、0 error。
- UnitTests：138/138 通过；IntegrationTests：18/18 通过；均为 0 跳过、0 失败。
- URI 矩阵覆盖精确 Chat origin、HTTP、显式 443、非默认端口、UserInfo、尾点、IDN 相似字符、后缀域名、危险协议和外链决策。
- ViewModel 测试确认启动默认 Code、Chat 只请求一次初始化、刷新按模式路由、Chat 下重启不可执行、清除必须确认，且 Chat 操作不调用 Harness 生命周期。

完整 `eng\Verify-Release.ps1` 门禁通过：

- 报告：`output/validation/release-gate-0.3.0-win-x64.json`
- ZIP：`output/DeepSeekHarnessDesktop-0.3.0-win-x64.zip`
- ZIP SHA-256：`17222F1DC6959D301B7478695B29649566F6EC3768168E86B4ABDFABA8839516`
- EXE FileVersion 为 `0.3.0.0`，ProductVersion 为 `0.3.0`。
- ZIP 仅包含 `DeepSeekHarnessDesktop.exe` 与从 `docs/installation.md` 复制的 `README.md`，不含 profile、缓存、日志、测试数据或凭据。

## 真实 WebView2 Runtime 验证

隔离验证程序使用唯一临时用户数据目录，未访问真实 `%LOCALAPPDATA%\DeepSeekHarnessDesktop\WebView2`：

- 一个共享 `CoreWebView2Environment` 成功创建默认 Code controller 和固定 `Chat` profile controller，profile 路径互相隔离。
- Chat profile 为非 InPrivate，密码保存和常规自动填充设置可写。
- Chat 控件在 `Visible -> Collapsed -> Visible` 后保留本地可控页面的未提交输入。
- `ClearBrowsingDataAsync(AllProfile)` 成功完成；临时 profile 在控制器释放后清理。
- 自动 F5 前台注入在当前会话未获得焦点，未作为失败的产品结论；既有阶段 0/4 记录已有真实 HWND F5 通过证据，本轮双页面 F6 和多 DPI 仍需人工复核。

## 安全边界

- Code 仍只加载健康检查确认后的 loopback DSH 同源页面。
- Chat 只内嵌 `https://chat.deepseek.com:443`；没有使用 `*.deepseek.com` 或其他远程通配符。
- 其他安全 HTTP(S) 外链交给受控系统浏览器；危险协议拒绝。
- Chat 权限默认拒绝、下载默认取消；正式应用不注册宿主对象、不注入脚本，也不读取 Cookie、密码、Token、DOM、消息、站点存储或网络正文。
- 清除只调用 Chat profile 的受支持 API，不直接删除用户 profile 目录，不影响 Code 或 DSH。

## 未完成的外部验收

当前 Codex 浏览器通道没有可用实例，且本次验证没有专用测试账号，因此以下计划门禁未标记通过：

- 官方 Chat 首页、登录、验证码、退出、会话和新窗口的完整顶层 origin 清单。
- 登录是否始终停留在当前精确 origin；若跳转到其他 origin，当前最小 allowlist 会外开且不会把系统浏览器会话带回应用 profile。
- 有效 Chat 会话跨应用退出、应用升级和 Windows 重启的持久化，以及网页主动退出行为。
- 原生密码提示的接受、拒绝和企业策略禁用三种结果。
- 官方文件选择、必要下载和 `ProcessFailed` 的真实事件顺序。
- Windows 10/11 的 125%、150%、200% DPI，820x600 最小窗口、托盘恢复和第二实例激活。

尝试通过 Windows 应用控制打开发布 EXE 做本机布局复核时，启动授权超时且应用未启动；已确认没有遗留正式应用窗口或由此产生的 DSH/Chat 操作。因此本轮不把可见布局标记为已验收。

这些项目属于计划 02 阶段 0/5/6 的发布验收阻塞项。代码保持最小安全行为，不以通配符、Cookie 复制、DOM 注入或系统浏览器登录冒充降级方案。
