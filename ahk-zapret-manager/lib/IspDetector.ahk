; ============================================================================
; IspDetector.ahk — Определение провайдера по IP
; Primary: ip-api.com | Fallback: ipinfo.io
; Кэш в utils/isp_cache.json (инвалидируется через 24 часа)
; ============================================================================
#Requires AutoHotkey v2.0

class IspDetector
{
    static CACHE_FILE         := "isp_cache.json"
    static STRATEGIES_MAP_URL := "https://raw.githubusercontent.com/WildeSR98/12345/main/utils/isp_strategies.json"
    static CACHE_TTL_HOURS    := 24

    ; Определить ISP и вернуть Map с полями: Ip, Isp, Org, City, Region, Country, As
    ; Если online — сохраняет кэш. Если offline — возвращает кэш или пустой Map.
    static Detect(utilsDir)
    {
        ; Попытка получить данные онлайн
        Try
        {
            ; Primary: ip-api.com (нет ключа, 45 req/min)
            resp := HttpGet("http://ip-api.com/json/?fields=query,isp,org,city,regionName,country,as")
            if (resp.Status = 200 && resp.Body != "")
            {
                data := JsonParser.Parse(resp.Body)
                if (data is Map) && data.Has("query")
                {
                    info := IspDetector._MakeInfo(
                        data.Has("query")      ? data["query"]      : "",
                        data.Has("isp")        ? data["isp"]        : "",
                        data.Has("org")        ? data["org"]        : "",
                        data.Has("city")       ? data["city"]       : "",
                        data.Has("regionName") ? data["regionName"] : "",
                        data.Has("country")    ? data["country"]    : "",
                        data.Has("as")         ? data["as"]         : ""
                    )
                    IspDetector.SaveCache(utilsDir, info)
                    return info
                }
            }
        }
        Catch {}

        ; Fallback: ipinfo.io
        Try
        {
            resp := HttpGet("https://ipinfo.io/json")
            if (resp.Status = 200 && resp.Body != "")
            {
                data := JsonParser.Parse(resp.Body)
                if (data is Map) && data.Has("ip")
                {
                    org := data.Has("org") ? data["org"] : ""
                    info := IspDetector._MakeInfo(
                        data.Has("ip")      ? data["ip"]      : "",
                        org,
                        org,
                        data.Has("city")    ? data["city"]    : "",
                        data.Has("region")  ? data["region"]  : "",
                        data.Has("country") ? data["country"] : "",
                        ""
                    )
                    IspDetector.SaveCache(utilsDir, info)
                    return info
                }
            }
        }
        Catch {}

        ; Вернуть кэш если онлайн недоступен
        cached := IspDetector.LoadCache(utilsDir)
        return cached ? cached : IspDetector._MakeInfo("", "", "", "", "", "", "")
    }

    ; Сохранить кэш
    static SaveCache(utilsDir, info)
    {
        Try
        {
            DirCreate(utilsDir)
            path := utilsDir . "\" . IspDetector.CACHE_FILE
            FileDelete(path)
            FileAppend(JsonParser.Stringify(info), path, "UTF-8")
        }
        Catch {}
    }

    ; Загрузить кэш (вернуть Map или "" если устарел)
    static LoadCache(utilsDir)
    {
        path := utilsDir . "\" . IspDetector.CACHE_FILE
        if !FileExist(path)
            return ""

        ; Проверка возраста файла
        Try
        {
            modTime := FileGetTime(path, "M")
            ; modTime в формате YYYYMMDDHHMMSS
            if IspDetector._HoursAgo(modTime) > IspDetector.CACHE_TTL_HOURS
                return ""

            data := JsonParser.ParseFile(path)
            if (data is Map) && data.Has("Ip")
                return data
        }
        Catch {}
        return ""
    }

    ; Форматированная строка для отображения
    static Format(info)
    {
        if !(info is Map)
            return "Нет данных"
        return "IP: " . info["Ip"] . "`n"
             . "ISP: " . info["Isp"] . "`n"
             . "Организация: " . info["Org"] . "`n"
             . "AS: " . info["As"] . "`n"
             . "Город: " . info["City"] . "`n"
             . "Регион: " . info["Region"] . "`n"
             . "Страна: " . info["Country"]
    }

    ; Получить рекомендованные стратегии для ISP
    static GetRecommendations(utilsDir, ispName)
    {
        mapPath := utilsDir . "\isp_strategies.json"

        ; Попробовать скачать актуальную карту
        Try
        {
            resp := HttpGet(IspDetector.STRATEGIES_MAP_URL)
            if (resp.Status = 200 && resp.Body != "")
            {
                FileDelete(mapPath)
                FileAppend(resp.Body, mapPath, "UTF-8")
            }
        }
        Catch {}

        if !FileExist(mapPath)
            return []

        Try
        {
            stratMap := JsonParser.ParseFile(mapPath)
            if !(stratMap is Map)
                return []

            ; Поиск по ISP name
            for key, strategies in stratMap
            {
                if InStr(ispName, key)
                    return (strategies is Array) ? strategies : []
            }

            ; Дефолтные рекомендации
            if stratMap.Has("default")
            {
                def := stratMap["default"]
                return (def is Array) ? def : []
            }
        }
        Catch {}
        return []
    }

    ; ── Private ───────────────────────────────────────────────────────────────
    static _MakeInfo(ip, isp, org, city, region, country, asn)
    {
        m := Map()
        m["Ip"]      := ip
        m["Isp"]     := isp
        m["Org"]     := org
        m["City"]    := city
        m["Region"]  := region
        m["Country"] := country
        m["As"]      := asn
        return m
    }

    static _HoursAgo(yyyymmddhhmmss)
    {
        Try
        {
            y  := SubStr(yyyymmddhhmmss, 1, 4)
            mo := SubStr(yyyymmddhhmmss, 5, 2)
            d  := SubStr(yyyymmddhhmmss, 7, 2)
            h  := SubStr(yyyymmddhhmmss, 9, 2)
            mi := SubStr(yyyymmddhhmmss, 11, 2)
            s  := SubStr(yyyymmddhhmmss, 13, 2)
            ; Простое приближение: считаем только часы
            nowH  := Integer(FormatTime(, "H"))
            fileH := Integer(h)
            return Abs(nowH - fileH)  ; упрощённо для суточного TTL
        }
        Catch
            return 9999
    }
}
