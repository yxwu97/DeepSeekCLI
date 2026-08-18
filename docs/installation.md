# DeepSeek Harness Desktop

## 系统要求

- Windows 10 或 Windows 11 x64。
- [.NET 8 Desktop Runtime x64](https://dotnet.microsoft.com/download/dotnet/8.0)。
- Microsoft Edge WebView2 Evergreen Runtime。
- Node.js LTS x64（包含 npm 和 npx）；如果已全局安装可用的 `dsh`，Node.js 可不单独安装。
- 首次通过 npx 准备 DSH 时需要访问 npm registry。

发布包是 framework-dependent 轻量 ZIP，不包含 .NET、Node.js 或 DSH。应用本身必须依赖 .NET 8 Desktop Runtime 才能启动，因此 .NET 是唯一无法在应用内部补装的前置条件。

## 安装与启动

1. 安装 .NET 8 Desktop Runtime x64。
2. 解压发布 ZIP，双击 `DeepSeekHarnessDesktop.exe`。
3. 应用依次检查 WebView2、Node.js、npx、全局 DSH 和已有的固定版本 npx DSH。
4. 缺少 WebView2 或 Node.js 时，主按钮只打开当前缺失项的官方安装页；安装完成后返回应用并点击“重新检查”。
5. 环境满足后选择工作目录，点击“准备并启动”。
6. 如果没有全局 DSH，应用先复用当前用户 npx 缓存中已准备好的固定版本；只有缓存也不存在时，才询问是否通过 npx 下载并启动 `@deepseek-ai/dsh@0.1.0-rc.6`。

应用不会静默安装系统软件，也不会自动全局安装 Node.js 或 DSH。缓存复用只接受标准 `_npx` 目录中包名、版本、bin 映射和入口均匹配的 `@deepseek-ai/dsh@0.1.0-rc.6`，不会执行其他缓存包或 manifest 指定的任意入口。

## 启动行为

Auto 模式按以下顺序解析命令：

1. PATH 中可执行的 `dsh.cmd`，执行 `dsh web`。
2. 当前用户标准 npx 缓存中校验通过的固定版本 DSH，通过 PATH 中的 `node.exe` 直接执行固定 `lib/bin.js web`，不访问 npm registry。
3. PATH 中可执行的 `npx.cmd`，执行 `npx -y @deepseek-ai/dsh@0.1.0-rc.6 web`。
4. 非默认端口只追加受控的 `--port <纯数字端口>`。

工作目录始终通过 `ProcessStartInfo.WorkingDirectory` 传递，不拼接到 Shell 命令。自定义启动模式只接受已存在的原生 `.exe` 或 `.com`，不接受 `.cmd`、`.bat` 或任意 Shell 文本。

主窗口会先显示，再执行环境诊断、WebView2 初始化和 DSH 生命周期初始化。页面“刷新”只刷新 Web UI，不重启 DSH。“重启”只适用于当前 Desktop 创建的进程；已有外部 DSH 只连接和刷新，应用不会停止或重启它。

## 本机服务与网页安全

默认服务地址为 `http://127.0.0.1:3080/`，只接受绝对 loopback HTTP(S) 地址。端口可访问并不等于 DSH 可用；应用还会检查 HTTP 状态、页面标题和 DSH 身份标记。

Code WebView2 只导航到已确认 DSH 地址的同源页面。Chat 只允许精确的 `https://chat.deepseek.com:443`，并使用独立 profile；权限默认拒绝、下载默认取消。其他安全 HTTP(S) 链接交给系统浏览器。

## 版本信息

“关于”窗口显示 Desktop、.NET、WebView2、系统 Node.js、npx 和实际选用的 DSH 信息。npm `latest` 查询只用于查看上游发布状态，不会改变 Auto 使用的固定 DSH 版本。

## 本地数据

- 配置：`%APPDATA%\DeepSeekHarnessDesktop\settings.json`
- 配置备份：`%APPDATA%\DeepSeekHarnessDesktop\settings.json.bak`
- 日志：`%LOCALAPPDATA%\DeepSeekHarnessDesktop\logs`
- WebView2 数据：`%LOCALAPPDATA%\DeepSeekHarnessDesktop\WebView2`
- npm/npx 缓存：由当前用户的 npm 配置决定，不由 Desktop 管理

API Key、Authorization、Cookie、Token 和密码不会写入配置、日志或发布包。进入 UI 的外部输出会先规范化、限长并脱敏。

## 常见错误

- 应用无法启动并提示缺少 .NET：安装 .NET 8 Desktop Runtime x64 后重试。
- `WEB-E301`：WebView2 Runtime 不可用；使用安装引导打开官方页面，安装后重试。
- `DSH-E101`：未找到全局或缓存 DSH，且 Node.js 或 npx 不可用；安装 Node.js LTS x64 后重新检查。
- `DSH-E201`：DSH 进程意外退出；查看界面中的脱敏日志。
- `DSH-E203`：DSH 未在配置的期限内通过身份检查。
- `DSH-E205`：目标端口有 HTTP 服务，但不是已确认的 DSH。
- `DSH-E211`：npm DNS 或网络连接失败。
- `DSH-E212`：npm TLS/证书校验失败。
- `DSH-E213`：npm registry 拒绝请求或找不到固定 DSH 包。
- `DSH-E214`：npm 缓存或目录权限不足。

## 卸载

1. 从托盘完整退出应用，确保 Owned DSH 进程树已结束。
2. 删除解压后的程序目录。
3. 如需清除设置、日志和网页数据，再删除 `%APPDATA%\DeepSeekHarnessDesktop` 与 `%LOCALAPPDATA%\DeepSeekHarnessDesktop`。

Desktop 不会删除 Node.js、全局 DSH 或 npm 缓存；这些由用户自行管理。
