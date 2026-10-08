# Android / CodexLauncher 联调契约

联调对象：`E:/Workspace/project/AI-Usage-Monitor/repo` 与当前 CodexLauncher 工作树。Android 聊天：`01a1152b-3928-7ce0-b5c1-c5316678abd5`。

当前发现的阻断点：Android 既有 Go Bridge 协议与此 Windows 桥不一致。Android 聊天正在补兼容实现；电脑端保留本机审批和当前契约。

## Windows 桥的实际协议

- 二维码：`https://<LAN IPv4>:43189/v1/device#<percent-encoded JSON>`。
- JSON 字段：`schemaVersion: 1`、`bridgeId`（UUID）、`endpoint`（HTTPS）、`certificateSha256`（整张 X.509 DER 证书 SHA-256，64 位十六进制）、`pairToken`（32 字节的无填充 base64url，43 字符）、`expiresAt`（UTC RFC3339）。不要按旧 Go 协议的 `br_`、64 位十六进制 pairToken 或 SPKI 摘要解析。
- `POST /v1/pair` 请求：`{pairToken, deviceName}`。返回 `202`：`{pairId, sessionToken, state:"PendingApproval", expiresAt}`。
- 本机批准前 `GET /v1/pair/{pairId}`，带 `Authorization: Bearer <sessionToken>`，返回 `PendingApproval` 且没有设备令牌。
- 本机批准后同一路径返回 `Approved`、`deviceId`、`deviceToken`。拒绝为 `Rejected`；过期需重新扫码。
- 保存设备令牌后 `POST /v1/pair/{pairId}/ack`，仍带会话令牌，返回 `204`。首次正常设备认证也会完成交付。之后 `Delivered` 不再返回设备令牌。
- 用设备 Bearer 调用 `GET /v1/accounts` 获得获准账号数组（账号字段 `accountId` 是匿名 ID，不是固定 `codex`）。额度：`GET /v1/accounts/{accountId}/usage`；刷新：`POST /v1/accounts/{accountId}/refresh`。
- TLS 凭据发送前必须匹配邀请中的完整证书摘要。旧 Go 桥的 SPKI 钉定需要按协议区分，不能混用。
- 一次性邀请有效 2 分钟；审批有效 5 分钟；批准后的设备令牌领取窗口 2 分钟。禁止日志输出邀请、会话令牌或设备令牌。

## 电脑端已验证与待验收

当前 App 构建路径：`CodexLauncher.App/bin/AgentVerifyHardware/Debug/net10.0-windows/CodexLauncher.exe`。

已实机验证 WLAN 硬件标志、选定网卡地址及 Public / Private 条件检查通过；不是实际手机连接验收。实体识别改用 `GetIfEntry2`，不会依赖 WMI。防火墙命令只生成文本，支持 Public / Private，并限制当前程序、监听 IPv4、端口和 LocalSubnet。

联调时完全退出旧实例，启动新版、刷新并选择推荐 WLAN、应用后生成新二维码。手机提交后在电脑“待确认申请”中批准。主窗口应已开启额度监测且账号可识别，否则配对成功也不能返回获准额度。

当前本会话的 ADB 查询未发现已连接设备；真机 Wi-Fi 可达性、防火墙放行、实际扫码尚待验证。模拟器访问宿主可用 ADB 端口转发或宿主别名，但不能把回环验证当成真机局域网验收。

## 合成跨项目测试服务

电脑端新增仅存在于测试工程的 `--bridge-interop-server` 模式，真实 Kestrel + TLS + 当前配对服务，账号与额度均合成，监听严格限定 `127.0.0.1:43190`。审批延迟 3 秒，因此手机必须实现等待审批。不会修改正式应用配置或真实设备授权。

构建目录：`CodexLauncher.Tests/bin/AgentInterop/Debug/net10.0-windows/`。启动：`dotnet CodexLauncher.Tests/bin/AgentInterop/Debug/net10.0-windows/CodexLauncher.Tests.dll --bridge-interop-server`。测试需要正常 Windows 用户 DPAPI/ACL 权限，沙箱环境可能拒绝。

启动后控制台仅打印当前用户临时测试目录路径，其中 `desktop-invitation.txt` 供宿主 JVM 测试读取，`emulator-invitation.txt` 供 Android 模拟器（地址 `10.0.2.2`）读取；相应 `.txt.png` 文件可放入模拟器相册实际识别。文件含测试用一次性邀请，禁止输出或提交其内容。每分钟更新，服务最多运行 15 分钟并在退出时删除邀请与图片。真机不能使用这些回环邀请，必须改用正式应用的局域网二维码和人工批准。

## 2026-10-08 联调结果

- 电脑端最终测试工程构建成功；两项真实回环 Kestrel HTTPS 协议回归通过，覆盖授权、缓存读取、刷新、账号隔离、申请、批准、拒绝、重放和过期。
- 临时服务先发生 Schannel 握手问题；切换为正式 `BridgeIdentityStore` 生成并加载证书后，HTTPS `/v1/health` 返回 `READY`。测试服务不再另造证书加载路径。
- 安卓端聊天报告兼容修改后的 524 项回归通过，调试 APK 构建成功并安装到 `emulator-5554`。跨聊天工作通过本机日志和本契约核对。
- 安卓模拟器实际发起配对，电脑端合成服务延迟 3 秒批准；仅检查临时设备仓库数量与交付状态：`SyntheticDevices=1`、`CompletedDeliveries=1`。不输出令牌、哈希或邀请。
- 本聊天独立通过 ADB UI 层只读核对账户 `CodexLauncher_Test` 的详情页，实际显示 **5 小时剩余 78%、每周剩余 19%**，与合成上游 `usedPercent=22/81` 一致。
- 安卓端聊天最终确认：重启 App 后账户和令牌保留，仍能查询 78% / 19%；最终 526 项回归通过。本聊天也独立核对了新增的后续查询记录。
- 验收范围：模拟器 → 真实电脑桥 HTTPS/TLS → 配对审批 → 设备凭据交付 → 授权账户 → 额度显示。测试审批是测试工程自动完成，不是正式主窗口的人工点击验收。
- 未验收：真实手机 Wi-Fi、防火墙放行、真实 Codex 账号读取与正式窗口人工批准。测试服务的自动批准只存在于测试工程，正式窗口仍必须由本机用户批准。

用户实际使用时，启动最新电脑 App、开启额度监测及手机共享、选择推荐网卡后应用并生成新二维码；安卓兼容版进入“添加账户 → OpenAI Codex → 扫码连接电脑”，然后电脑端批准待确认申请。二维码过期需重新生成。模拟器测试账号不能替代真实账号。
