# 阶段五实施与验证记录

日期：2026-10-02。依据补充规格 D-9、D-10、D-18、D-22 和 `stage-five-plan.md`。本次接续已有阶段五实现，完成启动竞争、主题输入控件和审查问题修复及最终验证。代码集成完成，实机验收与阶段六发行未完成。

## 已实现

- 主窗口“AI 额度桥”分区：默认关闭的服务开关、明确网卡选择、端口、状态与证书指纹、内存二维码与倒计时、申请批准/拒绝、设备重命名/撤销/全部撤销、账号昵称与逐设备授权、连接说明和防火墙命令复制。
- 二维码隐藏、过期、被消费或端点重绑时清除图像及控件邀请状态，不落盘、不写日志。
- `BridgeRuntime` 拥有身份、设备、授权和 LAN 生命周期；主窗口持有唯一共享额度协调器。网卡选择和账号昵称分别保存在 DPAPI 文件，明文设置仅新增桥开关与端口。
- 当前用户自启命令使用 `--background`，兼容旧的带引号无参命令。后台实例隐藏主窗，只初始化额度与桥；恢复主窗后才执行原代理诊断。普通关窗隐藏继续共享，显式退出先清理桥再释放额度。
- 独立后台单实例信号不请求显示主窗口；没有保存的网卡时要求用户明确选择，不自动选网卡。

## 修复与审查

主题回归先失败 4/5：默认系统输入颜色未被深色主题映射。显式使用主题颜色修复 ComboBox、NumericUpDown、账号昵称和重命名输入；检查嵌套编辑控件后专项 5/5。

提前到达的后台信号先失败 4/5（UI operation timed out）：监听在句柄创建前消费事件，`BeginInvoke` 失败后退出。改为 `HandleCreated` 后启动监听，并以标记避免重复监听；命名事件保留提前信号。复现测试在 `Show` 前发送事件并等待，修复后 5/5。

独立只读审查 `/root/stage_five_final_review`：Critical 0，Important 1，Minor 1。此前审查代理在环境切换后不可访问且未返回结果，本次使用一个可返回结果的新审查代理；未进行二次复审。

Important 已修复：隐藏后台实例收到信号时，尚未载入的代理控件默认值覆盖已有代理 URL、Mihomo 路径和 Party/Verge 端口。测试预存独立连接值并断言完整设置除桥开关外不变，先失败 4/5；修复为连接设置尚未初始化时从当前保存记录保留这四个字段，随后 5/5。该保护同时覆盖后台托盘偏好保存。

Minor 延后：首次默认关闭时网卡列表需手动点击“刷新网卡”；直接先勾选应用会提示选择网卡。代价是首次操作多一步刷新，不影响明确选择或安全边界。

## 最终验证

- 隔离 Release 构建：退出码 0，0 警告、0 错误。
- 阶段五专项：5/5；涵盖启动命令、双事件、DPAPI 设置/设备、主题控件、提前信号、隐藏窗口、真实回环 HTTPS、QR 消费/隐藏和退出端口释放。
- 最终全量：129/129，退出码 0。
- OpenAPI 3.1、六个合成响应 JSON Schema、额外身份字段与非法状态负例校验通过。

原 Release 目录被用户正在运行的启动器锁定，因此使用独立输出，不停止用户进程。最终构建命令：

```powershell
dotnet build CodexLauncher.slnx -c Release --no-restore -m:1 -nr:false -p:UseSharedCompilation=false -p:DirectoryBuildPropsPath=D:/Workspace/Codex/project/Codex启动器/.superpowers/sdd/2026-10-01-ai-usage-bridge/stage-five-isolated.props
dotnet .superpowers/sdd/2026-10-01-ai-usage-bridge/bin/CodexLauncher.Tests/Release/net10.0-windows/CodexLauncher.Tests.dll
node .superpowers/sdd/2026-10-01-ai-usage-bridge/validate-contracts.mjs
```

证据在项目 `.superpowers/sdd/2026-10-01-ai-usage-bridge/`：`stage-five-build.log`、`stage-five-full-suite.log`，保留独立输出与 props 文件；工作区无 Git 元数据。

隔离产物 SHA-256：

| 文件 | SHA-256 |
| --- | --- |
| Core DLL | `42382000C8122444373A1C1D0ABCD77BDE0A123539254F4FD206923A9EB606C6` |
| App DLL | `673EA0019FA690EB12490169A6E3BC6EEE0780C822C8E57BACB460887B5E20B2` |

## 决策与实机边界

Ruling：D-22 主窗口分区优先于旧计划独立共享对话框；托盘恢复主窗。代价：共享操作需要进入主窗口。

Ruling：D-9 敏感网卡选择另存 DPAPI，没有合法保存网卡不自动选择。代价：首次开启须明确选卡。

Ruling：后台仅启动额度和桥，常规诊断在首次恢复主窗后初始化；保存未初始化的连接字段时保留磁盘值。代价：隐藏期间没有原窗口诊断刷新。

Ruling：合成测试只证明程序路径，不能替代真实环境验收。代价：Windows 登录自启、注销/关机、物理 Private 网卡正向手机连接、休眠恢复、浏览器证书操作、真实 100%—200% DPI 仍未验证。STA `Scale(2,2)` 只证明缩放操作可执行，不能宣称真实 DPI 可用。

Ruling：保留第四阶段 `MDNS_ISOLATION_UNVERIFIED` 门禁，二维码直连路径存在但不等于实机验收通过。代价：自动发现不能启用，须修正并提供实际网卡/TUN 出站抓包证据。此前延后的发现终止清理失败与每请求同步网络验证开销仍待处理，开启真实发现前先解决清理项。

未启用真实账号共享，未修改实际自启注册表、防火墙、代理或 TUN，未发布或更新 `dist`。下一阶段是协议测试客户端、依赖许可证/连接文档、自包含发行候选与整体验收；当前运行程序不会因本次隔离构建自动更新。
