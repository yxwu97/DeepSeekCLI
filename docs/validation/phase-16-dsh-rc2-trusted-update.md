# Phase 16：DSH rc.2 受信私有更新验证

- 日期：2026-08-29
- Desktop：0.11.0
- Bootstrap DSH：`@deepseek-ai/dsh@0.1.1-rc.2`

## 已执行事实

- 官方 registry 元数据：版本 `0.1.1-rc.2`，发布时间 `2026-08-21T12:42:19.422Z`，固定入口 `dsh -> lib/bin.js`。
- 标准 npm peer 解析在 npm 10/11 上长时间停滞；legacy 候选被标准 `npm ci` 明确拒绝，不作为生产资源。
- 将 npm 报告的循环 peer 以精确版本提升为根依赖后，标准 `npm ci --omit=dev --ignore-scripts` 成功，落盘 453 个包；lockfile 共 511 个 package entry。
- 顶层 188 个 `@deepseek-ai/dsh*` 条目全部为 `0.1.1-rc.2`；registry host 仅 `registry.npmjs.org`，全部 resolved 资源均有 integrity。
- install-script 标记仍为 5 个包，但生产使用 `--ignore-scripts`；固定入口 `--version` 返回 `0.1.1-rc.2`，真实 `dsh web` 在独立 loopback 端口通过标题与 rc.2 `globalThis["__DSH_BOOT__"]` 双身份。
- 最终 `package-lock.json` SHA-256：`511069f3506bb0798c7152f4297f80cf73ce19ed028d1944822cd5bc72038850`。
- npm 11.12.1 在真正空 cache 下会重新检查 `use-sync-external-store@1.2.0` 的 React 16-18 peer 与根 React 19 冲突；生产 `package.json` 增加精确 override 绑定到已锁定 `react@19.2.8`。候选以全新 cache 执行标准 `npm ci --omit=dev --ignore-scripts` 安装 453 个包成功，未使用 `--force`/`--legacy-peer-deps`；lockfile 字节和 SHA-256 均未变化。

## 自动化结果

- Debug build：0 warning / 0 error。
- UnitTests：218 项通过。其中 catalog 覆盖签名篡改、未知字段、固定 host、sequence 回放/碰撞、离线缓存重验签、兼容性、latest 零权限、资产长度/hash、Store descriptor、安装提交顺序、生命周期所有权和 About 按钮门禁。
- HarnessHealthMonitor 集成测试：13 项通过。
- HarnessProcessManager/NpmInstallRunner 集成测试：7 项通过。
- 完整 Debug solution 回归：216 项 UnitTests、25 项 IntegrationTests 全部通过；随后新增 2 项 About catalog/latest UI 契约测试，最终完整 UnitTests 为 218 项并全部通过，生产代码未再变更。
- 用户关闭 PID 1780 后，`Verify-Release.ps1 -SkipInteractiveWebView2` 完整通过：Debug/Release build 均为 0 warning / 0 error，Release UnitTests 218/218、IntegrationTests 25/25，真实私有安装、身份 smoke、激活与第二次零 npm 复用通过。WebView2 交互项按显式参数跳过，仍需正常桌面会话人工验证。
- Debug Phase0 真实私有安装通过：官方 registry 标准安装 453 个包，smoke 激活 install id `0.1.1-rc.2-511069f3506bb079`，第二次准备零 npm 复用，私有目录 269,509,461 bytes，进程树均已回收。
- `eng/dsh-catalog/catalog-schema-v1.json` 可解析，`New-DshCatalog.ps1` 通过 PowerShell AST 语法检查；脚本拒绝仓库内私钥路径，只接受证书存储私钥或仓库外 PFX。
- 发布产物：`output/DeepSeekHarnessDesktop-0.11.0-win-x64.zip`，1,590,118 bytes，SHA-256 `CA6DC1237B48B8367C43680D109BAC91083F017C4337BAD273287F3D8BB492CC`。ZIP 主 EXE 文件版本 `0.11.0.0`、产品版本 `0.11.0`，不包含 CoreCLR、Node、npm、npx、`node_modules`、DSH cache 或用户数据。

## 外部发布项

RSA-3072 生产私钥不得进入仓库或本机输出目录。客户端受限 parser、固定源客户端、sequence 状态、资产 downloader、N+1 installer/coordinator 与发布脚本已实现并用测试 key/内存资产验证。当前 DI 使用 `DshCatalogNotConfiguredService` 失败关闭；正式启用远程 catalog 前，必须在受控签名环境生成生产 key、嵌入公钥、冻结 catalog/signature/asset 端点、发布首个资产，并执行无重编译 N+1 演练。
