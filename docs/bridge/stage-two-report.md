# AI Usage Bridge 阶段二实施记录

日期：2026-10-02。按补充规格覆盖旧计划冲突处；用户「先继续执行」授权继续阶段二。

## 已实现

- App 内嵌 `BridgeHost`，HTTPS 单地址监听，公开 health/device 说明页；providers/accounts/usage/refresh 均先验证设备身份。没有临时设备令牌或开放授权端点进入产品程序集。
- `BridgeIdentityStore` 将 Bridge ID、证书私钥、256 位账号映射密钥放入独立当前用户 DPAPI 原子文件，目录 ACL 仅当前用户。默认目录 `%LOCALAPPDATA%\CodexLauncher\bridge`。自签证书稳定保存，不安装信任根、不自动续签。身份缺失、密文或结构损坏生成全新身份并返回 `WasReset`，未来 UI 必须明确展示该标记。
- `LauncherSettings` 只增加默认关闭的 `BridgeEnabled` 和可空 `BridgePort`，null 默认43189；MainForm 保存偏好时保留字段。
- GET 只读缓存；刷新立即202并后台复用同一 provider。新增手动刷新接收结果接口，UI与HTTP共用原来的10秒调度节流，没有HTTP独立的节流时钟。停桥释放Kestrel，不停止共享额度所有者。
- 设备每分钟60次滑动窗口与429/Retry-After；拒绝跨源 Origin 和跨站 Fetch Metadata；16KiB上限覆盖已知长度、HTTP/1分块与无长度HTTP/2正文。日志只记录规范化 bridge-* 状态，关闭默认HTTP请求日志。
- 生产监听策略拒绝通配、公网、IPv6和回环，要求地址属于在线实体以太网/Wi-Fi适配器，使用Windows WMI确认PhysicalAdapter和NLM确认Private。无法验证时失败关闭；请求时重新校验网络状态。测试工程单独注入回环策略，只监听临时端口。

## 验证

- 锁定还原通过；Release build 0警告、0错误。
- 完整回归108/108；桥测试23/23。使用真实Kestrel+HttpClient临时回环HTTPS，包括HTTP/2精确版本请求；测试仅使用合成账号、凭据与临时身份目录。
- 正确SHA-256证书指纹可访问，错误指纹被TLS客户端拒绝；重复GET不增加上游次数；刷新202在上游阻塞时也立即返回；401、403、409、429及Retry-After、端口冲突、账号切换、CLI缺失、过期缓存、查询失败、停桥不停止共享所有者均通过。
- 身份跨加载保持Bridge ID和证书；损坏密文或结构触发重置。监测关闭响应NO_DATA/QUOTA_MONITORING_DISABLED且没有打开上游会话。
- OpenAPI3.1、JSON Schema2020-12六种合成响应与额外身份字段/非法状态拒绝校验通过。
- RED→GREEN：缺失设置/身份；未实现HTTPS；关闭监测403；损坏结构NullReference；HTTP/1分块请求；HTTP/2无长度请求。

## 审查与裁决

- 独立只读审查采用executing-plans规定的fresh reviewer：无剩余Critical/Important，延后1个Minor。审查员复读结构修复；执行者提供身份损坏与HTTP/1/2正文上限的RED→GREEN，以及最终108/108回归。没有用二次审查替代修复验证。
- 关闭监测时没有可识别账号：accounts仍为空，已认证usage/refresh返回无账号数据的状态回执，accountId只回显请求值，不创建账号授权。若未来持久授权要求更严格，阶段三可收紧回执路由。
- 正式凭据发放、持久设备与账号授权按计划留在阶段三；只有测试程序集含合成认证器。手机实际使用须等待后续阶段，当前App未接入服务启用界面。成本是不能将此阶段构建当作手机共享发行版。
- 当前工作区无Git元数据，保留工作区证据，未创建仓库或提交；既有dist目录未更新。
- 延后小项：每请求同步WMI/NLM检查的性能优化，阶段四采用网卡事件失效的缓存时仍须失败关闭。
- 未验证的真实环境：物理网卡Private正向监听、Private→Public/适配器消失、真实TUN/虚拟网卡排除、手机浏览器、真实上游身份字段语义、mDNS出站网卡隔离及自包含发布。当前只证明生产策略的拒绝路径与真实回环HTTPS链路，不宣称局域网整体验收。若监听验证与Windows实际环境不兼容，表现应为拒绝启动/503，绝不放宽到全接口。
- 没有第二台实体设备、浏览器证书查看器或Windows ACL跨用户实测。当前用户DPAPI持久化经过合成测试，ACL策略经过静态审查；这些不代替后续真实环境验收。

本次构建SHA-256：Core DLL `69E52CAEFB63047A56B2FFF29110E693D449261B01F1BB735E0CBE1C902173A6`，App DLL `3E9EF44C2A46D531466B11809A1D63E1DD6258F31B90F0D1205B376481724ADA`。这两个值标识当前框架依赖构建，不标识自包含发行包。

## 后续阶段

阶段三实现两分钟邀请、电脑确认、设备令牌交付与撤销；阶段四实现发现与网络生命周期；阶段五接入主窗口桥分区与后台启动；阶段六单独进行发布和整体验收。浏览器仍须在证书查看器人工比对实际证书，页面显示指纹不能独立证明TLS对端。
