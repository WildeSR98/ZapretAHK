; ============================================================================
; HttpService.ahk - HTTP запросы через WinHttpRequest
; ============================================================================
#Requires AutoHotkey v2.0

class HttpService
{
    ; GET запрос
    static Get(url, headers:={})
    {
        Try
        {
            http := ComObject("WinHttp.WinHttpRequest.5.1")
            http.Open("GET", url, false)
            
            ; Устанавливаем таймауты (мс)
            http.SetTimeouts(5000, 5000, 5000, 10000)
            
            ; Заголовки по умолчанию
            http.SetRequestHeader("User-Agent", "ZapretManager/3.0 AHK")
            http.SetRequestHeader("Accept", "*/*")
            
            ; Пользовательские заголовки
            for key, value in headers
                http.SetRequestHeader(key, value)
            
            http.Send()
            
            ; Возвращаем Body для любого статуса (редиректы, ошибки — всё нужно)
            return { Status: http.Status, Body: http.ResponseText, Headers: this.ParseHeaders(http.GetAllResponseHeaders()) }
        }
        Catch as e
        {
            return { Status: 0, Body: "", Error: e.Message }
        }
    }
    
    ; POST запрос
    static Post(url, data, headers:={})
    {
        Try
        {
            http := ComObject("WinHttp.WinHttpRequest.5.1")
            http.Open("POST", url, false)
            http.SetTimeouts(5000, 5000, 5000, 10000)
            
            http.SetRequestHeader("Content-Type", "application/json")
            http.SetRequestHeader("User-Agent", "ZapretManager/3.0 AHK")
            
            for key, value in headers
                http.SetRequestHeader(key, value)
            
            http.Send(data)
            
            if (http.Status = 200 || http.Status = 201)
                return { Status: http.Status, Body: http.ResponseText }
            else
                return { Status: http.Status, Body: "", Error: "HTTP " . http.Status }
        }
        Catch as e
        {
            return { Status: 0, Body: "", Error: e.Message }
        }
    }
    
    ; Загрузка файла
    static DownloadFile(url, destPath)
    {
        Try
        {
            http := ComObject("WinHttp.WinHttpRequest.5.1")
            http.Open("GET", url, false)
            http.SetTimeouts(5000, 10000, 10000, 30000)
            http.Send()
            
            if (http.Status = 200)
            {
                ; Получаем бинарные данные
                ado := ComObject("ADODB.Stream")
                ado.Type := 1  ; adTypeBinary
                ado.Open()
                ado.Write(http.ResponseBody)
                ado.SaveToFile(destPath, 2)  ; adSaveCreateOverWrite
                ado.Close()
                return true
            }
            return false
        }
        Catch
            return false
    }
    
    ; Парсинг заголовков
    static ParseHeaders(rawHeaders)
    {
        headers := {}
        for line in StrSplit(rawHeaders, "`r`n")
        {
            if InStr(line, ":")
            {
                pos := InStr(line, ":")
                key := Trim(SubStr(line, 1, pos-1))
                value := Trim(SubStr(line, pos+1))
                headers[key] := value
            }
        }
        return headers
    }
}

HttpGet(url, headers:={}) => HttpService.Get(url, headers)
HttpPost(url, data, headers:={}) => HttpService.Post(url, data, headers)
HttpDownload(url, dest) => HttpService.DownloadFile(url, dest)
