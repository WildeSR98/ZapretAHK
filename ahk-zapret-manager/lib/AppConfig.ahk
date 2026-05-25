; ============================================================================
; AppConfig.ahk - Загрузка конфигурации из JSON
; ============================================================================
#Requires AutoHotkey v2.0

class AppConfig
{
    static Load(jsonPath)
    {
        if !FileExist(jsonPath)
            return this.Defaults()
        
        Try
        {
            content := FileRead(jsonPath, "UTF-8")
            return this.ParseJson(content)
        }
        Catch
            return this.Defaults()
    }
    
    static Defaults()
    {
        return {
            version: "3.0.0",
            project: { name: "Zapret Autosetup", version: "3.0.0" },
            repositories: {
                zapret_core: {
                    owner: "Flowseal",
                    repo: "zapret-discord-youtube",
                    release_api: "https://api.github.com/repos/Flowseal/zapret-discord-youtube/releases/latest",
                    archive_url: "https://github.com/Flowseal/zapret-discord-youtube/archive/refs/heads/main.zip"
                }
            },
            diagnostics: {
                check_targets: [
                    { name: "discord.com", type: "url", url: "https://discord.com", host: "discord.com" },
                    { name: "youtube.com", type: "url", url: "https://www.youtube.com", host: "www.youtube.com" }
                ],
                dpi_suite_url: "https://hyperion-cs.github.io/dpi-checkers/ru/tcp-16-20/suite.json"
            },
            backup: { keep_count: 5 },
            features: { verbose_logging: false, log_retention_days: 14 }
        }
    }
    
    ; Простой JSON парсер для базовых структур
    static ParseJson(json)
    {
        ; Удаляем пробелы и переносы строк для упрощения
        json := RegExReplace(json, "^\s*\{|\}\s*$")
        
        result := {}
        
        ; Извлекаем версию
        if RegExMatch(json, '"version"\s*:\s*"([^"]+)"', m)
            result.version := m[1]
        
        ; Извлекаем project.name
        if RegExMatch(json, '"name"\s*:\s*"([^"]+)"', m)
            result.project := { name: m[1] }
        
        return result
    }
}

LoadConfig(path) => AppConfig.Load(path)
