# 阶段 5：可靠性与发布验收记录

## 完成范围

- `SettingsService` 实现版本化 JSON 校验、临时文件写入、原子替换、单份 `.bak`、备份恢复和默认值回退。
- 应用启动加载持久配置，退出保存工作目录、窗口位置、尺寸和最大化状态。
- 文件日志按日和 10 MB 滚动，保留 7 个文件；Bearer、敏感环境变量值及 URL Query 凭据在格式化后统一脱敏。
- 命名 Mutex 保证单实例，当前用户命名管道仅接受固定 `Activate` 指令；第二实例恢复并激活首实例。
- 主窗口关闭将配置保存和服务释放纳入 8 秒总清理窗口，超时后由进程 Job Handle 随宿主退出兜底。
- 启动诊断检测 WebView2 Runtime、Node.js 和 npx；“关于”窗口展示 Desktop、.NET、WebView2、Node.js 和固定 DSH 版本。
- 已加入普通用户权限、Per-Monitor V2 DPI、长路径 manifest、版本资源和原创多尺寸应用图标。
- 发布脚本生成 Windows x64 self-contained 单文件 EXE 和免安装 ZIP；每次发布前清理受控的生成目录，避免旧文件混入归档。
- `eng/Verify-Release.ps1` 串联 Debug/Release 构建、单元/集成测试、发布、ZIP 内容和版本元数据校验，并生成 JSON 审计报告。

## 自动化门禁

| 验证项 | 结果 |
|---|---|
| Debug 全解决方案构建 | 通过，0 警告/0 错误 |
| Release 全解决方案构建 | 通过，0 警告/0 错误 |
| 单元测试 | 57 项通过，0 失败，0 跳过 |
| 集成测试 | 18 项通过，0 失败，0 跳过 |
| 配置测试 | 默认值、原子保存、单份备份、损坏恢复、非法版本和边界校验通过 |
| 日志测试 | Bearer、API Key、Token、Secret Query 脱敏及事件 ID 落盘通过 |
| 依赖测试 | 完整依赖与 WebView2/Node/npx 缺失分支通过 |
| 单实例测试 | Mutex 主从判定通过；真实双进程激活通过 |
| 最终 RC 门禁 | 通过；报告位于 `artifacts/validation/release-gate-0.1.0-win-x64.json` |

## 真实桌面验收

- 普通桌面环境从 `%APPDATA%\DeepSeekHarnessDesktop\settings.json` 加载和保存 schema 1 配置。
- `%LOCALAPPDATA%\DeepSeekHarnessDesktop\logs` 成功写入启动、配置加载/保存、关闭请求和退出事件。
- 正常退出日志序列包含 `1000 -> 1003 -> 1110 -> 1001`，修正了异步 `OnExit` 尾部未刷新的问题。
- 首实例最小化后启动第二实例，未出现第二个主窗口；首实例被恢复和激活。
- “关于”窗口识别 WebView2 `151.0.4129.86`、Node.js `v24.15.0`、npx 路径和 DSH `0.1.0-rc.6`。
- Debug 与 self-contained Release EXE 均成功加载官方 DSH Web UI。
- 关闭 Debug 和 Release 宿主后，Owned DSH 无监听残留，`3080` 仅出现短暂 `TIME_WAIT` 或完全释放。
- 1280x820 当前桌面 DPI 下命令栏、WebView2、状态栏和“关于”窗口无重叠或裁切。

## 发布产物

| 项目 | 值 |
|---|---|
| ZIP | `artifacts/DeepSeekHarnessDesktop-0.1.0-win-x64.zip` |
| ZIP 大小 | 66,430,901 字节 |
| ZIP 内容 | `DeepSeekHarnessDesktop.exe`、`README.md` |
| EXE 大小 | 163,760,663 字节 |
| 产品版本 | `0.1.0` |
| 文件版本 | `0.1.0.0` |
| SHA-256 | `AA5EB88BDAEEAEF2A896112D82B09C362898807A3A3091A454E4073A5318B11D` |

## 缓冲期复验

2026-08-15 执行 `eng/Verify-Release.ps1`，结果如下：

- Debug 与 Release 全解决方案构建均为 0 警告、0 错误。
- 单元测试 57/57、集成测试 18/18 通过，无失败或跳过。
- 补充 IT-003/004/008/016 真实进程回归，确认意外退出映射为 `DSH-E201`、超时保持 `DSH-E203`，退出码与 stderr 可用于诊断。
- 统一 Codex 风格工具按钮并完成 self-contained Release EXE 的真实界面、关于弹窗和正常退出复验。
- 发布目录经清理后重新生成，ZIP 仅包含 `DeepSeekHarnessDesktop.exe` 和 `README.md`。
- EXE 文件版本为 `0.1.0.0`，产品版本为 `0.1.0`。
- 复验结束后无 `DeepSeekHarnessDesktop` 进程，端口 `3080` 无监听。

## 剩余环境验收

以下项目需要独立目标环境，当前开发机未伪造结果：

- Windows 10 x64 干净用户环境。
- Windows 11 x64 的 125% 和 150% DPI；当前机器仅完成现有 DPI 的真实截图验收。
- 完全缺少 Node.js、npm 缓存或 WebView2 Runtime 的干净机提示与首次 npx 下载。

对应错误分支已有自动化覆盖，manifest 已启用 Per-Monitor V2；上述真实环境矩阵仍是 MVP 0.1.0 从 RC 转正式发布前的外部验收项。

## 阶段结论

阶段 5 的实现、自动化门禁和本机发布验收完成，已生成可运行的 MVP 0.1.0 RC ZIP。正式发布门禁保留上述目标环境矩阵，不将未执行的平台验收标记为通过。
