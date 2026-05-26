; ============================================================================
; DomainManagerGui.ahk — Редактор пользовательских списков доменов/IP
; Управляет: list-general-user.txt, ipset-exclude-user.txt, list-exclude-user.txt
; ============================================================================
#Requires AutoHotkey v2.0

class DomainManagerGui
{
    static Show(listsDir)
    {
        files := Map()
        files["Домены (list-general-user)"]     := listsDir . "\list-general-user.txt"
        files["Исключения IP (ipset-exclude-user)"] := listsDir . "\ipset-exclude-user.txt"
        files["Исключения доменов (list-exclude-user)"] := listsDir . "\list-exclude-user.txt"

        tabNames := []
        tabFiles := []
        for label, path in files {
            tabNames.Push(label)
            tabFiles.Push(path)
        }

        dlg := Gui("+Resize", "Пользовательские списки — Zapret Manager")
        dlg.MarginX := 8
        dlg.MarginY := 8

        tab := dlg.Add("Tab3", "x0 y0 w680 h440", tabNames)

        editors := []
        for i, label in tabNames
        {
            tab.UseTab(i)
            ed := dlg.Add("Edit", "x8 y30 w660 h360 +Multi +WantReturn +VScroll", "")
            editors.Push(ed)
            ; Загрузить содержимое
            path := tabFiles[i]
            Try
            {
                if FileExist(path)
                    ed.Value := FileRead(path, "UTF-8")
            }
            Catch as _e
            {
            }
        }

        tab.UseTab()

        btnSave  := dlg.Add("Button", "x8 y452 w120 h28 Default", "Сохранить")
        btnOpen  := dlg.Add("Button", "x140 y452 w160 h28", "Открыть в Notepad")
        btnReset := dlg.Add("Button", "x312 y452 w120 h28", "Сбросить")
        btnClose := dlg.Add("Button", "x550 y452 w120 h28", "Закрыть")

        dlg.OnEvent("Close", (*) => dlg.Destroy())
        btnClose.OnEvent("Click", (*) => dlg.Destroy())

        btnSave.OnEvent("Click", _Save)
        btnOpen.OnEvent("Click", _OpenNotepad)
        btnReset.OnEvent("Click", _Reset)

        dlg.Show("w700 h492")

        _CurrentIdx() {
            ; Tab3 не предоставляет прямого свойства Value — используем ограниченный способ
            return tab.Value
        }

        _Save(*) {
            tabIdx := tab.Value
            if (tabIdx < 1 || tabIdx > tabFiles.Length)
                return
            path := tabFiles[tabIdx]
            content := editors[tabIdx].Value
            Try {
                DirCreate(listsDir)
                FileDelete(path)
                FileAppend(content, path, "UTF-8")
                Logger_Info("DomainManager: сохранён " . tabFiles[tabIdx])
                MsgBox("Файл сохранён", "Zapret Manager", 64)
            } Catch as e {
                MsgBox("Ошибка сохранения: " . e.Message, "Ошибка", 16)
            }
        }

        _OpenNotepad(*) {
            tabIdx := tab.Value
            if (tabIdx < 1 || tabIdx > tabFiles.Length)
                return
            path := tabFiles[tabIdx]
            if !FileExist(path)
                FileAppend("", path, "UTF-8")
            Run("notepad.exe `"" . path . "`"")
        }

        _Reset(*) {
            tabIdx := tab.Value
            if (tabIdx < 1 || tabIdx > tabFiles.Length)
                return
            if MsgBox("Сбросить содержимое вкладки?", "Подтверждение", 4) = "Yes"
                editors[tabIdx].Value := ""
        }
    }
}
