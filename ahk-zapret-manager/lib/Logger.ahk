; ============================================================================
; Logger.ahk - Система логирования
; ============================================================================
#Requires AutoHotkey v2.0

class Logger
{
    static LogFile := ""
    static Verbose := false
    static MaxAgeDays := 14
    
    static Init(rootDir, verbose:=false, maxAge:=14)
    {
        this.LogFile := rootDir . "\logs\zapret_" . FormatTime(, "yyyy-MM-dd_HH-mm-ss") . ".log"
        this.Verbose := verbose
        this.MaxAgeDays := maxAge
        DirCreate(rootDir . "\logs")
    }
    
    static Info(msg)
    {
        this.Write("INFO", msg)
    }
    
    static Warn(msg)
    {
        this.Write("WARN", msg)
    }
    
    static Error(msg)
    {
        this.Write("ERROR", msg)
    }
    
    static Ok(msg)
    {
        this.Write("OK", msg)
    }
    
    static Debug(msg)
    {
        if (this.Verbose)
            this.Write("DEBUG", msg)
    }
    
    static Write(level, msg)
    {
        Try
        {
            line := FormatTime(, "yyyy-MM-dd HH:mm:ss") . " [" . level . "] " . msg
            FileAppend(line . "`n", this.LogFile, "UTF-8")
        }
        Catch
            return
    }
}

Logger_Init() => Logger.Init(A_ScriptDir . "\..", false, 14)
Logger_Info(msg) => Logger.Info(msg)
Logger_Warn(msg) => Logger.Warn(msg)
Logger_Error(msg) => Logger.Error(msg)
Logger_Ok(msg) => Logger.Ok(msg)
