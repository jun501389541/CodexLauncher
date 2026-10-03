# AI Usage Bridge 规格补充与冲突裁决

- **日期：** 2026-10-02
- **上游文档：** [docs/superpowers/plans/2026-10-01-ai-usage-bridge.md](../plans/2026-10-01-ai-usage-bridge.md)
- **性质：** 本文档是对计划的**裁决与补充**，不是替代。计划第 1—3 节仍是确认规格；凡本文档与计划冲突处，**以本文档为准**并在下表登记编号。
- **产生方式：** 对现有代码做了实测审计后逐项追问形成的共识（28 项决策）。

---

## 0. 与计划冲突的裁决（2 项，优先阅读）

### C-1 · 计划 :140「不能通过广播到全部网卡规避要求」vs 退路方案

- **冲突：** 计划 :140 明文禁止以广播到全部网卡来规避"指定网卡隔离测试"要求。
- **裁决：服从计划。** mDNS 只向筛选后的 IPv4 私有局域网网卡广播。指定网卡广播失败时**记为依赖阻塞**（写日志 + 界面明示原因 + 停止发现），**绝不退回全网卡广播**。
- **连带定义：** "阻塞"不等于"桥不可用"——桥仍按配置地址监听，二维码直连路径保留（见 D-11）。

### C-2 · 计划 :148「手机必须在发送任何凭据前核对实际 TLS 证书指纹」vs 手机端形态

- **冲突：** 计划 :148 要求手机在发送凭据前核对 TLS 证书指纹（即证书 pinning），但计划 §1.2 同时规定**不做 Android App**。浏览器不提供 pinning API，无法在发送前于 TLS 层强制校验指纹。
- **裁决：保留防护意图，降级为人工比对。** 桥内置只读页 `/v1/device`，页面顶部显著位置显示**本次连接实际证书的 SHA-256 指纹**，并与二维码中的指纹并列呈现；用户在人工比对后才提交一次性配对码。
- **残余风险（明确披露，不隐藏）：** 局域网内攻击者若同时控制发现消息与 TLS 终端，可在用户不比对指纹的前提下完成中间人。缓解手段是配对码 2 分钟有效、单次使用、需电脑点击确认（D-18）。**此风险不可以通过"放弃指纹核对"来消除。**

---

## 1. 身份、账号与数据保真（计划 §2.3 / §3.4）

### D-1 · 账号身份判定口径

- **问题：** `CodexQuotaProvider.SameAccount`（`CodexLauncher.Core/CodexQuotaProvider.cs:623-626`）仅比较 `Kind` + `Email` + `WorkspaceAccountId`，三者**皆可为空**；ApiKey / Unknown 账号很可能全空 → 换了完全不同的人却被判为"未换号"。
- **决策：补稳定 ID。** 扩展 `account/read` 解析，优先取更稳定的标识（workspace 账号 ID / rateLimits 根 `accountId` 等）进入 `QuotaAccount`。
- **规则：** `Kind` + `Email` + `WorkspaceAccountId` + 新增稳定 ID **全部为空 = 账号不可识别**。不可识别账号 → **桥拒绝新绑定**；已授权账号一旦身份字段发生任何变化 → 强制回到"需电脑重新确认"。
- **影响面：** 需改 `QuotaAccount`（`QuotaModels.cs:110-115`）、`QuotaResponseParser.ReadAccount`（`QuotaModels.cs:247`）、`SameAccount`。

### D-2 · DTO 字段保真（usedPercent / sourceTimestamp / 窗口 ID）

- **问题：** `QuotaWindow`（`QuotaModels.cs:47-53`）只有 `RemainingPercent`，`usedPercent` 在 `QuotaModels.cs:364-366` 已被 `clamp(100 - usedPercent, 0, 100)` 抹平（越界脏数据不可逆）；`QuotaSnapshot` 只有 `CheckedAt`，无 `sourceTimestamp`；窗口 `Name` 是"主要窗口/次要窗口"字面量，无稳定 ID。
- **决策：改 Core 模型补齐真数据。**
  - `QuotaWindow` 增加 `UsedPercent`（原始值，保留越界真相）。
  - `QuotaWindow` 增加稳定 `Id`（由桶 `LimitId` + 窗口身份合成，满足计划 :213）。
  - `QuotaSnapshot` 增加 `SourceTimestamp`——取上游响应自带时间；**没有就 `null`，不伪造**（遵守计划 :215/:217）。
  - `ResetsAt` 解析从 `.ToLocalTime()`（`QuotaModels.cs:370`）改为 UTC，满足计划 :216。
- **UI：** MainForm 与悬浮窗**不强制跟进**新字段展示，但不得因新增字段而回归。

### D-3 · 账号基数与 provider

- **问题：** 现有 `CodexQuotaProvider` 只有单个 `_account` 字段（最新快照），无多账号存储；而计划接口是复数 `/v1/accounts` 且 DTO 带 `providerId`。
- **决策：第一版即单 provider 单账号，但接口形状保留。** `providerId` 固定 `"codex"`；`/v1/accounts` 恒定返回 0 或 1 个条目（当前账号不可用时返回空数组）。**不为未来多 provider 预留注册表或存储层**——将来真加再升 `schemaVersion`。

### D-4 · status 映射

- **决策：** 由现有 `QuotaAvailability` + `IsStale` + `FailureCategory` 直接映射，**不新增计划枚举之外的状态值**：

| 现有状态 | 对外 `status` |
| --- | --- |
| `Available` 且非陈旧 | `OK` |
| `Available` 且陈旧 | `STALE` |
| `NotLoggedIn` | `AUTH_REQUIRED` |
| `Unsupported` | `UNSUPPORTED` |
| `Unavailable` 且 `FailureCategory` 属超时/网络类 | `NETWORK_ERROR` |
| 其余 `Unavailable` | `NO_DATA` |

- **额度监测关闭**（见 D-8）：`status = NO_DATA`，以 `errorCode` 区分（如 `QUOTA_MONITORING_DISABLED`），**不新增枚举值**。

---

## 2. 上游查询与取消语义（计划阶段一）

### D-5 · HTTP 取消与上游共享查询解耦

- **问题（已实测确认存在）：** `ReadAsync`（`CodexQuotaProvider.cs:473-482`）把同一个 `_inflight` Task 返回给所有调用者，token 取自**第一个**调用者；`ReadCoreAsync` 用 `_gate.WaitAsync(cancellationToken)`（:486）把该 token 绑进共享查询 → 任一消费者取消会取消共享上游查询；且取消路径 `:498` 重抛后 **`_inflight` 从不清除**，后续调用者会一直 await 一个已取消的 Task。今天 `MainForm.cs:876` 传的是窗体生命周期 token 所以没炸，桥传 HTTP request token 必然炸。
- **决策：HTTP token 只让调用者放弃等待，不取消上游。**
  - 上游共享查询只接受**启动器生命周期 token**。
  - 桥侧用 `WaitAsync(httpRequestAborted, ...)` 实现"放弃等待"；放弃后上游查询继续跑完并照常写入缓存。
  - **不引入引用计数**（复杂度与收益不匹配）。
- **后果（接受）：** 手机锁屏断连零副作用；上游查询次数由调度与共享合并控制，不由 HTTP 客户端数量控制。

### D-6 · `POST /v1/accounts/{id}/refresh` 语义

- **决策：立即返回当前快照 + 后台触发刷新。** 桥触发一次上游手动刷新（沿用现有 10 秒节流 `QuotaSchedule.ManualInterval`），HTTP 立即返回 `202` + 当前快照 + `isStale`；手机随后再 `GET` 取新数据。
- **不采用**同步等待上游完成（会让 HTTP 长挂，并与 D-5 叠加出复杂语义）。

---

## 3. 托管、进程与配置

### D-7 · Kestrel 托管位置

- **决策：App 直接内嵌 Kestrel。** `CodexLauncher.App` 增加 `FrameworkReference Microsoft.AspNetCore.App`，接受自包含单文件 win-x64 产物增大。
- **不新建** `CodexLauncher.Bridge` 类库——FrameworkReference 同样传染到 App 输出，产物体积无差别，只多一层目录。

### D-8 · 桥开关与额度监测开关的耦合

- **问题：** 现有 UI **没有任何额度监测开关**（高级设置面板仅 5 个控件，`MainForm.cs:467-470`），`QuotaMonitoringEnabled` 只存在于 `LauncherSettings.cs:18`。
- **决策：桥独立开关，互不绑架。** 桥开启、额度监测关闭时：接口**正常响应**，但 `status = NO_DATA` + 明确 `errorCode`，**不启动 provider**。桥绝不静默改写 `QuotaMonitoringEnabled`。

### D-9 · 配置落盘

- **决策：设置里只存开关与端口。**
  - `LauncherSettings` 增加 `BridgeEnabled`（默认 `false`）与 `BridgePort`（`int?`，`null` 即用 `43189`），同步修改 `MainForm.cs:708-718` 的位置参数构造。
  - `bridgeId`、证书私钥、账号映射密钥、设备授权**全部另存** `%LOCALAPPDATA%\CodexLauncher\bridge`，原子写入、限制当前用户访问、敏感项用当前用户 DPAPI 保护（计划 :151/:163）。
- **不进 `settings.json`**——它是明文 JSON。

### D-10 · `--background` 与开机自启

- **问题：** `Program.cs:11` 是 `private static void Main()` **无 args**；`AutoStartRegistration.Command()`（`AutoStartRegistration.cs:28`）生成的值是无参带引号路径，且 `IsEnabled`（:48-53）用**全串相等**比较 → 一旦加参数，UI 会永远显示"未启用"。
- **决策：**
  - `Main()` 改为接收 `string[] args`。
  - Run 键值改为带 `--background`；**同步把 `IsEnabled` 的比较逻辑改为"解析出可执行路径后比较"**，否则加参数即永久误报未启用。
  - `--background` = 不显示主窗，驻托盘并启动桥。
- **单实例闸门冲突（已识别并就地解决，不单独问询）：** `Program.cs:8` 的 `SingleInstanceName = @"Local\CodexLauncher.Monitoring"` 会让二次启动只唤醒已有实例后 return（:17-21）。开机自启时若启动器已在运行，第二次带 `--background` 的进程**直接退出**——桥由已运行那份承载。若已运行那份**未开桥**，则把"启用桥"作为激活信号传给既有实例；未在运行则以无窗模式启动并驻托盘。

---

## 4. 网络、发现与暴露面

### D-11 · mDNS 退路与二维码直连

- **决策：尽力而为，失败不影响桥。** 广播失败（见 C-1）不报错式中断服务：桥照常监听，二维码内容包含 **协议版本 + Bridge ID + 连接地址（IP:端口）+ 证书 SHA-256 指纹 + 一次性配对凭据 + 失效时间**（计划 :147），手动直连始终可用。
- **不采用**"mDNS 失败即拒绝服务"（会误伤多网卡/虚拟机/热点场景），也**不采用**"完全不做 mDNS"。

### D-12 · 无已配对设备时的监听策略

- **决策：始终监听，仅在需要时广播与显示码。** 桥开关打开即按配置地址监听；mDNS 仅在"有配对窗口或已有批准设备"时广播；二维码仅在用户点击"添加设备"时生成并限时显示。
- **不采用**"无设备就不监听"（会造成"第一次连不上"的困惑）。

### D-13 · 防火墙

- **决策：只给指引 + 一键复制命令，程序自身永不提权。** 界面提供入口，展示 `netsh advfirewall` 等价命令（仅当前程序、Private 网络、LocalSubnet 来源、所需端口、本软件固定前缀标识），由用户自行以管理员执行。
- **不静默尝试加规则**（会触发 UAC 且违反计划 :36「不自动修改防火墙」）；**也不完全不管**——至少给出可复制的命令。

### D-14 · 证书与 Bridge 身份生命周期

- **决策：永久身份，丢失即重置并明确提示。** `bridgeId` 与自签证书一次性生成后由 DPAPI 持久保存；数据目录被删除或损坏即视为**全新身份**，所有已配对设备作废、需重新扫码，并在界面明确提示"身份已重置"（计划 :157）。**不静默重签，不自动续签，不每次启动重签。**

### D-15 · 信任建立方式

- **决策：自签证书 + 指纹 pinning 意图随配对下发。** 二维码与内置页都携带/展示证书 SHA-256 指纹；手机换 WiFi、换网段、多网卡切换均不断连（信任锚是证书，不是 IP）。
- **浏览器场景下的落地形式见 C-2**（人工比对，非 TLS 层强制）。

---

## 5. 配对、设备与限流

### D-16 · 配对凭据交付形态

- **决策：二维码带配对码，设备凭据单独下发。** 二维码里的一次性凭据只用于"证明扫了码并提交申请"；真正的长期设备凭据在电脑确认后经 HTTPS 单独交付（计划 :155/:161）。
- **不采用**"二维码直接含完整设备凭据"——截图/肩窥暴露面更大。

### D-17 · 内置只读页 `/v1/device` 的访问权限

- **决策：页面公开，数据需凭据。** `/v1/device` 无需认证即可访问，但**只**显示连接地址、证书指纹、配对状态与提交配对码的表单；额度数据一律要求有效设备凭据。这样既满足"先核对指纹再交凭据"（C-2），又不泄露任何额度数据。
- **不采用**"页面也要凭据"（首次扫码时手机还没有凭据，会导致看不到指纹页，与 C-2 自相矛盾）。

### D-18 · 账号切换后已配对设备的表现

- **决策：设备保留、授权失效。** 检测到账号切换 → 该设备对该账号的授权标记为失效 → 后续请求返回 `403` + 明确 `errorCode`，**必须在电脑上重新确认**（设备无需重扫二维码，符合计划 :30）。设备列表明确显示"待重新授权"。
- **不采用**"换号即清空所有设备"（体验差）或"自动重新授权"（违反计划 :30）。

### D-19 · 设备上限与撤销

- **决策：硬上限 20。** 达到上限即拒绝新配对申请（`409` + 明确错误码），界面提示先撤销；撤销**立即生效**，被撤设备下次请求返回 `401`。
- **不采用**"只提示不拒绝"（设备表会无限增长，违反计划 :221）。

### D-20 · 限流计数存储

- **决策：内存滑动窗口，重启清零。** 每设备 60 次/分钟、配对申请每来源 5 次/分钟，超限返回 `429` + `Retry-After`。不做持久化。
- **不落盘**——高频写入会污染"只存身份与授权"的桥目录职责。

### D-21 · 刷新全局间隔

- 沿用计划约束（≥10 秒），与现有 `QuotaSchedule.ManualInterval = 10s` 天然一致，不额外引入新节流层。

---

## 6. 界面、日志与测试

### D-22 · 界面落位

- **决策：主窗口新增「AI 额度桥」分区。** 含开关、端口、状态、显示二维码按钮、已配对设备列表（可重命名/单设备撤销/全部撤销）。不新建独立对话框，也不把设备管理藏进托盘子菜单。
- **主题/DPI：** 新控件必须显式注册到 `UiTheme.Style`（无共享主题基类），并在浅深主题与 100%—200% DPI 下可用（计划 :325）。

### D-23 · 日志

- **决策：复用现有 `DiagnosticLogger`，新增 `bridge-*` 阶段**（`bridge-pair` / `bridge-ack` / `bridge-revoke` / `bridge-serve` / `bridge-refresh` / `bridge-limit`）。只记规范化状态与错误类别，**绝不记录请求体、认证头、二维码内容、额度原始响应**（计划 :223）。
- **不单独开日志文件**（多一份轮转负担），**也不完全不记**（出问题无法自查）。

### D-24 · 协议测试形态

- **决策：`CodexLauncher.Tests` 增加 ASP.NET FrameworkReference，真起 Kestrel 跑端到端。** 使用临时端口 + 真实 `HttpClient`，覆盖 TLS、设备凭据 `401`、账号未授权 `403`、账号切换 `409`、限流 `429 + Retry-After`、配对状态机 `Invited → PendingApproval → Approved → Delivered`、配对过期/重放。
- **不采用**"只测逻辑层"（测不到 TLS/路由/中间件真实链路），**不新建**独立控制台调试客户端工程。

---

## 7. 交付节奏

### D-25 · 分批交付

- **决策：分批确认。** 六个阶段中**先做阶段一（共享数据源与契约 + 取消语义修复）**，跑通测试并确认口径后再进入阶段二。理由：阶段一的账号身份口径（D-1）与 DTO 保真（D-2）一旦判错，后面五个阶段全部返工。
- 阶段六（打包/publish/整体验收）单独一轮验收。

### D-26 · 五项优先复查项与本决策的对应

| 计划 §5.1 复查项 | 承载决策 |
| --- | --- |
| 跨账号数据泄漏 | D-1、D-3、D-18 |
| 共享查询取消传播 | D-5、D-6 |
| 配对凭据重放 | D-16、D-19、D-20 |
| 发现消息替换证书信任 | C-2、D-15、D-17 |
| 网络配置变化后错误暴露服务 | C-1、D-11、D-12 |

---

## 8. 审计确认的现状事实（本报告的依据）

- 全仓**无** ASP.NET Core / Kestrel、无 `Makaretu.Dns.Multicast`、无 QRCoder、无 DPAPI/`ProtectedData`、无 DI 容器、无产品代码 HTTP 监听、无防火墙代码、无 `NetworkInterface` 枚举、无 `NetworkChange` 订阅。
- `SharedQuotaCoordinator`、`UsageResult`、持久化额度缓存、公开的账号切换事件——计划点名，**全仓 0 命中**。
- 端口 `7896/9098/7890/7897` 属外部 mihomo，全为 `127.0.0.1` 且 `allow-lan: false`；`43189` 全仓 0 命中。
- 关窗默认 `HideToTray()` 进程不退出（`MainForm.cs:598-606`）；仅 `_exitRequested` 或 `SystemEvents.SessionEnding` 才真正退出 → 桥"隐藏态继续服务"自然成立，但**用户点退出即必须停桥**。
- 退出时 mihomo 固定入口**故意继续运行**（`README.md:109`）——桥不得沿用该语义。

---

## 9. 已明确披露的残余风险

1. **C-2 的浏览器 pinning 缺口**：局域网中间人在用户不比对指纹时可完成攻击。缓解 = 2 分钟单次配对码 + 电脑点击确认。**不消除，只压缩窗口。**
2. **D-1 的不可识别账号**：稳定 ID 仍为 `null` 的账号（部分 ApiKey/Unknown 场景）在桥侧不可用，属**有意的功能缺失**而非漏洞。
3. **D-20 限流重启清零**：攻击者可通过诱导重启绕过限流；当前判定为可接受（本地信任模型，非公网服务）。
