; ============================================================================
; AppConfig.ahk — Загрузка полной конфигурации из config.json
; Использует JsonParser для полного разбора всех секций
; ============================================================================
#Requires AutoHotkey v2.0

class AppConfig
{
    static DEFAULT_VERSION := "3.0.0"

    ; Загрузить config.json и вернуть полный объект Map
    static Load(jsonPath)
    {
        result := AppConfig._Defaults()

        if !FileExist(jsonPath)
            return result

        Try
        {
            parsed := JsonParser.ParseFile(jsonPath)
            if !(parsed is Map)
                return result

            ; version
            if parsed.Has("version")
                result["version"] := parsed["version"]
            else if parsed.Has("project") && (parsed["project"] is Map) && parsed["project"].Has("version")
                result["version"] := parsed["project"]["version"]

            ; repositories
            if parsed.Has("repositories") && (parsed["repositories"] is Map)
                result["repositories"] := parsed["repositories"]

            ; lists
            if parsed.Has("lists") && (parsed["lists"] is Map)
                result["lists"] := parsed["lists"]

            ; diagnostics
            if parsed.Has("diagnostics") && (parsed["diagnostics"] is Map)
                result["diagnostics"] := parsed["diagnostics"]

            ; backup
            if parsed.Has("backup") && (parsed["backup"] is Map)
                result["backup"] := parsed["backup"]

            ; features
            if parsed.Has("features") && (parsed["features"] is Map)
                result["features"] := parsed["features"]

            ; strategies
            if parsed.Has("strategies") && (parsed["strategies"] is Map)
                result["strategies"] := parsed["strategies"]
        }
        Catch as e
        {
            ; JSON broken — return defaults
        }

        return result
    }

    ; Получить значение из вложенного Map по пути "a.b.c"
    static Get(cfg, path, default := "")
    {
        parts := StrSplit(path, ".")
        cur := cfg
        for part in parts
        {
            if !(cur is Map) || !cur.Has(part)
                return default
            cur := cur[part]
        }
        return cur
    }

    ; Получить элемент массива из конфига
    static GetArr(cfg, path)
    {
        val := AppConfig.Get(cfg, path, [])
        return (val is Array) ? val : []
    }

    ; ── Умолчания ─────────────────────────────────────────────────────────────
    static _Defaults()
    {
        d := Map()
        d["version"] := AppConfig.DEFAULT_VERSION

        ; repositories
        repoCore := Map()
        repoCore["release_api"]  := "https://api.github.com/repos/Flowseal/zapret-discord-youtube/releases/latest"
        repoCore["version_url"]  := "https://raw.githubusercontent.com/Flowseal/zapret-discord-youtube/main/.service/version.txt"
        repoCore["hosts_service"]:= "https://raw.githubusercontent.com/Flowseal/zapret-discord-youtube/refs/heads/main/.service/hosts"
        repoCore["ipset_service"]:= "https://raw.githubusercontent.com/Flowseal/zapret-discord-youtube/refs/heads/main/.service/ipset-service.txt"
        repoCore["archive_url"]  := "https://github.com/Flowseal/zapret-discord-youtube/archive/refs/heads/main.zip"

        repoAHK := Map()
        repoAHK["commit_api"]  := "https://api.github.com/repos/WildeSR98/ZapretAHK/commits?per_page=1"
        repoAHK["archive_url"] := "https://github.com/WildeSR98/ZapretAHK/archive/refs/heads/main.zip"

        repos := Map()
        repos["zapret_core"]     := repoCore
        repos["scripts_zapretahk"] := repoAHK
        d["repositories"] := repos

        ; lists
        lists := Map()
        lists["base_url"] := "https://raw.githubusercontent.com/Flowseal/zapret-discord-youtube/refs/heads/main"
        lists["files"]    := []
        d["lists"] := lists

        ; diagnostics
        diag := Map()
        diag["check_targets"]       := []
        diag["conflicting_services"]:= ["GoodbyeDPI", "discordfix_zapret", "winws1", "winws2"]
        diag["dpi_suite_url"]       := "https://hyperion-cs.github.io/dpi-checkers/ru/tcp-16-20/suite.json"
        d["diagnostics"] := diag

        ; backup
        backup := Map()
        backup["keep_count"]       := 5
        backup["include_patterns"] := ["bin\*", "*.bat", "utils\*", "lists\*"]
        backup["exclude_patterns"] := ["*-user*", "logs\*", "backups\*"]
        d["backup"] := backup

        ; features
        features := Map()
        features["parallel_downloads"]  := true
        features["remove_cidr_overlap"] := true
        features["verbose_logging"]     := false
        d["features"] := features

        return d
    }
}

; Обёртка для совместимости
LoadConfig(path) => AppConfig.Load(path)
