# 开始菜单头像刷新

## 已观察到的行为

用户在 v0.1.1 的实机反馈：锁屏头像会更新，开始菜单底部头像重启电脑后更新，Microsoft 账户卡片重启后仍未改变。这不等于全部 Windows 版本均已验证。

v0.1.1 只写入本机 AccountPicture 注册表值，不请求开始菜单重新加载。底部头像能在重启后改变，说明该界面能够使用新配置；具体缓存机制尚未通过系统跟踪确认。

## v0.1.2 的处理

应用或恢复头像后自动刷新，不增加勾选框或手动刷新按钮。只有头像操作成功才执行自动刷新。刷新失败单独报告，不撤销已经成功写入的头像。

刷新由普通权限的交互进程执行：只结束当前会话中、可执行文件路径与 Windows SystemApps 下的标准 StartMenuExperienceHost 路径完全匹配的进程，不结束进程树。由 Windows 在需要时重新激活开始菜单；请按 Win 键查看。不会重启 Explorer、注销用户、修改注册表权限或清理账户缓存。若系统阻止访问则显示错误，不提升权限重试。

开始菜单会短暂关闭。进程结束不等于已确认头像视觉刷新成功，具体效果仍需实机反馈。

微软的支持资料确认开始菜单使用独立宿主，也在其他开始菜单故障场景中提供了结束宿主进程的办法；这些资料并不保证动图头像兼容性：

- https://learn.microsoft.com/en-us/troubleshoot/windows-client/shell-experience/troubleshoot-start-menu-errors
- https://jpwinsup.github.io/blog/2026/04/16/Shell/Explorer/installed-applications-dont-appear-in-new-StartMenu/

## Microsoft 账户卡片

当前不支持同步该卡片。它在重启后仍不跟随本机配置，表明仅重新加载本机头像不足以覆盖该界面。尚未确认其在用户当前 Windows 版本上的具体数据来源，不能断言一定是云端头像或某一个缓存文件。

本软件不调用云端账户头像修改接口，不改写身份缓存，也不以 GIF 第一帧覆盖其他账户资料。Windows 与 Microsoft 账户头像修改入口可参考：

- https://support.microsoft.com/en-us/windows/security/identity-signin/change-your-account-picture-in-windows

## 验证范围

已提供演示模式交互测试，演示模式不会结束真实进程。实际底部头像是否无需重启即可刷新，需用户使用新版本验证；账户卡片暂不作为已修复功能。
