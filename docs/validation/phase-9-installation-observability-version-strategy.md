# 阶段 9：安装可观测性与无版本策略验收记录

## 验证信息

- 验证日期：2026-08-17
- 应用版本：Desktop `0.4.0`
- 验证平台：Windows 11 x64 宿主机
- 验证范围：无版本命令、PATH 刷新、配置迁移、安装日志、阶段/总耗时、手动操作、npm 失败分类、发布包和虚拟机前置条件

## 自动化与发布门禁

在仓库根目录执行：

```powershell
.\eng\Verify-Release.ps1
```

结果：

- Debug 和 Release 构建均为 0 warning、0 error。
- UnitTests：154/154 通过，0 跳过，0 失败。
- IntegrationTests：18/18 通过，0 跳过，0 失败。
- WebView2 双 profile、状态保留、profile 清理和快捷键路由交互验证通过。
- 发布报告：`output/validation/release-gate-0.4.0-win-x64.json`。
- 发布包：`output/DeepSeekHarnessDesktop-0.4.0-win-x64.zip`。
- ZIP SHA-256：`27C8CBC199D8E285699E6C8FAADD7523320AA4E821E205A5EC023D4299C08D12`。
- ZIP 只包含 `DeepSeekHarnessDesktop.exe` 和 `README.md`；FileVersion 为 `0.4.0.0`，ProductVersion 为 `0.4.0`。

自动测试覆盖：

- 自动 npx 参数精确为 `-y @deepseek-ai/dsh web`，非默认端口只追加受控数字参数。
- Machine/User/Process PATH 合并与大小写不敏感去重。
- schema v1 的 60 秒迁移为 300 秒、其他合法值保留，以及主文件损坏时从 v1 `.bak` 迁移。
- desktop/stdout/stderr 日志来源、1000 行淘汰、ANSI 清理、单行限长和敏感值脱敏。
- 阶段与总计单调计时、阶段切换结算、手动命令复制、PowerShell 工作目录和操作失败恢复。
- npm DNS、TLS、registry、权限稳定签名映射和未知错误回退。
- Owned 进程立即退出、运行中崩溃、超时、取消和进程树回收。

## WPF 检查

- 首次启动 Debug 应用时发现只读 `ManualInstallCommand` 被 `TextBox.Text` 默认 TwoWay 绑定，窗口在 `Show()` 阶段抛出异常。
- 将该绑定显式改为 `Mode=OneWay` 后重新构建，主窗口成功显示，WebView2 页面正常加载，底部版本显示为 `0.4.0`。
- 自动化检查期间检测到用户正在操作桌面，按 GUI 自动化规则停止继续输入；因此 820x600、125%/150%/200% DPI、安装引导展开态和日志滚动仍需在隔离 VM 中完成。
- 测试应用已结束，未留下从仓库启动的 Desktop 进程；未清理或修改宿主机 npm 缓存。

## 虚拟机门禁

管理员只读审计结果：

- Hyper-V `vmms` 与 `vmcompute` 服务正在运行。
- Hyper-V VM 清单为空，没有可建立测试前检查点的现有虚拟机。
- 未检测到 VMware 或 VirtualBox 安装。
- 常见本地目录未发现可复用的 Windows ISO、VHD 或 VHDX。

因此没有启动、创建、删除或还原任何虚拟机，也没有可声称完成的环境恢复操作。以下发布前场景保持阻塞：

- Windows 10/11 x64 空白用户安装 Node.js LTS 后的 PATH 刷新。
- 隔离 npm 缓存的首次无版本 npx 下载。
- 下载耗时超过 60 秒且小于 300 秒的受控慢网络。
- DNS、TLS、registry、权限、取消和重试的真实故障矩阵。
- 手动命令启动后 `RunningExternal` 所有权隔离。
- 820x600 与 125%/150%/200% DPI 的完整安装引导检查。

继续验证需要提供一台可由当前账号管理的 Windows 10/11 x64 VM，且具有测试前检查点；或者提供 Windows 安装 ISO 和允许创建临时 Hyper-V VM 的资源。执行时应先创建专用检查点，导入发布 ZIP，使用 VM 内独立用户和 npm 缓存完成矩阵，导出脱敏证据，再恢复检查点并核对 VM 状态。
