"""从源码里抽取方法签名和注释，生成技术文档。"""
import os
import re
import sys

SIG = re.compile(r'^(\s*)(?:public|private|protected|internal|static)[\w\s<>,\[\]\.]*?\s+(\w+)\s*\(')

ORDER = [
    "Models/AppData.cs",
    "AppState.cs",
    "App.xaml.cs",
    "Services/ScheduleEngine.cs",
    "Services/StorageService.cs",
    "Services/DisplayHost.cs",
    "Services/IdleWatcher.cs",
    "Services/FullscreenGuard.cs",
    "Services/BoardTheme.cs",
    "Services/MessageService.cs",
    "Services/SpeechService.cs",
    "Services/WebServer.cs",
    "Services/WebAssets.cs",
    "Services/QrService.cs",
    "Services/TunnelHost.cs",
    "Services/AppIcons.cs",
    "Services/AutoStart.cs",
    "Views/BoardWindowBase.cs",
    "Views/SignboardWindow.xaml.cs",
    "Views/NarrowBoardWindow.xaml.cs",
    "Views/MessageOverlay.xaml.cs",
    "Views/SettingsWindow.xaml.cs",
]

PURPOSE = {
    "Models/AppData.cs": "全部数据结构与默认值，整份数据序列化成一个 JSON 文件。",
    "AppState.cs": "全局状态中枢，把数据、引擎、各项服务串在一起，并暴露给手机端读写。",
    "App.xaml.cs": "程序入口，管托盘、班牌窗口、消息弹窗、屏保和退出清理。",
    "Services/ScheduleEngine.cs": "课程引擎，按作息时间判断当前状态并算出倒计时。",
    "Services/StorageService.cs": "JSON 持久化，带防抖写盘、损坏备份和每日备份。",
    "Services/DisplayHost.cs": "把班牌窗口按到桌面层，支持壁纸层和置底两种做法。",
    "Services/IdleWatcher.cs": "检测鼠标键盘空闲时长，驱动屏保式全屏。",
    "Services/FullscreenGuard.cs": "识别前台全屏应用，避免屏保盖住视频和课件。",
    "Services/BoardTheme.cs": "深色与浅色两套配色。",
    "Services/MessageService.cs": "消息的排队与历史记录。",
    "Services/SpeechService.cs": "调用系统语音引擎朗读消息。",
    "Services/WebServer.cs": "内嵌的 HTTP 服务，给手机端提供页面和接口。",
    "Services/WebAssets.cs": "内嵌的手机端页面资源读取。",
    "Services/QrService.cs": "把访问地址生成二维码。",
    "Services/TunnelHost.cs": "托管外网隧道进程，并保证它随班牌一起结束。",
    "Services/AppIcons.cs": "运行时绘制托盘和窗口图标。",
    "Services/AutoStart.cs": "开机自启的注册表读写。",
    "Views/BoardWindowBase.cs": "两个班牌界面的公共逻辑：数据刷新、主题切换、桌面层挂载、屏保与收起。",
    "Views/SignboardWindow.xaml.cs": "宽屏界面（全屏 / 壁纸层）：左栏时钟和当前状态，右栏课表，底部值日。",
    "Views/NarrowBoardWindow.xaml.cs": "右侧停靠专用的窄版界面：左栏时钟、状态和值日，右栏整条课表。",
    "Views/MessageOverlay.xaml.cs": "老师消息的全屏大字弹窗。",
    "Views/SettingsWindow.xaml.cs": "电脑端设置窗口。",
}

HEADER = """# 班级班牌 技术文档

一个跑在 Windows 上的班级班牌程序：把班牌做成桌面壁纸层或置底窗口，显示当前时间、今日课表、上下课倒计时和值日安排；老师可以用手机给它发消息，班牌大字弹出并用机器人声音念一遍。

---

## 一、运行环境

| 项目 | 说明 |
|---|---|
| 运行时 | .NET Framework 4.8（Win10 / Win11 系统自带，无需安装） |
| 界面 | WPF |
| 第三方库 | Newtonsoft.Json 13.0.3（JSON）、QRCoder 1.4.3（二维码） |
| 语音 | System.Speech，调用系统 SAPI，中文系统一般自带中文语音 |
| 开发环境 | Visual Studio，项目文件 `NEWCLASSroom.csproj` |

## 二、目录结构

```
NEWCLASSroom_solution/
├─ NEWCLASSroom/                 主程序
│  ├─ Models/AppData.cs          数据结构
│  ├─ Services/                  各项服务（课程、显示、网络、隧道…）
│  ├─ Views/                     三个窗口
│  ├─ Web/index.html             手机端页面（编译时嵌入 exe）
│  └─ App.xaml.cs                程序入口
├─ _check/                       开发期用的编译校验和冒烟测试
└─ docs/技术文档.md               本文档
```

运行时会在程序目录下自动生成：

```
data/classboard.json        课表、值日、设置
data/classboard.YYYYMMDD.json  每日备份
frp/frpc.exe  frp/frpc.toml   外网隧道（便携版随程序附带）
```

## 三、几条关键链路

**1. 班牌是怎么算出“现在上什么课”的**

`ScheduleEngine.Build(now)` 拿当前时间跟作息时间表逐个比对：落在某个节次区间内就是“正在上”，否则找下一个开始的节次，算出剩余秒数和进度。结果打包成一个 `BoardSnapshot`，界面只管照着显示，不做任何判断。

**2. 手机端一条消息是怎么变成大字弹窗的**

手机 POST `/api/message` → `WebServer.HandlePostMessage` 校验 → `AppState.PushMessage` 切回界面线程 → `MessageService.Enqueue` → `App.ShowNextMessage` 弹出 `MessageOverlay` → 播放提示音、SAPI 朗读、倒计时结束淡出 → 回到队列取下一 条。

**3. 班牌怎么待在桌面上又不挡老师干活**

三种做法，设置里可切换：

- `BottomDocked` 置底窗口：铺满全屏，永远压在 Z 序最底、不抢焦点、不进 Alt+Tab。兼容性最好，但会挡住桌面图标。
- `RightDocked` 右侧停靠：只占屏幕右半边，左侧桌面图标照常可见。这个模式用的是另一套专门排版的界面。
- `Wallpaper` 桌面壁纸层：把窗口 `SetParent` 到资源管理器的 WorkerW 层，成为桌面背景，图标浮在它上面。

三种模式都由一个 900ms 的看门狗维持位置（壁纸层除外，它靠重新挂载）。用户主动收起时看门狗必须停掉，否则会把已隐藏的窗口重新显示出来（那会是一整块黑屏）。

班牌有两套界面，共用一个 `BoardWindowBase`：

| 界面 | 用于 | 排版 |
|---|---|---|
| `SignboardWindow` | 置底窗口 / 壁纸层 / 屏保 | 左栏时钟和当前状态，右栏今日课表，底部一条值日 |
| `NarrowBoardWindow` | 右侧停靠 | 左栏时钟、正在上课、今日值日，右栏整条课表 |

两边的 XAML 各自声明同名控件，基类通过 `FindName` 绑定，所以刷新逻辑只有一份；排版差异全部写在各自的 XAML 里，不在运行时改布局。

**4. 屏保什么时候出现**

只在班牌被收起时才有意义。空闲超过设定分钟数后弹一个全屏班牌窗口，鼠标键盘一动立刻收起。弹之前先问 `FullscreenGuard`：前台如果正在放视频或放映 PPT，就推迟，每五秒复查一次。

## 四、模块与方法
"""

FOOTER = """
---

## 五、手机端接口

所有接口都是 JSON，除 `/`、`/api/ping`、`/api/login` 外都需要请求头 `X-Token`。

| 方法 | 路径 | 说明 |
|---|---|---|
| GET | `/` | 手机端页面 |
| GET | `/api/ping` | 探活，返回电脑名 |
| POST | `/api/login` | 用口令换访问令牌 |
| GET | `/api/state` | 班牌当前显示的内容 |
| GET | `/api/config` | 课表、值日、时间表、部分设置 |
| POST | `/api/config` | 提交配置改动 |
| POST | `/api/message` | 发一条消息到班牌 |
| GET | `/api/messages` | 最近发送记录 |

## 六、主要设置项

| 字段 | 说明 |
|---|---|
| `DisplayMode` | `BottomDocked` 铺满置底 / `RightDocked` 右半边停靠 / `Wallpaper` 桌面壁纸层 |
| `Theme` | `Dark` 深色 / `Light` 浅色 |
| `MonitorIndex` | 显示在哪一块屏幕 |
| `EnableIdleFullscreen` / `IdleMinutes` | 无操作全屏开关与时长 |
| `AutoStart` | 开机自启 |
| `Port` / `Pin` | 手机端端口与访问口令 |
| `SpeechEnabled` / `SpeechVoice` / `SpeechRate` / `SpeechVolume` | 语音播报相关 |
| `MessageDurationSeconds` | 消息弹窗停留秒数 |
| `TunnelCommand` / `TunnelAutoStart` / `PublicUrl` | 外网隧道命令、自启开关、公网地址 |

## 七、发布打包

```
班级班牌/
├─ NEWCLASSroom.exe
├─ NEWCLASSroom.exe.config
├─ 其余 dll
└─ frp/
   ├─ frpc.exe
   └─ frpc.toml      ← 每人自己的，别把你的发出去
```

启动命令默认是 `{app}\\frp\\frpc.exe -c {app}\\frp\\frpc.toml`，`{app}` 在运行时替换成程序目录，所以整个文件夹拷到任何电脑都不用改配置。

`frpc.toml` 里含隧道 token，属于个人凭据。对外分发时只带 `frpc.exe`，让对方自己注册账号、建隧道、导出配置。

## 八、已知限制

- 壁纸层依赖资源管理器的 WorkerW 结构，个别 Windows 版本挂不上，程序会自动退回置底模式。
- 置底模式下会挡住桌面图标，需要看桌面时用托盘菜单收起班牌。
- 中文语音依赖系统语音包，精简版系统可能需要到「设置 → 时间和语言 → 语音」里安装。
- 公网地址无法自动推导，隧道种类太多，只能手动填一次。
- 单班使用。多班同屏需要另外扩展数据模型。
"""


def strip_tags(text):
    text = re.sub(r'<[^>]+>', '', text)
    return re.sub(r'\s+', ' ', text).strip()


def summary_before(lines, index):
    block = []
    j = index - 1
    while j >= 0 and lines[j].strip().startswith('///'):
        block.append(lines[j].strip()[3:])
        j -= 1
    if not block:
        return ""
    block.reverse()
    return strip_tags(" ".join(block))


def clean_signature(line):
    text = line.strip()
    if text.endswith('{'):
        text = text[:-1].strip()
    if text.endswith(';'):
        text = text[:-1].strip()
    for mod in ('public ', 'private ', 'protected ', 'internal ', 'static '):
        while text.startswith(mod):
            text = text[len(mod):]
    return text


def main(root, out_path):
    parts = [HEADER]
    for rel in ORDER:
        path = os.path.join(root, rel.replace('/', os.sep))
        if not os.path.exists(path):
            continue
        lines = open(path, encoding='utf-8-sig').read().splitlines()
        rows = []
        for i, line in enumerate(lines):
            if 'extern ' in line or 'delegate ' in line:
                continue
            m = SIG.match(line)
            if not m:
                continue
            tail = line.rstrip()
            if not (tail.endswith(')') or tail.endswith(') {') or tail.endswith(');')):
                continue
            sig = clean_signature(line)
            doc = summary_before(lines, i)
            if not doc:
                continue
            rows.append((sig.replace('|', '\\|'), doc.replace('|', '\\|')))

        title = rel.replace('/', ' / ')
        parts.append("\n### `%s`\n" % title)
        parts.append("%s\n" % PURPOSE.get(rel, ""))
        parts.append("| 方法 | 说明 |")
        parts.append("|---|---|")
        for sig, doc in rows:
            parts.append("| `%s` | %s |" % (sig, doc))
        parts.append("")

    parts.append(FOOTER)
    os.makedirs(os.path.dirname(out_path), exist_ok=True)
    open(out_path, 'w', encoding='utf-8').write("\n".join(parts))
    total = sum(1 for line in parts if line.startswith('| `'))
    print('methods documented:', total)


if __name__ == '__main__':
    main(sys.argv[1], sys.argv[2])
