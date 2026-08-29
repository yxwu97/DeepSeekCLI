# Phase 15：DSH rc.7 手动更新与启动固化验证

- 日期：2026-08-21
- Desktop：`0.10.2`
- 目标 DSH：`@deepseek-ai/dsh@0.1.0-rc.7`
- npm registry：`https://registry.npmjs.org/`

## 上游与锁图

- npm 元数据确认 rc.7 顶层包的 `dsh` bin 为 `lib/bin.js`，integrity 为 `sha512-ZceDCJ8FAywih+USW/OMk9jEhunlvJBGEz4kqrhau23hPzbciOazZrywH0nBRsaalSeAJ1JGBmjtw4OSjToStw==`。
- 普通时间点解析会把范围型 DSH 子包带到 rc.8；`--legacy-peer-deps` 又会遗漏运行必需的 `@deepseek-ai/cordis-plugin-group`，两者均未采用。
- 最终 lockfile 使用 `--before=2026-08-18T00:00:00.000Z` 解析 rc.7 当时的完整 peer 图，共 588 个 package 条目；所有 `@deepseek-ai/dsh*` 已解析条目均为 rc.7。
- `eng/dsh-runtime/package-lock.json` 大小 367,248 字节，候选生成时 SHA-256 为 `FC5CA8D599B7CDE15A5BD989B033F5556E66AEB55CD24ED645908FE3B40701EF`。

## 真实安装与启动

- 使用唯一空 npm cache 执行 `npm ci --omit=dev`，24 秒安装 530 个包并正常退出。
- 直接运行私有固定入口 `node lib/bin.js --version` 返回 `0.1.0-rc.7`。
- 以固定入口启动 `web --port 38123` 后 HTTP 返回 200，HTML 同时包含 `<title>DeepSeek Harness</title>` 与 `window.__DSH_BOOT__`。
- 停止验证进程后端口 38123 已释放，无遗留验证进程。

## 自动化覆盖

- 全局候选只接受精确 rc.7；rc.6、rc.8、稳定版或探测失败时继续选择私有/缓存 rc.7。
- 版本探测覆盖多行、非法、超长、非零退出、超时和调用方取消；超时/取消均通过 Job Object 回收本次创建的进程树。
- npm latest 高于 rc.7 时只更新关于窗口展示，不修改验证常量、诊断或启动候选。
- 发布门禁核对代码常量、package 根依赖、lock 根依赖、完整 DSH 已解析图和发布资源哈希，并执行真实私有安装与第二次零 npm 复用 smoke。
- `Verify-Release.ps1 -SkipInteractiveWebView2` 已通过：UnitTests 191/191、IntegrationTests 23/23，真实私有安装 37 秒完成 530 个包，第二次准备未调用 npm。
- 发布 ZIP 为 `DeepSeekHarnessDesktop-0.10.2-win-x64.zip`，大小 1,565,404 字节，SHA-256 为 `AAE8D40C0A1FF49AF0D6BDF9AFF794604D838B3C625AFB411B5BF1BB6D129756`。

## 仍需外部验收

- 在独立 Windows 10/11 x64 用户环境手动执行固定全局更新命令，重新检查后验证全局来源、启动、停止和重启。
- 在 100%、125% 和 150% DPI 下确认关于窗口新增“Desktop 验证 DSH”行无裁切。
- 本机交互 WebView2 门禁因 `CreateCoreWebView2ControllerAsync` 返回 `E_UNEXPECTED (0x8000FFFF)` 未通过；需在正常桌面会话重新执行该单项，不影响已通过的 DSH、测试和发布内容门禁。
