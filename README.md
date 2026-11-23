# 技能语音提醒

给 Windows 游戏用的技能计时器。按下绑定键后开始计时，到点用本机中文语音念出技能名。初始示例是 F1「护盾」，持续 60 秒，提前 5 秒，在按键后的第 55 秒播报。

## 在 Windows 上使用

1. 解压 `SkillVoiceTimer-Windows.zip` 到任意文件夹，双击 `SkillVoiceTimer.exe`。适用于装有 .NET Framework 4.8 或更高版本的 Windows 10 / 11。
2. 双击「护盾」修改示例，或点击「添加技能」。填写技能名、单个按键、持续秒数、提前秒数。提前量可以为 0。
3. 选中技能，点击「试听」确认中文语音和输出设备。程序优先使用系统已安装的简体中文桌面语音。如果找不到可用语音，可在 Windows 语言或语音设置中为中文安装“文本到语音”功能，然后重开工具并试听。
4. 点击「开始监听」，切回游戏。可将窗口最小化到任务栏通知区域，双击托盘图标返回。暂停监听会清空倒计时，关闭主窗口会退出。

## 计时规则

- 每次按下技能键，按「持续时间 − 提前量」安排一次提醒。长按只计一次，松开重按会替换该技能原有的提醒。
- 各个技能独立计时。语音逐条播放，同时到期时先播先按的技能。等待播报时补放，也会取消该技能的旧提醒。
- 监听期间，在其他应用里按绑定键也会开始计时。在技能编辑窗口内输入时会暂时忽略新的施放。程序按实际按键推定施放时间，游戏是否施放成功由玩家确认。
- 修改或删除一个技能会清除它自己的倒计时。设置保存在 `%LOCALAPPDATA%\SkillVoiceTimer\skills.xml`，下次启动时恢复。启动时默认暂停监听。
- 使用 Windows Raw Input 接收按键，游戏继续正常收键。支持功能键 F1–F12、字母、主键盘数字、小键盘数字等单键，小键盘数字需开启 Num Lock。语音使用本机 Windows 语音合成。

## 从源码构建

在装有 .NET SDK 和 .NET Framework 4.8 Targeting Pack 的 Windows 电脑上运行：

```powershell
dotnet build tests/SkillVoiceTimer.Checks.csproj -c Release
.\tests\bin\Release\net48\SkillVoiceTimer.Checks.exe
.\bin\Release\net48\SkillVoiceTimer.exe
```

GitHub Actions 在 Windows 上构建便携包，检查重新计时、长按去重、多技能播报顺序、配置持久化、窗口创建和后台键盘注册。
