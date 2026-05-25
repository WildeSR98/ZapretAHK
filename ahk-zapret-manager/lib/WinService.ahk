; ============================================================================
; WinService.ahk - Управление службами Windows через advapi32.dll
; ============================================================================
#Requires AutoHotkey v2.0

class WinService
{
    ; P/Invoke константы
    static SC_MANAGER_ALL := 0xF003F
    static SERVICE_ALL := 0xF01FF
    static SERVICE_WIN32_OWN_PROCESS := 0x10
    static SERVICE_AUTO_START := 0x02
    static SERVICE_DEMAND_START := 0x03
    static SERVICE_ERROR_NORMAL := 0x01
    static SERVICE_CONTROL_STOP := 0x01
    static SERVICE_CONFIG_DESCRIPTION := 1
    static SERVICE_CONFIG_FAILURE_ACTIONS := 2
    
    ; Состояния службы
    static SERVICE_STOPPED := 1
    static SERVICE_START_PENDING := 2
    static SERVICE_RUNNING := 4
    static SERVICE_STOP_PENDING := 3
    
    ; ── Установка службы ──────────────────────────────────────────────────────
    static Install(name, displayName, description, binPathWithArgs, autoStart:=true)
    {
        scm := DllCall("advapi32\OpenSCManagerW", "str", "", "str", "", "uint", this.SC_MANAGER_ALL, "ptr")
        if (scm = 0)
        {
            this.Log("OpenSCManager failed: " . A_LastError)
            return false
        }
        
        Try
        {
            ; Удаляем старую службу если существует
            old := DllCall("advapi32\OpenServiceW", "ptr", scm, "str", name, "uint", this.SERVICE_ALL, "ptr")
            if (old != 0)
            {
                st := Buffer(28, 0)  ; SERVICE_STATUS structure
                DllCall("advapi32\ControlService", "ptr", old, "uint", this.SERVICE_CONTROL_STOP, "ptr", st.Ptr)
                Sleep(1000)
                DllCall("advapi32\DeleteService", "ptr", old)
                DllCall("advapi32\CloseServiceHandle", "ptr", old)
                Sleep(2000)
            }
            
            startType := autoStart ? this.SERVICE_AUTO_START : this.SERVICE_DEMAND_START
            
            ; Dependencies: Tcpip, Afd
            dependencies := "Tcpip`0Afd`0"
            
            svc := DllCall("advapi32\CreateServiceW",
                "ptr", scm,
                "str", name,
                "str", displayName,
                "uint", this.SERVICE_ALL,
                "uint", this.SERVICE_WIN32_OWN_PROCESS,
                "uint", startType,
                "uint", this.SERVICE_ERROR_NORMAL,
                "str", binPathWithArgs,
                "ptr", 0,  ; group
                "ptr", 0,  ; tag
                "str", dependencies,
                "ptr", 0,  ; account
                "ptr", 0,  ; password
                "ptr")
            
            if (svc = 0)
            {
                this.Log("CreateService failed: " . A_LastError)
                DllCall("advapi32\CloseServiceHandle", "ptr", scm)
                return false
            }
            
            ; Устанавливаем описание
            desc := Buffer(4 + A_PtrSize, 0)
            NumPut("str", description, desc, 0)
            DllCall("advapi32\ChangeServiceConfig2W", "ptr", svc, "uint", this.SERVICE_CONFIG_DESCRIPTION, "ptr", desc.Ptr)
            
            ; Устанавливаем политику восстановления
            this.SetRecoveryPolicy(svc)
            
            ; Запускаем службу
            started := DllCall("advapi32\StartServiceW", "ptr", svc, "uint", 0, "ptr", 0)
            if (!started)
                this.Log("StartService failed: " . A_LastError)
            
            ; Ждём запуска
            running := this.WaitForState(svc, this.SERVICE_RUNNING, 10000)
            DllCall("advapi32\CloseServiceHandle", "ptr", svc)
            DllCall("advapi32\CloseServiceHandle", "ptr", scm)
            
            if (running)
            {
                this.Log("Служба установлена и запущена: " . name)
                return true
            }
            else
            {
                this.Log("Служба установлена, но не запущена")
                return false
            }
        }
        Catch as e
        {
            this.Log("Install exception: " . e.Message)
            DllCall("advapi32\CloseServiceHandle", "ptr", scm)
            return false
        }
    }
    
    ; ── Удаление службы ───────────────────────────────────────────────────────
    static Remove(name)
    {
        scm := DllCall("advapi32\OpenSCManagerW", "str", "", "str", "", "uint", this.SC_MANAGER_ALL, "ptr")
        if (scm = 0)
            return false
        
        Try
        {
            svc := DllCall("advapi32\OpenServiceW", "ptr", scm, "str", name, "uint", this.SERVICE_ALL, "ptr")
            if (svc = 0)
            {
                DllCall("advapi32\CloseServiceHandle", "ptr", scm)
                return true  ; Уже удалена
            }
            
            st := Buffer(28, 0)
            DllCall("advapi32\ControlService", "ptr", svc, "uint", this.SERVICE_CONTROL_STOP, "ptr", st)
            Sleep(500)
            
            ok := DllCall("advapi32\DeleteService", "ptr", svc)
            DllCall("advapi32\CloseServiceHandle", "ptr", svc)
            DllCall("advapi32\CloseServiceHandle", "ptr", scm)
            
            if (ok)
                this.Log("Служба удалена: " . name)
            
            return ok
        }
        Catch
        {
            DllCall("advapi32\CloseServiceHandle", "ptr", scm)
            return false
        }
    }
    
    ; ── Получение состояния ───────────────────────────────────────────────────
    static GetState(name)
    {
        scm := DllCall("advapi32\OpenSCManagerW", "str", "", "str", "", "uint", 0x0001, "ptr")
        if (scm = 0)
            return "Unknown"
        
        Try
        {
            svc := DllCall("advapi32\OpenServiceW", "ptr", scm, "str", name, "uint", 0x0004, "ptr")
            if (svc = 0)
            {
                DllCall("advapi32\CloseServiceHandle", "ptr", scm)
                return "NotInstalled"
            }
            
            st := Buffer(28, 0)
            if (!DllCall("advapi32\QueryServiceStatus", "ptr", svc, "ptr", st.Ptr))
            {
                DllCall("advapi32\CloseServiceHandle", "ptr", svc)
                DllCall("advapi32\CloseServiceHandle", "ptr", scm)
                return "Unknown"
            }
            
            currentState := NumGet(st, 4, "uint")
            DllCall("advapi32\CloseServiceHandle", "ptr", svc)
            DllCall("advapi32\CloseServiceHandle", "ptr", scm)
            
            switch currentState
            {
                case this.SERVICE_STOPPED: return "Stopped"
                case this.SERVICE_START_PENDING: return "Starting"
                case this.SERVICE_STOP_PENDING: return "Stopping"
                case this.SERVICE_RUNNING: return "Running"
                default: return "Unknown"
            }
        }
        Catch
        {
            DllCall("advapi32\CloseServiceHandle", "ptr", scm)
            return "Unknown"
        }
    }
    
    ; ── Запуск службы ─────────────────────────────────────────────────────────
    static Start(name)
    {
        scm := DllCall("advapi32\OpenSCManagerW", "str", "", "str", "", "uint", this.SC_MANAGER_ALL, "ptr")
        if (scm = 0)
            return false
        
        Try
        {
            svc := DllCall("advapi32\OpenServiceW", "ptr", scm, "str", name, "uint", this.SERVICE_ALL, "ptr")
            if (svc = 0)
            {
                DllCall("advapi32\CloseServiceHandle", "ptr", scm)
                return false
            }
            
            ok := DllCall("advapi32\StartServiceW", "ptr", svc, "uint", 0, "ptr", 0)
            DllCall("advapi32\CloseServiceHandle", "ptr", svc)
            DllCall("advapi32\CloseServiceHandle", "ptr", scm)
            return ok
        }
        Catch
        {
            DllCall("advapi32\CloseServiceHandle", "ptr", scm)
            return false
        }
    }
    
    ; ── Остановка службы ──────────────────────────────────────────────────────
    static Stop(name)
    {
        scm := DllCall("advapi32\OpenSCManagerW", "str", "", "str", "", "uint", this.SC_MANAGER_ALL, "ptr")
        if (scm = 0)
            return false
        
        Try
        {
            svc := DllCall("advapi32\OpenServiceW", "ptr", scm, "str", name, "uint", this.SERVICE_ALL, "ptr")
            if (svc = 0)
            {
                DllCall("advapi32\CloseServiceHandle", "ptr", scm)
                return false
            }
            
            st := Buffer(28, 0)
            ok := DllCall("advapi32\ControlService", "ptr", svc, "uint", this.SERVICE_CONTROL_STOP, "ptr", st)
            DllCall("advapi32\CloseServiceHandle", "ptr", svc)
            DllCall("advapi32\CloseServiceHandle", "ptr", scm)
            return ok
        }
        Catch
        {
            DllCall("advapi32\CloseServiceHandle", "ptr", scm)
            return false
        }
    }
    
    ; ── Получение ImagePath из реестра ────────────────────────────────────────
    static GetImagePath(name)
    {
        Try
        {
            key := RegOpenKey("HKLM\SYSTEM\CurrentControlSet\Services\" . name)
            if (key)
            {
                val := RegReadKey(key, "ImagePath")
                RegCloseKey(key)
                return val
            }
        }
        Catch
            return ""
        return ""
    }
    
    ; ── Private helpers ───────────────────────────────────────────────────────
    static SetRecoveryPolicy(svc)
    {
        ; SC_ACTION structure: 8 bytes (4 type + 4 delay)
        actionsSize := 24  ; 3 actions * 8 bytes
        actions := Buffer(actionsSize, 0)
        
        ; Action 1: Restart after 5s
        NumPut("uint", 1, actions, 0)   ; SC_ACTION_RESTART
        NumPut("uint", 5000, actions, 4)
        
        ; Action 2: Restart after 30s
        NumPut("uint", 1, actions, 8)
        NumPut("uint", 30000, actions, 12)
        
        ; Action 3: Restart after 60s
        NumPut("uint", 1, actions, 16)
        NumPut("uint", 60000, actions, 20)
        
        ; SERVICE_FAILURE_ACTIONS structure
        faSize := 4 + A_PtrSize*2 + 4 + A_PtrSize  ; ResetPeriod + RebootMsg + Command + ActionsCount + Actions
        fa := Buffer(faSize, 0)
        NumPut("uint", 86400, fa, 0)     ; ResetPeriod (1 day)
        NumPut("uint", 3, fa, 8 + A_PtrSize*2)  ; ActionsCount
        NumPut("ptr", actions.Ptr, fa, 12 + A_PtrSize*2)  ; Actions pointer
        
        ok := DllCall("advapi32\ChangeServiceConfig2W", "ptr", svc, "uint", this.SERVICE_CONFIG_FAILURE_ACTIONS, "ptr", fa.Ptr)
        if (ok)
            this.Log("Recovery policy установлена")
        else
            this.Log("SetRecoveryPolicy failed: " . A_LastError)
    }
    
    static WaitForState(svc, targetState, timeoutMs)
    {
        startTime := A_TickCount
        while ((A_TickCount - startTime) < timeoutMs)
        {
            st := Buffer(28, 0)
            if (DllCall("advapi32\QueryServiceStatus", "ptr", svc, "ptr", st.Ptr))
            {
                currentState := NumGet(st, 4, "uint")
                if (currentState = targetState)
                    return true
            }
            Sleep(500)
        }
        return false
    }
    
    static Log(msg)
    {
        ; Простое логирование в файл
        logFile := A_ScriptDir . "\logs\service_" . A_Now . ".log"
        Try
            FileAppend(A_Now . " - " . msg . "`n", logFile, "UTF-8")
        Catch
            return
    }
}

; Обёртки для совместимости со старым кодом
WinService_Install(name, displayName, description, binPath, autoStart:=true) => WinService.Install(name, displayName, description, binPath, autoStart)
WinService_Remove(name) => WinService.Remove(name)
WinService_GetState(name) => WinService.GetState(name)
WinService_Start(name) => WinService.Start(name)
WinService_Stop(name) => WinService.Stop(name)
WinService_GetImagePath(name) => WinService.GetImagePath(name)
