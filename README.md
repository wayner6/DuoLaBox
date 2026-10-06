# DuoLaBox

中文 Windows 桌面工具箱，使用 C#、WPF 和 .NET 10，MIT 许可。

## 功能

- 窗口置顶、鼠标选择及取消置顶标记。
- Windows 深浅色手动/定时切换、Hosts 编辑备份、保持唤醒。
- 批量重命名、检测和解除文件占用、批量调整图片尺寸。
- 规则驱动的磁盘清理，先扫描预览再确认删除。
- 屏幕取色、HEX/RGB/HSL/HSV 复制和颜色收藏。
- 系统硬件信息、托盘、开机启动、静默启动、应用主题和资源管理器右键菜单。

## 下载与测试

在 [Releases](https://github.com/wayner6/DuoLaBox/releases) 下载测试版 Windows x64 ZIP，解压后运行 `DuoLaBox.exe`。自包含版本不要求另行安装 .NET。

测试版尚未完成全部代码审查修复。升级前请先退出旧版本：本次单实例通信名称与协议有变化。首次测试建议使用临时文件或虚拟机，并先备份重要数据。

**注意：**

- 启动程序会按设置同步当前用户的资源管理器右键菜单；启用开机启动时也会更新启动项。
- 清理使用永久删除，不进入回收站。默认规则避开重要目录，但自定义规则不受同样的目录限制。
- Hosts 保存需要管理员权限，并创建备份。
- 解除文件占用可能关闭程序；强制关闭可能丢失未保存的工作。
- 图片调整输出到新目录，不修改原图；动画和多页图像不保证完整保留。

## 开发

需要 Windows 和 `global.json` 指定的 .NET 10 SDK。

```powershell
dotnet build DuoLaBox.sln --configuration Release
dotnet run --project tests/DuoLaBox.RegressionVerification --configuration Release
dotnet publish src/DuoLaBox/DuoLaBox.csproj --configuration Release --runtime win-x64 --self-contained true --output artifacts/win-x64
```

测试说明见 [tests/README.md](tests/README.md)。原有 `DuoLaBox.Verification` 包含真实注册表写入，不应作为无副作用测试运行。

GitHub Actions 在 Windows 上编译并运行隔离回归及剪贴板检查，验证通过后生成便携 ZIP 和 SHA-256 校验文件。版本标签必须与项目版本一致；标签构建成功后发布 GitHub 预发布版本。
