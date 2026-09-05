# Codex Cache HUD

Windows 本地缓存命中悬浮窗，基于 [Codex Monitor HUD](https://github.com/LH-03/codex-monitor-hud) v3.1.0 / `ace7a89bdb2692eec4f974ef8323865c22a4e68c` 改造，保留 MIT 许可证和原作者版权声明。本工具为社区项目，不代表 OpenAI 官方。

## 使用

在 [Releases](../../releases) 下载 Windows x64 ZIP，完整解压后双击 `CodexCacheHUD.exe`；也可以为它创建桌面快捷方式。支持 Windows 10/11 x64，内含运行时，无需单独安装 .NET。需本机已经存在 Codex 会话数据。

无需 API key、登录账号、管理员权限或安装服务；不修改 Codex 配置及更新机制。程序目前没有代码签名；如果安全软件报警，请保留保护并报告版本、检测名称与文件哈希。

## 指标

- **左侧“上下文增长”：** 启动时为已发现的每个会话建立基线，从零累计 `last_token_usage.total_tokens` 的正增长。新建会话从零计入，上下文压缩不倒扣。关闭即清零，重开重新计算。它是观察到的上下文增长量，**不是计费 tokens、账单或套餐额度**。
- **右侧命中率：** 最新一次真实调用的 `cached_input_tokens / input_tokens`，与曲线最后一个点完全一致，不使用累计平均值。
- **曲线：** 合并已发现主 Agent、子 Agent 最近 30 次真实调用。横轴是调用顺序，不是等长时间间隔；没有调用就不补造数据。
- **空闲、暂停或新任务尚未返回首次调用：** 数值显示 `--`，历史曲线淡化。悬停显示状态、最近调用时间和距今秒数。
- **颜色：** 98% 及以上绿色，95% 至不足 98% 黄色，90% 至不足 95% 红色，低于 90% 深红色；数值和整条曲线采用同一末次调用颜色。

数据来自 Codex 已写入本机的记录，不会在调用完成前预测 tokens。发现范围有限（默认最近 30 分钟）；文件锁定、缺失、格式变更及写入延迟会影响覆盖。悬停额度仅是本地最近观察到的快照，不是联网查询。上下文正增长可能漏过两次观察间的下降和回升，因此不应作为精确用量账本。

## 调整

尺寸有 20%、40%、60%、78%、100%；背景可全透明或选择 20%–100% 可见度。全透明时仅顶端与文字等高的横条接收鼠标并显示设置，曲线区域点击穿透。右键或托盘可暂停、设置和退出。悬停左侧可查看统计说明及拆分。

配置保存在 `%LOCALAPPDATA%\CodexMonitorHUD`。最近调用只保留在内存，默认不开机启动、不保存调用历史、不启用调试日志。详见 [PRIVACY.md](PRIVACY.md)。

## 构建

使用符合 `global.json` 的官方 .NET 10 SDK，在仓库根目录运行：

```powershell
dotnet run --project tests-dotnet/CodexMonitorHud.Core.Tests -c Release
dotnet run --project tests-dotnet/CodexMonitorHud.App.Tests -c Release
pwsh -File scripts/Publish-Windows.ps1
```

输出为 `artifacts/` 中的程序 ZIP 与 SHA-256 校验文件。测试使用合成数据，不读取个人会话；WPF 检查不弹出窗口。公开仓库不包含旧版安装脚本、SDK、个人截图、运行日志或本机工作历史。
