# Phase 13：.NET Framework 4.8 迁移验证

验证日期：2026-08-18
版本：0.10.0
目标框架：`net48` / Windows x64

## 验证结果

- `dotnet restore DeepSeekHarnessDesktop.sln`：通过。
- Debug 与 Release 全解决方案构建：通过，0 警告、0 错误。
- 单元测试：164/164 通过。
- Windows 集成测试：20/20 通过，覆盖挂起创建、参数转义、立即退出、取消竞态和 Job Object 子进程树回收。
- `eng/Verify-Release.ps1 -SkipInteractiveWebView2`：通过。
- 发布 ZIP：`output/DeepSeekHarnessDesktop-0.10.0-win-x64.zip`，1,471,458 字节。
- 主 EXE：355,328 字节；文件版本 `0.10.0.0`，产品版本 `0.10.0`。
- 发布包不含 `.runtimeconfig.json`、`.deps.json`、CoreCLR、hostfxr、hostpolicy、Node.js 或 DSH。

## 真实 DSH 启动

从发布目录直接启动 `DeepSeekHarnessDesktop.exe`，当前配置启用 AutoStart，测试前 `127.0.0.1:3080` 空闲。

- Desktop 直接选择 PATH 中的 `node.exe` 和现有标准 `_npx` 缓存内固定 `@deepseek-ai/dsh` 入口。
- 实际命令为 `node.exe <npm-cache>\node_modules\@deepseek-ai\dsh\lib\bin.js web`，未调用 npx，未发生下载。
- 根页面返回 HTTP 200，并同时包含 `<title>DeepSeek Harness</title>` 与 `window.__DSH_BOOT__`。
- 终止本次测试启动的 Desktop 后，Owned DSH 进程随 Job Object 退出，端口 3080 在 10 秒门限内释放。

## 未执行项

本轮发布门禁显式跳过需要人工桌面交互的 Code/Chat WebView2 smoke；应用启动阶段已成功创建窗口并完成真实 DSH 自动启动。Chat 登录态、缩放和交互仍需正常桌面会话人工验收。
