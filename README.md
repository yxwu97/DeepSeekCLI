# DeepSeek Harness Desktop

DeepSeek Harness Desktop 是面向 Windows 10/11 x64 的轻量 WPF 宿主。它负责选择工作目录、启动和管理 `dsh web`、验证本机服务身份，并通过 WebView2 展示官方 DeepSeek Harness Web UI。

本项目不重新实现 Harness 或 DeepSeek Chat 的会话、模型、工具、审批、登录和消息功能。

## 技术实现

- C# / .NET Framework 4.8
- WPF + CommunityToolkit.Mvvm
- Microsoft Edge WebView2
- Microsoft.Extensions.DependencyInjection
- Serilog

发布包采用 .NET Framework 4.8 轻量模式，不携带 CoreCLR、Node.js 或 DSH。Windows 11 和已更新的 Windows 10 通常已包含 .NET Framework 4.8；应用启动后会检查 WebView2、Node.js/npm 和可复用 DSH，并按当前缺失项引导处理。

## 主要能力

- 优先复用 PATH 中手动/全局安装的 `dsh.cmd`，其次复用 Desktop 私有安装，再复用严格校验的当前用户 npx 缓存。
- 只有没有可复用 DSH 时，才经用户确认用精确 lockfile 执行一次 `npm ci --omit=dev`；后续直接运行私有固定入口，不再下载。
- 保留固定全局安装和手动 npx 启动入口；Desktop 只复制命令并打开 PowerShell，不自动执行。
- 只停止或重启本程序创建的 Owned DSH 进程树；外部 DSH 仅连接。
- 使用 Job Object、串行生命周期和 generation 校验处理退出、取消和重启竞态。
- 只接受 loopback DSH 服务并验证 HTTP 身份，Code WebView2 保持同源。
- Chat 使用独立 profile，只允许精确官方 HTTPS origin，权限默认拒绝、下载默认取消。
- 配置原子写入，外部日志规范化、限长并脱敏。
- net48 发布门禁限制 ZIP 不超过 30 MiB、主 EXE 不超过 5 MiB，并拒绝 CoreCLR 运行时文件。

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
