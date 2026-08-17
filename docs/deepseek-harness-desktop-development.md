# DeepSeek Harness Desktop 开发文档

## 1. 文档信息

- 项目名称：DeepSeek Harness Desktop
- 目标平台：Windows 10/11 x64
- 文档版本：0.3
- 更新日期：2026-08-17
- 项目目录：`E:\DeepSeekCLI`
- 官方文档：<https://deepseek-harness.github.io/deepseek-harness/guide/quickstart>
- 官方仓库：<https://github.com/deepseek-ai/deepseek-harness>

相关文档：

- [详细设计](./deepseek-harness-desktop-detailed-design.md)
- [开发计划](./deepseek-harness-desktop-development-plan.md)
- [交互原型](./deepseek-harness-desktop-prototype.html)

## 2. 项目目标

开发一个 Windows 桌面 EXE，用于启动本机 DeepSeek Harness Web UI，并在桌面窗口内切换显示本机 Code 页面与官方 DeepSeek Chat 页面。

应用负责：

1. 选择并记住 DSH 工作目录。
2. 自动启动 `dsh web`。
3. 等待 Web 服务可用。
4. 在内嵌浏览器中打开 DSH Web UI。
5. 提供页面刷新、DSH 启动、停止和重启操作。
6. 显示启动过程、运行状态和错误日志。
7. 在应用退出时正确处理由应用创建的 DSH 子进程。
8. 以独立、持久的 WebView2 profile 懒加载官方 DeepSeek Chat，并与 DSH 生命周期隔离。

本项目不重新实现 DeepSeek Harness 的会话、模型、工具、审批、工作区和配置界面。这些能力继续由官方 Web UI 提供。

## 3. 已确认的 DSH 行为

根据官方文档：

- npm Quick Start 命令为 `npx @deepseek-ai/dsh web`。
- 默认访问地址为 `http://127.0.0.1:3080`。
- DSH 将启动命令所在目录作为默认文件系统位置。
- Web UI 内仍需选择工作区，选择前会话输入不可用。
- DSH 当前处于 Developer Preview，后续版本可能存在不兼容变更。

桌面宿主将官方命令收紧为 `npx -y @deepseek-ai/dsh@0.1.0-rc.6 web`：`-y` 避免无控制台时卡在首次安装确认，显式已验证版本避免 npm 静默切换到未经验证的预发布版本。

当前开发机环境：

- Node.js：`v24.15.0`
- npm：`11.12.1`
- 已验证 DSH：`0.1.0-rc.6`
- `dsh` 当前不在全局 PATH 中
- .NET SDK：已安装 9.0 和 10.0
- .NET 8 Windows Desktop Runtime：已安装
- 默认端口 `3080` 在检查时未被占用

因此，首版默认使用以下命令启动：

```powershell
npx -y @deepseek-ai/dsh@0.1.0-rc.6 web
```

不得在程序中硬编码 npm 缓存目录。npm 的 `_npx` 缓存哈希属于内部实现，升级或清理缓存后可能变化。

## 4. 技术方案

### 4.1 技术栈

- 桌面框架：C#、.NET 8、WPF
- 内嵌浏览器：Microsoft Edge WebView2
- 配置存储：JSON
- 日志：应用内文本日志和本地滚动日志文件
- 测试：xUnit，必要时增加 WPF UI 自动化测试
- 发布：`dotnet publish`，Windows x64 self-contained
- 安装包：后续使用 WiX Toolset 或 MSIX

不采用 Electron。该应用只需要一个原生控制栏、进程管理和 WebView2，WPF 的安装体积、启动速度和系统集成更合适。

### 4.2 总体架构

```text
DeepSeekHarnessDesktop.exe
├── WPF 主窗口
│   ├── 顶部控制栏
│   ├── 状态与错误提示
│   ├── Code WebView2（默认 profile）
│   └── Chat WebView2（固定 Chat profile，懒加载）
├── HarnessProcessManager
│   ├── 启动 DSH
│   ├── 捕获 stdout/stderr
│   ├── 停止进程树
│   └── 进程退出监控
├── HarnessHealthMonitor
│   ├── 解析输出中的访问地址
│   └── HTTP 就绪检测
├── AppSettingsService
└── FileLogService
```

### 4.3 进程所有权边界

应用必须区分两类 DSH 实例：

- 应用实例：由当前 EXE 启动，允许停止和重启。
- 外部实例：应用启动前已在目标地址运行、且页面身份已确认是 DSH，只允许连接和刷新页面。

不得根据端口号直接结束进程。若 `3080` 返回 HTTP 但无法确认是 DSH，应显示“检测到已有服务，但无法确认它是 DeepSeek Harness”（`DSH-E205`），不得加载、覆盖或结束该服务；只有通过身份校验后才显示“外部实例运行中”并禁用停止和重启操作。

## 5. 用户界面

### 5.1 主窗口

主窗口由自适应两行顶部控制栏、内容区和状态栏组成。第一行提供 Code/Chat 分段切换及当前模式操作，Code 模式的第二行显示工作目录；内容区同时持有两个 WebView2 控件，但任意时刻只显示当前模式页面。

顶部控制栏包含：

- 工作目录选择框
- 选择目录按钮
- 运行状态指示
- 启动按钮
- 停止按钮
- 重启按钮
- 刷新页面按钮
- 查看日志按钮
- 更多设置菜单
- Code/Chat 模式切换
- Chat 登录信息清除

控制栏不覆盖网页内容，窗口缩放时 WebView2 自动填充剩余空间。

### 5.2 状态页面

在 DSH 尚未就绪时，WebView2 区域显示原生状态页：

- 未启动：显示“启动 DSH”操作。
- 启动中：显示进度和最新一行启动日志。
- 启动失败：显示错误摘要、重试和查看日志。
- 已停止：显示重新启动操作。
- 端口冲突：显示目标地址和诊断提示。

### 5.3 操作语义

- 刷新页面：仅刷新当前可见页面，不操作 DSH 进程。
- 启动 DSH：创建新进程并等待服务就绪。
- 停止 DSH：停止当前应用创建的进程树并显示已停止页。
- 重启 DSH：停止应用实例，确认退出后重新启动并恢复 Web UI。
- 选择工作目录：只在 DSH 停止时允许修改；运行中修改需先确认重启。
- 切换到 Chat：第一次切换时懒加载，后续切换保留页面实例；不停止、启动或重启 DSH。
- 清除 Chat 登录信息：二次确认后只清除 Chat profile，并重新加载固定 Chat 入口。

## 6. 应用状态机

```text
Stopped
  └── Start -> Starting

Starting
  ├── Health check success -> RunningOwned
  ├── Existing DSH identity confirmed -> RunningExternal
  ├── Unknown HTTP service found -> Failed (DSH-E205)
  ├── Process exited -> Failed
  └── Timeout -> Failed

RunningOwned
  ├── Stop -> Stopping -> Stopped
  ├── Restart -> Restarting
  └── Unexpected exit -> Failed

Restarting
  ├── Old process exited -> Restarting
  ├── Old endpoint released -> Starting
  └── Old endpoint still occupied -> Failed (DSH-E205)

RunningExternal
  ├── Reload -> RunningExternal
  └── Active health check lost -> Stopped

Failed
  └── Retry -> Starting
```

任何时刻只允许执行一个生命周期操作。启动、停止或重启进行中时，应禁用可能冲突的按钮。

## 7. 启动流程

### 7.1 正常启动

1. 读取应用配置。
2. 校验工作目录存在且可访问。
3. 检查配置的 Web URL 是否可访问并验证 DSH 页面身份。
4. 若确认是 DSH，标记为外部实例并直接加载页面；若有未知 HTTP 服务，返回 `DSH-E205` 且不启动新进程。
5. 检查 Node.js 和 npm/npx 是否可用。
6. 以所选工作目录作为 `WorkingDirectory` 启动 DSH。
7. 异步捕获标准输出和标准错误。
8. 从日志中识别 DSH 打印的 HTTP 地址。
9. 若未识别到地址，则使用配置中的默认 URL。
10. 循环执行 HTTP 就绪检测。
11. 服务就绪后导航 WebView2。
12. 标记为应用实例运行中。

### 7.2 DSH 命令解析

默认启动策略：

1. 若设置中配置了自定义命令，则使用自定义命令。
2. 若 PATH 中存在 `dsh.cmd`，执行 `dsh.cmd web`。
3. 否则执行 `npx.cmd -y @deepseek-ai/dsh@0.1.0-rc.6 web`。
4. 均不可用时显示 Node.js/npm 未安装或 PATH 配置错误。

Windows 下 `.cmd` 启动脚本需要通过受控的 `cmd.exe /d /v:off /s /c` 子进程执行，以便重定向输出。默认 `.cmd` 只接收程序内置参数；工作目录通过 `ProcessStartInfo.WorkingDirectory` 设置，不拼接到 Shell 命令中。自定义模式首版只接受 `.exe`/`.com`，参数逐项加入 `ProcessStartInfo.ArgumentList`，不支持自定义 `.cmd`/`.bat`。

首版不安装 Node.js、不全局安装或主动升级 DSH。默认启动的 `npx -y` 在缓存缺失时可以访问 npm registry 并写入当前用户缓存；这是“双击启动且无隐藏交互”的必要取舍，下载进度和失败原因必须显示在启动日志中。

Desktop 0.6.1 将自动 npx 包规格固定为已验证的 `@deepseek-ai/dsh@0.1.0-rc.6`。npm `latest` 检查只提供信息，不得直接改变启动版本；升级该常量前必须完成真实启动、身份探测、停止和重启验证。

### 7.3 服务就绪检测

- 默认地址：`http://127.0.0.1:3080/`
- 首次探测间隔：300 毫秒
- 稳定探测间隔：500 毫秒
- 默认启动超时：300 秒；单次 HTTP 探测仍保持 2 秒短超时
- 最终响应必须为 2xx HTML，并同时包含 `<title>DeepSeek Harness</title>` 与 `window.__DSH_BOOT__` 才确认是 DSH
- 仅端口打开不足以判定页面可用
- 输出中出现其他本机 HTTP URL 时，应优先使用输出地址
- 自动导航只能发生一次，避免每次探测都刷新页面
- 最多跟随 5 次 loopback 到 loopback 的重定向；跳向非 loopback 时返回 `DSH-E204`
- `RunningExternal` 每 5 秒主动探测一次，连续 3 次不可达后进入 `Stopped`，不自动启动新实例

## 8. 停止与重启

### 8.1 停止

停止时执行：

1. 状态切换为 `Stopping`。
2. 取消健康检查和待执行的页面导航。
3. 请求 DSH 子进程退出。
4. 等待不超过 5 秒。
5. 超时后结束整个子进程树。
6. 释放进程对象和输出读取任务。
7. WebView2 导航到本地已停止状态页。
8. 状态切换为 `Stopped`。

所有应用实例必须加入 Windows Job Object 并启用 `KILL_ON_JOB_CLOSE`，防止应用崩溃后遗留 Node.js 子进程。首版固定“桌面宿主退出即停止应用实例”，不提供退出后保留 Owned DSH 的配置。

### 8.2 重启

重启是严格串行的“停止完成后再启动”。禁止在旧进程仍占用端口时启动新进程。

重启后 WebView2 可以保留用户数据目录，但应重新导航到服务 URL。若 DSH 会话由服务端持久化，页面恢复后由官方 Web UI 自行恢复状态。

## 9. WebView2 集成

### 9.1 导航限制

Code 只允许内嵌已经过健康检查和 DSH 身份确认的 loopback origin，重定向和后续主导航保持同源。Chat 只允许精确的 `https://chat.deepseek.com:443` origin；HTTP、用户信息、非默认端口、尾点主机、IDN 混淆和危险协议均不能内嵌。其他合法 HTTP(S) 地址由受控服务交给系统浏览器。

额外登录或验证码 origin 尚未通过真实流程冻结，不使用 `*.deepseek.com` 通配符。系统浏览器与应用专用 Chat profile 不共享 Cookie，因此外部打开不能被描述为应用内登录降级方案。

### 9.2 浏览器数据

WebView2 用户数据存放于：

```text
%LOCALAPPDATA%\DeepSeekHarnessDesktop\WebView2
```

Code 保留现有默认 profile，避免升级后丢失 Harness 页面数据。Chat 使用固定命名 `Chat` profile、关闭 InPrivate，并请求启用 WebView2 原生密码保存和常规自动填充；功能是否实际可用取决于 Runtime 和企业策略。宿主不读取密码、Cookie、Token 或站点存储。

### 9.3 错误处理

- 导航失败时不自动重启 DSH，先重新进行健康检查。
- Code 与 Chat 分别维护恢复预算，一个页面失败不会刷新另一个页面。
- 缺少 WebView2 Runtime 时显示安装说明。
- 页面刷新和 DSH 重启必须是两个独立操作。

Chat 使用独立 `ChatPageState`（未初始化、初始化、就绪、失败、清除中）和 `WEB-E31x` 错误，不向 `HarnessRuntimeState` 增加 Chat 状态。权限请求默认拒绝，下载默认取消，不注册宿主对象、WebMessage 或脚本注入。

## 10. 配置设计

配置文件位置：

```text
%APPDATA%\DeepSeekHarnessDesktop\settings.json
```

建议结构：

```json
{
  "workspacePath": "E:\\Projects\\example",
  "webUrl": "http://127.0.0.1:3080/",
  "launchMode": "auto",
  "customExecutable": null,
  "customArguments": [],
  "startupTimeoutSeconds": 60,
  "autoStart": true,
  "window": {
    "width": 1280,
    "height": 820,
    "maximized": false
  }
}
```

配置写入采用临时文件加原子替换，避免异常退出造成配置损坏。路径、命令和参数必须使用结构化字段存储，不保存为一段可任意拼接的 Shell 文本。

## 11. 日志设计

日志目录：

```text
%LOCALAPPDATA%\DeepSeekHarnessDesktop\logs
```

记录内容：

- 应用启动和退出
- DSH 启动命令类型，不记录敏感环境变量
- 工作目录
- DSH 进程 ID 和退出码
- stdout/stderr
- 服务地址和健康检查结果
- WebView2 导航失败
- 停止、重启和异常退出

日志按日期滚动，默认保留 7 天，每个文件限制最大尺寸。应用界面提供复制日志和打开日志目录功能。

不得记录 API Key、Authorization Header、Cookie 或 WebView2 本地存储内容。

## 12. 安全要求

- WebView2 禁止任意网页调用本机进程管理功能。
- 首版不向网页注入宿主对象或执行自定义 JavaScript。
- 不将用户选择的目录拼接进 Shell 命令。
- 不安装 Node.js，不执行全局 npm 安装或主动升级；默认 `npx -y` 可以按启动请求填充当前用户缓存。
- 不停止未由当前应用创建的进程。
- 外部链接使用系统浏览器打开。
- 应用使用普通用户权限运行，不请求管理员权限。
- 配置自定义命令属于高级功能，需要在设置页明确提示风险。

## 13. 建议代码结构

```text
E:\DeepSeekCLI
├── docs
│   └── deepseek-harness-desktop-development.md
├── src
│   └── DeepSeekHarnessDesktop
│       ├── App.xaml
│       ├── MainWindow.xaml
│       ├── Models
│       │   ├── AppSettings.cs
│       │   └── HarnessState.cs
│       ├── Services
│       │   ├── HarnessProcessManager.cs
│       │   ├── HarnessHealthMonitor.cs
│       │   ├── SettingsService.cs
│       │   ├── LogService.cs
│       │   ├── WebViewEnvironmentProvider.cs
│       │   ├── CodeWebViewService.cs
│       │   └── ChatWebViewService.cs
│       ├── ViewModels
│       │   └── MainWindowViewModel.cs
│       └── Views
│           ├── StartupView.xaml
│           └── LogWindow.xaml
├── tests
│   └── DeepSeekHarnessDesktop.Tests
└── DeepSeekHarnessDesktop.sln
```

UI 使用 MVVM，但不引入超出项目规模的复杂框架。命令绑定、状态通知和资源释放可采用 CommunityToolkit.Mvvm。

## 14. 实施阶段

### 阶段一：最小闭环

- 创建 WPF 项目和 WebView2
- 启动 `npx -y @deepseek-ai/dsh@0.1.0-rc.6 web`
- 捕获日志
- 探测 `127.0.0.1:3080`
- 服务就绪后加载页面
- 实现停止和重启

### 阶段二：可用性

- 工作目录选择和持久化
- 启动状态页和错误页
- 页面刷新
- 日志窗口
- 单实例运行
- 外部 DSH 实例识别

### 阶段三：发布质量

- Windows Job Object
- 退出确认
- WebView2 Runtime 检测
- 自动化测试
- 应用图标和版本信息
- self-contained 发布和安装包

## 15. 测试要求

### 15.1 单元测试

- 状态机合法转换
- DSH 输出 URL 解析
- ANSI 控制序列先清理后解析 URL
- 配置读写和损坏恢复
- 启动命令选择、`-y` 固定参数和 `.cmd` 双层转义
- DSH 页面身份确认、未知 HTTP 服务和重定向边界
- 外部实例主动健康监测及 generation 失效
- 日志脱敏

### 15.2 集成测试

- 正常启动后 Web UI 可访问
- 工作目录正确传递给 DSH
- 启动超时能进入失败状态
- DSH 异常退出能被检测
- 停止后应用创建的进程树全部退出
- 重启期间不会创建两个 DSH 实例
- 已有外部实例时不创建或停止额外进程
- 端口被非 HTTP 服务占用时给出明确错误
- 端口被未知 HTTP 服务占用时返回 `DSH-E205`，不导航、不创建或结束进程
- 无 npx 缓存时不会等待不可见的交互确认
- 外部 DSH 连续失联后自动离开 `RunningExternal`

### 15.3 UI 验证

- 1280x720、1920x1080 和高 DPI 下控件不重叠
- 启动中、运行中、停止中和失败状态显示正确
- 按钮在不合法状态下不可用
- WebView2 填充窗口剩余区域
- 外部链接不会覆盖 Harness 页面

## 16. 验收标准

满足以下条件即可认为首个版本完成：

1. 双击 EXE 后，无需打开终端或回答 npx 控制台提示即可启动 DSH。
2. DSH 就绪后，应用内自动显示官方 Web UI。
3. 用户能够选择工作目录，并在重启应用后继续使用该目录。
4. 页面刷新不重启 DSH。
5. 停止操作能结束当前应用创建的 DSH 进程树。
6. 重启操作能在旧实例退出后恢复 Web UI。
7. 启动失败时能够看到可理解的错误和完整日志。
8. 应用不会结束外部创建的 DSH 实例。
9. 应用关闭后不存在由本应用遗留的 Node.js 子进程。
10. API Key 和浏览器凭据不会写入应用日志。

## 17. 后续可选功能

以下功能不属于首版：

- 系统托盘和后台常驻
- 多个 DSH 工作区实例
- 远程 Harness 地址
- 自动检查 DSH 新版本
- 开机自动启动
- 页面缩放和开发者工具开关
- 崩溃报告导出

只有在单实例启动、停止和重启稳定后，才考虑加入多实例支持。

## 18. Desktop 0.2.0 可用性增量

### 18.1 安装引导

依赖诊断通过 `IDependencyDiagnosticsService` 表达 WebView2、全局 `dsh.cmd`、Node.js 和 `npx.cmd` 的 `Available`、`Missing` 或 `Unusable` 状态。只要全局 DSH 可用，Node.js/npx 缺失不构成 `DSH-E101`；否则必须同时具备 Node.js 与 npx。

安装引导是主窗口显示模式，不是新的 `HarnessRuntimeState`。用户确认“准备并启动”后仍调用 `IHarnessLifecycleCoordinator.StartAsync`，因此 npx 下载、取消、进程输出、Job Object 和 generation 防护都使用原有 Owned 生命周期。引导与共享缓冲统一保留最近 1000 行已规范化、限长并脱敏的 desktop/stdout/stderr 日志，同时显示独立的阶段耗时和总耗时。

### 18.2 服务 origin 与端口

`ServiceUriValidator` 是配置、健康探测、输出解析和 WebView2 导航的共同安全原语。配置只接受绝对、无用户信息、无 query/fragment 的 loopback HTTP(S) URI，并规范化到 `/`。默认端口继续使用原命令；非默认端口只生成以下两种受控模板之一：

```text
dsh.cmd web --port <1-65535>
npx.cmd -y @deepseek-ai/dsh@0.1.0-rc.6 web --port <1-65535>
```

`.cmd` 构造器不接受用户参数列表或 Shell 文本。包规格、`web` 和可选数字端口均由程序生成；更新版本锁定不允许用户替换包名、dist-tag 或附加参数。

地址应用由生命周期协调器串行处理：停止/失败状态只原子保存；外部实例先确认新地址身份再替换 watcher，失败则恢复原 watcher；Owned 实例由 UI 确认后保存，并严格执行停止旧进程、两次确认旧端点不可达、启动新进程。启动、停止、重启或初始化期间返回 `DSH-E207`。

### 18.3 手动更新检查

`DshReleaseService` 只访问固定 npm 官方 `latest` endpoint，禁用自动重定向和 Cookie，超时 15 秒，响应上限 64 KiB，只解析合法的 `version` 字段。结果只保存在 `AboutViewModel` 内存中；应用不做启动时请求、不持久化检查时间、不下载、不安装，也不改变启动参数或 Harness 状态。

### 18.5 Desktop 0.4.0 安装体验增量

- `EnvironmentPathProvider` 每次诊断和解析时合并 Machine、User 与 Process PATH，并按 Windows 语义去重，不修改或记录完整环境变量。
- 自动 npx 使用无版本受控模板；新配置默认准备期限为 300 秒。v1 配置中的 60 秒统一迁移为 300 秒，其他合法自定义值保留，主文件与 `.bak` 共用迁移入口。
- `InstallationGuideViewModel` 提供阶段/总计单调计时、详细脱敏日志、复制命令、复制日志、在合法工作目录打开 PowerShell及固定官方链接。
- npm DNS、TLS、registry 和权限失败分别映射为 `DSH-E211` 至 `DSH-E214`；未知 stderr 保持 `DSH-E201`，不做不可靠推断。

### 18.6 Desktop 0.6.1 已验证版本启动修正

- 自动 npx 包规格固定为 `@deepseek-ai/dsh@0.1.0-rc.6`，取代 0.4.0 的无版本模板。
- npm `latest` 仍可手动查询，但结果不修改启动参数；新版本必须经过真实 Windows 生命周期验证后再更新代码常量。

### 18.4 新错误语义

- `DSH-E207`：生命周期忙碌，暂时不能应用服务地址。
- `DSH-E208`：候选本机 DSH 地址不可达。

`DSH-E101`、`DSH-E201` 至 `DSH-E205` 保持原语义。更新检查错误只显示在关于窗口，不进入 Harness 状态机。
