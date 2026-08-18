# DeepSeek Harness Desktop

DeepSeek Harness Desktop 是面向 Windows 10/11 x64 的轻量 WPF 宿主。它负责选择工作目录、启动和管理 `dsh web`、验证本机服务身份，并通过 WebView2 展示官方 DeepSeek Harness Web UI。

本项目不重新实现 Harness 或 DeepSeek Chat 的会话、模型、工具、审批、登录和消息功能。

## 技术实现

- C# / .NET 8
- WPF + CommunityToolkit.Mvvm
- Microsoft Edge WebView2
- Microsoft.Extensions.DependencyInjection
- Serilog

发布包采用 framework-dependent 模式，不携带 .NET、Node.js 或 DSH。目标机需安装 .NET 8 Desktop Runtime；应用启动后会依次检查 WebView2、Node.js、npx 和 DSH，并按当前缺失项引导安装。

## 主要能力

- 优先启动 PATH 中已安装的 `dsh.cmd`，其次直接复用当前用户 npx 缓存中已准备好的固定版本 DSH。
- 只有没有可复用 DSH 时，才经用户确认通过 npx 下载并启动 `@deepseek-ai/dsh@0.1.0-rc.6`。
- 一个主操作按 WebView2、Node.js、npx、DSH 的顺序处理环境缺失。
- 只停止或重启本程序创建的 Owned DSH 进程树；外部 DSH 仅连接。
- 使用 Job Object、串行生命周期和 generation 校验处理退出、取消和重启竞态。
- 只接受 loopback DSH 服务并验证 HTTP 身份，Code WebView2 保持同源。
- Chat 使用独立 profile，只允许精确官方 HTTPS origin，权限默认拒绝、下载默认取消。
- 配置原子写入，外部日志规范化、限长并脱敏。
- framework-dependent 发布门禁限制 ZIP 不超过 30 MiB、主 EXE 不超过 5 MiB。

## 使用与开发

- [安装与使用说明](docs/installation.md)
- [开发文档](docs/deepseek-harness-desktop-development.md)
- [详细设计](docs/deepseek-harness-desktop-detailed-design.md)

本地开发：

```powershell
dotnet restore DeepSeekHarnessDesktop.sln
dotnet build DeepSeekHarnessDesktop.sln -c Debug --no-restore -m:1
dotnet run --project src/DeepSeekHarnessDesktop/DeepSeekHarnessDesktop.csproj --no-build
```

本项目是 DeepSeek Harness 的独立桌面宿主，不代表 DeepSeek 官方产品。
