# 主界面细节调整验证记录

日期：2026-10-02

## 已实现

- 标题栏、任务栏及系统托盘共用运行时 Codex 图标；从现有 PNG 品牌素材生成 16–256px 多尺寸图标，没有修改 EXE 的 ApplicationIcon 配置。
- 页头直接显示渐变方形 Codex Logo，删除外围深色框及仿制 C 回退。资源缺失时页头不绘制替代 Logo，窗口和托盘共用系统备用图标。
- 页面顶部留白从 24 改为 12 个逻辑像素，左右和底部仍为 24，页头到状态卡的 12 像素间距保留。
- 路径、证据、耗时、更新时间分为四行；更新时间独占末行，元信息右对齐。

## 构建与测试

Release 构建命令：

```powershell
dotnet build CodexLauncher.slnx -c Release --no-restore -m:1 -v:minimal
```

结果：成功，0 警告、0 错误。当前环境默认并行 MSBuild 在项目引用枚举阶段曾返回失败且无编译诊断；单线程构建成功，没有修改 SDK 或项目配置。

完整回归命令：

```powershell
dotnet CodexLauncher.Tests\bin\Release\net10.0-windows\CodexLauncher.Tests.dll
```

结果：103/116 通过。本轮四项图标、元信息四行、元信息右对齐、顶部留白契约先失败后通过；其他原有界面及启动相关用例通过。

以下 13 项 Bridge 用例在修改前后的测试运行中均失败，未修改 Bridge 功能代码：

1. pairing consumes an invitation once and delivers one token until acknowledged
2. concurrent scans create exactly one pending pairing
3. pair approval rejection expiry and per-source throttling are enforced
4. device capacity is twenty and revocation immediately frees access
5. device hashes and account grants survive restart but account changes revoke
6. malformed device records reset to an empty fail-closed store
7. Kestrel pairing routes issue credentials only after local approval
8. bridge identity persists and corruption explicitly resets its identity
9. bridge HTTPS enforces auth, cache-only reads, immediate refresh and account isolation
10. bridge disabled monitoring serves empty status without opening an upstream session
11. bridge refuses unsafe production bindings and an occupied port
12. bridge identity detects structurally corrupt protected payloads
13. Kestrel dependency serves synthetic HTTPS with a pinned self-signed certificate

其中第 8 项返回反射调用异常，第 13 项返回“拒绝访问”，其余返回 unauthorized operation。沙箱外测试申请因自动审批服务连接错误未执行；尚未确认这些用例在不受限制的 Windows 环境中的结果。

## 验证边界

- 未逐项完成浅色、深色与 100%、125%、150%、200% DPI 的实际屏幕显示验收。现有真实 WinForms 几何与主题测试通过，不能替代完整实机验收。
- GraphFlow 已尝试增量索引；当前配置未将 .cs 纳入 includeExtensions，部分调用又遇到自动审批连接错误或超时，因此不能声称本轮源码已被图谱收录。
- 当前目录没有 Git 元数据，不能提供 Git diff 或提交记录；本轮源文件改动限于 MainForm.cs、CodexLogo.cs、StatusCardLayout.cs、UiControls.cs、RuntimeHealth.cs 和测试 Program.cs。

## 运行产物

`CodexLauncher.App\bin\Release\net10.0-windows\CodexLauncher.exe` 为本次 Release 构建产物，运行需要已安装的 .NET 10 Windows Desktop Runtime。

请从原启动器托盘选择退出后打开上述产物，避免单实例机制将新进程请求转发给仍运行的旧版本。
