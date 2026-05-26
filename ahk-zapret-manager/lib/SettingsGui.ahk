; ============================================================================
; SettingsGui.ahk — Окно настроек
; Управляет: game_filter.enabled, check_updates.enabled, update_mode.txt
; ============================================================================
#Requires AutoHotkey v2.0

class SettingsGui
{
    static Show(utilsDir)
    {
        gfEnabled  := FileExist(utilsDir . "\game_filter.enabled")   ? 1 : 0
        updEnabled := FileExist(utilsDir . "\check_updates.enabled") ? 1 : 0
        updMode    := UpdateChecker.GetUpdateMode(A_ScriptDir)

        dlg := Gui("+AlwaysOnTop", "Настройки — Zapret Manager")
        dlg.MarginX := 16
        dlg.MarginY := 12

        dlg.Add("GroupBox", "x8 y8 w360 h160", "Параметры")

        cbGF  := dlg.Add("Checkbox", "x20 y34 w300 Checked" . gfEnabled,  "Включить игровой фильтр (TCP+UDP)")
        cbUpd := dlg.Add("Checkbox", "x20 y64 w300 Checked" . updEnabled, "Автоматическая проверка обновлений")

        dlg.Add("Text",  "x20 y100 w120 h22 +0x200", "Режим обновлений:")
        ddMode := dlg.Add("DropDownList", "x150 y98 w200", ["Вручную", "Автоматически"])
        ddMode.Value := (updMode = "auto") ? 2 : 1

        dlg.Add("Text",  "x8 y178 w360 h1 +0xEtched")

        btnSave   := dlg.Add("Button", "x20 y190 w100 h30 Default", "Сохранить")
        btnCancel := dlg.Add("Button", "x270 y190 w100 h30", "Отмена")

        btnSave.OnEvent("Click", _Save)
        btnCancel.OnEvent("Click", (*) => dlg.Destroy())
        dlg.OnEvent("Close", (*) => dlg.Destroy())

        dlg.Show("w380 h230")

        _Save(*) {
            ; Игровой фильтр
            gfPath := utilsDir . "\game_filter.enabled"
            if cbGF.Value {
                if !FileExist(gfPath)
                    FileAppend("1", gfPath, "UTF-8")
            } else {
                if FileExist(gfPath)
                    FileDelete(gfPath)
            }

            ; Автообновления
            updPath := utilsDir . "\check_updates.enabled"
            if cbUpd.Value {
                if !FileExist(updPath)
                    FileAppend("1", updPath, "UTF-8")
            } else {
                if FileExist(updPath)
                    FileDelete(updPath)
            }

            ; Режим обновлений
            mode := (ddMode.Value = 2) ? "auto" : "manual"
            UpdateChecker.SetUpdateMode(A_ScriptDir, mode)

            Logger_Info("Настройки сохранены")
            dlg.Destroy()
            MsgBox("Настройки сохранены", "Zapret Manager", 64)
        }
    }
}
