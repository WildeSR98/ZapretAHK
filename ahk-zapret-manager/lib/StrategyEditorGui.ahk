; ============================================================================
; StrategyEditorGui.ahk — Выбор и применение стратегий winws
; По образцу StrategyEditor.cs
; ============================================================================
#Requires AutoHotkey v2.0

class StrategyEditorGui
{
    static Show(strategiesDir, rootDir)
    {
        batFiles := []
        Loop Files, strategiesDir . "\general*.bat"
            batFiles.Push(A_LoopFileName)
        Loop Files, strategiesDir . "\custom*.bat"
            batFiles.Push(A_LoopFileName)

        if (batFiles.Length = 0)
        {
            MsgBox("Нет .bat файлов в папке strategies/`n`nПапка: " . strategiesDir, "Стратегии", 64)
            return
        }

        ; Определить текущую стратегию из реестра
        currentStrat := StrategyEditorGui._ReadCurrentStrategy()

        dlg := Gui("+AlwaysOnTop", "Стратегии — Zapret Manager")
        dlg.MarginX := 10
        dlg.MarginY := 10

        dlg.Add("Text",, "Выберите стратегию для применения:")
        lv := dlg.Add("ListView", "x10 y30 w500 h300 Grid -Multi", ["Файл", "Статус"])
        lv.ModifyCol(1, 380)
        lv.ModifyCol(2, 110)

        for fn in batFiles
        {
            isActive := InStr(currentStrat, StrReplace(fn, ".bat", "")) ? "✔ Активна" : ""
            lv.Add("", fn, isActive)
        }

        dlg.Add("Text",, "")
        btnApply  := dlg.Add("Button", "x10 y342 w140 h30 Default", "✔ Применить")
        btnEdit   := dlg.Add("Button", "x160 y342 w140 h30", "✎ Открыть в блокноте")
        btnCopy   := dlg.Add("Button", "x310 y342 w110 h30", "Копировать")
        btnClose  := dlg.Add("Button", "x430 y342 w80 h30", "Закрыть")

        dlg.OnEvent("Close", (*) => dlg.Destroy())
        btnClose.OnEvent("Click", (*) => dlg.Destroy())

        btnApply.OnEvent("Click", _Apply)
        btnEdit.OnEvent("Click", _Edit)
        btnCopy.OnEvent("Click", _Copy)

        dlg.Show("w524 w524")
        dlg.Show()

        _GetSelected() {
            row := lv.GetNext()
            if (row = 0)
                return ""
            return lv.GetText(row, 1)
        }

        _Apply(*) {
            fn := _GetSelected()
            if (fn = "")
                return MsgBox("Выберите стратегию в списке", "Zapret Manager", 48)
            batPath := strategiesDir . "\" . fn

            ; Перезапустить службу zapret с новой стратегией
            if MsgBox("Применить стратегию `"" . fn . "`"?`nСлужба будет перезапущена.", "Подтверждение", 4) != "Yes"
                return

            WinService_Stop("zapret")
            Sleep(1000)

            ; Обновить ImagePath в реестре
            StrategyEditorGui._SetStrategy(batPath, rootDir)
            WinService_Start("zapret")
            Sleep(2000)

            newState := WinService_GetState("zapret")
            status   := (newState = "Running") ? "✔ Запущена" : "✘ " . newState
            MsgBox("Стратегия применена.`nСтатус службы: " . status, "Zapret Manager", 64)
            Logger_Info("Стратегия изменена: " . fn)
            dlg.Destroy()
        }

        _Edit(*) {
            fn := _GetSelected()
            if (fn = "")
                return MsgBox("Выберите файл", "Zapret Manager", 48)
            Run("notepad.exe `"" . strategiesDir . "\" . fn . "`"")
        }

        _Copy(*) {
            fn := _GetSelected()
            if (fn = "")
                return MsgBox("Выберите файл для копирования", "Zapret Manager", 48)

            newName := InputBox("Имя новой стратегии (без .bat):", "Копировать стратегию",, StrReplace(fn, ".bat", "") . "_copy").Value
            if (newName = "")
                return

            newPath := strategiesDir . "\" . StrReplace(newName, ".bat", "") . ".bat"
            FileCopy(strategiesDir . "\" . fn, newPath)
            Logger_Info("Стратегия скопирована: " . newPath)
            MsgBox("Создан файл: " . StrReplace(newName, ".bat", "") . ".bat", "Zapret Manager", 64)
            dlg.Destroy()
            StrategyEditorGui.Show(strategiesDir, rootDir)
        }
    }

    ; Прочитать текущую стратегию из реестра
    static _ReadCurrentStrategy()
    {
        Try
        {
            imgPath := RegRead("HKLM\SYSTEM\CurrentControlSet\Services\zapret", "ImagePath")
            return imgPath
        }
        Catch
            return ""
    }

    ; Записать новую стратегию — обновить ImagePath
    static _SetStrategy(batPath, rootDir)
    {
        Try
        {
            ; ImagePath для службы: cmd /c "batfile"
            newImage := "cmd /c `"" . batPath . "`""
            RegWrite(newImage, "REG_EXPAND_SZ", "HKLM\SYSTEM\CurrentControlSet\Services\zapret", "ImagePath")
        }
        Catch as e
        {
            Logger_Error("StrategyEditor: ошибка записи реестра — " . e.Message)
        }
    }
}
