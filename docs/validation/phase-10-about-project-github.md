# 阶段 10：关于窗口项目 GitHub 入口验证

验证日期：2026-08-17  
应用版本：0.4.1

## 验证范围

- “关于”窗口提供本系统 GitHub 项目入口。
- 固定官方资源映射指向 `https://github.com/yxwu97/DeepSeekCLI`。
- 操作区在最小窗口宽度下使用两行布局，避免按钮挤压或溢出。
- 安装说明、README、应用版本和 manifest 版本同步更新。

## 自动化结果

- `FeatureViewModelTests`：10/10 通过，包含 GitHub 命令资源路由测试。
- Debug 全解决方案构建：通过，0 个警告、0 个错误。
- Release 全解决方案构建：通过，0 个警告、0 个错误。
- UnitTests：155/155 通过。
- IntegrationTests：18/18 通过。
- 发布包：`DeepSeekHarnessDesktop-0.4.1-win-x64.zip`。
- 发布包条目：`DeepSeekHarnessDesktop.exe`、`README.md`。
- 文件版本：`0.4.1.0`；产品版本：`0.4.1`。
- ZIP SHA-256：`7E5A2690695CDBAECE57DECC5B699DCCF265A03312E7C5EC53BE306C6F21D068`。

## 未通过项

- `Verify-Release.ps1` 的交互式 WebView2 验证在当前桌面环境初始化 WebView2 时返回 `0x8000FFFF (E_UNEXPECTED)`；独立重跑得到相同结果。失败发生在 WebView2 环境初始化阶段，与本次外链命令及 About XAML 逻辑无调用关系。
- Windows 界面控制器未能捕获 Debug 应用窗口，因此未完成截图式 About 窗口检查。XAML 已由 Debug 和 Release 构建成功编译；仍需在可用的交互桌面会话中检查 100%、125% 和 150% DPI。

## 结论

GitHub 固定资源映射、ViewModel 命令、XAML 绑定、文档、版本元数据和发布包验证通过。完整发布门禁尚不能标记为通过，需在 WebView2 交互环境恢复后补跑 `eng/Verify-Release.ps1`。
