; ============================================================================
; BackupManager.ahk — Резервное копирование через PowerShell Compress-Archive
; По образцу BackupManager.cs
; ============================================================================
#Requires AutoHotkey v2.0

class BackupManager
{
    static BACKUP_DIR    := "backups"
    static BACKUP_PREFIX := "zapret_backup_"
    static INCLUDE_DIRS  := ["bin", "lists", "strategies", "utils"]
    static INCLUDE_FILES := ["config.json"]

    ; Создать ZIP-бэкап. Возвращает путь к архиву или ""
    static CreateBackup(rootDir, keepCount := 5)
    {
        backupDir := rootDir . "\" . BackupManager.BACKUP_DIR
        DirCreate(backupDir)

        timestamp := FormatTime(, "yyyy-MM-dd_HH-mm-ss")
        zipName   := BackupManager.BACKUP_PREFIX . timestamp . ".zip"
        zipPath   := backupDir . "\" . zipName

        ; Собираем список файлов для архива через PowerShell
        ; PowerShell Compress-Archive принимает массив путей
        pathList := []

        for dirName in BackupManager.INCLUDE_DIRS
        {
            dirPath := rootDir . "\" . dirName
            if DirExist(dirPath)
                pathList.Push(dirPath)
        }

        for fileName in BackupManager.INCLUDE_FILES
        {
            fp := rootDir . "\" . fileName
            if FileExist(fp)
                pathList.Push(fp)
        }

        if (pathList.Length = 0)
        {
            Logger_Warn("BackupManager: нет файлов для бэкапа")
            return ""
        }

        ; Формируем PowerShell команду
        psPathArray := ""
        for i, p in pathList
            psPathArray .= (i > 1 ? "," : "") . "'" . StrReplace(p, "'", "''") . "'"

        psCmd := "Compress-Archive -Path @(" . psPathArray . ") -DestinationPath '" . StrReplace(zipPath, "'", "''") . "' -Force"

        exitCode := RunWait("powershell.exe -NoProfile -NonInteractive -Command `"" . psCmd . "`"",, "Hide")

        if (exitCode != 0 || !FileExist(zipPath))
        {
            Logger_Error("BackupManager: Compress-Archive завершился с кодом " . exitCode)
            return ""
        }

        fileSize := FileGetSize(zipPath, "K")
        Logger_Info("Бэкап создан: " . zipName . " (" . fileSize . " KB)")

        ; Ротация: удалить старые
        BackupManager.RotateBackups(backupDir, keepCount)

        return zipPath
    }

    ; Восстановить из ZIP-архива
    static RestoreBackup(rootDir, zipPath)
    {
        if !FileExist(zipPath)
        {
            Logger_Error("BackupManager: файл не найден: " . zipPath)
            return false
        }

        psCmd := "Expand-Archive -Path '" . StrReplace(zipPath, "'", "''") . "' -DestinationPath '" . StrReplace(rootDir, "'", "''") . "' -Force"

        exitCode := RunWait("powershell.exe -NoProfile -NonInteractive -Command `"" . psCmd . "`"",, "Hide")

        if (exitCode = 0)
        {
            Logger_Info("Восстановлено из: " . zipPath)
            return true
        }

        Logger_Error("BackupManager: Expand-Archive ошибка, код " . exitCode)
        return false
    }

    ; Получить список существующих бэкапов (отсортированных по дате — новые первые)
    static ListBackups(rootDir)
    {
        backupDir := rootDir . "\" . BackupManager.BACKUP_DIR
        if !DirExist(backupDir)
            return []

        result := []
        Loop Files, backupDir . "\" . BackupManager.BACKUP_PREFIX . "*.zip"
            result.Push(A_LoopFilePath)

        ; Сортировка по имени (содержит timestamp → лексикографически = по дате)
        Loop result.Length - 1
        {
            swapped := false
            Loop result.Length - A_Index
            {
                i := A_Index
                if (result[i] < result[i + 1])
                {
                    tmp := result[i]
                    result[i] := result[i + 1]
                    result[i + 1] := tmp
                    swapped := true
                }
            }
            if !swapped
                break
        }

        return result
    }

    ; ── Private ───────────────────────────────────────────────────────────────
    static RotateBackups(backupDir, keepCount)
    {
        Try
        {
            files := []
            Loop Files, backupDir . "\" . BackupManager.BACKUP_PREFIX . "*.zip"
                files.Push(A_LoopFilePath)

            ; Сортировка: новые первые
            Loop files.Length - 1
            {
                Loop files.Length - A_Index
                {
                    i := A_Index
                    if (files[i] < files[i + 1])
                    {
                        tmp := files[i]
                        files[i] := files[i + 1]
                        files[i + 1] := tmp
                    }
                }
            }

            ; Удалить лишние
            Loop files.Length
            {
                if (A_Index > keepCount)
                {
                    Logger_Info("BackupManager: удалён старый бэкап: " . files[A_Index])
                    FileDelete(files[A_Index])
                }
            }
        }
        Catch as e
        {
            Logger_Warn("BackupManager: ротация — " . e.Message)
        }
    }
}
