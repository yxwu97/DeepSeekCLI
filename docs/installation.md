# DeepSeek Harness Desktop

## 系统要求

- Windows 10 或 Windows 11 x64。
- .NET Framework 4.8（Windows 11 和已更新的 Windows 10 通常已内置；缺失时使用 Windows Update 或微软官方安装程序补齐）。
- Microsoft Edge WebView2 Evergreen Runtime。
- Node.js LTS x64（包含 npm；手动临时启动还会使用 npx）；如果 PATH 中已有可用的 `dsh.cmd`，Desktop 启动不依赖 npm。
- 没有可复用 DSH 时，首次私有安装需要访问 npm registry，并占用约 300 MiB 当前用户磁盘空间。

发布包是 .NET Framework 4.8 轻量 ZIP，不包含 .NET、Node.js 或 DSH，也不携带 CoreCLR。应用启动前需要系统已有 .NET Framework 4.8。

## 安装与启动

1. 确认 Windows 已启用 .NET Framework 4.8；缺失时先通过 Windows Update 或微软官方安装程序补齐。
2. 解压发布 ZIP，双击 `DeepSeekHarnessDesktop.exe`。
3. 应用检查 WebView2、Node.js/npm、PATH 全局 DSH、Desktop 私有安装和已有的固定版本 npx 缓存。
4. 缺少 WebView2 或 Node.js 时，主按钮只打开当前缺失项的官方安装页；安装完成后返回应用并点击“重新检查”。
5. 环境满足后选择工作目录，点击“准备并启动”。
6. 如果没有任何可复用 DSH，应用会说明版本、私有安装位置和预计占用；用户确认后，以发布包内精确 lockfile 执行一次 `npm ci --omit=dev`，真实启动验证通过后才激活。

应用不会静默安装系统软件，也不会自动全局安装 Node.js 或 DSH。私有安装位于 `%LOCALAPPDATA%\DeepSeekHarnessDesktop\dsh`；失败、取消或超时不会激活不完整版本。初始选择版本为精确 `@deepseek-ai/dsh@0.1.1-rc.2`；旧私有目录不会冒充当前版本，也不会被自动删除。

## 启动行为

Auto 模式按以下顺序解析命令：

1. PATH 中可执行且 `dsh --version` 精确返回当前选择版本的 `dsh.cmd`，执行 `dsh web --no-open`；版本不符或探测失败时继续查找后续候选。
2. Desktop 已激活的私有安装，通过 PATH 中的 `node.exe` 直接执行固定 `lib/bin.js web --no-open`，不访问 npm registry。
3. 当前用户标准 npx 缓存中校验通过的固定版本 DSH，同样通过 `node.exe` 直接执行，不运行 npx。
4. 全部缺失时，经确认执行一次私有锁定安装；安装成功后回到第 2 项。
5. 非默认端口只追加受控的 `--port <纯数字端口>`。

安装引导的“手动安装与启动”区域保留 Bootstrap 固定命令：持久全局安装 `npm install -g @deepseek-ai/dsh@0.1.1-rc.2`，以及临时外部启动 `npx @deepseek-ai/dsh@0.1.1-rc.2 web`。正式更新优先使用关于窗口的“下载并更新”，只写 Desktop 私有目录，不修改全局 npm。External DSH 正在运行时应用只提示退出后重试，不会停止外部进程。

工作目录始终通过 `ProcessStartInfo.WorkingDirectory` 传递，不拼接到 Shell 命令。自定义启动模式只接受已存在的原生 `.exe` 或 `.com`，不接受 `.cmd`、`.bat` 或任意 Shell 文本。

主窗口会先显示，再执行环境诊断、WebView2 初始化和 DSH 生命周期初始化。页面“刷新”只刷新 Web UI，不重启 DSH。“重启”只适用于当前 Desktop 创建的进程；已有外部 DSH 只连接和刷新，应用不会停止或重启它。

Auto 模式启动的 DSH 就绪后只在应用内 Code WebView2 加载服务页面，不会自动调用系统默认浏览器打开本机服务地址。手动外部启动命令由用户自行控制其浏览器行为。

## 本机服务与网页安全

默认服务地址为 `http://127.0.0.1:3080/`，只接受绝对 loopback HTTP(S) 地址。端口可访问并不等于 DSH 可用；应用还会检查 HTTP 状态、页面标题和 DSH 身份标记。

Code WebView2 只导航到已确认 DSH 地址的同源页面。Chat 只允许精确的 `https://chat.deepseek.com:443`，并使用独立 profile；权限默认拒绝、下载默认取消。其他安全 HTTP(S) 链接交给系统浏览器。

## 版本信息

“关于”窗口分别显示实际 DSH、当前选择版本和 npm `latest`。npm latest 只用于发现；未进入有效签名目录的版本只显示“等待验证”，不能生成安装命令。下载必须由用户点击并确认，npm 使用官方 registry、空 user/global config、独立 cache 和 `--ignore-scripts`，安装后通过固定入口与 loopback Web 身份 smoke 才激活。

当前 0.11.1 开发构建尚未配置生产 catalog 公钥和固定发布端点，因此远程目录检查以 `DSH-E226` 失败关闭；这不会影响内置 rc.2 的首次私有安装和复用。正式发布无代码 N+1 前必须由维护方完成生产信任根、签名资产和未重编译 Desktop 的更新演练，用户不能自行配置更新源或公钥绕过该门禁。

## 本地数据

- 配置：`%APPDATA%\DeepSeekHarnessDesktop\settings.json`
- 配置备份：`%APPDATA%\DeepSeekHarnessDesktop\settings.json.bak`
- 日志：`%LOCALAPPDATA%\DeepSeekHarnessDesktop\logs`
- WebView2 数据：`%LOCALAPPDATA%\DeepSeekHarnessDesktop\WebView2`
- Desktop 私有 DSH：`%LOCALAPPDATA%\DeepSeekHarnessDesktop\dsh`
- npm/npx 缓存：由当前用户的 npm 配置决定，不由 Desktop 管理

API Key、Authorization、Cookie、Token 和密码不会写入配置、日志或发布包。进入 UI 的外部输出会先规范化、限长并脱敏。

## 常见错误

- 应用无法启动并提示缺少 .NET Framework：通过 Windows Update 或微软官方安装程序安装 .NET Framework 4.8 后重试。
- `WEB-E301`：WebView2 Runtime 不可用；使用安装引导打开官方页面，安装后重试。
- `DSH-E101`：未找到可复用 DSH，且 Node.js 或 npm 不可用；安装 Node.js LTS x64 后重新检查。
- `DSH-E201`：DSH 进程意外退出；查看界面中的脱敏日志。
- `DSH-E203`：DSH 未在配置的期限内通过身份检查。
- `DSH-E205`：目标端口有 HTTP 服务，但不是已确认的 DSH。
- `DSH-E211`：npm DNS 或网络连接失败。
- `DSH-E212`：npm TLS/证书校验失败。
- `DSH-E213`：npm registry 拒绝请求或找不到固定 DSH 包。
- `DSH-E214`：npm 缓存或目录权限不足。
- `DSH-E221`：首次私有安装超过总期限或连续无进展；检查网络、registry 和脱敏安装日志后重试。
- `DSH-E222`：发现了全局 DSH，但版本不是当前选择版本，且没有其他候选或私有安装条件；手动更新后重新检查，或安装 Node.js/npm 以使用私有安装。
- `DSH-E223`：内置或下载的 package/lock 与受信描述符不一致；npm 不会启动或事务立即终止。
- `DSH-E224`：npm 完成后的 manifest、lock、bin、入口或路径校验失败；旧 active 保持不变。
- `DSH-E225`：固定入口版本或真实 Web 身份 smoke 失败；新版本不会激活。
- `DSH-E226`：更新目录签名、schema、sequence、来源或资产 hash 无效；远程候选会被忽略。
- `DSH-E227`：目录版本需要更高 Desktop、运行协议或 Node.js；先升级相应组件。

## 卸载

1. 从托盘完整退出应用，确保 Owned DSH 进程树已结束。
2. 删除解压后的程序目录。
3. 如需清除设置、日志和网页数据，再删除 `%APPDATA%\DeepSeekHarnessDesktop` 与 `%LOCALAPPDATA%\DeepSeekHarnessDesktop`。

Desktop 不会删除 Node.js、全局 DSH 或 npm 缓存；这些由用户自行管理。
