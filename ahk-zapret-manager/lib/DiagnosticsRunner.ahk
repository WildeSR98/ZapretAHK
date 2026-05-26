; ============================================================================
; DiagnosticsRunner.ahk — Проверка доступности сервисов + конфликты
; Цели из config.json:diagnostics.check_targets
; ============================================================================
#Requires AutoHotkey v2.0

class DiagnosticsRunner
{
    ; Запустить полную диагностику, показать результаты в GUI
    static Run(cfg, rootDir)
    {
        targets     := DiagnosticsRunner._LoadTargets(cfg)
        conflicts   := DiagnosticsRunner._CheckConflicts(cfg)
        svcStatus   := WinService_GetState("zapret")

        ; Создать GUI
        dlg := Gui("+AlwaysOnTop", "Диагностика — Zapret Manager")
        dlg.MarginX := 12
        dlg.MarginY := 10

        dlg.Add("Text",, "Статус службы zapret:")
        statusColor := (svcStatus = "Running") ? "00AA00" : "AA0000"
        statusText  := (svcStatus = "Running") ? "✔ Запущена" : "✘ " . MapServiceState(svcStatus)
        dlg.Add("Text", "c" . statusColor, statusText)
        dlg.Add("Text",, "")

        ; Конфликты
        if (conflicts.Length > 0)
        {
            dlg.Add("Text", "cAA0000", "⚠ Обнаружены конфликтующие службы:")
            for svc in conflicts
                dlg.Add("Text", "x20 cAA4400", "  • " . svc)
            dlg.Add("Text",, "")
        }
        else
            dlg.Add("Text", "c00AA00", "✔ Конфликтующих служб не найдено")

        dlg.Add("Text",, "")
        dlg.Add("Text",, "Проверка доступности (подождите...):")
        dlg.Show("w500 h400")

        ; Проверка целей
        resultLines := []
        for target in targets
        {
            name := target.Has("name") ? target["name"] : "?"
            type := target.Has("type") ? target["type"] : "url"
            url  := target.Has("url")  ? target["url"]  : ""
            host := target.Has("host") ? target["host"] : ""

            if (type = "ping" || url = "")
            {
                pingRes := DiagnosticsRunner._DoPing(host)
                resultLines.Push({name: name, ok: (pingRes != "Timeout"), label: "Ping: " . pingRes})
            }
            else
            {
                httpOk := DiagnosticsRunner._DoHttp(url)
                resultLines.Push({name: name, ok: httpOk, label: (httpOk ? "HTTP: OK" : "HTTP: ОШИБКА")})
            }
        }

        ; Перерисовать с результатами
        dlg.Destroy()
        dlg2 := Gui("+AlwaysOnTop", "Диагностика — Zapret Manager")
        dlg2.MarginX := 12
        dlg2.MarginY := 10

        dlg2.Add("Text",, "Служба zapret: " . MapServiceState(svcStatus))
        dlg2.Add("Text",, "")

        if (conflicts.Length > 0)
        {
            dlg2.Add("Text", "cAA0000", "⚠ Конфликты: " . DiagnosticsRunner._Join(conflicts, ", "))
            dlg2.Add("Text",, "")
        }

        dlg2.Add("Text",, "Результаты проверки:")

        for res in resultLines
        {
            c := res.ok ? "c00AA00" : "cAA0000"
            icon := res.ok ? "✔" : "✘"
            dlg2.Add("Text", c, "  " . icon . "  " . res.name . " — " . res.label)
        }

        dlg2.Add("Text",, "")
        btnClose := dlg2.Add("Button", "Default w100", "Закрыть")
        btnClose.OnEvent("Click", (*) => dlg2.Destroy())

        dlg2.Show("w520 AutoSize")
        Logger_Info("Диагностика завершена: " . resultLines.Length . " целей проверено")
    }

    ; ── Private ───────────────────────────────────────────────────────────────

    static _LoadTargets(cfg)
    {
        diag := AppConfig.Get(cfg, "diagnostics")
        if (diag is Map) && diag.Has("check_targets") && (diag["check_targets"] is Array)
            return diag["check_targets"]
        return []
    }

    static _CheckConflicts(cfg)
    {
        found := []
        diag  := AppConfig.Get(cfg, "diagnostics")
        conflictList := []

        if (diag is Map) && diag.Has("conflicting_services") && (diag["conflicting_services"] is Array)
            conflictList := diag["conflicting_services"]

        for svcName in conflictList
        {
            state := WinService_GetState(svcName)
            if (state = "Running")
                found.Push(svcName)
        }
        return found
    }

    static _DoHttp(url)
    {
        Try
        {
            resp := HttpGet(url)
            return resp.Status >= 100 && resp.Status < 600
        }
        Catch
            return false
    }

    static _DoPing(host)
    {
        Try
        {
            tmp  := A_Temp . "\ping_" . A_TickCount . ".txt"
            code := RunWait("ping.exe -n 2 -w 1500 " . host . " > `"" . tmp . "`"",, "Hide")
            if FileExist(tmp)
            {
                output := FileRead(tmp)
                FileDelete(tmp)
                if InStr(output, "TTL=")
                    return "OK"
            }
            return "Timeout"
        }
        Catch
            return "Timeout"
    }

    static _Join(arr, sep)
    {
        r := ""
        for i, v in arr
            r .= (i > 1 ? sep : "") . v
        return r
    }
}
