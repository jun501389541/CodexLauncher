# AI Usage Bridge v1 对接契约

本目录包含完整 v1 契约与合成样例。阶段二完成 HTTPS，阶段三完成配对授权，阶段四实现固定网卡网络生命周期与发现适配器；真实 Kestrel 回环 HTTPS 验证重绑时身份稳定和旧邀请失效。阶段五已接入主窗口启用、二维码、审批、设备管理与后台启动，见 [阶段五记录](stage-five-report.md)。mDNS 完整出站隔离尚未验证，生产默认阻塞；实机手机与发行验收仍待后续阶段。权威规格为 [10 月 2 日补充规格](../superpowers/specs/2026-10-02-ai-usage-bridge-spec.md)，它覆盖上游计划冲突处。

## 文件与校验

- `openapi.json`：OpenAPI 3.1，包含全部 `/v1/` 路径、认证方式、请求/响应及错误。
- `usage-result.schema.json`：JSON Schema Draft 2020-12，对外额度响应的允许字段列表。
- `examples/`：六种状态的纯合成响应；示例地址 `192.0.2.1` 仅供文档使用。
- `generate-contracts.mjs`：从项目根执行 `node docs/bridge/generate-contracts.mjs` 可重生成契约。
- `stage-one-report.md` 至 `stage-six-report.md`：分阶段实施、验证证据、依赖阻塞与后续交付边界。
- `THIRD-PARTY-NOTICES.md` 与 `licenses/`：阶段六 Windows 发布候选携带的 NuGet 依赖清单及许可证文本。

手机数据只接收 `UsageResult`，不直接序列化 `QuotaSnapshot`。账号昵称默认 `Codex账号`；匿名 ID 用至少 32 字节本地秘密对有序身份字段做 HMAC-SHA256。阶段二身份仓库将 bridgeId、私钥与映射密钥保存在当前用户 DPAPI 保护的原子文件，目录 ACL 仅授权当前用户；设置明文只保存开关与端口。损坏或缺失生成新身份并返回重置标志。

## 手机连接与配对

二维码契约为 OpenAPI `Invitation`：版本、Bridge ID、HTTPS 地址、SHA-256 证书指纹、一次性配对码、UTC 失效时间。浏览器扫描路径应把邀请信息放入 `/v1/device` 的 URL **fragment**，由页面解析并立即清除 fragment；不把设备或配对会话令牌放进 URL、查询字符串或 Referer。长期令牌仅用 `Authorization: Bearer …`。

`/v1/device` 是公开空壳，仅展示连接和证书信息、配对状态和表单。二维码期望指纹与页面显示的服务端证书指纹并列，用户人工比对后才提交码。**浏览器没有 TLS pinning API，服务端显示值也不能独立证明浏览器实际对端证书；高保证客户端须在 TLS 验证回调中核对证书，浏览器用户须通过证书查看器核对实际证书。** 若攻击者同时控制 TLS 终端和发现消息，且用户未比对，仍存在中间人风险。两分钟单次码加电脑确认缩短风险窗口，但不消除风险（C-2）。

流程：

1. 电脑生成 `Invited`，两分钟有效；`POST /v1/pair` 原子占用一次性码，返回 `202`、随机 `pairId` 和独立 `sessionToken`。
2. 状态变为 `PendingApproval`，五分钟内须电脑允许或拒绝；之后只用会话 Bearer Token 查询，邀请不可重用。
3. 电脑批准后变为 `Approved`；`GET /v1/pair/{id}` 通过 HTTPS 领取独立 256 位设备令牌，明文在内存最多两分钟，可以重试领取。
4. ACK 或首次成功设备认证变为 `Delivered`，删除明文；之后领取响应 `deviceToken = null`。超时撤销未完成设备，需重配对。
5. 拒绝、过期、停桥、重启和身份重置清除未完成配对；已完成设备配对与登录独立。最多 20 设备。

## 读取、刷新与账号切换

- 普通 GET 只读取共享内存快照，不触发上游。provider 的五分钟循环、退避、十秒手动节流仍由同一所有者维护。
- 刷新接口立即返回 `202 + 当前 UsageResult`，后台触发共享手动刷新，客户端之后 GET；请求断开仅放弃等待，不取消上游。
- `usedPercent` 保存原始数据，允许越界与小数；`remainingPercent` 在 0–100 内，缺失时 null。桌面原有整数条形图仍保留整数显示，网络 DTO 保留小数剩余比例。
- `id` 是转义额度桶 ID 加 `primary/secondary`，不能用中文窗口名充当身份。重置到点保持实际值，等待查询确认。
- `updatedAt` 是最近查询/状态更新；`dataTimestamp` 是最近成功采集额度的时间，失败保留；`sourceTimestamp` 只取上游同名显式字段，未给出时 null，不用本机时钟冒充。当前官方示例未保证源时间或根 `accountId` 存在，这些字段按可选兼容读取。
- 全部时间输出 UTC RFC3339，以 `Z` 结尾；桌面重置文案转换成本地时间。
- 单 provider 固定 `codex`，账号列表仅 0/1。账号身份字段有任何变化都会撤销现有账号授权，切回旧账号也需电脑再确认（D-18）；设备本身保留。
- 每次成功读取 ChatGPT 额度后再次只读核对账号身份，检测两次调用间换号；显式额度根 `accountId` 与优先稳定 ID 冲突时丢弃数据、清缓存，规范错误为 `ACCOUNT_IDENTITY_CONFLICT`。这些 ID 的上游语义未被保证相同时，采取保守拒绝，不能猜测后继续共享。
- 无邮箱、工作区 ID 和稳定 ID 的 API Key/Unknown 等账号不可新授权，`Kind` 本身不能识别一个人。邮箱、工作区和稳定 ID 使用精确比较，以满足“任何变化”失效要求。

错误边界：无效设备 `401 DEVICE_INVALID`；有效设备未获当前账号授权 `403 ACCOUNT_REAUTHORIZATION_REQUIRED`；请求旧匿名账号 ID `409 ACCOUNT_CHANGED`；不可识别账号的 usage `403 ACCOUNT_UNIDENTIFIABLE`，accounts 返回空数组。阶段三设备仓库只持久化设备 bearer 的 SHA-256，账号授权以 DPAPI 文件保存；设备列表和撤销通过本地服务接缝提供，测试合成认证器仍仅存在测试工程。错误只输出规范化 `errorCode`，没有原始异常或账号字段。

## 状态、关闭监测与暴露范围

只有 `OK / STALE / AUTH_REQUIRED / NO_DATA / NETWORK_ERROR / UNSUPPORTED` 六个状态。监测关闭返回 `NO_DATA + QUOTA_MONITORING_DISABLED`，不启动 provider。可用空窗口映射 `NO_DATA`，不是 `OK`。

默认桥关闭。监听按明确的物理私有 IPv4 地址绑定，默认端口 43189。Windows 网络类型支持 Public（公用）与 Private（专用），不必为了共享修改系统网络类型；DomainAuthenticated 暂不支持。实体网卡或网络类型无法验证时暂停，每次请求重新检查。阶段四固定 adapterId，在地址变化时停旧再绑新，休眠暂停、恢复重验，证书与 bridgeId 保持稳定，旧邀请失效。真实局域网正向监听尚未验收。绝不退回全地址监听/全网卡广播。发现类型 `_aiusage._tcp.local`，仅含 Bridge ID、协议版本和定位信息；默认 `MDNS_ISOLATION_UNVERIFIED` 在创建 socket 前阻塞，二维码直连保留。Makaretu 的接口筛选不足以证明传统单播回复路径的出站隔离，须实际修正与验证后再启用。

“可用 / 推荐”表示网卡符合本机监听策略，不表示已验证手机能穿过防火墙访问。手机与电脑需在同一局域网；配对仍要求 HTTPS、一次性邀请、本机批准和设备授权。共享运行后可复制防火墙命令，规则覆盖 Public / Private，限定当前程序、实际监听 IPv4、TCP 端口和 LocalSubnet 来源。启动器只生成命令文本，用户检查后自行在管理员终端执行；不会自动修改规则或系统网络类型。监听地址或程序路径变化后需重新生成规则，并自行移除不再使用的旧规则。较早阶段报告里的 Private-only 描述记录当时行为，以本段为当前实现说明。

实体网卡通过 Windows IP Helper 的 `ConvertInterfaceGuidToLuid` / `GetIfEntry2` 按选定 GUID 查询，要求 `HardwareInterface` 标志且排除 `FilterInterface`；不再依赖 WMI 服务或其 COM 自动化权限。网络类型仍由 Network List Manager 验证。读取失败会区分硬件信息（含 Windows 错误码）与网络类型读取阶段。

HTTP 层执行 16 KiB 请求体上限（包含分块与 HTTP/2 无长度正文）、每设备 60 次/分钟及配对每来源 5 次/分钟滑动窗口，并返回 `429 + Retry-After`。同源页可访问，不开放宽松 CORS，并拒绝跨站请求；不能只靠不设置 CORS 阻止跨站提交。产品日志复用 DiagnosticLogger 的 `bridge-*` 分类，不写请求体、认证头、二维码或额度原文。

## 构建

常规联网环境：

```powershell
dotnet restore CodexLauncher.slnx --locked-mode
dotnet build CodexLauncher.slnx -c Release --no-restore -m:1 -nr:false
dotnet run --project CodexLauncher.Tests/CodexLauncher.Tests.csproj -c Release --no-build
```

本机 Schannel/NuGet 沙箱限制场景：已经准备的工作区包源可执行 `dotnet restore CodexLauncher.slnx --configfile NuGet.Bridge.Config --locked-mode -m:1 -nr:false`。`.superpowers/` 仅本机缓存，不随源码提交；新机器使用正常 NuGet 还原。HTTPS 探针需要 Windows 临时用户密钥容器，受限沙箱会拒绝；应在普通用户权限环境运行，不提升为管理员、不安装根证书。

固定依赖：Kestrel 随 .NET 10 shared framework；QRCoder 1.8.0；Makaretu.Dns.Multicast 0.27.0。三项目 `packages.lock.json` 锁定转依赖和内容哈希。QRCoder 的旧绘图库依赖由 .NET 10 WinForms 的框架引用裁剪，运行时使用框架绘图库。包源恢复期间未关闭 TLS 校验或包签名验证。阶段六候选带 NuGet 依赖许可证文本，以及匹配自包含运行时版本的 .NET Runtime 和 Windows Forms 第三方通知。

阶段六本机合成协议回归可从仓库根运行：`dotnet run --project CodexLauncher.Tests/CodexLauncher.Tests.csproj -c Release -- --bridge-protocol-client`。该模式只使用回环 Kestrel、合成设备和账号数据，不连接真实局域网或用户账号；HTTP 路由覆盖本机审批、邀请重放拒绝和邀请过期。发布候选应带 `THIRD-PARTY-NOTICES.md` 和 `licenses/` 中的锁定包与 .NET runtime notices。候选产物、哈希、验证结果和真实设备验收门槛见[阶段六报告](stage-six-report.md)。

