# 提肛节律

基于 `WinUI 3 + Windows App SDK` 的 Win11 原生桌面应用。

当前工程保留 `WinUI 3` 打包模型。在这台机器上不建议使用 `dotnet run` 做未打包启动，纯终端开发请使用“打包-安装-启动”脚本。

## 当前功能

- 自定义提醒计划
- 多个提醒时间
- 每周生效日切换
- 训练节奏页与动画球
- 训练完成历史与连续天数统计
- 本地 JSON 存储
- Win11 Toast 测试与计划重排
- 通知点击回到训练页
- 系统托盘常驻、最小化到托盘、关闭到托盘
- OpenAI 兼容 AI 计划生成

## 终端开发

```powershell
.\scripts\dev-run.ps1
```

这条命令会自动：

- 生成或复用开发证书
- 信任证书到 `CurrentUser\TrustedPeople`
- 打包 `msix`
- 卸载旧版本
- 安装新版本
- 启动应用

## 开发构建

```powershell
.\scripts\dev-build.ps1
```

## 清理

```powershell
.\scripts\dev-clean.ps1
```

## 文件夹发布

```powershell
dotnet publish .\TigangReminder.App\TigangReminder.App.csproj -p:Platform=x64 -c Release
```

输出目录：

`TigangReminder.App\bin\Release\net9.0-windows10.0.26100.0\win-x64\publish`

## 使用说明

- 关闭窗口或最小化窗口时，可在设置页切换是否转入托盘后台。
- AI 工坊中未配置 API 时，会自动回退到本地建议计划。
- 如果脚本提示文件被占用，先执行 `.\scripts\dev-clean.ps1` 再重新运行。
- 不要再用 `dotnet run` 直接启动当前工程。
