# 阶段 3：探测与真实 DSH 闭环验收记录

## 完成范围

- `HarnessHealthMonitor` 实现手动重定向、loopback 边界、256 KiB 有界读取和 rc.6 双特征确认。
- `WaitUntilReadyAsync` 支持 fallback URI，并在输出解析到新 URI 后切换候选地址。
- `RuntimeHealthWatcher` 每次只执行一个探测，连续 3 次不可达后发布失联；成功探测会清零计数。
- 协调器初始化即可识别外部 DSH；外部实例失联进入 `Stopped`，不自动创建进程。
- `FakeHarnessServer` 使用真实 loopback TCP 覆盖 DSH、未知服务、重定向、超大响应和不可达分类。

## 自动化结果

| 验证项 | 结果 |
|---|---|
| Debug/Release 构建 | 通过，0 警告/0 错误 |
| 单元测试 | 35 项通过，0 失败，0 跳过 |
| 集成测试 | 14 项通过，0 失败，0 跳过 |
| DSH 双特征 | 仅标题与 `window.__DSH_BOOT__` 同时存在时确认 |
| loopback 内部重定向 | 跟随并返回最终 URI |
| 外部重定向 | 返回 `ExternalRedirect` / `DSH-E204` |
| 未知 HTTP 服务 | 返回 `ReachableUnknown` / `DSH-E205` |
| 外部实例连续失联 | 进入 `Stopped`，创建进程数保持 0 |

## 真实 rc.6 联调

- 桌面宿主从 PATH 解析 `npx.cmd`，成功启动 `@deepseek-ai/dsh@0.1.0-rc.6 web`。
- `http://127.0.0.1:3080/` 通过双特征身份检查。
- 关闭 Owned 桌面宿主后，宿主和 DSH 均退出，`3080` 释放。
- 先独立启动外部 DSH，再启动和关闭桌面宿主，外部 `3080` 保持监听；宿主未停止或接管外部实例。
- 外部验证会话单独终止后，`3080` 正常释放。
