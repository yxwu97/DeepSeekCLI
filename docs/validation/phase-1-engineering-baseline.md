# 阶段 1：工程骨架与核心模型验收记录

## 完成范围

- `DeepSeekHarnessDesktop.sln` 包含 WPF、单元测试、集成测试和阶段 0 验证项目。
- `Directory.Packages.props` 集中锁定全部 NuGet 版本，项目文件无浮动版本。
- 全局启用 Nullable、x64、确定性构建和警告即错误。
- 领域模型、错误模型、配置模型与六个核心服务接口已建立。
- `HarnessStateMachine` 实现详细设计 §15.1 的完整合法转换表和 generation 防陈旧提交。
- 主窗口使用模拟协调器演示 Stopped、Starting、RunningOwned、Stopping、Restarting 和 Failed 状态；ViewModel 不直接修改状态。

## 验证结果

| 验证项 | 结果 |
|---|---|
| Debug 全解决方案构建 | 通过，0 警告/0 错误 |
| Release 全解决方案构建 | 通过，0 警告/0 错误 |
| 单元测试 | 12 项通过，0 失败，0 跳过 |
| 集成基线测试 | 1 项通过，0 失败，0 跳过 |
| 主窗口启动烟测 | 窗口标题正确、进程响应正常、可正常关闭 |
| 浮动包版本检查 | 无；版本均由 Central Package Management 管理 |

当前 SDK 10.0.201 在解决方案并行构建多个引用同一 WPF 项目的测试工程时，会并发生成 XAML 临时程序集并相互取消。阶段门禁命令使用单 MSBuild 节点：

```powershell
dotnet build DeepSeekHarnessDesktop.sln -c Debug --no-restore -m:1
dotnet build DeepSeekHarnessDesktop.sln -c Release --no-restore -m:1
dotnet test DeepSeekHarnessDesktop.sln -c Release --no-build --no-restore -m:1
```

该限制只影响解决方案级并行构建，不影响应用运行、测试执行或发布产物。
