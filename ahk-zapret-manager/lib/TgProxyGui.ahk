; ============================================================================
; TgProxyGui.ahk — Управление TG WS Proxy (TgWsProxy_windows.exe)
; По образцу TgProxyManager.cs
; ============================================================================
#Requires AutoHotkey v2.0

class TgProxyGui
{
    static EXE_NAME  := "TgWsProxy_windows.exe"
    static PROC_NAME := "TgWsProxy_windows"
    static SETTINGS_FILE := "tg-proxy-settings.json"

    static Show(rootDir)
    {
        exePath      := rootDir . "\" . TgProxyGui.EXE_NAME
        settingsPath := rootDir . "\" . TgProxyGui.SETTINGS_FILE
        settings     := TgProxyGui._LoadSettings(settingsPath)
        isRunning    := TgProxyGui._IsRunning()

        dlg := Gui("+AlwaysOnTop", "TG WS Proxy — Zapret Manager")
        dlg.MarginX := 14
        dlg.MarginY := 12

        ; Статус
        statusText  := isRunning ? "✔ Запущен" : "✘ Не запущен"
        statusColor := isRunning ? "00AA00" : "AA0000"
        dlg.Add("Text",, "Статус:")
        lblStatus := dlg.Add("Text", "c" . statusColor . " w340", statusText)

        dlg.Add("Text",, "")

        ; Параметры
        dlg.Add("GroupBox", "x8 y60 w380 h220", "Настройки прокси")

        dlg.Add("Text", "x20 y84 w110", "Порт:")
        edPort := dlg.Add("Edit", "x140 y82 w80", settings["port"])

        dlg.Add("Text", "x20 y112 w110", "Секрет:")
        edSecret := dlg.Add("Edit", "x140 y110 w240", settings["secret"])

        dlg.Add("Text", "x20 y140 w110", "FakeTLS домен:")
        edFakeTLS := dlg.Add("Edit", "x140 y138 w240", settings["fakeTlsDomain"])

        dlg.Add("Text", "x20 y168 w110", "CF Proxy:")
        cbCF := dlg.Add("Checkbox", "x140 y168 w120 Checked" . (settings["cfProxyEnabled"] ? 1 : 0), "Включить")

        dlg.Add("Text", "x20 y200 w110", "Pool size:")
        edPool := dlg.Add("Edit", "x140 y198 w60", settings["poolSize"])

        dlg.Add("Text", "x20 y228 w110", "Buffer (KB):")
        edBuf := dlg.Add("Edit", "x140 y226 w60", settings["bufKb"])

        ; Ссылка tg://
        dlg.Add("Text",, "")
        dlg.Add("Text", "x14 y294 w80", "TG ссылка:")
        edLink := dlg.Add("Edit", "x100 y292 w280 ReadOnly", TgProxyGui._MakeLink(settings))
        btnCopyLink := dlg.Add("Button", "x388 y290 w0 h0", "")
        ; Кнопка явно маленькая — используем вместо неё кнопку ниже

        ; Кнопки
        dlg.Add("Text", "x8 y322 w380 h2 +0x10")
        btnStart   := dlg.Add("Button", "x14 y334 w110 h30", isRunning ? "Перезапустить" : "Запустить")
        btnStop    := dlg.Add("Button", "x136 y334 w80 h30", "Остановить")
        btnGenKey  := dlg.Add("Button", "x230 y334 w100 h30", "Новый секрет")
        btnClose   := dlg.Add("Button", "x344 y334 w50 h30", "✕")

        btnStop.Enabled := isRunning

        dlg.OnEvent("Close", (*) => dlg.Destroy())
        btnClose.OnEvent("Click", (*) => dlg.Destroy())
        btnStart.OnEvent("Click",  (*) => _Start())
        btnStop.OnEvent("Click",   (*) => _Stop())
        btnGenKey.OnEvent("Click", (*) => _GenKey())

        dlg.Show("w404 h374")

        _GetCurrentSettings() {
            s := Map()
            s["host"]          := "127.0.0.1"
            s["port"]          := edPort.Value
            s["secret"]        := edSecret.Value
            s["fakeTlsDomain"] := edFakeTLS.Value
            s["cfProxyEnabled"]:= cbCF.Value ? true : false
            s["cfProxyDomain"] := ""
            s["poolSize"]      := edPool.Value
            s["bufKb"]         := edBuf.Value
            s["logMaxMb"]      := 5
            s["verbose"]       := false
            s["dcIps"]         := ["2:149.154.167.220", "4:149.154.167.220"]
            return s
        }

        _Start(*) {
            if !FileExist(exePath) {
                MsgBox("Файл не найден:`n" . exePath, "Ошибка", 16)
                return
            }
            s := _GetCurrentSettings()
            TgProxyGui._SaveSettings(settingsPath, s)
            TgProxyGui._Stop()
            Sleep(500)
            TgProxyGui._Start(exePath, rootDir, s)
            Sleep(1000)
            edLink.Value := TgProxyGui._MakeLink(s)
            Logger_Info("TG Proxy запущен")
            dlg.Destroy()
            TgProxyGui.Show(rootDir)
        }

        _Stop(*) {
            TgProxyGui._Stop()
            Logger_Info("TG Proxy остановлен")
            dlg.Destroy()
            TgProxyGui.Show(rootDir)
        }

        _GenKey(*) {
            newKey := SubStr(A_TickCount . A_Now . Random(), 1, 32)
            ; Простой псевдо-случайный hex
            hex := ""
            Loop 32 {
                h := Format("{:X}", Random(0, 15))
                hex .= h
            }
            edSecret.Value := StrLower(hex)
            edLink.Value   := TgProxyGui._MakeLink(_GetCurrentSettings())
        }
    }

    ; ── Private ───────────────────────────────────────────────────────────────

    static _IsRunning()
    {
        return ProcessExist(TgProxyGui.PROC_NAME) ? true : false
    }

    static _Stop()
    {
        pid := ProcessExist(TgProxyGui.PROC_NAME)
        if pid
            ProcessClose(pid)
    }

    static _Start(exePath, rootDir, settings)
    {
        logFile := rootDir . "\logs\tg-proxy.log"
        DirCreate(rootDir . "\logs")

        args := "--host " . settings["host"]
              . " --port " . settings["port"]
              . " --secret " . settings["secret"]
              . " --buf-kb " . settings["bufKb"]
              . " --pool-size " . settings["poolSize"]
              . " --log-file `"" . logFile . "`""
              . " --log-max-mb " . settings["logMaxMb"]

        if (settings["fakeTlsDomain"] != "")
            args .= " --fake-tls-domain " . settings["fakeTlsDomain"]

        if !settings["cfProxyEnabled"]
            args .= " --no-cfproxy"

        for dcip in settings["dcIps"]
            args .= " --dc-ip " . dcip

        Run("`"" . exePath . "`" " . args, rootDir)
    }

    static _MakeLink(settings)
    {
        port   := settings.Has("port")          ? settings["port"]   : "1443"
        secret := settings.Has("secret")        ? settings["secret"] : ""
        domain := settings.Has("fakeTlsDomain") ? settings["fakeTlsDomain"] : ""

        if (domain != "")
        {
            ; Hex-encode domain
            hex := ""
            Loop StrLen(domain)
                hex .= Format("{:02X}", Ord(SubStr(domain, A_Index, 1)))
            return "tg://proxy?server=127.0.0.1&port=" . port . "&secret=ee" . secret . StrLower(hex)
        }
        return "tg://proxy?server=127.0.0.1&port=" . port . "&secret=dd" . secret
    }

    static _LoadSettings(path)
    {
        s := Map()
        s["host"]          := "127.0.0.1"
        s["port"]          := "1443"
        s["secret"]        := ""
        s["fakeTlsDomain"] := ""
        s["cfProxyEnabled"]:= true
        s["cfProxyDomain"] := ""
        s["poolSize"]      := "4"
        s["bufKb"]         := "256"
        s["logMaxMb"]      := 5
        s["verbose"]       := false
        s["dcIps"]         := ["2:149.154.167.220", "4:149.154.167.220"]

        if FileExist(path)
        {
            Try
            {
                loaded := JsonParser.ParseFile(path)
                if (loaded is Map)
                {
                    for key, val in loaded
                        s[key] := val
                }
            }
            Catch as _e
            {
            }
        }

        ; Генерировать секрет если пустой
        if (s["secret"] = "")
        {
            hex := ""
            Loop 32 {
                h := Format("{:x}", Random(0, 15))
                hex .= h
            }
            s["secret"] := hex
            TgProxyGui._SaveSettings(path, s)
        }
        return s
    }

    static _SaveSettings(path, settings)
    {
        Try
        {
            FileDelete(path)
            FileAppend(JsonParser.Stringify(settings), path, "UTF-8")
        }
        Catch as _e
        {
        }
    }
}
