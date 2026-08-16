# 阶段 2：进程生命周期核心验收记录

## 完成范围

- `DshCommandResolver` 按自定义原生程序、`dsh.cmd`、`npx.cmd` 顺序解析，回退命令固定包含 `-y` 和 rc.6 版本。
- `CmdCommandLineBuilder` 实现受控 `cmd.exe /d /v:off /s /c` 构造，拒绝用户参数和危险脚本路径。
- `HarnessProcessManager` 捕获 stdout/stderr、退出码并为每代进程分配独立 Windows Job Object。
- 输出流水线完成 ANSI CSI/OSC 清理、16 KiB 截断和 loopback URL 解析。
- `HarnessLifecycleCoordinator` 使用生命周期锁、operation CTS、generation 和重复 Start 合并保护。
- 重启严格等待旧进程退出及旧地址连续两次不可达，地址仍占用时返回 `DSH-E205`。
- 独立测试子进程覆盖输出、立即退出和父子进程树清理，不依赖真实 DSH。
- 协调器按 PID 和 generation 订阅 Owned `ProcessExited`；启动中或运行中意外退出立即进入 `Failed(DSH-E201)`，主动停止产生的旧回调不会污染新状态。

## 阶段门禁

| 验证项 | 结果 |
|---|---|
| Debug 全解决方案构建 | 通过，0 警告/0 错误 |
| Release 全解决方案构建 | 通过，0 警告/0 错误 |
| 单元测试 | 31 项通过，0 失败，0 跳过 |
| 集成测试 | 4 项通过，0 失败，0 跳过 |
| 重复启动 | 并发 Start 合并，仅创建 1 个进程 |
| 启动中停止 | 取消探测并清理 Owned 进程 |
| 陈旧异步结果 | 取消后返回的 Ready 结果不能覆盖 Stopped |
| 重启守卫 | 旧进程退出且地址连续两次不可达后才创建新进程 |
| Job Object | 停止后测试父进程及其后代均退出 |

阶段构建和测试命令：

```powershell
dotnet build DeepSeekHarnessDesktop.sln -c Debug --no-restore -m:1
dotnet build DeepSeekHarnessDesktop.sln -c Release --no-restore -m:1
dotnet test DeepSeekHarnessDesktop.sln -c Release --no-build --no-restore -m:1
```

## 发布缓冲期回归

2026-08-15 对照 IT-003、IT-004、IT-008 和 IT-016 补充真实子进程集成测试，并修复协调器遗漏 `ProcessExited` 订阅的问题：

- 进程在 `StartAsync` 返回前退出时，仍按 PID 和 generation 关联到当前启动操作。
- 启动中立即退出与运行中崩溃均进入 `Failed(DSH-E201)`，技术信息保留退出码，stderr 保留在日志缓冲区。
- 启动超时仍保持 `DSH-E203`，主动清理产生的退出事件不会覆盖超时错误。
- 启动中停止和超时均通过真实 Job Object 测试确认子进程树无残留。

修复后最终门禁为单元测试 57 项、集成测试 18 项全部通过。
