# DeepSeek Harness Desktop

## 系统要求

- Windows 10 或 Windows 11 x64。
- Microsoft Edge WebView2 Evergreen Runtime。
- Node.js 与 `npx` 已安装并加入当前用户或系统 PATH。
- 首次通过 npx 启动固定版本 DSH 时，需要能够访问 npm registry；应用不会全局安装或自动升级 DSH。

## 使用

1. 解压 ZIP 到可写目录。
2. 双击 `DeepSeekHarnessDesktop.exe`。
3. 在停止状态下选择工作目录；应用会保存该目录及窗口状态。
4. 启动成功后，官方 DSH Web UI 会显示在主窗口中。

页面“刷新”只刷新 Web UI，不会重启 DSH。“重启”只适用于当前桌面宿主创建的 DSH；已存在的外部 DSH 只连接，不停止、不重启。

## 本地数据

- 配置：`%APPDATA%\DeepSeekHarnessDesktop\settings.json`
- 配置备份：`%APPDATA%\DeepSeekHarnessDesktop\settings.json.bak`
- 日志：`%LOCALAPPDATA%\DeepSeekHarnessDesktop\logs`
- WebView2 数据：`%LOCALAPPDATA%\DeepSeekHarnessDesktop\WebView2`

日志按日和 10 MB 文件大小滚动，默认保留 7 个文件。Bearer 凭据、敏感环境变量值及 URL 中的 key/token/secret 参数会在写入前脱敏。

## 故障诊断

- `DSH-E101`：检查 `node --version` 和 `npx --version` 是否可在普通用户环境运行。
- `WEB-E301`：安装或修复 Microsoft Edge WebView2 Evergreen Runtime。
- `DSH-E205`：默认地址被无法确认身份的其他服务占用；应用不会结束该服务。
- `CFG-E401`：主配置与备份均不可用，应用已使用默认设置启动。

“关于”窗口会显示 Desktop、.NET、WebView2、Node.js 和固定 DSH 版本，便于核对运行环境。
