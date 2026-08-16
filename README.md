# DeepSeek Harness Desktop

DeepSeek Harness Desktop 是面向 Windows 的 .NET 桌面宿主，为 DeepSeek Harness 提供一个开箱即用的图形化外壳。它集中处理工作目录选择、`dsh web` 进程启动与停止、服务状态探测和页面承载，让使用体验更接近 Codex CLI 与 Claude Code 等开发工具，减少手工启动和管理本地服务的操作。

本项目不重新实现 DeepSeek Harness 的会话、模型、工具、审批、工作区或配置功能。这些能力继续由官方 DeepSeek Harness Web UI 提供，桌面宿主只负责 Windows 集成和本地生命周期管理。

## 技术实现

- C# / .NET 8
- WPF 桌面界面
- Microsoft Edge WebView2
- CommunityToolkit.Mvvm
- Microsoft.Extensions.DependencyInjection
- Serilog

应用面向 Windows 10/11 x64，发布为 self-contained、single-file ZIP，不采用 Electron。

## 主要能力

- 选择并记住 DeepSeek Harness 工作目录。
- 启动、停止和重启当前应用创建的 `dsh web` 进程。
- 识别并连接已有的外部 DeepSeek Harness 服务，但不会停止外部进程。
- 探测服务健康状态和 DeepSeek Harness 身份。
- 在 WebView2 中承载官方 Web UI，并限制主导航到已确认的本机服务。
- 提供运行状态、诊断信息、本地日志、托盘和账户余额查询。

## 使用与开发

- [安装与使用说明](docs/installation.md)
- [开发文档](docs/deepseek-harness-desktop-development.md)
- [详细设计](docs/deepseek-harness-desktop-detailed-design.md)

本项目是 DeepSeek Harness 的独立桌面宿主，不代表 DeepSeek 官方产品。
