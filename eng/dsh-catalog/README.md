# DSH 签名目录发布

本目录只保存公开 schema 和发布脚本，不保存生产证书、私钥、PFX 密码或生成后的发布资产。

`New-DshCatalog.ps1` 接受经过独立空缓存安装、固定入口版本检查和真实 Web 身份 smoke 的 `package.json` / `package-lock.json`。脚本重新检查精确 DSH 版本与 lockfile v3，复制公开资产，生成无 BOM 的 catalog 原始字节，并使用 RSA-3072（或更高）证书执行 SHA-256 / PKCS#1 detached 签名。

生产签名只能使用以下来源之一：

- 当前用户证书存储 `Cert:\CurrentUser\My` 中由 thumbprint 精确选择的私钥证书；
- 仓库目录之外的 PFX。脚本会拒绝仓库内路径，且不会复制证书到输出目录。

示例（证书存储）：

```powershell
.\eng\dsh-catalog\New-DshCatalog.ps1 `
  -Version 0.1.1-rc.3 `
  -CatalogSequence 2 `
  -SigningKeyId dsh-prod-2026 `
  -AssetBaseUri https://fixed.example/dsh-assets/ `
  -PackagePath C:\validated\package.json `
  -LockPath C:\validated\package-lock.json `
  -OutputDirectory C:\publish\dsh-catalog `
  -CertificateThumbprint 0123456789ABCDEF0123456789ABCDEF01234567
```

更新已有目录时必须同时传入 `PreviousCatalogPath` 与 `PreviousSignaturePath`。脚本会先用同一证书验证旧目录，要求新 sequence 严格递增，然后保留其他条目并替换同版本条目。

发布前还必须在未修改 Desktop 二进制的环境中完成 N -> N+1 客户端演练。正式公钥、固定 catalog/signature URL 和固定 asset host 需要进入 Desktop 源码并随 Bootstrap 发布；这些信任参数不能来自用户配置或远端响应。
