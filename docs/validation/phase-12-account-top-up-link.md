# 阶段 12：DeepSeek 官方充值入口验证

验证日期：2026-08-17  
应用版本：0.6.0

## 验证范围

- “DeepSeek 账号”窗口增加“官方充值”按钮。
- 充值入口通过受控 `OfficialResource.DeepSeekTopUp` 路由。
- 固定地址为 `https://platform.deepseek.com/top_up`，由系统默认浏览器打开。
- 充值链接不携带 API Key、余额或其他账户数据，且不依赖余额查询结果。

## 自动化结果

- DeepSeek 账号定向测试：11/11 通过，包含充值命令固定资源路由测试。
- Debug 全解决方案构建：通过，0 个警告、0 个错误。
- Release 全解决方案构建：通过，0 个警告、0 个错误。
- UnitTests：158/158 通过。
- IntegrationTests：18/18 通过。
- 发布包：`DeepSeekHarnessDesktop-0.6.0-win-x64.zip`。
- 发布包条目：`DeepSeekHarnessDesktop.exe`、`README.md`。
- 文件版本：`0.6.0.0`；产品版本：`0.6.0`。
- ZIP SHA-256：`25559B7E466CD02E98453EBA0106A8D7303DCBDE5952BF052CCEAB77EF9E9F99`。

## 未通过项

- `Verify-Release.ps1` 的交互式 WebView2 验证在当前桌面环境初始化 WebView2 时继续返回 `0x8000FFFF (E_UNEXPECTED)`；该失败发生在独立 Phase0Validation，和账号充值外链无调用关系。
- Windows 界面控制器首次返回了已失效的应用窗口句柄，安全恢复后确认应用窗口已不存在，因此未点击任何控件，也未完成账号窗口截图及多 DPI 检查。Account XAML 已通过 Debug 和 Release 编译。

## 结论

固定充值资源、ViewModel 命令、XAML 绑定、无账户数据 URL、版本一致性和发布打包验证通过。完整发布门禁尚不能标记为通过，需在 WebView2 交互环境恢复后补跑，并在真实桌面会话补充账号窗口多 DPI 检查。
