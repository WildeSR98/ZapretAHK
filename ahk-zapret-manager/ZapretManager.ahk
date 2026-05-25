; ============================================================================
; Zapret Manager — AutoHotkey v2 Edition (Полная версия)
; GUI-приложение для управления DPI bypass службой
; ============================================================================
#Requires AutoHotkey v2.0
#SingleInstance Force
#WinActivateForce
Persistent

; ── Подключение библиотек ─────────────────────────────────────────────────────
#Include lib
#include WinService.ahk
#include Logger.ahk
#include AppConfig.ahk
#include AdminHelper.ahk
#include HttpService.ahk

; ── Глобальные переменные ─────────────────────────────────────────────────────
global RootDir := ""
global BinDir := ""
global ListsDir := ""
global UtilsDir := ""
global StrategiesDir := ""
global Config := {}
global ServiceName := "zapret"
global MainWindow := ""

; ── Инициализация ─────────────────────────────────────────────────────────────
Init()
{
    ; Определяем корневую директорию (где лежит скрипт)
    RootDir := A_ScriptDir
    BinDir := RootDir . "\bin"
    ListsDir := RootDir . "\lists"
    UtilsDir := RootDir . "\utils"
    StrategiesDir := RootDir . "\strategies"
    
    ; Создаём необходимые папки
    DirCreate(UtilsDir)
    DirCreate(RootDir . "\logs")
    DirCreate(RootDir . "\backups")
    
    ; Загружаем конфигурацию
    Config := LoadConfig(RootDir . "\config.json")
    
    ; Проверяем права администратора
    RequireAdmin()
    
    ; Инициализируем логирование
    Logger_Init()
    Logger_Info("=== Запуск Zapret Manager AHK ===")
}

; ── Основное окно ─────────────────────────────────────────────────────────────
CreateMainWindow()
{
    global MainWindow
    
    MainWindow := Gui("+Resize MinSize800x600", "Zapret Manager v" . Config["version"])
    MainWindow.OnEvent("Close", GuiClose)
    MainWindow.OnEvent("Size", GuiSize)
    
    ; ── Меню ──────────────────────────────────────────────────────────────────
    MenuBar := Menu()
    
    FileMenu := Menu()
    FileMenu.Add("Проверить обновления`tCtrl+U", (*) => CheckUpdates())
    FileMenu.Add("Экспорт отчёта`tCtrl+E", (*) => ExportReport())
    FileMenu.Add()
    FileMenu.Add("Выход`tAlt+F4", (*) => ExitApp())
    MenuBar.Add("Файл", FileMenu)
    
    ServiceMenu := Menu()
    ServiceMenu.Add("Установить службу`tCtrl+I", (*) => InstallService())
    ServiceMenu.Add("Удалить службу`tCtrl+D", (*) => RemoveService())
    ServiceMenu.Add()
    ServiceMenu.Add("Запустить`tF5", (*) => StartService())
    ServiceMenu.Add("Остановить`tF6", (*) => StopService())
    ServiceMenu.Add("Перезапустить`tF7", (*) => RestartService())
    ServiceMenu.Add()
    ServiceMenu.Add("Статус`tF9", (*) => ShowServiceStatus())
    MenuBar.Add("Служба", ServiceMenu)
    
    ToolsMenu := Menu()
    ToolsMenu.Add("Диагностика`tCtrl+D", (*) => RunDiagnostics())
    ToolsMenu.Add("Тест стратегий`tCtrl+T", (*) => TestStrategies())
    ToolsMenu.Add("Мониторинг трафика`tCtrl+M", (*) => TrafficMonitor())
    ToolsMenu.Add()
    ToolsMenu.Add("TG Proxy`tCtrl+G", (*) => ManageTgProxy())
    ToolsMenu.Add("Бэкап`tCtrl+B", (*) => DoBackup())
    MenuBar.Add("Инструменты", ToolsMenu)
    
    SettingsMenu := Menu()
    SettingsMenu.Add("Настройки`tCtrl+S", (*) => OpenSettings())
    SettingsMenu.Add("Редактор стратегий`tCtrl+R", (*) => EditStrategies())
    SettingsMenu.Add("Управление доменами", (*) => ManageDomains())
    SettingsMenu.Add("Сетевой адаптер", (*) => SelectNic())
    MenuBar.Add("Настройки", SettingsMenu)
    
    HelpMenu := Menu()
    HelpMenu.Add("О программе", (*) => ShowAbout())
    MenuBar.Add("Справка", HelpMenu)
    
    MainWindow.Menu := MenuBar
    
    ; ── Горячие клавиши ───────────────────────────────────────────────────────
    HotIf(MainWindow)
    ^u::CheckUpdates()
    ^e::ExportReport()
    ^i::InstallService()
    ^d::RemoveService()
    ^t::TestStrategies()
    ^m::TrafficMonitor()
    ^b::DoBackup()
    ^s::OpenSettings()
    F5::StartService()
    F6::StopService()
    F7::RestartService()
    F9::ShowServiceStatus()
    HotIf()
    
    ; ── Основная панель ───────────────────────────────────────────────────────
    Panel := MainWindow.Add("Panel", "x10 y10 w770 h540")
    
    ; Заголовок
    Panel.Add("Text", "x20 y15 w300 h30 +0x200", "Zapret Manager")
    
    ; Статус службы
    Panel.Add("Text", "x20 y55 w100 h25", "Служба:")
    global ServiceStatusLabel := Panel.Add("Text", "x130 y55 w150 h25 +0x200 cGreen", "Загрузка...")
    
    ; Стратегия
    Panel.Add("Text", "x20 y85 w100 h25", "Стратегия:")
    global StrategyLabel := Panel.Add("Text", "x130 y85 w300 h25 +0x200", "...")
    
    ; Версии
    Panel.Add("Text", "x20 y115 w100 h25", "Manager:")
    Panel.Add("Text", "x130 y115 w150 h25", GetManagerVersion())
    
    Panel.Add("Text", "x300 y115 w80 h25", "Core:")
    Panel.Add("Text", "x390 y115 w150 h25", GetCoreVersion())
    
    ; ── Кнопки управления службой ─────────────────────────────────────────────
    BtnGroup := Panel.Add("GroupBox", "x20 y160 w350 h130", "Управление службой")
    
    BtnInstall := BtnGroup.Add("Button", "x30 y25 w130 h35", "Установить")
    BtnInstall.OnEvent("Click", (*) => InstallService())
    
    BtnRemove := BtnGroup.Add("Button", "x180 y25 w130 h35", "Удалить")
    BtnRemove.OnEvent("Click", (*) => RemoveService())
    
    BtnStart := BtnGroup.Add("Button", "x30 y70 w130 h35", "Запустить")
    BtnStart.OnEvent("Click", (*) => StartService())
    
    BtnStop := BtnGroup.Add("Button", "x180 y70 w130 h35", "Остановить")
    BtnStop.OnEvent("Click", (*) => StopService())
    
    ; ── Быстрые действия ──────────────────────────────────────────────────────
    ActionsGroup := Panel.Add("GroupBox", "x400 y160 w370 h220", "Быстрые действия")
    
    actions := [
        ["Диагностика", (*) => RunDiagnostics()],
        ["Тест стратегий", (*) => TestStrategies()],
        ["TG Proxy", (*) => ManageTgProxy()],
        ["Бэкап / Восстановление", (*) => DoBackup()],
        ["Обновить списки IPSet", (*) => UpdateLists()],
        ["Обновить Hosts", (*) => UpdateHosts()],
        ["Проверить обновления", (*) => CheckUpdates()],
        ["Мониторинг трафика", (*) => TrafficMonitor()],
        ["Редактор стратегий", (*) => EditStrategies()],
        ["Настройки", (*) => OpenSettings()],
        ["Определение провайдера", (*) => DetectIsp()],
        ["Управление доменами", (*) => ManageDomains()]
    ]
    
    Loop 12
    {
        i := A_Index
        if (i <= actions.Length)
        {
            btn := ActionsGroup.Add("Button", "x30 y" . (20 + (i-1)*32) . " w150 h28", actions[i][1])
            btn.OnEvent("Click", actions[i][2])
        }
    }
    
    ; ── Нижняя панель статусов ────────────────────────────────────────────────
    BottomPanel := Panel.Add("Panel", "x20 y400 w730 h90")
    
    ; Watchdog статус
    wdEnabled := FileExist(UtilsDir . "\watchdog.enabled")
    BottomPanel.Add("Text", "x20 y15 w100 h25", "Watchdog:")
    BottomPanel.Add("Text", "x130 y15 w50 h25 c" . (wdEnabled ? "Green" : "Gray"), wdEnabled ? "ВКЛ" : "ВЫКЛ")
    
    ; Игровой фильтр
    gfEnabled := FileExist(UtilsDir . "\game_filter.enabled")
    BottomPanel.Add("Text", "x200 y15 w100 h25", "Игровой фильтр:")
    BottomPanel.Add("Text", "x310 y15 w50 h25 c" . (gfEnabled ? "Green" : "Gray"), gfEnabled ? "ВКЛ" : "ВЫКЛ")
    
    ; Обновления
    updEnabled := FileExist(UtilsDir . "\check_updates.enabled")
    BottomPanel.Add("Text", "x20 y50 w100 h25", "Обновления:")
    BottomPanel.Add("Text", "x130 y50 w50 h25 c" . (updEnabled ? "Green" : "Gray"), updEnabled ? "ВКЛ" : "ВЫКЛ")
    
    ; Кнопка выхода
    BtnExit := BottomPanel.Add("Button", "x600 y50 w100 h35", "Выход")
    BtnExit.OnEvent("Click", (*) => ExitApp())
    
    ; Показываем окно
    MainWindow.Show("w820 h620")
    
    ; Обновляем статус каждые 5 секунд
    SetTimer(UpdateStatus, 5000)
    UpdateStatus()
}

; ── Обновление статуса ────────────────────────────────────────────────────────
UpdateStatus()
{
    global ServiceStatusLabel, StrategyLabel
    
    state := WinService_GetState(ServiceName)
    strategy := GetCurrentStrategy()
    
    ; Обновляем метку состояния службы
    stateText := MapServiceState(state)
    ServiceStatusLabel.Text := stateText
    ServiceStatusLabel.Opt("c" . GetStateColor(state))
    
    ; Обновляем стратегию
    StrategyLabel.Text := strategy
    
    ; Обновляем трей
    UpdateTrayIcon(state)
}

MapServiceState(state)
{
    switch state
    {
        case "Running": return "Запущена"
        case "Stopped": return "Остановлена"
        case "NotInstalled": return "Не установлена"
        case "Starting": return "Запуск..."
        case "Stopping": return "Остановка..."
        default: return state
    }
}

GetStateColor(state)
{
    switch state
    {
        case "Running": return "00AA00"  ; Green
        case "Stopped": return "AAAA00"   ; Yellow
        case "NotInstalled": return "AA0000"  ; Red
        default: return "888888"  ; Gray
    }
}

; ── Управление службой ────────────────────────────────────────────────────────
InstallService()
{
    strategyFile := SelectStrategyFile()
    if !strategyFile
    {
        MsgBox("Стратегии не найдены в папке strategies/", "Ошибка", 48)
        return
    }
    
    args := ParseBatArgs(strategyFile)
    winws := BinDir . "\winws.exe"
    binPath := """" . winws . """ " . args
    
    Logger_Info("Установка службы: " . binPath)
    
    if WinService_Install(ServiceName, "Zapret DPI Bypass", "Обход DPI блокировок", binPath)
    {
        MsgBox("Служба успешно установлена и запущена!", "Zapret Manager", 64)
        UpdateStatus()
    }
    else
    {
        MsgBox("Не удалось установить службу. Проверьте логи.", "Ошибка", 48)
    }
}

RemoveService()
{
    if WinService_Remove(ServiceName)
    {
        MsgBox("Служба удалена", "Zapret Manager", 64)
        UpdateStatus()
    }
    else
    {
        MsgBox("Не удалось удалить службу", "Ошибка", 48)
    }
}

StartService()
{
    if WinService_Start(ServiceName)
    {
        Logger_Info("Служба запущена")
        UpdateStatus()
    }
    else
    {
        MsgBox("Не удалось запустить службу", "Ошибка", 48)
    }
}

StopService()
{
    if WinService_Stop(ServiceName)
    {
        Logger_Info("Служба остановлена")
        UpdateStatus()
    }
    else
    {
        MsgBox("Не удалось остановить службу", "Ошибка", 48)
    }
}

RestartService()
{
    StopService()
    Sleep(1000)
    StartService()
}

ShowServiceStatus()
{
    state := WinService_GetState(ServiceName)
    imagePath := WinService_GetImagePath(ServiceName)
    strategy := GetCurrentStrategy()
    
    msg := "Состояние: " . MapServiceState(state) . "`n"
         . "Стратегия: " . strategy . "`n"
         . "ImagePath: " . (imagePath ? SubStr(imagePath, 1, 80) . "..." : "N/A")
    
    MsgBox(msg, "Статус службы", 64)
}

; ── Вспомогательные функции ───────────────────────────────────────────────────
SelectStrategyFile()
{
    if !DirExist(StrategiesDir)
        return ""
    
    files := []
    Loop Files, StrategiesDir . "\general*.bat"
        files.Push(A_LoopFilePath)
    
    if files.Length == 0
        return ""
    
    return files[1]  ; Возвращаем первую стратегию
}

ParseBatArgs(batFile)
{
    content := FileRead(batFile)
    for line in StrSplit(content, "`n")
    {
        if InStr(line, "winws.exe") || InStr(line, "%~dp0bin\winws.exe")
        {
            pos := InStr(line, "winws.exe")
            if pos
            {
                args := SubStr(line, pos + 9)
                args := Trim(args)
                args := StrReplace(args, "%~dp0bin\", BinDir . "\")
                args := StrReplace(args, "%~dp0lists\", ListsDir . "\")
                return args
            }
        }
    }
    return ""
}

GetCurrentStrategy()
{
    Try
    {
        key := RegOpenKey("HKLM\SYSTEM\CurrentControlSet\Services\" . ServiceName)
        if key
        {
            val := RegReadKey(key, "zapret-discord-youtube")
            RegCloseKey(key)
            return val ? val : "не установлена"
        }
    }
    return "не установлена"
}

GetManagerVersion()
{
    versionFile := UtilsDir . "\manager_version.txt"
    if FileExist(versionFile)
        return Trim(FileRead(versionFile))
    return Config["version"] ?? "?.?.?"
}

GetCoreVersion()
{
    versionFile := BinDir . "\version.txt"
    if FileExist(versionFile)
        return Trim(FileRead(versionFile))
    return "не установлен"
}

; ── Заглушки для функций меню ─────────────────────────────────────────────────
RunDiagnostics() => MsgBox("Диагностика будет реализована", "Info", 64)
TestStrategies() => MsgBox("Тест стратегий будет реализован", "Info", 64)
ManageTgProxy() => MsgBox("TG Proxy меню будет реализовано", "Info", 64)
DoBackup() => MsgBox("Бэкап будет реализован", "Info", 64)
UpdateLists() => MsgBox("Обновление списков будет реализовано", "Info", 64)
UpdateHosts() => MsgBox("Обновление Hosts будет реализовано", "Info", 64)
CheckUpdates() => MsgBox("Проверка обновлений будет реализована", "Info", 64)
TrafficMonitor() => MsgBox("Мониторинг трафика будет реализован", "Info", 64)
EditStrategies() => MsgBox("Редактор стратегий будет реализован", "Info", 64)
OpenSettings() => MsgBox("Настройки будут реализованы", "Info", 64)
DetectIsp() => MsgBox("Определение провайдера будет реализовано", "Info", 64)
ManageDomains() => MsgBox("Управление доменами будет реализовано", "Info", 64)
ExportReport() => MsgBox("Экспорт отчёта будет реализован", "Info", 64)
ShowAbout() => MsgBox("Zapret Manager AHK v3.0.0`nПеренос проекта на AutoHotkey v2", "О программе", 64)

; ── Обработчики событий ───────────────────────────────────────────────────────
GuiClose(*)
{
    MainWindow.Hide()
}

GuiSize(*)
{
    ; Адаптивный размер можно добавить здесь
}

; ── Трей ──────────────────────────────────────────────────────────────────────
Tray_SetIcon()
{
    TrayMenu := A_TrayMenu
    TrayMenu.Delete()
    
    TrayMenu.Add("Открыть", (*) => MainWindow.Show())
    TrayMenu.Add("Статус службы", (*) => ShowServiceStatus())
    TrayMenu.Add()
    TrayMenu.Add("Перезапустить службу", (*) => RestartService())
    TrayMenu.Add("Остановить службу", (*) => StopService())
    TrayMenu.Add()
    TrayMenu.Add("Выход", (*) => ExitApp())
    
    UpdateTrayIcon("Unknown")
}

UpdateTrayIcon(state)
{
    switch state
    {
        case "Running": TrayTip("Zapret", "Служба запущена", 1)
        case "Stopped": TrayTip("Zapret", "Служба остановлена", 1)
        case "NotInstalled": TrayTip("Zapret", "Служба не установлена", 1)
    }
}

; ── Запуск приложения ─────────────────────────────────────────────────────────
Init()
CreateMainWindow()
Tray_SetIcon()

return
