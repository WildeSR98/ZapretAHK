; ============================================================================
; Zapret Manager — AutoHotkey v2 Edition (Полная версия)
; GUI-приложение для управления DPI bypass службой
; ============================================================================
#Requires AutoHotkey v2.0
#SingleInstance Force
#WinActivateForce
Persistent

; ── Подключение библиотек ─────────────────────────────────────────────────────
#Include lib\WinService.ahk
#Include lib\Logger.ahk
#Include lib\JsonParser.ahk
#Include lib\AppConfig.ahk
#Include lib\AdminHelper.ahk
#Include lib\HttpService.ahk
#Include lib\IspDetector.ahk
#Include lib\UpdateChecker.ahk
#Include lib\ListsUpdater.ahk
#Include lib\BackupManager.ahk
#Include lib\DiagnosticsRunner.ahk
#Include lib\SettingsGui.ahk
#Include lib\DomainManagerGui.ahk
#Include lib\StrategyEditorGui.ahk
#Include lib\TgProxyGui.ahk
#Include lib\TrafficMonitorGui.ahk
#Include lib\NicSelectorGui.ahk
#Include lib\StrategyTester.ahk

; ── Глобальные переменные ─────────────────────────────────────────────────────
global RootDir      := ""
global BinDir       := ""
global ListsDir     := ""
global UtilsDir     := ""
global StrategiesDir := ""
global Config       := {}
global ServiceName  := "zapret"
global MainWindow   := ""

; ── Инициализация ─────────────────────────────────────────────────────────────
Init()
{
    global RootDir, BinDir, ListsDir, UtilsDir, StrategiesDir, Config

    RootDir       := A_ScriptDir
    BinDir        := RootDir . "\bin"
    ListsDir      := RootDir . "\lists"
    UtilsDir      := RootDir . "\utils"
    StrategiesDir := RootDir . "\strategies"

    DirCreate(UtilsDir)
    DirCreate(RootDir . "\logs")
    DirCreate(RootDir . "\backups")

    Config := AppConfig.Load(RootDir . "\config.json")

    RequireAdmin()

    Logger_Init()
    Logger_Info("=== Запуск Zapret Manager AHK ===")
}

; ── Основное окно ─────────────────────────────────────────────────────────────
CreateMainWindow()
{
    global MainWindow, Config, UtilsDir

    MainWindow := Gui("+Resize MinSize800x600", "Zapret Manager v" . AppConfig.Get(Config, "version", "3.0.0"))
    MainWindow.OnEvent("Close", GuiClose)
    MainWindow.OnEvent("Size", GuiSize)

    ; ── Меню ──────────────────────────────────────────────────────────────────
    MB := MenuBar()

    FileMenu := Menu()
    FileMenu.Add("Проверить обновления`tCtrl+U", (*) => CheckUpdates())
    FileMenu.Add("Экспорт отчёта`tCtrl+E", (*) => ExportReport())
    FileMenu.Add()
    FileMenu.Add("Выход`tAlt+F4", (*) => ExitApp())
    MB.Add("Файл", FileMenu)

    ServiceMenu := Menu()
    ServiceMenu.Add("Установить службу`tCtrl+I", (*) => InstallService())
    ServiceMenu.Add("Удалить службу`tCtrl+D", (*) => RemoveService())
    ServiceMenu.Add()
    ServiceMenu.Add("Запустить`tF5", (*) => StartService())
    ServiceMenu.Add("Остановить`tF6", (*) => StopService())
    ServiceMenu.Add("Перезапустить`tF7", (*) => RestartService())
    ServiceMenu.Add()
    ServiceMenu.Add("Статус`tF9", (*) => ShowServiceStatus())
    MB.Add("Служба", ServiceMenu)

    ToolsMenu := Menu()
    ToolsMenu.Add("Диагностика`tCtrl+D", (*) => RunDiagnostics())
    ToolsMenu.Add("Тест стратегий`tCtrl+T", (*) => TestStrategies())
    ToolsMenu.Add("Мониторинг трафика`tCtrl+M", (*) => TrafficMonitor())
    ToolsMenu.Add()
    ToolsMenu.Add("TG Proxy`tCtrl+G", (*) => ManageTgProxy())
    ToolsMenu.Add("Бэкап`tCtrl+B", (*) => DoBackup())
    MB.Add("Инструменты", ToolsMenu)

    SettingsMenu := Menu()
    SettingsMenu.Add("Настройки`tCtrl+S", (*) => OpenSettings())
    SettingsMenu.Add("Редактор стратегий`tCtrl+R", (*) => EditStrategies())
    SettingsMenu.Add("Управление доменами", (*) => ManageDomains())
    SettingsMenu.Add("Сетевой адаптер", (*) => SelectNic())
    MB.Add("Настройки", SettingsMenu)

    HelpMenu := Menu()
    HelpMenu.Add("О программе", (*) => ShowAbout())
    MB.Add("Справка", HelpMenu)

    MainWindow.MenuBar := MB

    ; ── Заголовок и статус ────────────────────────────────────────────────────
    MainWindow.Add("Text", "x20 y15 w300 h30 +0x200", "Zapret Manager")

    MainWindow.Add("Text", "x20 y55 w100 h25", "Служба:")
    global ServiceStatusLabel := MainWindow.Add("Text", "x130 y55 w200 h25 cGreen", "Загрузка...")

    MainWindow.Add("Text", "x20 y85 w100 h25", "Стратегия:")
    global StrategyLabel := MainWindow.Add("Text", "x130 y85 w420 h25", "...")

    MainWindow.Add("Text", "x20 y115 w100 h25", "Manager:")
    MainWindow.Add("Text", "x130 y115 w150 h25", GetManagerVersion())
    MainWindow.Add("Text", "x300 y115 w60 h25", "Core:")
    MainWindow.Add("Text", "x370 y115 w180 h25", GetCoreVersion())

    ; ── Управление службой ────────────────────────────────────────────────────
    MainWindow.Add("GroupBox", "x20 y148 w350 h130", "Управление службой")

    BtnInstall := MainWindow.Add("Button", "x40 y173 w130 h35", "Установить")
    BtnInstall.OnEvent("Click", (*) => InstallService())

    BtnRemove := MainWindow.Add("Button", "x190 y173 w130 h35", "Удалить")
    BtnRemove.OnEvent("Click", (*) => RemoveService())

    BtnStart := MainWindow.Add("Button", "x40 y218 w130 h35", "Запустить")
    BtnStart.OnEvent("Click", (*) => StartService())

    BtnStop := MainWindow.Add("Button", "x190 y218 w130 h35", "Остановить")
    BtnStop.OnEvent("Click", (*) => StopService())

    ; ── Быстрые действия ──────────────────────────────────────────────────────
    MainWindow.Add("GroupBox", "x400 y148 w370 h390", "Быстрые действия")

    actions := [
        ["Диагностика",           (*) => RunDiagnostics()],
        ["Тест стратегий",        (*) => TestStrategies()],
        ["TG Proxy",              (*) => ManageTgProxy()],
        ["Бэкап / Восстановление",(*) => DoBackup()],
        ["Обновить списки IPSet", (*) => UpdateLists()],
        ["Обновить Hosts",        (*) => UpdateHosts()],
        ["Проверить обновления",  (*) => CheckUpdates()],
        ["Мониторинг трафика",    (*) => TrafficMonitor()],
        ["Редактор стратегий",    (*) => EditStrategies()],
        ["Настройки",             (*) => OpenSettings()],
        ["Определение провайдера",(*) => DetectIsp()],
        ["Управление доменами",   (*) => ManageDomains()]
    ]

    Loop 12
    {
        i := A_Index
        if (i <= actions.Length)
        {
            btn := MainWindow.Add("Button", "x420 y" . (168 + (i-1)*31) . " w330 h27", actions[i][1])
            btn.OnEvent("Click", actions[i][2])
        }
    }

    ; ── Состояние компонентов ─────────────────────────────────────────────────
    MainWindow.Add("GroupBox", "x20 y295 w350 h110", "Состояние компонентов")

    wdEnabled  := FileExist(UtilsDir . "\watchdog.enabled")
    gfEnabled  := FileExist(UtilsDir . "\game_filter.enabled")
    updEnabled := FileExist(UtilsDir . "\check_updates.enabled")

    MainWindow.Add("Text", "x35 y318 w110 h22", "Watchdog:")
    MainWindow.Add("Text", "x155 y318 w60 h22 c" . (wdEnabled ? "Green" : "Gray"), wdEnabled ? "ВКЛ" : "ВЫКЛ")

    MainWindow.Add("Text", "x35 y348 w110 h22", "Игровой фильтр:")
    MainWindow.Add("Text", "x155 y348 w60 h22 c" . (gfEnabled ? "Green" : "Gray"), gfEnabled ? "ВКЛ" : "ВЫКЛ")

    MainWindow.Add("Text", "x35 y378 w110 h22", "Обновления:")
    MainWindow.Add("Text", "x155 y378 w60 h22 c" . (updEnabled ? "Green" : "Gray"), updEnabled ? "ВКЛ" : "ВЫКЛ")

    ; ── Кнопка выхода ─────────────────────────────────────────────────────────
    BtnExit := MainWindow.Add("Button", "x670 y548 w100 h35", "Выход")
    BtnExit.OnEvent("Click", (*) => ExitApp())

    MainWindow.Show("w800 h600")

    SetTimer(UpdateStatus, 5000)
    UpdateStatus()
}

; ── Обновление статуса ────────────────────────────────────────────────────────
UpdateStatus()
{
    global ServiceStatusLabel, StrategyLabel, ServiceName

    state    := WinService_GetState(ServiceName)
    strategy := GetCurrentStrategy()

    ServiceStatusLabel.Text := MapServiceState(state)
    ServiceStatusLabel.Opt("c" . GetStateColor(state))
    StrategyLabel.Text := strategy

    UpdateTrayIcon(state)
}

MapServiceState(state)
{
    switch state
    {
        case "Running":      return "Запущена"
        case "Stopped":      return "Остановлена"
        case "NotInstalled": return "Не установлена"
        case "Starting":     return "Запуск..."
        case "Stopping":     return "Остановка..."
        default:             return state
    }
}

GetStateColor(state)
{
    switch state
    {
        case "Running":      return "00AA00"
        case "Stopped":      return "AAAA00"
        case "NotInstalled": return "AA0000"
        default:             return "888888"
    }
}

; ── Управление службой ────────────────────────────────────────────────────────
InstallService()
{
    global BinDir, ServiceName

    strategyFile := SelectStrategyFile()
    if !strategyFile
    {
        MsgBox("Стратегии не найдены в папке strategies/", "Ошибка", 48)
        return
    }

    args    := ParseBatArgs(strategyFile)
    winws   := BinDir . "\winws.exe"
    binPath := Chr(34) . winws . Chr(34) . " " . args

    Logger_Info("Установка службы: " . binPath)

    if WinService_Install(ServiceName, "Zapret DPI Bypass", "Обход DPI блокировок", binPath)
    {
        MsgBox("Служба успешно установлена и запущена!", "Zapret Manager", 64)
        UpdateStatus()
    }
    else
        MsgBox("Не удалось установить службу. Проверьте логи.", "Ошибка", 48)
}

RemoveService()
{
    global ServiceName

    if WinService_Remove(ServiceName)
    {
        MsgBox("Служба удалена", "Zapret Manager", 64)
        UpdateStatus()
    }
    else
        MsgBox("Не удалось удалить службу", "Ошибка", 48)
}

StartService()
{
    global ServiceName

    if WinService_Start(ServiceName)
    {
        Logger_Info("Служба запущена")
        UpdateStatus()
    }
    else
        MsgBox("Не удалось запустить службу", "Ошибка", 48)
}

StopService()
{
    global ServiceName

    if WinService_Stop(ServiceName)
    {
        Logger_Info("Служба остановлена")
        UpdateStatus()
    }
    else
        MsgBox("Не удалось остановить службу", "Ошибка", 48)
}

RestartService()
{
    StopService()
    Sleep(1000)
    StartService()
}

ShowServiceStatus()
{
    global ServiceName

    state     := WinService_GetState(ServiceName)
    imagePath := WinService_GetImagePath(ServiceName)
    strategy  := GetCurrentStrategy()

    msg := "Состояние: " . MapServiceState(state) . "`n"
         . "Стратегия: " . strategy . "`n"
         . "ImagePath: " . (imagePath ? SubStr(imagePath, 1, 80) . "..." : "N/A")

    MsgBox(msg, "Статус службы", 64)
}

; ── Вспомогательные функции ───────────────────────────────────────────────────
SelectStrategyFile()
{
    global StrategiesDir

    if !DirExist(StrategiesDir)
        return ""

    files := []
    Loop Files, StrategiesDir . "\general*.bat"
        files.Push(A_LoopFilePath)

    return files.Length > 0 ? files[1] : ""
}

ParseBatArgs(batFile)
{
    global BinDir, ListsDir

    content := FileRead(batFile)
    for line in StrSplit(content, "`n")
    {
        if InStr(line, "winws.exe")
        {
            pos := InStr(line, "winws.exe")
            if pos
            {
                args := Trim(SubStr(line, pos + 9))
                args := StrReplace(args, "%~dp0bin\",   BinDir   . "\")
                args := StrReplace(args, "%~dp0lists\", ListsDir . "\")
                return args
            }
        }
    }
    return ""
}

GetCurrentStrategy()
{
    global ServiceName

    Try
    {
        val := RegRead("HKLM\SYSTEM\CurrentControlSet\Services\" . ServiceName, "ImagePath")
        return val ? val : "не установлена"
    }
    Catch
        return "не установлена"
}

GetManagerVersion()
{
    global UtilsDir, Config

    versionFile := UtilsDir . "\manager_version.txt"
    if FileExist(versionFile)
        return Trim(FileRead(versionFile))
    return AppConfig.Get(Config, "version", "3.0.0")
}

GetCoreVersion()
{
    global BinDir

    versionFile := BinDir . "\version.txt"
    if FileExist(versionFile)
        return Trim(FileRead(versionFile))
    return "не установлен"
}

; ── Реализация функций меню ───────────────────────────────────────────────────

RunDiagnostics()
{
    global Config, RootDir
    DiagnosticsRunner.Run(Config, RootDir)
}

TestStrategies()
{
    global Config, RootDir
    StrategyTester.Run(RootDir, Config)
}

ManageTgProxy()
{
    global RootDir
    TgProxyGui.Show(RootDir)
}

DoBackup()
{
    global RootDir, Config
    keepCount := Integer(AppConfig.Get(Config, "backup.keep_count", 5))
    choice := MsgBox("Выберите действие:`n`n[Да] — Создать бэкап`n[Нет] — Восстановить из бэкапа", "Бэкап — Zapret Manager", 3)
    if (choice = "Yes")
    {
        result := BackupManager.CreateBackup(RootDir, keepCount)
        MsgBox(result != "" ? "Бэкап создан:`n" . result : "Ошибка создания бэкапа.", "Zapret Manager", result != "" ? 64 : 16)
    }
    else if (choice = "No")
    {
        backups := BackupManager.ListBackups(RootDir)
        if (backups.Length = 0)
        {
            MsgBox("Нет доступных бэкапов.", "Zapret Manager", 64)
            return
        }
        names := ""
        for i, bp in backups
            names .= i . ". " . StrReplace(bp, RootDir . "\backups\", "") . "`n"
        idx := InputBox("Введите номер бэкапа для восстановления:`n`n" . names, "Восстановление",, "1").Value
        if !IsInteger(idx) || Integer(idx) < 1 || Integer(idx) > backups.Length
            return
        if MsgBox("Восстановить из: " . StrReplace(backups[Integer(idx)], RootDir . "\backups\", "") . "?", "Подтверждение", 4) = "Yes"
            MsgBox(BackupManager.RestoreBackup(RootDir, backups[Integer(idx)]) ? "Восстановление завершено." : "Ошибка восстановления.", "Zapret Manager", 64)
    }
}

UpdateLists()
{
    global Config, ListsDir
    dlgProg := Gui("+AlwaysOnTop", "Обновление списков...")
    dlgProg.Add("Text",, "Загрузка списков из репозитория...")
    lblProg := dlgProg.Add("Text", "w360", "Подготовка...")
    dlgProg.Show("w380 h80")
    _cb(fname, ok)
    {
        lblProg.Text := (ok ? "OK: " : "ERR: ") . fname
    }
    ListsUpdater.UpdateAll(Config, ListsDir, _cb)
    dlgProg.Destroy()
    MsgBox("Списки обновлены", "Zapret Manager", 64)
}

UpdateHosts()
{
    global Config, ListsDir
    dlgProg := Gui("+AlwaysOnTop", "Обновление Hosts...")
    dlgProg.Add("Text",, "Загрузка hosts файла...")
    dlgProg.Show("w300 h60")
    ok := ListsUpdater.UpdateHosts(Config, ListsDir)
    dlgProg.Destroy()
    MsgBox(ok ? "Hosts файл обновлён" : "Не удалось обновить hosts (нет сети?)", "Zapret Manager", ok ? 64 : 48)
}

CheckUpdates()
{
    global Config, RootDir
    dlgWait := Gui("+AlwaysOnTop", "Проверка обновлений...")
    dlgWait.Add("Text",, "Запрос к GitHub API...")
    dlgWait.Show("w300 h60")
    result := UpdateChecker.CheckNow(Config, RootDir)
    dlgWait.Destroy()
    mgrUpd  := result is Map && result.Has("managerUpdateAvailable") ? result["managerUpdateAvailable"] : false
    coreUpd := result is Map && result.Has("coreUpdateAvailable")    ? result["coreUpdateAvailable"]    : false
    mgrNew  := result is Map && result.Has("managerRemote")          ? result["managerRemote"]          : "—"
    coreNew := result is Map && result.Has("coreRemote")             ? result["coreRemote"]             : "—"
    mgrOld  := GetManagerVersion()
    coreOld := GetCoreVersion()
    msg  := "=== Менеджер ==="
    msg .= "`nУстановлено: " . mgrOld . "   Последнее: " . mgrNew
    msg .= "`nСтатус: " . (mgrUpd ? "Доступно обновление!" : "Актуальная версия")
    msg .= "`n`n=== Ядро zapret ==="
    msg .= "`nУстановлено: " . coreOld . "   Последнее: " . coreNew
    msg .= "`nСтатус: " . (coreUpd ? "Доступно обновление!" : "Актуальная версия")
    MsgBox(msg, "Проверка обновлений", 64)
}

TrafficMonitor()
{
    global UtilsDir
    TrafficMonitorGui.Show(UtilsDir)
}

EditStrategies()
{
    global StrategiesDir, RootDir
    StrategyEditorGui.Show(StrategiesDir, RootDir)
}

OpenSettings()
{
    global UtilsDir
    SettingsGui.Show(UtilsDir)
}

DetectIsp()
{
    global UtilsDir
    dlgWait := Gui("+AlwaysOnTop", "Определение провайдера...")
    dlgWait.Add("Text",, "Запрос к ip-api.com / ipinfo.io...")
    dlgWait.Show("w280 h55")
    info := IspDetector.Detect(UtilsDir)
    dlgWait.Destroy()
    msg  := IspDetector.Format(info)
    isp  := (info is Map) && info.Has("Isp") ? info["Isp"] : ""
    recs := IspDetector.GetRecommendations(UtilsDir, isp)
    if (recs.Length > 0)
    {
        msg .= "`n`nРекомендованные стратегии:"
        for r in recs
            msg .= "`n  * " . r
    }
    MsgBox(msg, "Информация о провайдере", 64)
}

ManageDomains()
{
    global ListsDir
    DomainManagerGui.Show(ListsDir)
}

ExportReport()
{
    global RootDir, UtilsDir
    logsDir := RootDir . "\logs"
    DirCreate(logsDir)
    outPath := logsDir . "\diagnostics_" . FormatTime(, "yyyyMMdd_HHmmss") . ".txt"
    svcState := WinService_GetState("zapret")
    imgPath  := WinService_GetImagePath("zapret")
    mgrVer   := GetManagerVersion()
    coreVer  := GetCoreVersion()
    report   := "ZAPRET MANAGER — ДИАГНОСТИЧЕСКИЙ ОТЧЁТ"
    report   .= "`nСгенерирован: " . FormatTime(, "yyyy-MM-dd HH:mm:ss")
    report   .= "`nManager: " . mgrVer . "  Core: " . coreVer
    report   .= "`n"
    report   .= "`n--- СЛУЖБА ---"
    report   .= "`nzapret: " . MapServiceState(svcState)
    report   .= "`nImagePath: " . (imgPath ? SubStr(imgPath, 1, 100) : "N/A")
    report   .= "`n`n--- ПРОЦЕССЫ ---"
    report   .= "`nwinws.exe: " . (ProcessExist("winws.exe") ? "Запущен" : "Не запущен")
    report   .= "`nTgWsProxy: " . (ProcessExist("TgWsProxy_windows") ? "Запущен" : "Не запущен")
    report   .= "`n`n--- КОМПОНЕНТЫ ---"
    report   .= "`nИгровой фильтр: " . (FileExist(UtilsDir . "\game_filter.enabled") ? "ВКЛ" : "ВЫКЛ")
    report   .= "`nАвтообновления: " . (FileExist(UtilsDir . "\check_updates.enabled") ? "ВКЛ" : "ВЫКЛ")
    FileAppend(report, outPath, "UTF-8")
    Logger_Info("Отчёт сохранён: " . outPath)
    if MsgBox("Отчёт сохранён:`n" . outPath . "`n`nОткрыть?", "Zapret Manager", 4+64) = "Yes"
        Run("notepad.exe `"" . outPath . "`"")
}

ShowAbout()
{
    mgrVer  := GetManagerVersion()
    coreVer := GetCoreVersion()
    msg := "Zapret Manager — AutoHotkey v2 Edition"
    msg .= "`nВерсия менеджера: " . mgrVer
    msg .= "`nВерсия ядра: " . coreVer
    msg .= "`nAutoHotkey: " . A_AhkVersion
    msg .= "`n`nМенеджер: https://github.com/WildeSR98/12345"
    msg .= "`nЯдро: https://github.com/Flowseal/zapret-discord-youtube"
    MsgBox(msg, "О программе", 64)
}

SelectNic()
{
    global UtilsDir
    NicSelectorGui.Show(UtilsDir)
}

; ── Обработчики событий ───────────────────────────────────────────────────────
GuiClose(*)
{
    global MainWindow
    MainWindow.Hide()
}

GuiSize(*)
{
    ; Адаптивный размер можно добавить здесь
}

; ── Трей ──────────────────────────────────────────────────────────────────────
Tray_SetIcon()
{
    global MainWindow

    TrayMenu := A_TrayMenu
    TrayMenu.Delete()

    TrayMenu.Add("Открыть",            (*) => MainWindow.Show())
    TrayMenu.Add("Статус службы",      (*) => ShowServiceStatus())
    TrayMenu.Add()
    TrayMenu.Add("Перезапустить службу", (*) => RestartService())
    TrayMenu.Add("Остановить службу",  (*) => StopService())
    TrayMenu.Add()
    TrayMenu.Add("Выход",              (*) => ExitApp())

    UpdateTrayIcon("Unknown")
}

UpdateTrayIcon(state)
{
    switch state
    {
        case "Running":      TrayTip("Zapret", "Служба запущена",       1)
        case "Stopped":      TrayTip("Zapret", "Служба остановлена",    1)
        case "NotInstalled": TrayTip("Zapret", "Служба не установлена", 1)
    }
}

; ── Запуск приложения ─────────────────────────────────────────────────────────
Init()
CreateMainWindow()
Tray_SetIcon()
