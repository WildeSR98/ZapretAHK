; ============================================================================
; UpdateChecker.ahk — Проверка обновлений через GitHub Releases API
; Manager: WildeSR98/12345 | Core: Flowseal/zapret-discord-youtube
; ============================================================================
#Requires AutoHotkey v2.0

class UpdateChecker
{
    static CACHE_FILE       := "update-check.json"
    static UPDATE_MODE_FILE := "update_mode.txt"

    ; Проверить обновления и вернуть Map с результатом
    ; Сохраняет кэш в utils/update-check.json
    static CheckNow(cfg, rootDir)
    {
        utilsDir := rootDir . "\utils"
        DirCreate(utilsDir)

        mgrRemote      := ""
        mgrLocal       := ""
        mgrDownloadUrl := ""
        coreRemote     := ""
        coreLocal      := ""

        ; ── Manager (WildeSR98/12345) ──────────────────────────────────────
        Try
        {
            repos := AppConfig.Get(cfg, "repositories")
            if (repos is Map) && repos.Has("scripts_12345")
            {
                r12 := repos["scripts_12345"]
                if (r12 is Map) && r12.Has("release_api")
                {
                    resp := HttpGet(r12["release_api"])
                    if (resp.Status = 200 && resp.Body != "")
                    {
                        data := JsonParser.Parse(resp.Body)
                        if (data is Map)
                        {
                            if data.Has("tag_name")
                                mgrRemote := LTrim(data["tag_name"], "vV")

                            ; Найти zip в assets
                            if data.Has("assets") && (data["assets"] is Array)
                            {
                                for asset in data["assets"]
                                {
                                    if (asset is Map) && asset.Has("name") && asset.Has("browser_download_url")
                                    {
                                        if InStr(asset["name"], ".zip")
                                        {
                                            mgrDownloadUrl := asset["browser_download_url"]
                                            break
                                        }
                                    }
                                }
                            }
                            if (mgrDownloadUrl = "") && data.Has("zipball_url")
                                mgrDownloadUrl := data["zipball_url"]
                        }
                    }
                }
            }
        }
        Catch as _e {}

        mgrLocal := UpdateChecker.ReadManagerVersion(rootDir)

        ; ── Core (Flowseal/zapret-discord-youtube) ──────────────────────
        Try
        {
            repos := AppConfig.Get(cfg, "repositories")
            if (repos is Map) && repos.Has("zapret_core")
            {
                rcore := repos["zapret_core"]
                if (rcore is Map) && rcore.Has("release_api")
                {
                    resp := HttpGet(rcore["release_api"])
                    if (resp.Status = 200 && resp.Body != "")
                    {
                        data := JsonParser.Parse(resp.Body)
                        if (data is Map) && data.Has("tag_name")
                            coreRemote := LTrim(data["tag_name"], "vV")
                    }
                }
                else if (rcore is Map) && rcore.Has("version_url")
                {
                    resp := HttpGet(rcore["version_url"])
                    if (resp.Status = 200)
                        coreRemote := Trim(resp.Body)
                }
            }
        }
        Catch as _e {}

        coreLocal := UpdateChecker.ReadCoreVersion(rootDir)

        mgrUpdate  := UpdateChecker.IsNewer(mgrRemote, mgrLocal)
        coreUpdate := UpdateChecker.IsNewer(coreRemote, coreLocal)

        result := Map()
        result["managerRemote"]        := mgrRemote
        result["managerLocal"]         := mgrLocal
        result["managerDownloadUrl"]   := mgrDownloadUrl
        result["coreRemote"]           := coreRemote
        result["coreLocal"]            := coreLocal
        result["managerUpdateAvailable"] := mgrUpdate
        result["coreUpdateAvailable"]    := coreUpdate
        result["checkedAt"]            := FormatTime(, "yyyy-MM-dd'T'HH:mm:ss")

        ; Сохранить кэш
        Try
        {
            cachePath := utilsDir . "\" . UpdateChecker.CACHE_FILE
            FileDelete(cachePath)
            FileAppend(JsonParser.Stringify(result), cachePath, "UTF-8")
        }
        Catch as _e {}

        return result
    }

    ; Загрузить кэшированный результат
    static LoadCache(rootDir)
    {
        path := rootDir . "\utils\" . UpdateChecker.CACHE_FILE
        if !FileExist(path)
            return ""
        Try
        {
            data := JsonParser.ParseFile(path)
            return (data is Map) ? data : ""
        }
        Catch
            return ""
    }

    ; Прочитать режим обновлений (auto / manual)
    static GetUpdateMode(rootDir)
    {
        path := rootDir . "\utils\" . UpdateChecker.UPDATE_MODE_FILE
        if !FileExist(path)
            return "manual"
        mode := Trim(FileRead(path))
        return (mode = "auto") ? "auto" : "manual"
    }

    static SetUpdateMode(rootDir, mode)
    {
        path := rootDir . "\utils\" . UpdateChecker.UPDATE_MODE_FILE
        Try
        {
            FileDelete(path)
            FileAppend((mode = "auto") ? "auto" : "manual", path, "UTF-8")
        }
        Catch as _e {}
    }

    static ReadManagerVersion(rootDir)
    {
        vf := rootDir . "\utils\manager_version.txt"
        return FileExist(vf) ? Trim(FileRead(vf)) : ""
    }

    static ReadCoreVersion(rootDir)
    {
        vf := rootDir . "\bin\version.txt"
        return FileExist(vf) ? Trim(FileRead(vf)) : ""
    }

    ; Сравнение версий: возвращает true если remote > local
    ; Поддерживает форматы: 3.0.0, 1.9.8c, v2.1
    static IsNewer(remote, localVer)
    {
        if (remote = "" || localVer = "")
            return false

        remote   := LTrim(Trim(remote),   "vV")
        localVer := LTrim(Trim(localVer), "vV")

        if (remote = localVer)
            return false

        rParts := StrSplit(remote,   [".", "-", "_"])
        lParts := StrSplit(localVer, [".", "-", "_"])
        maxLen := Max(rParts.Length, lParts.Length)

        Loop maxLen
        {
            i  := A_Index
            rp := (i <= rParts.Length) ? rParts[i] : "0"
            lp := (i <= lParts.Length) ? lParts[i] : "0"

            ; Числовое сравнение
            if IsInteger(rp) && IsInteger(lp)
            {
                if Integer(rp) > Integer(lp)
                    return true
                if Integer(rp) < Integer(lp)
                    return false
                continue
            }

            ; Строковое сравнение (для "8c" vs "8b")
            if (rp > lp)
                return true
            if (rp < lp)
                return false
        }
        return false
    }
}
