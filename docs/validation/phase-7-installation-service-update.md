# 阶段 7：安装引导、服务地址与更新检查验收记录

## 验证信息

- 验证日期：2026-08-17
- 应用版本：Desktop `0.2.0`
- 固定 DSH：`@deepseek-ai/dsh@0.1.0-rc.6`
- 验证平台：Windows x64，100% DPI，主窗口 1280 x 820
- 验证范围：依赖诊断与安装引导、loopback 服务地址、手动 npm 更新检查、真实 DSH 端口参数、发布产物

## 自动化验证

在仓库根目录执行：

```powershell
dotnet restore DeepSeekHarnessDesktop.sln
dotnet build DeepSeekHarnessDesktop.sln -c Debug --no-restore -m:1
dotnet test DeepSeekHarnessDesktop.sln -c Release --no-restore -m:1
.\eng\Verify-Release.ps1
```

结果：

- Debug 和 Release 构建均为 0 warning、0 error。
- UnitTests：116/116 通过，0 跳过，0 失败。
- IntegrationTests：18/18 通过，0 跳过，0 失败。
- 覆盖全局 DSH 与 Node.js+npx 诊断、受控端口模板、URI 边界、服务切换回滚、generation 竞态、取消、npm 响应上限和 prerelease 比较。

## 真实 DSH 验证

- 确认固定版本同时支持 `dsh web --port <port>` 与 `dsh --profile web --port <port>`。
- 使用随机端口 `65193` 启动成功，HTTP 页面同时包含预期的两个 DSH 身份标记。
- 验证创建的宿主、DSH 进程树和临时目录已停止并清理；未按端口结束外部进程。

## WPF 手工验证

- 主窗口、WebView2 和工具栏在 1280 x 820、100% DPI 下显示正常，服务设置入口可用。
- 安装引导正确显示阶段说明、Node.js/npx 状态和固定版本；重新检查会刷新说明；返回恢复原状态页。
- 安装引导操作期间“重新检查”“返回”“下载并启动”禁用，“取消启动”启用；下载前显示 npm registry 与用户缓存二次确认。
- 服务设置窗口可通过键盘访问；远程 URI 输入不能应用；连接测试和保存不放宽 loopback 限制。
- 关于窗口显示 Desktop `0.2.0` 与依赖信息；手动更新检查返回 npm `latest` 为 `0.1.0-rc.6`，未自动下载或切换版本。
- Owned DSH 停止后正确进入停止/失败展示，未向 RunningExternal 暴露停止或重启能力。

## 发布门禁

- 报告：`output/validation/release-gate-0.2.0-win-x64.json`
- ZIP：`output/DeepSeekHarnessDesktop-0.2.0-win-x64.zip`
- ZIP SHA-256：`9E1BE4FC7EF905CDB79172FB52536AC0F8B18EBAC20DCA5740FF10F374AE54C9`
- ZIP 仅包含 `DeepSeekHarnessDesktop.exe` 和 `README.md`。
- EXE FileVersion 为 `0.2.0.0`，ProductVersion 为 `0.2.0`。
- `AGENTS.md` 与 `CLAUDE.md` 的 SHA-256 完全一致；AppVersion、manifest 和版本历史均为 `0.2.0`。

## 尚需外部环境验证

- Windows 10 x64 干净用户环境。
- Windows 11 x64 的 125% 与 150% DPI、最小窗口和最大化布局。
- 未安装 Node.js、无 npm 缓存或无 WebView2 Runtime 的干净环境。
- 首次无缓存下载期间的人工取消；自动化测试已覆盖取消、进程树回收与订阅释放。
