"""给每个方法加一句 <summary> 注释。

只补没有注释的方法，已有的保持原样。
"""
import os
import re
import sys

DESC = {
    # App.xaml.cs
    "App.xaml.cs::OnStartup": "程序入口：单实例检查、加载数据、创建班牌窗口和托盘。",
    "App.xaml.cs::ApplySettingsChanged": "设置改完后只处理需要重排窗口或重开服务的项。",
    "App.xaml.cs::CreateBoard": "创建一个班牌窗口，screenSaver 为真时是全屏屏保版。",
    "App.xaml.cs::OnUnhandledException": "兜底处理未捕获的界面异常，避免程序直接崩掉。",
    "App.xaml.cs::BuildTray": "创建托盘图标和右键菜单。",
    "App.xaml.cs::ShortUrl": "取一段截短后的手机端地址，用于菜单显示。",
    "App.xaml.cs::ToggleBoard": "切换班牌的显示与收起状态。",
    "App.xaml.cs::SetBoardVisible": "显示或收起班牌，并同步给设置窗口和托盘菜单。",
    "App.xaml.cs::UpdateTrayText": "根据当前状态更新托盘菜单里那一项的文案。",
    "App.xaml.cs::RefreshSettingsWindow": "设置窗口开着的话，让它刷新一下班牌状态。",
    "App.xaml.cs::RebuildBoard": "关掉旧窗口重建一个，用于切换显示方式或重新挂载桌面。",
    "App.xaml.cs::OpenSettings": "打开设置窗口，已经开着就切到前面。",
    "App.xaml.cs::OpenDataDirectory": "用资源管理器打开数据目录。",
    "App.xaml.cs::OpenWeb": "用默认浏览器打开手机端地址。",
    "App.xaml.cs::CopyUrl": "把手机端地址复制到剪贴板。",
    "App.xaml.cs::Toast": "在班牌上闪一条短提示。",
    "App.xaml.cs::NotifyTray": "弹一条托盘气泡通知，班牌藏起来时用它更合适。",
    "App.xaml.cs::QuitApp": "退出程序：先关窗口、停服务，再结束进程。",
    "App.xaml.cs::StartIdleWatcher": "启动空闲检测，用来触发屏保式全屏。",
    "App.xaml.cs::EnterScreenSaver": "空闲触发时弹全屏班牌，前台有全屏应用则推迟。",
    "App.xaml.cs::StartIdleRetry": "被全屏应用挡住时，启动五秒一次的复查定时器。",
    "App.xaml.cs::StopIdleRetry": "停掉屏保复查定时器。",
    "App.xaml.cs::ExitScreenSaver": "用户有输入时立刻收起屏保。",
    "App.xaml.cs::HideScreensaver": "关闭并释放屏保窗口。",
    "App.xaml.cs::ShowNextMessage": "从队列取一条消息弹出，弹完再取下一题。",
    "App.xaml.cs::OnExit": "程序结束前的清理：托盘、服务、互斥量。",

    # AppState.cs
    "AppState.cs::AppState": "初始化各服务和数据，并把自己登记成全局单例。",
    "AppState.cs::StartServices": "启动手机端 HTTP 服务，必要时拉起外网隧道。",
    "AppState.cs::StopServices": "停止 HTTP 服务和隧道进程。",
    "AppState.cs::CurrentSnapshot": "按当前时间算出班牌要显示的整份快照。",
    "AppState.cs::PersistNow": "立刻落盘，并通知界面重画。",
    "AppState.cs::Save": "请求保存，短时间内的多次修改会合并成一次写盘。",
    "AppState.cs::NotifyDataChanged": "数据变了：落盘并通知界面刷新。",
    "AppState.cs::NotifySettingsChanged": "设置变了：通知外部做相应的窗口调整。",
    "AppState.cs::PushMessage": "从任意线程推一条消息进入弹窗队列。",
    "AppState.cs::RecentMessages": "取最近发过的消息历史。",
    "AppState.cs::DeviceName": "返回这台电脑的名字，给手机端辨认用。",
    "AppState.cs::RestartServer": "改了端口之后重启 HTTP 服务。",
    "AppState.cs::Dispose": "释放语音、HTTP 和隧道等资源。",

    # Models/AppData.cs
    "AppData.cs::Period": "新建一个默认节次。",
    "AppData.cs::ParseTime": "把 08:00 这样的文本解析成时间。",
    "AppData.cs::FormatTime": "把时间格式化成 08:00 的样子。",
    "AppData.cs::CourseEntry": "新建一条空的课程记录。",
    "AppData.cs::DutyMember": "新建一个空的值日成员。",
    "AppData.cs::DutyDay": "新建某一天的值日安排。",
    "AppData.cs::ClassInfo": "用默认的学校和班级名初始化。",
    "AppData.cs::BannerMessage": "新建一条待显示的消息。",
    "AppData.cs::AppSettings": "所有设置项的默认值。",
    "AppData.cs::AppData": "新建一份空数据。",
    "AppData.cs::CreateDefault": "造一份能直接看到效果的示例数据。",

    # Services/AppIcons.cs
    "AppIcons.cs::CreateTrayIcon": "画一个 32 像素的托盘图标。",
    "AppIcons.cs::CreateAppIcon": "画一个 64 像素的窗口图标。",
    "AppIcons.cs::RoundedRect": "生成一个圆角矩形的路径。",

    # Services/AutoStart.cs
    "AutoStart.cs::IsEnabled": "查注册表判断是否已经设了开机自启。",
    "AutoStart.cs::Set": "写入或删除开机自启项。",

    # Services/BoardTheme.cs
    "BoardTheme.cs::Dark": "深色主题的调色板。",
    "BoardTheme.cs::Light": "浅色主题的调色板。",
    "BoardTheme.cs::All": "列出所有可选主题。",
    "BoardTheme.cs::Get": "按编号取主题，不认识的一律给深色。",

    # Services/DisplayHost.cs
    "DisplayHost.cs::DisplayHost": "默认先按置底窗口模式初始化。",
    "DisplayHost.cs::GetMonitorBounds": "取指定显示器的屏幕区域。",
    "DisplayHost.cs::Apply": "按设置把窗口摆到目标屏幕上并挂进桌面层。",
    "DisplayHost.cs::ApplyBottomDocked": "把窗口设成置底、不抢焦点、不进切换列表。",
    "DisplayHost.cs::TryAttachToDesktop": "尝试把窗口挂到资源管理器的桌面层里。",
    "DisplayHost.cs::DetachFromDesktop": "把窗口从桌面层摘出来，恢复成普通窗口。",
    "DisplayHost.cs::RefreshDesktop": "让桌面和壁纸重画一遍。",
    "DisplayHost.cs::StartWatchdog": "启动看门狗，防止窗口被别的操作挤出桌面层。",
    "DisplayHost.cs::StopWatchdog": "停掉看门狗定时器。",
    "DisplayHost.cs::KeepInPlace": "定期把窗口按回桌面层底部。",
    "DisplayHost.cs::Stop": "彻底停止：关看门狗、脱离桌面层、刷新桌面。",
    "DisplayHost.cs::ModeDisplayName": "把模式代号翻译成中文名。",

    # Services/FullscreenGuard.cs
    "FullscreenGuard.cs::IsForegroundWindowFullscreen": "兜底检查前台窗口是否盖满整块显示器。",

    # Services/IdleWatcher.cs
    "IdleWatcher.cs::IdleWatcher": "初始化空闲检测，默认阈值五分钟。",
    "IdleWatcher.cs::Start": "开始轮询空闲时间。",
    "IdleWatcher.cs::Stop": "停止轮询并清掉空闲状态。",
    "IdleWatcher.cs::GetIdleTime": "取系统记录的已经多久没有输入。",
    "IdleWatcher.cs::OnTick": "每次轮询判断是否进入或退出空闲。",
    "IdleWatcher.cs::Reset": "手动清掉空闲标记。",

    # Services/MessageService.cs
    "MessageService.cs::Enqueue": "把一条消息放进待显示队列并记入历史。",
    "MessageService.cs::Dequeue": "取出一条待显示的消息，没有就返回空。",
    "MessageService.cs::Clear": "清空待显示队列。",

    # Services/QrService.cs
    "QrService.cs::Create": "把一段文字生成二维码图片。",

    # Services/ScheduleEngine.cs
    "ScheduleEngine.cs::TimetableRow": "新建一行课表。",
    "ScheduleEngine.cs::BoardSnapshot": "新建一份空的显示快照。",
    "ScheduleEngine.cs::ScheduleEngine": "绑定到一份课表数据上。",
    "ScheduleEngine.cs::DayName": "把 1 到 7 转成周一到周日的简称。",
    "ScheduleEngine.cs::FullDayName": "把 1 到 7 转成星期一到星期日的全称。",
    "ScheduleEngine.cs::FormatCountdown": "把剩余时间格式化成 05:05 或 01:05:05。",
    "ScheduleEngine.cs::FormatChineseDuration": "把时长写成几小时几分钟这样的中文。",
    "ScheduleEngine.cs::SortedPeriods": "取按开始时间排好序的节次列表。",
    "ScheduleEngine.cs::FindCourse": "查某天某一节上什么课。",
    "ScheduleEngine.cs::DutyFor": "取某一天的值日安排。",
    "ScheduleEngine.cs::Build": "根据当前时间算出班牌该显示的全部内容。",
    "ScheduleEngine.cs::EndWord": "按节次类型选下课或结束这样的措辞。",
    "ScheduleEngine.cs::StartWord": "按节次类型选上课或开始这样的措辞。",
    "ScheduleEngine.cs::BuildNextHeadline": "课间时显示下一节要上什么。",
    "ScheduleEngine.cs::BuildPeriodSubText": "拼出老师、节次和时间这行副标题。",
    "ScheduleEngine.cs::BuildRows": "生成今日课表的每一行数据。",

    # Services/SpeechService.cs
    "SpeechService.cs::SpeechService": "初始化系统语音引擎。",
    "SpeechService.cs::InstalledVoices": "列出系统里装好的语音包。",
    "SpeechService.cs::PickChineseVoice": "优先挑中文语音，没有就用系统默认。",
    "SpeechService.cs::Speak": "朗读一段文字，会打断上一条。",
    "SpeechService.cs::Stop": "停止正在朗读的内容。",
    "SpeechService.cs::Dispose": "释放语音引擎。",

    # Services/StorageService.cs
    "StorageService.cs::StorageService": "确定数据目录，并准备防抖保存的定时器。",
    "StorageService.cs::ResolveDataDirectory": "优先用程序目录下的 data，不可写就退回用户目录。",
    "StorageService.cs::Load": "读取数据文件，坏了就用默认数据并备份原文件。",
    "StorageService.cs::TryBackupCorruptFile": "把读不出来的旧文件另存一份，避免直接丢数据。",
    "StorageService.cs::Normalize": "补齐旧版本文件里缺失的字段。",
    "StorageService.cs::Save": "请求保存，短时间内的多次修改会合并成一次写盘。",
    "StorageService.cs::SaveNow": "立刻写盘，不等防抖。",
    "StorageService.cs::WriteToDisk": "先写临时文件再替换，避免写一半断电损坏。",
    "StorageService.cs::RotateBackups": "每天留一份备份。",

    # Services/TunnelHost.cs
    "TunnelHost.cs::EnsureJobObject": "创建带关闭即杀标志的作业对象。",
    "TunnelHost.cs::BindToJob": "把隧道进程加入作业，让它随班牌一起结束。",
    "TunnelHost.cs::Start": "展开命令模板并启动隧道进程。",
    "TunnelHost.cs::ExpandTemplate": "把命令里的 app 和 port 占位符换成真实值。",
    "TunnelHost.cs::Quote": "参数里有空格就补上引号。",
    "TunnelHost.cs::OnData": "读取隧道输出，抓公网地址并识别常见错误。",
    "TunnelHost.cs::Append": "往日志里追加一行。",
    "TunnelHost.cs::RaiseChanged": "通知界面刷新隧道状态。",
    "TunnelHost.cs::Stop": "结束隧道进程。",
    "TunnelHost.cs::Dispose": "释放隧道进程。",
    "TunnelHost.cs::SplitCommandLine": "按 Windows 命令行习惯切分命令。",

    # Services/WebAssets.cs
    "WebAssets.cs::IndexHtml": "取出内嵌的手机端页面，读不到时给一个提示页。",

    # Services/WebServer.cs
    "WebServer.cs::WebServer": "绑定到提供数据的班牌宿主上。",
    "WebServer.cs::Start": "在指定端口开始监听。",
    "WebServer.cs::Stop": "停止监听并断开连接。",
    "WebServer.cs::Dispose": "停止服务并释放监听器。",
    "WebServer.cs::AcceptLoop": "后台线程不断接受新连接。",
    "WebServer.cs::HandleClient": "解析一个 HTTP 请求并交给路由处理。",
    "WebServer.cs::IndexOfHeaderEnd": "找出请求头结束的位置。",
    "WebServer.cs::HandleLogin": "校验口令并返回访问令牌。",
    "WebServer.cs::HandleState": "把班牌当前的显示内容返回给手机端。",
    "WebServer.cs::HandleGetConfig": "返回课表、值日、时间表等全部配置。",
    "WebServer.cs::HandlePostConfig": "接收手机端提交的配置改动并落盘。",
    "WebServer.cs::HandlePostMessage": "接收手机端发来的消息并推给班牌。",
    "WebServer.cs::HandleHistory": "返回最近发过的消息列表。",
    "WebServer.cs::ParseBody": "把请求体解析成 JSON。",
    "WebServer.cs::WriteJson": "回一个 JSON 响应。",
    "WebServer.cs::WriteText": "回一个文本或网页响应。",
    "WebServer.cs::StatusText": "把状态码翻译成英文短句。",
    "WebServer.cs::LocalAddress": "猜一个局域网内能访问到的 IP。",
    "WebServer.cs::Enumerate": "列出所有网卡上的地址。",

    # Views/MessageOverlay.xaml.cs
    "MessageOverlay.xaml.cs::MessageOverlay": "准备一条消息的弹窗。",
    "MessageOverlay.xaml.cs::OnLoaded": "摆到目标屏幕上并写入消息内容。",
    "MessageOverlay.xaml.cs::AnnounceAsync": "播放提示音并朗读消息。",
    "MessageOverlay.xaml.cs::PickFontSize": "按字数挑一个合适的字号。",
    "MessageOverlay.xaml.cs::UpdateCountdownText": "刷新几秒后自动关闭这行提示。",
    "MessageOverlay.xaml.cs::OnTickTick": "每秒递减，归零就关闭。",
    "MessageOverlay.xaml.cs::OnAckClick": "点知道了之后关闭弹窗。",
    "MessageOverlay.xaml.cs::CloseSmooth": "淡出之后再真正关闭。",
    "MessageOverlay.xaml.cs::OnClosed": "关闭时停掉计时和朗读。",

    # Views/SettingsWindow.xaml.cs
    "SettingsWindow.xaml.cs::SettingsWindow": "绑定数据并订阅隧道状态变化。",
    "SettingsWindow.xaml.cs::OnLoaded_": "把当前设置填进各个控件。",
    "SettingsWindow.xaml.cs::RefreshBoardState": "根据班牌是否收起，启用或禁用无操作全屏。",
    "SettingsWindow.xaml.cs::OnToggleBoard": "点击按钮收起或显示班牌。",
    "SettingsWindow.xaml.cs::RefreshNetworkInfo": "刷新局域网地址、服务状态和二维码。",
    "SettingsWindow.xaml.cs::RefreshTunnelInfo": "刷新隧道状态、日志和公网地址。",
    "SettingsWindow.xaml.cs::OnTunnelChanged": "隧道状态变化时回到界面线程刷新。",
    "SettingsWindow.xaml.cs::OnSave": "校验并把界面上的值写回设置。",
    "SettingsWindow.xaml.cs::ParseInt": "把文本框内容转成限定范围的整数。",
    "SettingsWindow.xaml.cs::NormalizeUrl": "补上协议头，避免二维码被当成一段文字。",
    "SettingsWindow.xaml.cs::OnTestMessage": "发一条测试消息看看弹窗效果。",
    "SettingsWindow.xaml.cs::OnTestVoice": "用当前的语音参数试听一句。",
    "SettingsWindow.xaml.cs::OnOpenWeb": "用浏览器打开手机端地址。",
    "SettingsWindow.xaml.cs::OnCopyUrl": "复制手机端地址到剪贴板。",
    "SettingsWindow.xaml.cs::OnTunnelStart": "用填写的命令启动隧道。",
    "SettingsWindow.xaml.cs::OnTunnelStop": "停止正在运行的隧道。",
    "SettingsWindow.xaml.cs::OnTunnelDefault": "把启动命令恢复成默认的便携路径。",
    "SettingsWindow.xaml.cs::OnOpenData": "打开数据目录。",
    "SettingsWindow.xaml.cs::OnClose": "关闭设置窗口。",

    # Views/SignboardWindow.xaml.cs
    "SignboardWindow.xaml.cs::SignboardWindow": "建窗口、套用主题、准备定时刷新。",
    "SignboardWindow.xaml.cs::OnDataChanged": "数据变了，下次刷新时重建课表。",
    "SignboardWindow.xaml.cs::Solid": "由一个颜色生成冻结的纯色画刷。",
    "SignboardWindow.xaml.cs::Gradient": "生成斜向渐变画刷。",
    "SignboardWindow.xaml.cs::VerticalGradient": "生成纵向渐变画刷。",
    "SignboardWindow.xaml.cs::HorizontalGradient": "生成横向渐变画刷。",
    "SignboardWindow.xaml.cs::ApplyTheme": "把整套配色写进资源字典，界面立刻跟着变。",
    "SignboardWindow.xaml.cs::SyncTheme": "设置里的主题变了就重新套用一次。",
    "SignboardWindow.xaml.cs::OnLoaded": "摆到目标屏幕并挂进桌面层。",
    "SignboardWindow.xaml.cs::OnClosing": "拦截关闭，改成收起。",
    "SignboardWindow.xaml.cs::HideBoard": "收起班牌并暂停桌面层。",
    "SignboardWindow.xaml.cs::ShowBoard": "重新显示班牌并恢复桌面层。",
    "SignboardWindow.xaml.cs::ClosePermanently": "真正销毁窗口。",
    "SignboardWindow.xaml.cs::Render": "刷新时钟、状态卡、课表和值日。",
    "SignboardWindow.xaml.cs::BadgeBrushFor": "按状态挑角标颜色。",
    "SignboardWindow.xaml.cs::HeadlineSize": "按标题字数调字号。",
    "SignboardWindow.xaml.cs::RenderTimetable": "重建右侧的今日课表。",
    "SignboardWindow.xaml.cs::BuildRow": "画出一行课表。",
    "SignboardWindow.xaml.cs::RenderDuty": "重建底部的值日名单。",
    "SignboardWindow.xaml.cs::ShowToast": "弹出短提示。",
    "SignboardWindow.xaml.cs::FadeToast": "把提示淡入或淡出。",
}

SIG = re.compile(r'^(\s*)(?:public|private|protected|internal|static)[\w\s<>,\[\]\.]*?\s+(\w+)\s*\(')


def has_doc(acc):
    for prev in reversed(acc):
        s = prev.strip()
        if not s:
            continue
        return s.startswith("///")
    return False


def main(root):
    total = 0
    missed = []
    for base, dirs, files in os.walk(root):
        if 'obj' in base or 'bin' in base:
            continue
        for name in sorted(files):
            if not name.endswith('.cs'):
                continue
            path = os.path.join(base, name)
            raw = open(path, 'rb').read()
            crlf = b'\r\n' in raw
            text = raw.decode('utf-8-sig')
            lines = text.splitlines()

            out = []
            changed = False
            for line in lines:
                m = SIG.match(line)
                if m and 'extern ' not in line and 'delegate ' not in line:
                    tail = line.rstrip()
                    ok = tail.endswith(')') or tail.endswith(') {') or tail.endswith(');')
                    if ok:
                        key = name + '::' + m.group(2)
                        if key in DESC:
                            if not has_doc(out):
                                out.append(m.group(1) + '/// <summary>' + DESC[key] + '</summary>')
                                total += 1
                                changed = True
                        else:
                            missed.append(key)
                out.append(line)

            if changed:
                nl = '\r\n' if crlf else '\n'
                data = nl.join(out)
                if text.endswith('\n'):
                    data += nl
                open(path, 'w', encoding='utf-8-sig', newline='').write(data)

    print('inserted', total)
    if missed:
        print('未覆盖：')
        for item in missed:
            print('  ' + item)


if __name__ == '__main__':
    main(sys.argv[1])
