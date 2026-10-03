# AI Usage Bridge 阶段四实施记录

日期：2026-10-02（Asia/Hong_Kong）。实施范围为指定网卡网络生命周期、发现适配器和连接指引。模块已开发；mDNS 实机隔离与自动重新定位验收仍阻塞，不能将本报告解释为局域网发布验收通过。

## 实现

- `WindowsBridgeNetworkSource` 精确解析选定 adapterId，只选择在线物理 Ethernet/Wi-Fi、Private、RFC1918 IPv4；优先保留原地址，不能确认时返回不可用。地址/可用性事件、Windows 休眠/恢复事件及两秒复查驱动协调器。
- `BridgeLanCoordinator` 串行停止旧监听后再启动新监听；事件立即使旧请求租约失效。选定网卡消失、转为 Public、检查异常或休眠时暂停，不替换网卡。恢复重新验证，停止后网络事件不能重新启用桥。启动期间网络变化的旧监听被清理。
- 每次重绑使用同一个证书、bridgeId、设备与账号授权仓库，创建新的配对服务和地址；旧邀请与未完成交付清理。停止 LAN 模块不关闭共享额度所有者。
- DNS-SD 显式指定单一 IPv4 地址，类型 `_aiusage._tcp.local`，TXT 仅依赖默认 `txtvers=1`、bridgeId、schemaVersion；无账号、额度、令牌或信任指纹。仅邀请/待审批/交付会话或已授权设备需要发现。无设备也可启用 HTTPS。
- 发现启动或清理失败保持 HTTPS 直连，失败清理对象保留以重试，清理完成前不再启动新发现。发现状态独立报告 BLOCKED 与规范原因。
- `BridgeConnectionGuidance` 提供重扫二维码/人工核对证书说明，并只生成固定规则名前缀、当前程序、Private、LocalSubnet、指定 TCP 端口和可选 UDP5353 的 netsh 命令文本；拒绝命令注入字符。不自动执行、不提权、不创建防火墙规则。

## mDNS 闸门与裁决

固定依赖 Makaretu.Dns.Multicast 0.27.0 的 [MulticastService 源码](https://raw.githubusercontent.com/richardschneider/net-mdns/v0.27.0/src/MulticastService.cs) 将接口筛选用于多播客户端，但传统单播响应使用独立、未绑定选定接口的 UdpClient。仅传入接口过滤器不能证明所有出站都隔离。当前没有实际多网卡/TUN 包捕获证据。

Ruling：默认 `MDNS_ISOLATION_UNVERIFIED` 在创建 socket 前阻塞发现，不提供用户绕过开关。代价是当前不能自动发现或在 IP 变化后自动重新定位；仍可重新扫码和手动 HTTPS 直连。启用前须修正依赖适配器并取得真实指定接口多播及传统单播隔离证据，绝不全网卡回退。

Ruling：沿用每请求同步 WMI/NLM 网络确认，暂不采用可能过期的状态缓存。代价是每请求检查开销；正确性先保持失败关闭，实际性能待实机测量。

## 验证

- Release build：0 警告、0 错误。
- 独立审查前专项 6/6、全量 122/122；审查修复后专项 **8/8**，最终全量 **124/124**。
- 真实回环 Kestrel HTTPS 校验证书 SHA-256，覆盖从 127.0.0.1 重绑 127.0.0.2、稳定 bridgeId/证书、立即拒绝旧监听请求、更新邀请地址、旧邀请码拒绝及共享额度所有者保留。
- 合成网络来源覆盖同网卡 IP 变化、拒绝其他网卡、消失/Public 结果、休眠恢复、检查异常、启动失败/重试、禁用后事件与启动期间网络变化；发现启动/清理异常不阻断 HTTPS，发现需求启停，默认门禁不开 socket。
- RED→GREEN：网络检查异常保留旧监听（4/5），修复为暂停并关闭；发现清理异常影响 HTTPS（5/6），独立清理并保留重试对象后 6/6。
- 独立审查后 RED→GREEN：阻塞网络检查后失效租约仍通过、Stop 异常跳过 Dispose（6/8）；增加检查结束时的租约/休眠复核，停止失败仍尝试释放，失败释放保留 Host 引用供重试，成功释放后原端口可再次绑定（8/8）。关闭中的 Host 拒绝新请求和重新启动。
- OpenAPI 3.1、六种 JSON Schema 响应及额外身份字段/非法状态拒绝校验通过。日志保存在 `.superpowers/sdd/2026-10-01-ai-usage-bridge/stage-four-full-suite.log`。

## 独立审查

一次独立只读审查：Critical 0、Important 2、Minor 2。两项 Important 已按上述测试修复，不进行重复审查。暂缓两项 Minor：发现终结清理持续失败时停止状态不够准确且 Dispose 后不再重试（当前生产门禁不开 socket，真实启用前必修）；每请求同步 WMI/NLM 成本（待实机测量）。

Ruling：审查拒绝判断的真实物理网卡、TUN、多网卡抓包、浏览器证书操作、休眠/网络类别事件及阶段五 UI，保留独立实机验收范围，不能用合成结果代替。代价是这些验收仍未完成，生产发现保持阻塞。

框架依赖构建 SHA-256：Core DLL `75AAC61DC209E9A7919791665BF9C25DD4C4A2D28D737B74D2E1C12F0B53472F`；App DLL `00BFBE9C7CD652706D655D0951E2372DE7E1DA0E1AF3A2E77CEB5F37A4E104AC`。它们不标识自包含发行包。

## 剩余范围

阶段五接入启用开关、网卡选择、二维码、本机审批、设备管理和后台启动。阶段六单独发布与整体验收。未更改 `dist`，未启用真实账户共享，也未更改防火墙、开机启动、代理或 TUN。工作区没有 Git 元数据，保留执行证据。

真实物理 Private 正向监听、双设备扫码/证书查看、Public 切换与断线、Windows 休眠恢复、TUN 出站隔离、跨用户 ACL 均未实机验证；合成结果不代替这些证据。
