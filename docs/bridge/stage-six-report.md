# AI Usage Bridge 阶段六报告：交付与整体验收

日期：2026-10-03  
状态：本机 win-x64 发布候选已生成并通过合成验证；真实设备与账号验收仍待完成。

## 交付内容

- 在现有 `CodexLauncher.Tests` 增加 `--bridge-protocol-client` 模式。按规格 D-24，它复用现有测试程序，启动真实回环 Kestrel HTTPS 服务，用合成账号和设备跑手机配对及 API 流程，不新增独立控制台项目。
- 添加依赖许可证清单及文本；自包含 .NET 10.0.11 Runtime、ASP.NET Core 和 Windows Forms 的匹配通知文件随候选发布。阶段六清单以当前 `packages.lock.json` 和实际发布框架为准。
- 生成候选目录 `dist/win-x64-bridge/`。阶段开始时该目录不存在；原有八个 `dist/win-x64-*` 目录均保留。
- `CodexLauncher.exe` 文件版本 `1.0.0.0`，产品版本 `1.0.0`，文件大小 `144,736,215` 字节。其 SHA-256 为 `d87543229ed01ab1a87ad11910c438555d487542f5b01b2809a0a50390d44d63`。
- `SHA256SUMS.txt` 覆盖候选中的 13 个载荷文件，逐项复核通过；清单自身不参与自校验。连同清单，目录现有 14 个文件。

此为本机候选产物，未上传或发布到外部。单文件发布将主托管程序收进 EXE；Windows/SQLite 原生侧车文件、PDB 和许可证文件仍作为独立文件随包提供。

## 验证记录

| 检查 | 结果 | 证据 |
| --- | --- | --- |
| App 的 win-x64 锁定还原 | 通过 | `dotnet restore CodexLauncher.App/CodexLauncher.App.csproj -r win-x64 --locked-mode --configfile NuGet.Bridge.Config -p:NuGetAudit=false` |
| Release 解决方案构建 | 通过，0 警告、0 错误 | 输出隔离到 `.superpowers/sdd/2026-10-01-ai-usage-bridge/stage-six-build/`；覆盖审查缺口后的日志 `stage-six-review-fix-build.log` |
| 完整测试套件 | 通过，131/131 | 使用阶段六构建的测试程序集直接运行；覆盖审查修正后的日志 `stage-six-review-fix-full-suite.log` |
| `--bridge-protocol-client` | 通过，2/2 | 回环 HTTPS、认证/账号隔离、配对审批、HTTP 邀请重放拒绝及 HTTP 邀请过期；日志 `stage-six-review-fix-protocol.log` |
| OpenAPI 与 JSON Schema | 通过 | OpenAPI 3.1、六种响应 fixture，以及额外身份字段/未知状态的拒绝校验；日志 `stage-six-contract-validation.log` |
| win-x64 自包含单文件发布 | 通过 | SDK 10.0.400；命令见下；输出为 `dist/win-x64-bridge/` |
| 发布载荷 SHA-256 | 通过，13 个文件 | `dist/win-x64-bridge/SHA256SUMS.txt` |

验证日志保存在本机工作区 `.superpowers/sdd/2026-10-01-ai-usage-bridge/`。

锁定还原按发布目标分别验证：App/Core 锁文件记录 `win-x64`，测试项目锁文件使用无 RID 的 Windows TFM。整份解决方案统一使用 `-r win-x64 --locked-mode` 会因测试项目锁文件未声明该 RID 而报 NU1004；整份解决方案不带 RID 锁定还原则会因 App/Core 锁文件记录了 `win-x64` 而报 NU1004。App 项目带 `win-x64` 的锁定还原通过；最终 Release 构建先分别准备 App/Core 的 RID 资产和 Tests 的无 RID 资产，再用 `--no-restore` 构建，因此完整测试与发布验证均通过。报告没有把整份解决方案的统一 RID 锁定还原记作通过，也没有扩展测试项目的锁文件。

## 复现命令

发布项目锁定还原：

```powershell
dotnet restore CodexLauncher.App/CodexLauncher.App.csproj -r win-x64 --locked-mode --configfile NuGet.Bridge.Config -p:NuGetAudit=false
```

阶段六构建使用隔离输出，避免写入日常 `bin`：

```powershell
dotnet build CodexLauncher.slnx -c Release --no-restore -m:1 -nr:false -p:UseSharedCompilation=false -p:DirectoryBuildPropsPath=D:/Workspace/Codex/project/Codex启动器/.superpowers/sdd/2026-10-01-ai-usage-bridge/stage-six-isolated.props
dotnet .superpowers/sdd/2026-10-01-ai-usage-bridge/stage-six-build/CodexLauncher.Tests/Release/net10.0-windows/CodexLauncher.Tests.dll
dotnet .superpowers/sdd/2026-10-01-ai-usage-bridge/stage-six-build/CodexLauncher.Tests/Release/net10.0-windows/CodexLauncher.Tests.dll --bridge-protocol-client
```

发布命令：

```powershell
dotnet publish CodexLauncher.App/CodexLauncher.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:UseSharedCompilation=false -p:DirectoryBuildPropsPath=D:/Workspace/Codex/project/Codex启动器/.superpowers/sdd/2026-10-01-ai-usage-bridge/stage-six-isolated.props -o dist/win-x64-bridge --no-restore
```

## 未完成的真实验收

- 未启动发布 EXE 去连接当前用户的 Codex App Server，也未用真实账号执行额度查询；协议测试全部使用合成账号和回环服务。
- 未验证真实 Windows 登录/登出场景下的额度提供者集成、真实设备的 DPI/交互，以及当前用户环境中的启动与退出流程。
- mDNS 仍因完整出站隔离尚未验证而保持默认阻塞；真实手机与电脑在同一局域网的发现、配对、断线恢复和撤销验收尚未执行。
- 未更改真实账号授权、启动项、防火墙、代理、TUN 或现有设置。

因此阶段六完成了本机实现、候选打包及合成回归；开放网络发现和真实设备验收仍是后续交付门槛。
