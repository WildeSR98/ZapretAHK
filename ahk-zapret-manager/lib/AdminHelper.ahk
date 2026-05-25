; ============================================================================
; AdminHelper.ahk - Проверка прав администратора
; ============================================================================
#Requires AutoHotkey v2.0

class AdminHelper
{
    ; Проверка прав администратора
    static IsAdmin()
    {
        Try
        {
            shell := ComObject("Shell.Application")
            return shell.IsRestricted() = 0 && shell.Environment("Process", "Administrator") != ""
        }
        Catch
        {
            ; Альтернативная проверка через SID
            return this.CheckAdminViaSid()
        }
    }
    
    ; Проверка через SID администратора
    static CheckAdminViaSid()
    {
        Try
        {
            wmi := ComObject("WbemScripting.SWbemLocator")
            svc := wmi.ConnectServer(".", "root\cimv2")
            svc.Security_.ImpersonationLevel := 3
            
            query := "SELECT * FROM Win32_UserAccount WHERE SID LIKE '%-500' AND LocalAccount=TRUE"
            result := svc.ExecQuery(query)
            
            for item in result
                return true  ; Если нашли встроенного админа, значит запущены от его имени
            
            return false
        }
        Catch
            return false
    }
    
    ; Требование прав администратора с перезапуском
    static RequireAdmin()
    {
        if !this.IsAdmin()
        {
            Try
            {
                Run '*RunAs "' . A_ScriptFullPath . '"'
            }
            Catch
            {
                MsgBox("Требуются права администратора для работы приложения!", "Zapret Manager", 48)
                ExitApp
            }
            ExitApp
        }
    }
}

IsAdmin() => AdminHelper.IsAdmin()
RequireAdmin() => AdminHelper.RequireAdmin()
