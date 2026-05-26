; ============================================================================
; StrategyTester.ahk — Тест стратегий в фоновом AHK-процессе
; Запускает StrategyTesterWorker.ahk отдельным процессом
; ============================================================================
#Requires AutoHotkey v2.0

class StrategyTester
{
    static WORKER_SCRIPT := A_ScriptDir . "\lib\StrategyTesterWorker.ahk"
    static RESULTS_DIR   := "test results"

    ; Запустить тест (GUI с прогрессом, реальная работа — в воркере)
    static Run(rootDir, cfg)
    {
        strategiesDir := rootDir . "\strategies"
        utilsDir      := rootDir . "\utils"
        targetsFile   := utilsDir . "\targets.txt"

        ; Найти .bat файлы стратегий
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

        if !FileExist(targetsFile)
        {
            MsgBox("Файл целей не найден:`n" . targetsFile, "Тест стратегий", 48)
            return
        }

        ; GUI прогресса
        dlg := Gui("+AlwaysOnTop", "Тест стратегий — Zapret Manager")
        dlg.MarginX := 14
        dlg.MarginY := 12

        dlg.Add("Text",, "Тест стратегий запущен в фоне.")
        dlg.Add("Text",, "Стратегий: " . batFiles.Length)
        dlg.Add("Text",, "")
        dlg.Add("Text",, "⚠ Во время теста интернет-соединение будет прерываться.")
        dlg.Add("Text",, "⚠ Тест займёт ~" . (batFiles.Length * 15) . " секунд.")
        dlg.Add("Text",, "")
        lblStatus := dlg.Add("Text", "w420", "Статус: ожидание запуска...")
        pgBar := dlg.Add("Progress", "w420 h20 Range0-" . batFiles.Length, 0)
        dlg.Add("Text",, "")
        btnCancel := dlg.Add("Button", "x14 y Default w100 h30", "Отмена")
        dlg.Show("w450 AutoSize")

        ; Лог-файл для связи с воркером
        logFile := utilsDir . "\" . StrategyTester.RESULTS_DIR . "\worker_progress.txt"
        DirCreate(utilsDir . "\" . StrategyTester.RESULTS_DIR)

        ; Запустить воркер
        workerArgs := "`"" . rootDir . "`" `"" . targetsFile . "`""
        workerPid  := ""
        Try
        {
            workerPid := Run(A_AhkPath . " `"" . StrategyTester.WORKER_SCRIPT . "`" " . workerArgs)
        }
        Catch as e
        {
            MsgBox("Ошибка запуска воркера: " . e.Message, "Ошибка", 16)
            dlg.Destroy()
            return
        }

        cancelled := false
        btnCancel.OnEvent("Click", _Cancel)
        dlg.OnEvent("Close", _Cancel)

        ; Мониторинг прогресса через SetTimer
        timer := ObjBindMethod(StrategyTester, "_PollProgress", dlg, lblStatus, pgBar, logFile, batFiles.Length, &cancelled, &workerPid)
        SetTimer(timer, 2000)

        _Cancel(*) {
            cancelled := true
            if (workerPid != "")
                Try ProcessClose(workerPid)
            SetTimer(timer, 0)
            dlg.Destroy()
            Logger_Info("Тест стратегий отменён")
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
            btnOk := dlg.Add("Button", "Default w100 h30", "OK")
            btnOk.OnEvent("Click", (*) => dlg.Destroy())
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
        Catch {}
    }
}
