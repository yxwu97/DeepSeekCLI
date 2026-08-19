# Phase 14：DSH 私有首次安装可靠性验证

## 范围

- 日期：2026-08-19
- Desktop：0.10.1 / .NET Framework 4.8 / Windows x64
- DSH：`@deepseek-ai/dsh@0.1.0-rc.6`
- 目标：修复新设备动态 npx 停滞误报 `DSH-E203`、重复下载和 loopback 代理继承。

## 锁定依赖证据

- `eng/dsh-runtime/package-lock.json` 包含 587 个带 resolved/integrity/license 的条目和 5 个 install script。
- 官方 npm registry 与 npmmirror 的独立空 cache `npm ci --omit=dev` 均成功；两次实际平台图为 526 个唯一包，图哈希均为 `23EA708BE109DC8C52304B82612C94F8EF16D1BBA83460FC7C71A2A774B25310`。
- 临时 prefix 模拟手动 `npm install -g @deepseek-ai/dsh@0.1.0-rc.6` 可启动，但动态解析有 193 个 DSH 子包漂移到 rc.7，证明固定顶层版本不能替代完整 lockfile。

## 自动验证

- Debug 全量测试：179 个 UnitTests、23 个 IntegrationTests 通过。
- 安装 runner 覆盖停滞 `DSH-E221`、取消、根进程退出但后代持有输出管道，以及 Job Object 整树回收。
- Store 覆盖激活前不可见、损坏主指针回退、lock 篡改、根版本拒绝和越界清理拒绝；真实 smoke 额外发现并修复“staging 已移动后 Cleanup 误报 reparse point”。
- 默认 loopback handler 断言 `UseProxy = false`；注入 handler、身份双标记、未知 HTTP、外部重定向和响应上限回归保持通过。

## 真实空 cache 闭环

执行：

```powershell
.\eng\Phase0Validation\bin\Debug\net48\DeepSeekHarnessDesktop.Phase0Validation.exe --private-dsh-smoke
```

结果：

- 唯一临时 npm cache 和私有根，registry 为 `https://registry.npmjs.org/`。
- `npm ci --omit=dev` 安装 530 个落盘包，用时 51 秒。
- 私有版本约 264,293,557 bytes（约 252 MiB）。
- 安装后 DSH smoke 与激活后正式启动均返回 HTTP 双身份标记。
- 第二次调用准备服务的 npm 次数仍为 1，即二次准备 0 次 npm/npx、0 次下载。
- 两次 DSH 均由 Job Object 回收，验证进程退出码为 0。

## 最终发布门禁

- 执行 `eng/Verify-Release.ps1 -SkipInteractiveWebView2`，Debug/Release 构建均为 0 warning、0 error；179 个 UnitTests 和 23 个 IntegrationTests 全部通过。
- Release 门禁再次从独立空 cache 安装 530 个包，安装后 smoke、激活后正式启动和第二次免 npm/npx 复用全部通过。
- `DeepSeekHarnessDesktop-0.10.1-win-x64.zip` 为 1,560,914 bytes，SHA-256 为 `A22B2490CCC0A9AE1F052D6DF02C097B0F59FEBAC52D35DBEE1B5748F7F7EA30`；主 EXE 为 415,744 bytes，文件版本 `0.10.1.0`、产品版本 `0.10.1`。
- ZIP 仅包含约 367 KiB 的 package/lock 资源，不含 Node、npm、npx、`node_modules`、CoreCLR、DSH cache 或用户数据；报告 schemaVersion 为 5。
- 受限验证沙箱曾使 registry 请求返回 `ECONNREFUSED`；在获准联网的相同命令中通过。该失败未激活半成品，且与 Desktop 进程托管无关。

## 待外部人工验收

- Windows 10 x64 干净用户首次/二次启动。
- Windows 11 x64 干净用户首次/二次启动。
- 缺少 WebView2、Node.js 时的逐项安装引导，以及正常桌面会话中的 Code/Chat WebView2 交互 smoke。
- 100%、125%、150% DPI 与最小窗口下的安装引导布局。
- 企业系统代理和受限 ACL 设备上的真实故障文案。

这些项目需要对应设备，未用当前 Windows 开发机结果替代。发布门禁会记录显式跳过项。
