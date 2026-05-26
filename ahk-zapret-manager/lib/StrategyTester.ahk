; ============================================================================
; StrategyTester.ahk — Тест стратегий (2 режима: Quick / Full)
; Quick — тест текущей стратегии без переключения
; Full  — перебор всех стратегий, выбор лучшей
; ============================================================================
#Requires AutoHotkey v2.0

class StrategyTester
{
    static WORKER_SCRIPT := A_ScriptDir . "\lib\StrategyTesterWorker.ahk"
    static RESULTS_DIR   := "test results"

    ; Показать окно выбора режима теста
    static ShowModeDialog(rootDir, cfg)
    {
        strategiesDir := rootDir . "\strategies"
        batFiles := []
        Loop Files, strategiesDir . "\general*.bat"
            batFiles.Push(A_LoopFilePath)
        Loop Files, strategiesDir . "\custom*.bat"
            batFiles.Push(A_LoopFilePath)

        if (batFiles.Length = 0)
        {
            MsgBox("Нет .bat файлов в strategies/", "Тест стратегий", 48)
            return
        }

        utilsDir    := rootDir . "\utils"
        targetsFile := utilsDir . "\targets.txt"
        if !FileExist(targetsFile)
        {
            MsgBox("Файл целей не найден:`n" . targetsFile, "Тест стратегий", 48)
            return
        }

        ; ── Выбор режима ───────────────────────────────────────────────────────
        dlg := Gui("+AlwaysOnTop", "Тест стратегий — Zapret Manager")
        dlg.MarginX := 18
        dlg.MarginY := 14

        dlg.Add("Text", "w460 cGray", "Выберите режим тестирования:")
        dlg.Add("Text",, "")

        ; Quick mode
        dlg.Add("GroupBox", "x10 y46 w460 h100", "⚡ Быстрый тест (Quick)")
        dlg.Add("Text", "x20 y68 w440",  "Тестирует только текущую активную стратегию.")
        dlg.Add("Text", "x20 y90 w440",  "Показывает, насколько хорошо работает текущая настройка.")
        dlg.Add("Text", "x20 y112 w440 cGray", "Время: ~30 секунд. Соединение не прерывается.")
        btnQuick := dlg.Add("Button", "x330 y124 w130 h28", "⚡ Быстрый тест")

        ; Full mode
        dlg.Add("GroupBox", "x10 y158 w460 h100", "🔍 Полный тест (Full)")
        dlg.Add("Text", "x20 y180 w440",  "Перебирает все " . batFiles.Length . " стратегий и выбирает лучшую.")
        dlg.Add("Text", "x20 y202 w440",  "Автоматически активирует наилучшую стратегию.")
        dlg.Add("Text", "x20 y224 w440 cRed", "⚠ Интернет будет прерываться. Время: ~" . (batFiles.Length * 15) . " сек.")
        btnFull := dlg.Add("Button", "x330 y240 w130 h28", "🔍 Полный тест")

        ; История
        dlg.Add("Text",, "")
        btnHistory := dlg.Add("Button", "x10 y276 w140 h28", "📋 История тестов")
        btnClose   := dlg.Add("Button", "x350 y276 w120 h28", "Закрыть")

        dlg.OnEvent("Close", (*) => dlg.Destroy())
        btnClose.OnEvent("Click", (*) => dlg.Destroy())
        btnQuick.OnEvent("Click", (*) => (dlg.Destroy(), StrategyTester.RunQuick(rootDir, targetsFile)))
        btnFull.OnEvent("Click",  (*) => (dlg.Destroy(), StrategyTester.RunFull(rootDir, targetsFile, batFiles)))
        btnHistory.OnEvent("Click", (*) => StrategyTester.ShowHistory(utilsDir))

        dlg.Show("w480 AutoSize")
    }

    ; ── Quick тест — только текущая активная стратегия ────────────────────────
    static RunQuick(rootDir, targetsFile)
    {
        utilsDir  := rootDir . "\utils"
        resultDir := utilsDir . "\" . StrategyTester.RESULTS_DIR
        DirCreate(resultDir)

        dlg := Gui("+AlwaysOnTop", "Быстрый тест — Zapret Manager")
        dlg.MarginX := 14
        dlg.MarginY := 12
        dlg.Add("Text",, "⚡ Тестируется текущая стратегия...")
        lblResult := dlg.Add("Text", "w420 h80 +Multi", "Ожидание...")
        dlg.Add("Text",, "")
        btnClose := dlg.Add("Button", "Default w100 h30", "Закрыть")
        btnClose.OnEvent("Click", (*) => dlg.Destroy())
        dlg.OnEvent("Close", (*) => dlg.Destroy())
        dlg.Show("w450 AutoSize")

        SetTimer(_DoQuick, -100)

        _DoQuick() {
            lines := []
            ; Читаем цели
            if !FileExist(targetsFile)
            {
                lblResult.Text := "Файл целей не найден"
                return
            }
            Loop Read, targetsFile
            {
                line := Trim(A_LoopReadLine)
                if (line = "" || SubStr(line, 1, 1) = "#")
                    continue
                lines.Push(line)
            }
            if (lines.Length = 0)
            {
                lblResult.Text := "Нет целей в targets.txt"
                return
            }

            okCount   := 0
            failCount := 0
            report    := ""
            for target in lines
            {
                ; Разбор строки: name = "URL" или просто URL
                url  := target
                name := target
                q := Chr(34)
                if RegExMatch(target, "^\s*(\w[\w\s]*)\s*=\s*" . q . "(.+)" . q . "\s*$", &m)
                {
                    name := m[1]
                    url  := m[2]
                }

                ok := StrategyTester._TestUrl(url)
                icon := ok ? "✔" : "✘"
                report .= icon . " " . name . "`n"
                (ok ? okCount++ : failCount++)
            }

            total  := okCount + failCount
            pct    := (total > 0) ? Round(okCount * 100 / total) : 0
            summary := "OK: " . okCount . " / " . total . " (" . pct . "%)`n`n" . report

            ; Сохранить результат
            ts := FormatTime(, "yyyy-MM-dd_HH-mm-ss")
            outFile := resultDir . "\quick_" . ts . ".txt"
            header  := "Quick Test — " . FormatTime(, "yyyy-MM-dd HH:mm:ss") . "`n"
                     . "═══════════════════════════════`n`n"
            FileAppend(header . summary, outFile, "UTF-8")

            lblResult.Text := summary
        }
    }

    ; ── Full тест — все стратегии через воркер-процесс ────────────────────────
    static RunFull(rootDir, targetsFile, batFiles)
    {
        utilsDir  := rootDir . "\utils"
        logFile   := utilsDir . "\" . StrategyTester.RESULTS_DIR . "\worker_progress.txt"
        DirCreate(utilsDir . "\" . StrategyTester.RESULTS_DIR)

        dlg := Gui("+AlwaysOnTop", "Полный тест — Zapret Manager")
        dlg.MarginX := 14
        dlg.MarginY := 12

        dlg.Add("Text",, "Тест всех стратегий запущен в фоне.")
        dlg.Add("Text",, "Стратегий: " . batFiles.Length)
        dlg.Add("Text",, "")
        dlg.Add("Text", "cRed",, "⚠ Интернет-соединение будет прерываться.")
        dlg.Add("Text", "cRed",, "⚠ Тест займёт ~" . (batFiles.Length * 15) . " секунд.")
        dlg.Add("Text",, "")
        lblStatus := dlg.Add("Text", "w420", "Статус: запуск воркера...")
        pgBar := dlg.Add("Progress", "w420 h20 Range0-" . batFiles.Length, 0)
        dlg.Add("Text",, "")
        btnCancel := dlg.Add("Button", "x14 w100 h30 Default", "Отмена")
        dlg.Show("w450 AutoSize")

        ; Запуск воркера с захватом PID
        workerArgs := "`"" . rootDir . "`" `"" . targetsFile . "`""
        workerPid  := 0
        Try
        {
            Run(A_AhkPath . " `"" . StrategyTester.WORKER_SCRIPT . "`" " . workerArgs,,, &workerPid)
        }
        Catch as e
        {
            MsgBox("Ошибка запуска воркера: " . e.Message, "Ошибка", 16)
            dlg.Destroy()
            return
        }

        cancelled := false
        btnCancel.OnEvent("Click", (*) => _Cancel())
        dlg.OnEvent("Close", (*) => _Cancel())

        timer := ObjBindMethod(StrategyTester, "_PollProgress", dlg, lblStatus, pgBar, logFile, batFiles.Length, &cancelled, &workerPid)
        SetTimer(timer, 2000)

        _Cancel() {
            cancelled := true
            if (workerPid != 0)
            {
                Try ProcessClose(workerPid)
            }
            SetTimer(timer, 0)
            Try dlg.Destroy()
            Logger_Info("Тест стратегий отменён")
        }
    }

    ; Показать историю тестов
    static ShowHistory(utilsDir)
    {
        TrafficMonitorGui.Show(utilsDir)
    }

    ; ── Вспомогательный HTTP-тест URL ─────────────────────────────────────────
    static _TestUrl(url)
    {
        Try
        {
            whr := ComObject("WinHttp.WinHttpRequest.5.1")
            whr.Open("HEAD", url, false)
            whr.SetTimeouts(5000, 5000, 5000, 8000)
            whr.Send()
            code := whr.Status
            return (code >= 100 && code < 600)
        }
        Catch as _e
        {
            return false
        }
    }

    ; Callback опроса прогресса (каждые 2 сек)
    static _PollProgress(dlg, lblStatus, pgBar, logFile, total, &cancelled, &pid)
    {
        if cancelled
        {
            SetTimer(, 0)
            return
        }

        ; Воркер завершился?
        if !ProcessExist(pid)
        {
            SetTimer(, 0)
            pgBar.Value := total
            lblStatus.Text := "✔ Тест завершён! Результаты сохранены."
            Logger_Info("Тест стратегий завершён")
            return
        }

        ; Читаем прогресс из лог-файла
        Try
        {
            if FileExist(logFile)
            {
                content := FileRead(logFile, "UTF-8")
                lines   := StrSplit(content, "`n")
                done    := 0
                lastLine := ""
                for line in lines
                {
                    clean := Trim(line, "`r")
                    if (clean != "")
                    {
                        lastLine := clean
                        done++
                    }
                }
                pgBar.Value := Min(done, total)
                lblStatus.Text := "Тестируется: " . lastLine . " (" . done . "/" . total . ")"
            }
        }
        Catch as _e
        {
        }
    }
}
