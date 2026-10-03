# Codex 启动器

面向 Windows 10/11 的轻量桌面启动器。它能管理本机固定代理入口 `127.0.0.1:7896`，手动选择 Clash Party 或 Clash Verge 作为上游，并从两款软件的配置自动读取当前代理端口；启动前比较系统默认网络与本机代理，选择可用路径启动已安装的 Codex MSIX 桌面版。主界面会用红、黄、绿状态说明当前线路是否稳定，并显示配套 CLI 的额度窗口与重置时间。所有检测都是免费的只读探测，不会发送 Codex 对话请求。

## 使用

1. 安装 Windows 桌面版 Codex，并先自行完成登录。
2. 运行 `CodexLauncher.exe`。程序会识别 MSIX 安装，并检测系统默认网络与已配置的本地代理。状态卡显示当前路径、代理入口、检测耗时和更新时间。Codex 图标从已安装的 MSIX 包读取；若无法读取，会显示中性备用图标。这是第三方启动助手，不代表 OpenAI 官方应用。
3. 要使用固定入口，点 Party 或 Verge 路径卡片。启动器会手动切换上游，并自动重新检测；切换只影响新连接。固定入口的启动、停止和 Mihomo 程序选择收在“高级设置”内。初次启动找不到 Mihomo 时，选择现有的 `mihomo.exe`。
   启动器优先读取 Party 的 `mihomo.yaml` 和 Verge 的 `verge.yaml` 中的混合代理端口；若混合端口关闭，则读取 HTTP 专用端口。窗口打开期间每 5 秒检查一次，并在“检测网络”和“启动 Codex”前再检查。若文件缺失，暂用上次端口或默认的 Party `7890`、Verge `7897`。
4. 启动器会读取额度与重置时间。它用桌面版配套的 Codex CLI 以只读方式查询订阅额度，用条状进度图显示每个窗口的剩余比例，并标出窗口长度、重置倒计时、剩余百分比、查询到的账号与计划类型。这个过程不消耗模型用量，也不读取或复制账号令牌。
5. 点击“启动 Codex”。如果桌面版已经运行，请先在 Codex 中正常退出，再重试。启动后可直接在桌面版中开始工作。

网络状态会每 30 秒自动轻检；代理地址停止编辑约 0.7 秒后立即重检。Party/Verge 的端口映射和 TUN 状态每 5 秒检查变化。自动轻检只访问 `https://chatgpt.com/`，不会发送 Codex 对话请求。

### 免费检测的范围

启动器只在两种情况下访问网络：对 `https://chatgpt.com/` 发起一次只读 HTTPS 请求，以及通过本地 CLI 的只读接口读取账号与额度。它不调用任何模型生成方法，不创建会话，不发送对话内容，也不执行付费请求。

- 每次请求都有上限：额度查询单次最多 10 秒，超时后按失败处理。
- 关闭主窗口会隐藏到托盘并继续监测；监测只做上面两类只读请求。
- 从托盘菜单点“退出启动器”后，所有定时器停止、在途请求取消，启动器自己创建的 CLI 查询进程会被回收，不会留下孤儿子进程。主窗口关闭按钮默认只隐藏到托盘，需要真正结束请用托盘菜单退出。
- 托盘菜单还可以：打开主窗口、显示/隐藏悬浮窗、切换主题、开关开机自启、立即刷新。

### 额度卡

- 每个额度窗口显示为一条进度条：上方是窗口长度（如“5 小时限额”“每周限额”），下方左侧是重置倒计时，右侧是剩余百分比，进度条的填充长度与剩余百分比一致。剩余百分比按 `clamp(100 - usedPercent, 0, 100)` 计算。
- 进度条会随主题换色（浅色深蓝、深色浅蓝），条高随 DPI 缩放，不依赖固定像素高度。
- 接口没有返回剩余百分比时，进度条不画填充，右侧显示“剩余未知”，不会画成满格或空格来暗示读数。
- 多个额度桶出现同名窗口时会附上所属桶名加以区分。
- 默认每 5 分钟刷新一次；点“刷新额度”可手动查询，两次手动查询之间至少间隔 10 秒。
- 查询失败时保留上一次结果并标注“数据未更新”与上次更新时间；超过 10 分钟会提示“数据可能已过期”。连续失败按 5、10、20、30 分钟退避，成功后回到 5 分钟周期。
- 重置时间到点会触发一次重新查询。额度接口没有返回新数据时显示“等待确认重置”，不会自行填成 100%。
- **账号归属限制**：额度来自启动器自己启动的 CLI，可能与桌面版当前登录的账号不同。卡片会标出查询到的账号；无法确认与桌面账号一致时会提示“请核对桌面账号”。检测到账号切换会立即清空上一个账号的额度缓存。
- API Key 或 Amazon Bedrock 登录方式不提供订阅额度，卡片会直接说明，不会推算余额。
- 接口未返回的字段显示“暂不可用”，不会伪造为零；也不推算剩余请求数或令牌数。额度只存在于内存，配置与日志不记录账号邮箱、完整响应或凭据。

- **绿色“线路稳定”**：连续 3 次正常响应，且没有未恢复的网络重连。
- **黄色“网络波动”或“正在确认线路”**：目标网站有回应但还在确认，或最近一次采样失败、超时、响应超过 2000 ms。
- **红色“连接异常，影响使用”**：连续 3 次采样失败、双 TUN 冲突、未找到 Codex，或重连故障持续超过 20 秒。
- 检测过程以灰蓝色“正在检测”显示。状态同时有图标和文字，不单靠颜色表达。
- “查看详细诊断”可以展开安装、进程、TUN、端口和 HTTPS 结果。Party/Verge、检测和固定入口的维护操作集中在网络路径与高级设置区域。
- 窗口底部的主要操作始终可见。若当前用户目录不能保存设置，启动器仍可继续检测和操作，并提示这些设置下次启动时需要重新配置。
- 检测用的诊断库只读打开本机 `logs_*.sqlite`，并且只接受白名单里的事件；对话内容、工具输出和其他实例的日志都会被排除，不会影响状态判定。诊断库不可用时自动退回只看线路检测。
- 主题默认跟随系统，有两个入口：托盘菜单的“主题”子菜单，和“高级设置”里的“跟随系统 / 浅色 / 深色”。两处始终显示同一个真实状态——在托盘里改，高级设置里的选择会跟着变，反之也一样；写入只有一条路径，不会出现“改了没生效”。切换后主窗口、悬浮窗、窗口标题栏，以及托盘菜单和悬浮窗右键菜单会一起换色；菜单不依赖窗口树遍历，主题变化时单独重绘，避免出现深色界面里弹出白底菜单导致文字看不清。
- 窗口标题栏按当前主题上色：深色主题下标题栏取页面底色，不再留一条醒目的浅色横条。高对比度模式下不覆盖系统配色，交给系统处理。
- 页面右侧的滚动指示条由程序自绘，颜色取自当前主题；内容不足一屏时不显示。不再使用与深色界面不搭的系统滚动条。
- “开机自动启动”也有两个入口：托盘菜单里可以直接开关，“高级设置”里也有同一个勾选框，两处显示的是同一个状态。勾选后会在当前用户的 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` 写入 `CodexLauncher` 启动项，取消勾选则删除。启动项状态以注册表为准：如果你在任务管理器的“启动”页手动禁用了它，启动器只会如实回显，不会自动把它装回去。写入被安全软件或组策略拒绝时会明确提示，界面按注册表实际状态回填。

### 悬浮窗

- 悬浮窗是一块圆角玻璃拟态卡片：圆角与柔和外阴影由窗口自己逐像素绘制，卡片本体半透明，桌面内容会从卡片底下透出来。颜色取自当前主题，深浅主题下都保证文字与卡片底的对比度不低于 4.5:1；在纯黑和纯白桌面上都验证过可读。
- 卡片尺寸按当前 DPI 下的实际文字量算，不写死像素高度。状态行和线路行换更长的文字时，卡片会跟着变宽变高，不会把第二行裁掉。换显示器或改缩放后会自动重新量算。
- 左侧状态圆点垂直居中于两行文字组成的整块，不是贴在第一行的顶边；两行文字左边缘对齐。
- 高对比度模式下卡片自动改为完全不透明，避免半透明底影响可读性。
- 拖动卡片可移动位置，位置会保存并在多显示器间校正回可见工作区。右键菜单可打开主窗口、隐藏悬浮窗或退出启动器；双击也可打开主窗口。

“停止固定入口”只会停止能确认身份的固定入口进程：它必须同时监听 `7896` 和 `9098`，而且程序路径与已选择的 `mihomo.exe` 一致。如果 Windows 手动代理仍指向 `7896`，启动器会拒绝停止，防止留下不可用的系统代理设置。启动器不会自动修改 Windows 系统代理或两套软件的 TUN 设置。

若固定入口由启动器管理，发现 Party/Verge 端口变化后会热重载入口配置，并保留已选上游；短暂的现有连接可能需要重试。若入口由旧脚本管理，启动器会显示新端口和“旧脚本入口未同步”，请先停止旧入口，再由启动器启动，以免改写旧脚本的配置。

如果不使用固定入口，也可以直接在输入框填写 `127.0.0.1:7890`、`127.0.0.1:7897` 或其他本机 HTTP 代理端口，再点“检测网络”。只接受本机 HTTP 代理，不支持远程代理或 SOCKS5 端口。

“系统默认网络”指 Windows 和当前进程原有的网络设置，可能已包含系统代理或 TUN；它不承诺强制直连。普通 HTTPS 检测只证明目标响应，额度查询只证明订阅窗口的读数，二者都不能自动证明桌面版全部功能正常。

固定入口已经明确把 Codex 流量交给 Party 或 Verge 的本地 HTTP 代理，所以通常不需要再打开对应软件的 TUN。请避免同时开启 Party 和 Verge 的 TUN；两套核心会争用系统路由和本地 DNS 端口，可能导致 Codex 启动后账号检查超时。启动器会显示两边的 TUN 状态，并在检测到双 TUN 时阻止启动。HTTP 403 表示代理链路已经收到目标网站的回复，启动器会将它作为可用启动路径。

### 看不懂检测结果时

- **黄色“网络波动”或“正在确认线路”**：轻量网页检查证明所选网络路径收到回应，但连续成功次数还不够，或者最近一次采样失败、超时、响应偏慢。保持 Codex 运行等待确认；持续波动可以切换线路或检查代理软件。
- **“HTTP 403 / 检测请求被拒绝”**：已经收到 HTTP 回答，但这次网页检查被拒绝。HTTPS 是加密连接方式，HTTP 403 是网站或中间网络设备返回的状态码；它仍算线路可达。
- **“本地代理端口无法连接”**：先打开代理软件，确认它提供的是 HTTP 代理，并核对端口号（例如 `127.0.0.1:7897`），再点“检测网络”。
- **“额度：需要先登录配套 CLI”**：桌面版已登录但配套 CLI 未登录。启动器不会自动登录或注销，也不会刷新凭据；请在命令行完成 CLI 登录后再刷新额度。
- **“额度：此登录方式不提供订阅额度”**：当前是 API Key 或 Bedrock 登录方式，没有订阅额度窗口可读。
- **Codex 已在运行**：如果已经能在桌面版正常发消息，就无需再次启动。若要让启动器用代理重新启动 Codex，请先在 Codex 中正常退出。
- **菜单文字看不清**：托盘菜单和悬浮窗右键菜单会随主题一起换色。若仍遇到深色底浅色字或白底白字，请重新切换一次主题；持续复现说明是缺陷，可反馈。

## 构建

需要 .NET 10 SDK：

```powershell
dotnet build CodexLauncher.slnx
dotnet run --project CodexLauncher.Tests/CodexLauncher.Tests.csproj
dotnet publish CodexLauncher.App/CodexLauncher.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o dist/CodexLauncher-v1.0.3-win-x64-self-contained
dotnet publish CodexLauncher.App/CodexLauncher.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o dist/CodexLauncher-v1.0.3-win-x64-portable
Set-Content -LiteralPath 'dist/CodexLauncher-v1.0.3-win-x64-portable/CodexLauncher.portable' -Value 'portable' -NoNewline -Encoding utf8
Compress-Archive -Path 'dist/CodexLauncher-v1.0.3-win-x64-self-contained/*' -DestinationPath 'dist/CodexLauncher-v1.0.3-win-x64-self-contained.zip' -CompressionLevel Optimal
Compress-Archive -Path 'dist/CodexLauncher-v1.0.3-win-x64-portable/*' -DestinationPath 'dist/CodexLauncher-v1.0.3-win-x64-portable.zip' -CompressionLevel Optimal
Get-FileHash 'dist/CodexLauncher-v1.0.3-win-x64-self-contained.zip' -Algorithm SHA256
```

正式发布版和 SHA-256 校验值请前往 [GitHub Releases](https://github.com/jun501389541/CodexLauncher/releases/latest) 下载。Windows x64 自包含版内含 .NET 运行时，无需在目标电脑另行安装 .NET。自包含常规版将设置和日志保存在 `%LOCALAPPDATA%\CodexLauncher`；免安装便携版将它们保存在程序目录的 `portable-data`，解压到可写目录后运行即可。测试程序不使用第三方测试包；在已安装 Codex 的本机可加 `-- --integration` 检查 MSIX、配套 CLI 定位、额度读取与诊断库只读读取。

便携包中的 `CodexLauncher.portable` 标记启用同目录数据模式。启动后，设置、日志与桥接文件写入 `portable-data`；首次启动时可导入现有 AppData 设置和桥接文件。目标目录需要支持写入，建议解压到用户可写的位置。

若 NuGet 暂时不可用，可用仓库中的离线源配置生成依赖本机 .NET 10 Desktop Runtime 的框架依赖版：

```powershell
dotnet restore CodexLauncher.App/CodexLauncher.App.csproj --configfile NuGet.Offline.Config -p:NuGetAudit=false
dotnet publish CodexLauncher.App/CodexLauncher.App.csproj -c Release --no-restore --self-contained false -o dist/win-x64-framework
```

该框架依赖版位于 `dist/win-x64-framework/CodexLauncher.exe`，目标机器需要安装 .NET 10 Desktop Runtime 和 ASP.NET Core Runtime。自包含发布需要从 NuGet 获取 .NET 运行时包；只有 SDK 和运行时安装目录时无法离线完成标准自包含发布。

旧版窗口仍打开时，先关闭旧启动器，再运行新版本。

## 工作方式与边界

- 先用 Windows 的 `Get-AppxPackage` 找到 `OpenAI.Codex`，从安装清单取得应用 ID，再通过 `shell:AppsFolder` 激活。不会修改 Codex 程序文件。
- 使用 HTTPS 请求测试 `https://chatgpt.com/`。HTTP 拒绝、代理认证、TLS 错误和超时会分别显示。配置固定入口 `7896` 且检测通过时优先使用它；其他情况优先系统默认网络，其次为本地代理。
- 运行期诊断只读打开本机 Codex 日志库 `%USERPROFILE%\.codex\logs_*.sqlite`（只读、不写、不复制），并且只接受白名单里的 HTTP 与重连事件；对话内容、工具输出和其他实例的日志一律排除。桌面实例无法确认或诊断库不可用时，自动退回只看线路检测。
- 额度查询从 `%LOCALAPPDATA%\OpenAI\Codex\bin` 查找桌面版配套 CLI，以 `codex app-server` 通过标准输入输出通信，只发送 `initialize`、`account/read`、`account/rateLimits/read` 三个只读方法。不读取、复制或记录账号令牌，也不调用任何模型生成方法。找不到配套 CLI 时不会改用 PATH 中的其他版本。
- 额度查询进程由启动器自己创建，退出时会一并回收；主窗口关闭到托盘后它继续运行，显式退出后停止。
- 代理启动会短暂修改当前 Windows 用户的代理环境变量，广播环境变更，并在启动完成后恢复原值。意外中断时，下次打开启动器会利用 `%LOCALAPPDATA%\CodexLauncher\proxy-recovery.json` 恢复；若变量已被其他程序改动，不会覆盖那次改动。此短暂窗口内启动的其他程序也可能继承代理变量。
- 固定入口使用现有的 Mihomo 程序，在 `%LOCALAPPDATA%\CodexLauncher\gateway` 保存配置和启动器自己的进程记录。若旧脚本入口已运行，会直接复用它，不再启动第二个。入口在启动器窗口关闭后仍会运行，直到用户在“高级设置”中点“停止固定入口”。
- 固定入口和启动器的代理环境变量仍不能保证 Codex 桌面版的所有连接都走该代理；若你实测只有 TUN 模式可用，请保留可用的 TUN 并在完全退出 Codex 后重新测试。旧脚本的手动 Windows 代理步骤不由启动器自动执行。
- 如果网络检查均未通过，仍允许以系统默认网络尝试启动，但界面会提示尚无通过的路径。
- 自包含常规版的日志和配置分别保存在 `%LOCALAPPDATA%\CodexLauncher\diagnostics.log` 与同目录的 `settings.json`。便携版将配置、日志、桥接设备数据保存在程序目录的 `portable-data` 文件夹；首次运行时会在目标文件不存在时导入当前用户已有的 `settings.json` 和桥接数据。

## 参考案例

AI Usage Bridge 已接入主窗口“AI 额度桥”分区，包括网卡/端口设置、二维码、本机审批、设备管理与后台启动。参见 [对接说明](docs/bridge/README.md)、[阶段五记录](docs/bridge/stage-five-report.md) 与[阶段六交付报告](docs/bridge/stage-six-report.md)。共享仍默认关闭；mDNS 因完整出站隔离未验证而保持阻塞，真实 Windows 登录/登出、真实账号查询和手机局域网流程仍待验收。便携桥接身份使用 Windows DPAPI，跨 Windows 账户或设备移动后需要重新配对；开机启动是当前用户注册表项，移动程序目录后需要重新启用。

项目独立实现，参考了 [codex-proxy-switcher-win](https://github.com/hloolx/codex-proxy-switcher-win) 的 MSIX 启动思路、[codex-no-tun](https://github.com/lisijia666-sketch/codex-no-tun) 的诊断流程，以及 [OpenAI 官方命令参考](https://learn.chatgpt.com/docs/developer-commands?surface=cli) 中的 CLI 检测命令。

