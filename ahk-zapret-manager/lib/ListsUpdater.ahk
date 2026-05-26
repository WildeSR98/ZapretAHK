; ============================================================================
; ListsUpdater.ahk — Загрузка и обновление списков IP/доменов
; По образцу ListDownloader.cs + HostsUpdater.cs
; ============================================================================
#Requires AutoHotkey v2.0

class ListsUpdater
{
    ; Обновить все списки из config.json:lists
    ; progressCb(filename, ok) — опциональный колбэк прогресса
    static UpdateAll(cfg, listsDir, progressCb := "")
    {
        DirCreate(listsDir)

        lists := AppConfig.Get(cfg, "lists")
        if !(lists is Map)
        {
            Logger_Warn("ListsUpdater: нет секции lists в config")
            return false
        }

        baseUrl := lists.Has("base_url") ? lists["base_url"] : ""
        files   := lists.Has("files") && (lists["files"] is Array) ? lists["files"] : []

        ok := true
        for entry in files
        {
            if !(entry is Map)
                continue

            localName := entry.Has("local")  ? entry["local"]  : ""
            remotePath:= entry.Has("remote") ? entry["remote"] : ""
            stub      := entry.Has("stub")   ? entry["stub"]   : ""
            isUser    := entry.Has("user")   ? entry["user"]   : false

            if (localName = "")
                continue

            localPath := listsDir . "\" . localName

            ; User-файл: создать stub если не существует
            if isUser
            {
                if !FileExist(localPath)
                {
                    ListsUpdater._WriteStub(localPath, stub)
                    if IsObject(progressCb)
                        progressCb(localName, true)
                }
                continue
            }

            ; Нет remote URL — только stub
            if (remotePath = "")
            {
                if !FileExist(localPath)
                    ListsUpdater._WriteStub(localPath, stub)
                continue
            }

            ; Формируем полный URL
            url := InStr(remotePath, "http") = 1 ? remotePath : baseUrl . "/" . remotePath

            ; Скачиваем
            Try
            {
                tmpPath := localPath . ".tmp"
                if HttpDownload(url, tmpPath)
                {
                    ; Слияние с существующим пользовательским содержимым
                    merged := ListsUpdater._MergeUserLines(localPath, tmpPath)
                    FileDelete(localPath)
                    FileAppend(merged, localPath, "UTF-8")
                    FileDelete(tmpPath)
                    Logger_Info("Lists: обновлён " . localName)
                    if IsObject(progressCb)
                        progressCb(localName, true)
                }
                else
                {
                    Logger_Warn("Lists: не удалось загрузить " . localName)
                    if !FileExist(localPath)
                        ListsUpdater._WriteStub(localPath, stub)
                    if IsObject(progressCb)
                        progressCb(localName, false)
                    ok := false
                }
            }
            Catch as e
            {
                Logger_Warn("Lists: ошибка " . localName . " — " . e.Message)
                if !FileExist(localPath)
                    ListsUpdater._WriteStub(localPath, stub)
                if IsObject(progressCb)
                    progressCb(localName, false)
                ok := false
            }
        }
        return ok
    }

    ; Обновить hosts файл (для блокировки рекламы/трекеров)
    static UpdateHosts(cfg, listsDir, progressCb := "")
    {
        repos := AppConfig.Get(cfg, "repositories")
        hostsUrl := ""

        if (repos is Map) && repos.Has("zapret_core")
        {
            rcore := repos["zapret_core"]
            if (rcore is Map) && rcore.Has("hosts_service")
                hostsUrl := rcore["hosts_service"]
        }

        if (hostsUrl = "")
        {
            Logger_Warn("ListsUpdater: нет URL для hosts")
            return false
        }

        DirCreate(listsDir)
        hostsPath := listsDir . "\hosts"
        tmpPath   := hostsPath . ".tmp"

        Try
        {
            if HttpDownload(hostsUrl, tmpPath)
            {
                FileDelete(hostsPath)
                FileMove(tmpPath, hostsPath)
                Logger_Info("Hosts обновлён: " . hostsPath)
                if IsObject(progressCb)
                    progressCb("hosts", true)
                return true
            }
        }
        Catch as e
        {
            Logger_Warn("Hosts: ошибка — " . e.Message)
        }

        if IsObject(progressCb)
            progressCb("hosts", false)
        return false
    }

    ; ── Private ───────────────────────────────────────────────────────────────

    ; Записать stub-содержимое в файл
    static _WriteStub(path, stubText)
    {
        Try
        {
            if !FileExist(path)
                FileAppend(stubText . "`n", path, "UTF-8")
        }
        Catch {}
    }

    ; Слить новые строки с существующими пользовательскими (начинающимися с #user или содержимым -user файла)
    static _MergeUserLines(existPath, newPath)
    {
        newContent := ""
        Try { newContent := FileRead(newPath, "UTF-8") }
        Catch { return "" }

        ; Если старый файл не существует — возвращаем только новое
        if !FileExist(existPath)
            return newContent

        ; Собираем пользовательские строки из старого файла (после маркера #user или в конце)
        existLines := StrSplit(FileRead(existPath, "UTF-8"), "`n")
        userLines  := []
        inUser     := false
        for line in existLines
        {
            clean := Trim(line, "`r")
            if (InStr(clean, "#user") = 1)
                inUser := true
            if inUser && clean != "" && !InStr(newContent, clean)
                userLines.Push(clean)
        }

        if (userLines.Length = 0)
            return newContent

        result := RTrim(newContent, "`n`r") . "`n`n# User entries`n"
        for ul in userLines
            result .= ul . "`n"
        return result
    }
}
