# 阶段 6：托盘与账户信息验收记录

## 官方 API 能力基线

- 官方 API 参考公开 `GET https://api.deepseek.com/user/balance`，使用 Bearer Token 认证。
- 返回字段包括 `is_available` 和按币种列出的 `balance_infos`。
- 官方 Token 文档说明实际 Token 数以每次模型返回的 `usage` 为准。
- 截至 2026-08-16，官方 API 参考未公开账号资料或账户级历史 Token 统计端点。

## 验收范围

- 普通关闭隐藏到系统托盘，不停止 Owned DSH。
- 托盘双击及“打开”菜单恢复并激活主窗口。
- 托盘“退出”执行原有 8 秒清理、配置保存及 Job Object 兜底。
- 系统注销/关机不进入隐藏路径。
- API Key 按 DSH 的环境、受管凭据、工作区 `.env`、用户 `.env` 顺序自动解析，也可单次手工覆盖；余额请求固定发往官方 HTTPS 端点。
- 账户窗口展示连接状态、Key 掩码、余额可用性和分币种余额。
- Token 区准确说明官方没有账户级历史统计；仅按需读取 DSH 的 DeepSeek Key，不读取 WebView2 凭据。

## 验证结果

实施完成后填写自动化、桌面交互和发布产物结果。
