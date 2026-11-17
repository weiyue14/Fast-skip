# Fast Skip

Windows 窗口循环切换工具。使用系统自带的 .NET Framework 4.x，无需安装额外依赖。

## 下载

从 [Releases](https://github.com/weiyue14/Fast-skip/releases) 下载 `FastSkip.exe`，放在可写文件夹后双击运行。可执行文件已包含应用和托盘图标，无需另行下载图标。

## 使用

1. 打开 `dist/FastSkip.exe`，点击“刷新窗口”。
2. 点击窗口预览卡片添加或取消选择，在右侧切换队列中用上下箭头调整顺序。
3. 按 `Alt+X` 进入下一个窗口；最后一个回到第一个。不在队列中的窗口处于前台时，从第一项开始。
4. 点击顶栏左侧设置图标修改快捷键。点击输入框、按组合键，再保存。设置对话框打开期间暂停原快捷键。
5. 点击 × 隐藏到系统托盘；双击托盘图标打开主界面，右键“退出”完全关闭。

主界面使用内置窗口按钮：右上角减号与 × 均隐藏到托盘；拖动顶部空白处移动窗口。主界面不显示底部状态栏和重复的托盘按钮，发生错误时使用托盘通知提示。

每次启动重新选择窗口。快捷键保存在可执行文件旁的 `FastSkip.settings.xml`，请放在可写文件夹中。最小化窗口切换时会恢复，已关闭窗口会跳过。

## 运行开销

快捷键由独立消息线程接收，收到后立即切换，不等修饰键松开，不受卡片界面渲染阻塞。切换路径不复制窗口列表、不查询进程启动时间，仅验证下一个候选窗口的句柄和所属进程 / 线程。

隐藏或最小化主界面后，停止列表刷新定时器，释放 DWM 预览及卡片控件；再次打开时重建预览。显示界面时只在缩略图几何位置变化时向 DWM 提交更新。

实际显示速度仍取决于目标应用响应、最小化恢复动画及 Windows 激活限制。测试中的路由微基准使用假的原生 API，不代表实际桌面切换耗时。

## 窗口与预览

枚举当前桌面的可见顶层窗口（包括最小化窗口），不按工具窗口标记或 owner 关系排除应用；无标题窗口使用应用名称。桌面、任务栏、菜单和提示浮层不进入选择列表。

预览使用 Windows DWM 原生缩略图，不保存截图。预览不可用或窗口最小化时显示说明，仍可选择。杀戮尖塔的游戏本体显示为“杀戮尖塔”，ModTheSpire 启动器 / 日志单独展示。其他应用使用相同的窗口枚举和切换逻辑。

受保护内容、独占全屏及应用自行限制激活的情形可能无法正常预览或激活。应用会保留窗口选择，并提示实际激活失败。

## 构建与逻辑验证

在项目目录运行：

```powershell
.\build.ps1
.\test.ps1
```

已有程序运行、无法覆盖时，可以编译到另一个文件，再自行从托盘退出旧版并打开新版：

```powershell
.\build.ps1 -OutputName FastSkip-Updated.exe
```

代码测试覆盖循环选择、设置读写与窗口分类；实际桌面交互和视觉效果需要另行验收。

原生 API 参考：[DWM 缩略图](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/nf-dwmapi-dwmregisterthumbnail)、[全局快捷键](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey)、[窗口激活规则](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setforegroundwindow)。
