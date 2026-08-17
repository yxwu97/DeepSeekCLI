# DeepSeek Harness Desktop

## 系统要求

- Windows 10 或 Windows 11 x64。
- Microsoft Edge WebView2 Evergreen Runtime。
- 以下启动路径至少满足一条：全局 `dsh.cmd` 可用；或 Node.js 与 `npx.cmd` 已加入当前用户或系统 PATH。
- 首次通过 npx 启动固定版本 DSH 时，需要能够访问 npm registry；应用只写入当前用户 npm 缓存，不会全局安装或自动升级 DSH。

## 使用

1. 解压 ZIP 到可写目录。
2. 双击 `DeepSeekHarnessDesktop.exe`。
3. 在停止状态下选择工作目录；应用会保存该目录及窗口状态。
4. 启动成功后，官方 DSH Web UI 会显示在主窗口中。

主窗口默认进入 Code。点击顶部 `Chat` 可在同一进程中懒加载 `https://chat.deepseek.com/`；再次切换不会重建页面，因此会保留当前页面、滚动位置和未提交输入。隐藏到托盘或由第二实例激活时保持当前模式，完整退出后下次启动仍默认 Code。

页面“刷新”只刷新 Web UI，不会重启 DSH。“重启”只适用于当前桌面宿主创建的 DSH；已存在的外部 DSH 只连接，不停止、不重启。

Chat 只内嵌精确的官方 HTTPS origin。其他安全 HTTP(S) 链接在系统浏览器打开，危险协议被拒绝。系统浏览器与应用专用 Chat profile 不共享登录信息，因此外部浏览器登录不会自动回写应用内 Chat。

## 安装引导

未找到可用的全局 DSH，且 Node.js 或 npx 不可用时，主窗口会显示安装引导。也可在停止页或失败页手动打开引导。

- “打开 Node.js 下载页”只打开 Node.js 官方网站，不会下载或运行安装程序。
- “重新检查”会重新探测 WebView2、全局 DSH、Node.js 和 npx。
- “下载并启动”会先要求确认，然后复用应用现有的 Owned DSH 启动链路运行固定版本 `@deepseek-ai/dsh@0.1.0-rc.6`。
- 准备和启动期间可以取消；应用会通过 Windows Job Object 回收本次创建的整个进程树。

## 本机服务地址

点击主工具栏的设置按钮可编辑服务地址、测试连接、恢复默认值并应用。默认地址为 `http://127.0.0.1:3080/`。

- 只接受绝对的 loopback `http/https` 地址，例如 `http://localhost:43123/` 或 `https://[::1]:8443/`。
- 不接受远程主机、用户信息、查询参数或片段；保存时路径统一为 `/`。
- “测试连接”不会保存设置，且只有同时匹配 DSH 页面标题与启动标记才会报告成功。
- 切换外部 DSH 时，应用先确认新地址身份；失败后保留原地址、原页面和原健康 watcher。
- 修改应用创建的 Owned DSH 地址需要确认并重启。旧进程退出且旧端点释放前不会创建新进程。

## 手动检查更新

“关于”窗口可手动查询 npm 官方 registry 的 `latest` 版本，并与当前验证版本比较。应用启动时不会后台检查，检查结果也不会下载、安装或切换 DSH 版本。

Developer Preview 的新版本可能与当前宿主不兼容。即使发现新版，应用仍继续使用固定验证版本 `0.1.0-rc.6`。

## 本地数据

- 配置：`%APPDATA%\DeepSeekHarnessDesktop\settings.json`
- 配置备份：`%APPDATA%\DeepSeekHarnessDesktop\settings.json.bak`
- 日志：`%LOCALAPPDATA%\DeepSeekHarnessDesktop\logs`
- WebView2 数据：`%LOCALAPPDATA%\DeepSeekHarnessDesktop\WebView2`

Code 沿用默认 WebView2 profile；Chat 使用同一数据根目录下固定且隔离的 `Chat` profile。有效的官方登录会话可由 WebView2 保存，但实际期限由官方网站决定。应用请求启用 WebView2 原生密码保存与自动填充，提示是否出现取决于 WebView2 Runtime、Windows 和企业策略；应用不读取、导出或记录密码、Cookie、Token、聊天正文或站点存储。

Chat 工具栏的清除按钮会先要求确认，然后仅对 Chat profile 调用 WebView2 的完整浏览数据清除，并重新加载 Chat。该操作不会清除 Code 页面数据、修改 DSH 设置或停止 DSH。Chat 权限请求和下载默认拒绝；需要下载时请使用官方页面提供的外部浏览器流程。

日志按日和 10 MB 文件大小滚动，默认保留 7 个文件。Bearer 凭据、敏感环境变量值及 URL 中的 key/token/secret 参数会在写入前脱敏。

## 故障诊断

- `DSH-E101`：全局 DSH 不可用，并且 Node.js 或 npx 缺失/无法运行；使用安装引导重新检查。
- `DSH-E202`：服务地址不符合本机 origin 规则；在服务设置中修正地址。
- `DSH-E205`：目标地址被无法确认身份的其他服务占用；应用不会加载或结束该服务。
- `DSH-E207`：当前正在启动、停止或重启；等待操作完成后再应用地址。
- `DSH-E208`：设置中的 DSH 地址不可达；启动服务或改用其他本机地址。
- `WEB-E301`：安装或修复 Microsoft Edge WebView2 Evergreen Runtime。
- `WEB-E311`：Chat WebView2/profile 初始化失败；可重试，持续失败时修复 Runtime。
- `WEB-E312`：Chat 网络或 DNS 失败；检查网络后重试。
- `WEB-E313`：Chat TLS/证书校验失败；检查系统时间、代理和企业证书策略。
- `WEB-E314`：Chat 官方服务返回 HTTP 错误；稍后重试。
- `WEB-E315`：Chat 页面进程异常；应用会进行一次单页恢复。
- `WEB-E316`：Chat 登录信息清除失败；Code 和 DSH 不受影响，可重试。
- `WEB-E318`：系统浏览器无法打开外部链接。
- `CFG-E401`：主配置与备份均不可用，应用已使用默认设置启动。

“关于”窗口会显示 Desktop、.NET、WebView2、Node.js、全局 DSH 路径、固定 DSH 版本和最近一次手动更新检查结果。
