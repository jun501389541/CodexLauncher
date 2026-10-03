# 1.0.1 诊断崩溃修复与额度桥折叠验证

## 修复内容

此前只延迟 VisibleChanged 的诊断行测量，仍遗漏 HandleCreated 路径。ListView 的托管 Items 已存在、原生列表尚未填充时，GetItemRect(0) 会抛 ArgumentOutOfRangeException。ResizeResultsHeight 现按字体与 DPI 预留表头及行高，不再读取原生行矩形，覆盖隐藏、字体变化与句柄重建路径。

AI 额度桥改为默认收起的折叠面板，点击标题展开或收起。折叠清除当前显示的二维码，不触发共享配置变更，也不停止桥服务。

## 验证证据

- 新增句柄重建回归测试：修复前复现 GetItemRect 异常，修复后通过。
- 新增额度桥默认收起、展开与收起高度恢复、配置事件不触发的真实 WinForms 布局测试：修复前缺少折叠控件，修复后通过。
- 诊断相关测试 6/6 通过，包括可见列表全部行实际矩形的容纳检查。
- 完整 Release 回归 134/134 通过。
- Release 解决方案构建：0 警告、0 错误。
- win-x64 自包含单文件发布完成；13 个发布文件的 SHA256SUMS.txt 校验通过。

## 本次验证入口

- 路径：`dist/win-x64-bridge-1.0.1/CodexLauncher.exe`
- ProductVersion：`1.0.1`
- FileVersion：`1.0.1.0`
- EXE SHA-256：`1D2630E98310B6ACAC589AC2018FE865895C7481C2F45989E24247BBE6F6BDEE`

旧候选目录保留。本次未启动发布后的桌面候选进行人工交互验收；自动化测试已使用真实 WinForms 控件验证相关路径。验证时先从托盘退出旧的 Codex 启动器，再启动上述文件，避免单实例机制激活旧版本。Codex 本体不必关闭。
