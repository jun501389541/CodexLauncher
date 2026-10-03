# AI Usage Bridge 阶段一实施记录

日期：2026-10-02（Asia/Hong_Kong）。范围遵循补充规格 D-25：只交付共享数据源、取消修复、身份与额度保真、匿名授权边界、独立契约及依赖探针。阶段二至六尚未实施，不能用于实际手机共享；本轮未发布自包含版本。

## 修改

- `QuotaModels`：新增稳定身份、身份可识别性与精确比较；原始 usedPercent、稳定窗口 ID、SourceTimestamp、DataTimestamp、UpdatedAt。UTC 内部时间，桌面 reset 文案继续本地时间；保持原有位置参数兼容。
- `CodexQuotaProvider`：在途任务统一由启动器生命周期令牌控制，调用者只取消自己的 WaitAsync；飞行任务最终清理。共享手动节流序列化，总查询超时沿用十秒，退出时等待自有查询并收尾会话；拆除异常不再发布晚到快照。生命周期取消进入真实 stdio 握手，`StopAsync` 明确为终止所有自有工作的停止操作，重启需新 provider；桥自身停止不得调用共享 provider 的终止操作。
- `SharedQuotaCoordinator`：唯一 provider 所有者，复用既有循环与调度；监测关闭不创建 provider；内部账号变化事件与版本。MainForm 使用同一协调器。
- `BridgeContracts` / `BridgeAccountGrantStore`：允许字段映射、UTC JSON、六种状态、规范错误码；至少 256 位注入 HMAC 密钥生成匿名账号 ID；进程内账号授权在身份改变时撤销，旧账号 ID 409、当前账号未授权 403；邮箱、平台标识和原始诊断不进入 DTO。
- App / Tests 增加 ASP.NET framework reference；QRCoder 1.8.0、Makaretu.Dns.Multicast 0.27.0 与三个 lockfile。依赖探针只用合成数据、临时证书与回环临时端口。
- `docs/bridge`：完整 OpenAPI 3.1、JSON Schema Draft 2020-12、六种合成响应、可重生成脚本及接入说明。

本阶段的授权仓库只在内存中运行，没有实际设备 Bearer Token 认证。当前用户 DPAPI、稳定 bridgeId/证书持久化及设备仓库属于阶段二/三；没有把秘密写进 settings.json。未启动真实账号集成测试，未改防火墙、系统代理、TUN 或开机启动。

## 验证记录

所有自动化测试使用合成账号。初始新增四项测试为 0/4（如期失败），修复后 4/4；新增契约测试后 8/8；依赖探针 11/11；拆除错误边界测试 RED→GREEN 后专项 13/13；审查的握手、停止、冲突标识与读取间换号问题各有 RED→GREEN 证据，专项最终 17/17。

1. Release 构建：`dotnet build CodexLauncher.slnx -c Release --no-restore -m:1 -nr:false -p:UseSharedCompilation=false`，零警告、零错误。

   最终框架依赖构建的 SHA-256（不是自包含发行包）：Core DLL `6A0BD4AA8C0295F41744070C2CAD81D517260AF3349F243439A1DA4A6FF6F683`；App DLL `A1A67B829EADC7279EBB31B5FEE8BD76AE5E2DC5FAA0FAF6EF39E6A3E3FC43BD`。完整路径分别为 `CodexLauncher.Core/bin/Release/net10.0-windows/CodexLauncher.Core.dll` 与 `CodexLauncher.App/bin/Release/net10.0-windows/CodexLauncher.dll`。
2. 全量既有与新增回归：最初 96/96 通过；新增退出边界与真实 DTO/schema 字段测试后 98/98；独立审查修复后最终 **102/102 通过**（85 项原有回归 + 17 项阶段一专项）。命令：`dotnet run --project CodexLauncher.Tests/CodexLauncher.Tests.csproj -c Release --no-build`，日志 `full-suite-102.log`。
3. HTTPS：Windows Schannel 对没有可用临时密钥容器的证书返回 SEC_E_NO_CREDENTIALS；使用默认 PKCS12 用户密钥导入后，在普通用户权限、沙箱之外通过握手、指纹匹配读取及错误指纹拒绝。探针结束释放密钥、证书和监听，不安装受信任根。
4. QRCoder：成功生成合成邀请 PNG。
5. mDNS：0.27.0 的指定接口筛选构造和 IPv4-only 配置兼容 .NET 10 Windows；探针不启动发现，**不证明实际出站广播隔离**。
6. 包还原：在线 .NET/NuGet 在沙箱中出现 Schannel 凭证错误，使用 Node 正常 HTTPS 从 NuGet 下载固定包，生成本地 feed；签名验证没有禁用。`dotnet restore CodexLauncher.slnx --configfile NuGet.Bridge.Config --locked-mode -m:1 -nr:false` 通过。
7. 契约：SwaggerParser 12.1.0 校验 OpenAPI 3.1；Ajv 8.17.1/ajv-formats 3.0.1 校验六个样例，额外身份字段和未知状态的负例均被拒绝；C# 测试校验实际序列化字段与独立 schema 以及 UTC `Z` 时间。

执行日志和改前备份保留在 `.superpowers/sdd/2026-10-01-ai-usage-bridge/`；本目录没有 `.git`，没有创建仓库、分支或虚构提交。依赖缓存位于工作区 `.superpowers`，不作为交付源码提交。

## 裁决与未验证事项

- 登录类型自身不能识别账号。仅 Kind 非空的 API Key 仍拒绝账号授权。代价：无法可靠区分身份的账号有意不可用于桥。
- D-18 覆盖上游“切回旧账号可恢复授权”：任何身份字段变化均需要电脑重新确认。代价：切回时增加一次确认。
- 可用但空窗口映射 NO_DATA；网络 DTO 保留小数剩余比例，桌面整数条形图保持原行为。代价：桌面数字可能与手机小数值存在取整差异。
- 无 Git 元数据时使用原文件备份和持久记录。代价：没有 Git 提交/回滚历史，备份予以保留。
- `StopAsync` 为终止而非暂停轮询。代价：重启额度服务需创建新 provider；桥停止仍保持共享 provider 运行，符合数据源独立所有权。
- 稳定 ID 来源冲突时保守拒绝，额度读取前后核对身份。代价：每个成功 ChatGPT 查询多一次只读 `account/read`；上游 ID 来自不同语义域而恰好不等时会暂不可用，需明确来源语义后再支持。只读方法白名单没有扩大。
- 浏览器只能人工指纹比对，服务端显示值本身不足以证明实际 TLS 对端。对接说明明确要求证书查看器或具备 TLS 验证回调的客户端；C-2 中间人残余风险仍存在。
- **mDNS 指定物理网卡出站隔离仍为依赖闸门**：未执行真实 LAN 包隔离实测。后续发现功能不能用全网卡广播代替；不通过即停止发现、界面明示阻塞，保留 HTTPS 直连。
- 真实账号、第二台局域网设备、TUN 下网卡隔离、IP 变化、休眠恢复、防火墙、手机浏览器和实际扫码均未验证；深浅主题/DPI 只运行既有控件回归，本阶段没有新增桥 UI。
- 阶段六打包、产物 SHA-256、发布及整体验收须单独进行。

## 下一阶段前置确认

按 D-25，此阶段测试与身份/DTO 口径需确认后才进入 HTTPS 服务阶段。后续必须继续沿用 D-7 的 App 内嵌 Kestrel、D-9 的 DPAPI 身份目录、D-18 的账号授权失效，以及 C-1 的网卡隔离失败关闭发现策略。

## 独立审查与修复

一次只读独立审查发现三项 Important：真实握手取消未传播、Stop 后迟到会话继续工作、冲突账号标识可能错挂额度。均已在单次修复阶段补失败测试并修复；读取间换号（无额度根账号 ID）的附加失败测试通过再次 `account/read` 阻断。没有 Critical，不以测试代替真实网络/账号证据。

审查未判定项的处理：mDNS 实际网卡隔离保留为发现启用闸门；上游 ID 语义未保证一致时保守拒绝；实际 WinForms 操作保留未验证，已有控件回归单独说明。这些不是发行就绪证明。

延期 Minor：`BridgeAccountGrantStore` 构造时订阅事件后初始身份观察未放入 `_sync`，极短初始化交错可能导致临时内部状态不一致；实际 Accounts/Usage/Confirm 都持锁并重新读取当前身份，当前没有生产 HTTP 消费者。本轮按审查流程记录延期，阶段二接入前修整构造锁顺序。

参考核验：[OpenAI App Server 文档](https://learn.chatgpt.com/docs/app-server)、[QRCoder 1.8.0](https://www.nuget.org/packages/QRCoder/1.8.0)、[Makaretu.Dns.Multicast 0.27.0](https://www.nuget.org/packages/Makaretu.Dns.Multicast/0.27.0)。版本/接口还同时以本次实际还原的包元数据、lockfile 和编译运行验证为准。
