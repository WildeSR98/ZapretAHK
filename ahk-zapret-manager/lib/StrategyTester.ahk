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
        resultsDir := utilsDir . "\" . StrategyTester.RESULTS_DIR
        dlg.Add("Text",, "")
        btnHistory := dlg.Add("Button", "x10 y276 w140 h28", "📋 История тестов")
        btnClose   := dlg.Add("Button", "x350 y276 w120 h28", "Закрыть")

        dlg.OnEvent("Close", (*) => dlg.Destroy())
        btnClose.OnEvent("Click", (*) => dlg.Destroy())
        btnQuick.OnEvent("Click", (*) => (dlg.Destroy(), StrategyTester.RunQuick(rootDir, targetsFile)))
        btnFull.OnEvent("Click",  (*) => (dlg.Destroy(), StrategyTester.RunFull(rootDir, targetsFile, batFiles)))
        btnHistory.OnEvent("Click", (*) => StrategyTester.ShowHistory(resultsDir))

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
        lblResult := dlg.Add("Text", "w420 h120 +Multi", "Ожидание...")
        dlg.Add("Text",, "")
        ; Кнопки появятся после завершения
        btnClose  := dlg.Add("Button", "w100 h30", "Закрыть")
        btnOpen   := dlg.Add("Button", "x+8 w120 h30", "📄 Открыть файл")
        btnOpen.Enabled := false
        btnClose.OnEvent("Click", (*) => dlg.Destroy())
        dlg.OnEvent("Close", (*) => dlg.Destroy())
        dlg.Show("w450 AutoSize")

        outFile := ""

        SetTimer(_DoQuick, -100)

        _DoQuick() {
            lines := []
            ; Читаем цели (поддержка ; и # как комментариев)
            if !FileExist(targetsFile)
            {
                lblResult.Text := "Файл целей не найден"
                return
            }
            Loop Read, targetsFile
            {
                line := Trim(A_LoopReadLine)
                if (line = "" || SubStr(line, 1, 1) = ";" || SubStr(line, 1, 1) = "#")
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
            q := Chr(34)
            for target in lines
            {
                ; Разбор строки: Name = "URL" или просто URL
                url  := target
                name := target
                if RegExMatch(target, "^\s*(.+?)\s*=\s*" . q . "(.+)" . q . "\s*$", &m)
                {
                    name := Trim(m[1])
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
            btnOpen.Enabled := true
            btnOpen.OnEvent("Click", (*) => Run("notepad.exe `"" . outFile . "`""))
        }
    }

    ; ── Full тест — все стратегии через воркер-процесс ────────────────────────
    static RunFull(rootDir, targetsFile, batFiles)
    {
        utilsDir   := rootDir . "\utils"
        resultsDir := utilsDir . "\" . StrategyTester.RESULTS_DIR
        logFile    := resultsDir . "\worker_progress.txt"
        DirCreate(resultsDir)

        dlg := Gui("+AlwaysOnTop", "Полный тест — Zapret Manager")
        dlg.MarginX := 14
        dlg.MarginY := 12

        dlg.Add("Text",, "Тест всех стратегий запущен в фоне.")
        dlg.Add("Text",, "Стратегий: " . batFiles.Length)
        dlg.Add("Text",, "")
        dlg.Add("Text", "cRed", "⚠ Интернет-соединение будет прерываться.")
        dlg.Add("Text", "cRed", "⚠ Тест займёт ~" . (batFiles.Length * 15) . " секунд.")
        dlg.Add("Text",, "")
        lblStatus := dlg.Add("Text", "w420", "Статус: запуск воркера...")
        pgBar := dlg.Add("Progress", "w420 h20 Range0-" . batFiles.Length, 0)
        dlg.Add("Text",, "")
        btnCancel  := dlg.Add("Button", "x14 w100 h30 Default", "Отмена")
        btnResults := dlg.Add("Button", "x+8 w140 h30", "📋 Результаты")
        btnResults.Enabled := false
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
        lastResultFile := ""
        btnCancel.OnEvent("Click", (*) => _Cancel())
        dlg.OnEvent("Close", (*) => _Cancel())
        btnResults.OnEvent("Click", (*) => StrategyTester._OpenLastResult(resultsDir))

        timer := ObjBindMethod(StrategyTester, "_PollProgress", dlg, lblStatus, pgBar, logFile,
                               batFiles.Length, &cancelled, &workerPid, btnCancel, btnResults, resultsDir)
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

    ; ── История тестов ─────────────────────────────────────────────────────────
    static ShowHistory(resultsDir)
    {
        if !DirExist(resultsDir)
        {
            MsgBox("История тестов пуста.`nПапка не найдена: " . resultsDir, "История тестов", 64)
            return
        }

        ; Собираем все txt файлы результатов
        files := []
        Loop Files, resultsDir . "\*.txt"
        {
            if (A_LoopFileName != "worker_progress.txt")
                files.Push({name: A_LoopFileName, path: A_LoopFilePath, time: A_LoopFileTimeModified})
        }

        if (files.Length = 0)
        {
            MsgBox("История тестов пуста.`nЗапустите хотя бы один тест.", "История тестов", 64)
            return
        }

        ; Сортировка по дате (новые первые) — простой bubble sort
        Loop files.Length - 1
        {
            i := A_Index
            Loop files.Length - i
            {
                j := A_Index
                if (files[j].time < files[j+1].time)
                {
                    tmp := files[j]
                    files[j] := files[j+1]
                    files[j+1] := tmp
                }
            }
        }

        ; GUI истории
        dlg := Gui("+AlwaysOnTop", "История тестов — Zapret Manager")
        dlg.MarginX := 14
        dlg.MarginY := 12
        dlg.Add("Text", "w480", "Выберите тест для просмотра:")
        dlg.Add("Text",, "")

        lb := dlg.Add("ListBox", "w480 r10 vChoice", [])
        for f in files
            lb.Add([f.name])
        lb.Choose(1)

        dlg.Add("Text",, "")
        btnOpen   := dlg.Add("Button", "Default w120 h30", "📄 Открыть")
        btnDelete := dlg.Add("Button", "x+8 w120 h30", "🗑 Удалить")
        btnClose  := dlg.Add("Button", "x+8 w90 h30", "Закрыть")

        btnOpen.OnEvent("Click", _Open)
        btnDelete.OnEvent("Click", _Delete)
        btnClose.OnEvent("Click", (*) => dlg.Destroy())
        dlg.OnEvent("Close", (*) => dlg.Destroy())
        lb.OnEvent("DoubleClick", _Open)

        _Open(*) {
            idx := lb.Value
            if (idx >= 1 && idx <= files.Length)
                Run("notepad.exe `"" . files[idx].path . "`"")
        }

        _Delete(*) {
            idx := lb.Value
            if (idx < 1 || idx > files.Length)
                return
            if MsgBox("Удалить " . files[idx].name . "?", "Подтверждение", 4) = "Yes"
            {
                FileDelete(files[idx].path)
                files.RemoveAt(idx)
                lb.Delete(idx)
                if (files.Length > 0)
                    lb.Choose(Min(idx, files.Length))
                if (files.Length = 0)
                {
                    MsgBox("История очищена.", "История тестов", 64)
                    dlg.Destroy()
                }
            }
        }

        dlg.Show("w500 AutoSize")
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

    ; Открыть последний файл результатов
    static _OpenLastResult(resultsDir)
    {
        lastFile := ""
        lastTime := ""
        Loop Files, resultsDir . "\*.txt"
        {
            if (A_LoopFileName = "worker_progress.txt")
                continue
            if (A_LoopFileTimeModified > lastTime)
            {
                lastTime := A_LoopFileTimeModified
                lastFile := A_LoopFilePath
            }
        }
        if (lastFile != "")
            Run("notepad.exe `"" . lastFile . "`"")
        else
            MsgBox("Файл результатов не найден.", "Тест стратегий", 48)
    }

    ; Callback опроса прогресса (каждые 2 сек)
    static _PollProgress(dlg, lblStatus, pgBar, logFile, total, &cancelled, &pid, btnCancel, btnResults, resultsDir)
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
            lblStatus.Text := "✔ Тест завершён! Нажмите «Результаты» для просмотра."
            Logger_Info("Тест стратегий завершён")
            btnCancel.Enabled  := false
            btnResults.Enabled := true
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
