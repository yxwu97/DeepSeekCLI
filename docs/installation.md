# DeepSeek Harness Desktop

## 系统要求

- Windows 10 或 Windows 11 x64。
- Microsoft Edge WebView2 Evergreen Runtime。
- 以下启动路径至少满足一条：全局 `dsh.cmd` 可用；或 Node.js 与 `npx.cmd` 已加入当前用户或系统 PATH。
- 首次通过 npx 启动 DSH 时，需要能够访问 npm registry；应用只写入当前用户 npm 缓存，不会全局安装 DSH。

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

- “Node.js 下载”只打开 Node.js 官方网站，不会下载或运行安装程序。
- “重新检查”会重新读取系统、用户和进程 PATH，再探测 WebView2、全局 DSH、Node.js 和 npx。
- “准备并启动”会先要求确认，然后复用应用现有的 Owned DSH 启动链路运行 `npx -y @deepseek-ai/dsh web`。包名和参数固定，但不锁定 DSH 版本。
- 准备和启动期间可以取消；应用会通过 Windows Job Object 回收本次创建的整个进程树。
- 引导会显示当前阶段、阶段耗时、总耗时和最长等待时间；新配置默认最长等待 5 分钟。
- 日志实时显示 `[时间] [desktop/stdout/stderr] 内容`，最多保留 1000 行，并支持复制已经规范化和脱敏的日志。

自动准备失败时，可展开“手动安装与启动”并按以下步骤操作：

1. 点击“Node.js 下载”，安装 Node.js LTS x64；安装后重新打开 PowerShell。
2. 执行 `node --version` 和 `npx --version`，确认两个命令均可用；返回应用点击“重新检查”，仍无法识别时重启应用。
3. 在应用所选工作目录打开 PowerShell，执行 `npx @deepseek-ai/dsh web` 并保持终端运行。
4. 等待终端显示本机服务地址，再返回应用连接默认地址 `http://127.0.0.1:3080/`。

手动启动的进程属于外部实例。应用只连接和刷新，不会停止或重启它。引导提供复制命令、在工作目录打开 PowerShell、DSH 官方文档和 npm 包页面的快捷入口；打开 PowerShell 不会自动执行命令。

## 本机服务地址

点击主工具栏的设置按钮可编辑服务地址、测试连接、恢复默认值并应用。默认地址为 `http://127.0.0.1:3080/`。

- 只接受绝对的 loopback `http/https` 地址，例如 `http://localhost:43123/` 或 `https://[::1]:8443/`。
- 不接受远程主机、用户信息、查询参数或片段；保存时路径统一为 `/`。
- “测试连接”不会保存设置，且只有同时匹配 DSH 页面标题与启动标记才会报告成功。
- 切换外部 DSH 时，应用先确认新地址身份；失败后保留原地址、原页面和原健康 watcher。
- 修改应用创建的 Owned DSH 地址需要确认并重启。旧进程退出且旧端点释放前不会创建新进程。

## 手动检查更新

“关于”窗口可手动查询 npm 官方 registry 的 `latest` 版本。应用启动时不会后台检查，检查结果不会下载、安装、持久化或改变启动参数。自动 npx 路径不固定版本，由 npm 在每次需要解析包时选择当前版本；全局 `dsh.cmd` 仍优先使用。

“关于”窗口还提供“项目 GitHub”入口，固定打开本系统项目 `https://github.com/yxwu97/DeepSeekCLI`。需要下载桌面程序时，可在项目页面进入 Releases，选择最新发布版本的 Windows x64 ZIP。“版本记录”页签内置于应用，可离线查看各版本日期和主要变更。

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
- `DSH-E211`：npm DNS 查询失败；检查网络、DNS 和代理设置。
- `DSH-E212`：npm TLS 或证书验证失败；检查系统时间、代理和企业证书策略。
- `DSH-E213`：npm registry 拒绝请求或未找到包；检查 npm registry 配置和访问策略。
- `DSH-E214`：npm 缓存或工作目录权限不足；检查当前用户对相关目录的权限。
- `WEB-E301`：安装或修复 Microsoft Edge WebView2 Evergreen Runtime。
- `WEB-E311`：Chat WebView2/profile 初始化失败；可重试，持续失败时修复 Runtime。
- `WEB-E312`：Chat 网络或 DNS 失败；检查网络后重试。
- `WEB-E313`：Chat TLS/证书校验失败；检查系统时间、代理和企业证书策略。
- `WEB-E314`：Chat 官方服务返回 HTTP 错误；稍后重试。
- `WEB-E315`：Chat 页面进程异常；应用会进行一次单页恢复。
- `WEB-E316`：Chat 登录信息清除失败；Code 和 DSH 不受影响，可重试。
- `WEB-E318`：系统浏览器无法打开外部链接。
- `CFG-E401`：主配置与备份均不可用，应用已使用默认设置启动。

“关于”窗口的“系统信息”页签会显示 Desktop、.NET、WebView2、Node.js、全局 DSH 路径和版本，以及最近一次 npm `latest` 手动查询结果，并提供本系统 GitHub 下载入口；“版本记录”页签显示系统历史版本和主要变更。
