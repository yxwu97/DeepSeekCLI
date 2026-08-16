# 阶段 0：技术预验证记录

## 环境

- 验证日期：2026-08-15
- 操作系统：Windows 10.0.26200 x64
- .NET SDK：10.0.201（目标框架 `net8.0-windows`）
- .NET Desktop Runtime：8.0.25
- Node.js：24.15.0
- npm：11.12.1
- WebView2 Evergreen Runtime：151.0.4129.86（API 初始化实测）
- DSH：`@deepseek-ai/dsh@0.1.0-rc.6`

## 可重复验证

构建验证工程：

```powershell
dotnet build eng/Phase0Validation/Phase0Validation.csproj -c Release
```

自动初始化 WebView2 并在成功后关闭窗口：

```powershell
dotnet eng/Phase0Validation/bin/Release/net8.0-windows/win-x64/DeepSeekHarnessDesktop.Phase0Validation.dll --webview-smoke
```

启动锁定版本 DSH 后执行非交互验证：

```powershell
eng/Phase0Validation/bin/Release/net8.0-windows/win-x64/DeepSeekHarnessDesktop.Phase0Validation.exe --self-test
```

直接运行验证 EXE 可检查 WebView2 初始化；焦点位于页面时按 `F5`，顶部状态应变为快捷键路由通过。

## 验证结果

| 编号 | 验证项 | 状态 | 证据 |
|---|---|---|---|
| DEV-001 | WPF `.NET 8` 与锁定 WebView2 构建运行 | 通过 | Release 构建 0 警告/0 错误；Runtime `151.0.4129.86` 初始化成功 |
| DEV-002 | WebView2 焦点内快捷键路由 | 通过 | 聚焦 WebView2 HWND 后发送真实 F5，WPF `PreviewKeyDown` 收到并标记处理 |
| DEV-003 | 特殊字符和 Unicode `.cmd` 路径 | 通过 | 空格、`&`、括号和中文脚本路径输出 `PHASE0_CMD_OK` |
| DEV-004 | Job Object 清理多层进程 | 通过 | 关闭 `KILL_ON_JOB_CLOSE` Job 后父、子进程均在 5 秒内退出 |
| DEV-005 | DSH 页面双特征 | 通过 | rc.6 根页面同时包含标题和 `window.__DSH_BOOT__` |

## 结论

五项验证均通过，可以进入阶段 1。首次 `npx -y` 从当前 npm registry 下载时无需交互，完成后输出 `dsh web: http://127.0.0.1:3080`；验证结束后服务已停止且端口释放。

阶段 0 发现锁定版 WPF WebView2 不公开 Controller 属性，而是在控件内部把 `AcceleratorKeyPressed` 转发为 WPF `PreviewKeyDown`。详细设计 §17.4 已据此修订，技术栈和总体架构无需改变。
