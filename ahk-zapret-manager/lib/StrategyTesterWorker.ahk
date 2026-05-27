; ============================================================================
; StrategyTesterWorker.ahk — Фоновый воркер для тестирования стратегий
; Запускается как отдельный AHK-процесс из StrategyTester.ahk
; Аргументы: RootDir TargetsFile
; ============================================================================
#Requires AutoHotkey v2.0

rootDir     := A_Args[1]
targetsFile := A_Args[2]
utilsDir    := rootDir . "\utils"
resultsDir  := utilsDir . "\test results"
progressFile:= resultsDir . "\worker_progress.txt"
binDir      := rootDir . "\bin"

DirCreate(resultsDir)
if FileExist(progressFile)
    FileDelete(progressFile)

; ── Загрузка целей ──────────────────────────────────────────────────────────

targets := []
Loop Read, targetsFile
{
    line := Trim(A_LoopReadLine)
    if (line = "" || SubStr(line, 1, 1) = ";" || SubStr(line, 1, 1) = "#")
        continue
    q := Chr(34)
    if RegExMatch(line, "^\\s*(\\w+)\\s*=\\s*" . q . "(.+)" . q . "\\s*$", &m)
    {
        name  := m[1]
        value := m[2]
        if (SubStr(value, 1, 5) = "PING:")
            targets.Push({name: name, url: "", host: SubStr(value, 6), isPing: true})
        else
        {
            host := RegExReplace(value, "^https?://", "")
            host := RegExReplace(host, "/.*$", "")
            targets.Push({name: name, url: value, host: host, isPing: false})
        }
    }
}

if (targets.Length = 0)
{
    FileAppend("Нет целей для тестирования`n", progressFile, "UTF-8")
    ExitApp(1)
}

; ── Найти .bat стратегии ────────────────────────────────────────────────────

batFiles := []
Loop Files, rootDir . "\strategies\general*.bat"
    batFiles.Push(A_LoopFilePath)
Loop Files, rootDir . "\strategies\custom*.bat"
    batFiles.Push(A_LoopFilePath)

if (batFiles.Length = 0)
{
    FileAppend("Нет стратегий`n", progressFile, "UTF-8")
    ExitApp(1)
}

; ── Тест каждой стратегии ───────────────────────────────────────────────────

allResults := []
timestamp  := FormatTime(, "yyyy-MM-dd_HH-mm-ss")

for batPath in batFiles
{
    batName := StrReplace(batPath, rootDir . "\strategies\", "")

    ; Запись прогресса
    FileAppend(batName . "`n", progressFile, "UTF-8")

    ; Убить winws
    _KillWinws()
    Sleep(300)

    ; Запустить стратегию
    Run("cmd.exe /c `"" . batPath . "`"",, "Hide")
    Sleep(5000)

    ; Тестируем цели
    results := []
    for target in targets
    {
        if target.isPing
        {
            pingOk := _DoPing(target.host)
            results.Push({name: target.name, status: (pingOk ? "Ping:OK" : "Ping:FAIL"), ok: pingOk})
        }
        else
        {
            httpOk := _DoHttp(target.url)
            results.Push({name: target.name, status: (httpOk ? "HTTP:OK" : "HTTP:ERR"), ok: httpOk})
        }
    }

    ; Остановить winws
    _KillWinws()
    Sleep(300)

    ; Подсчёт
    okCount   := 0
    failCount := 0
    for res in results
        (res.ok ? okCount++ : failCount++)

    allResults.Push({name: batName, results: results, ok: okCount, fail: failCount})
}

; ── Запись результатов ──────────────────────────────────────────────────────

; Найти лучшую
bestName   := ""
bestScore  := -1
for ar in allResults
{
    if (ar.ok > bestScore)
    {
        bestScore := ar.ok
        bestName  := ar.name
    }
}

outFile := resultsDir . "\test_results_" . timestamp . ".txt"
out     := "Запрет Менеджер — Результаты тестирования стратегий`n"
out     .= "Дата: " . FormatTime(, "yyyy-MM-dd HH:mm:ss") . "`n"
out     .= "═══════════════════════════════════════════════`n`n"

for ar in allResults
{
    out .= "Стратегия: " . ar.name . "`n"
    for res in ar.results
        out .= "  " . res.name . ": " . res.status . "`n"
    out .= "  Итог: OK=" . ar.ok . " FAIL=" . ar.fail . "`n`n"
}

out .= "═══════════════════════════════════════════════`n"
out .= "Лучшая стратегия: " . bestName . " (OK=" . bestScore . ")`n"

FileAppend(out, outFile, "UTF-8")

; Удалить лог прогресса
FileDelete(progressFile)

ExitApp(0)

; ── Вспомогательные функции ──────────────────────────────────────────────────

_KillWinws()
{
    Try ProcessClose("winws.exe")
    Catch as _e
    {
    }
}

_DoHttp(url)
{
    Try
    {
        whr := ComObject("WinHttp.WinHttpRequest.5.1")
        whr.Open("HEAD", url, false)
        whr.SetTimeouts(5000, 5000, 5000, 5000)
        whr.Send()
        code := whr.Status
        return (code >= 100 && code < 600)
    }
    Catch as _e
        return false
}

_DoPing(host)
{
    Try
    {
        tmp  := A_Temp . "\ping_" . A_TickCount . ".txt"
        RunWait("ping.exe -n 2 -w 1000 " . host . " > `"" . tmp . "`"",, "Hide")
        if FileExist(tmp)
        {
            ok := InStr(FileRead(tmp), "TTL=") ? true : false
            FileDelete(tmp)
            return ok
        }
        return false
    }
    Catch as _e
        return false
}
