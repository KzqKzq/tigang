# 提肛节律

基于 `WinUI 3 + Windows App SDK` 的 Win11 原生桌面应用。

当前工程已切换为“未打包开发模式”，优先支持纯终端 `dotnet run`。

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
dotnet run --project .\TigangReminder.App\TigangReminder.App.csproj -p:Platform=x64
```

## 开发构建

```powershell
dotnet build .\TigangReminder.App\TigangReminder.App.csproj -p:Platform=x64
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

## 已知事项

- 当前仓库优先服务于纯终端开发；如果后续需要正式分发的 `msix` 安装包，建议单独增加一个 packaging 项目。
