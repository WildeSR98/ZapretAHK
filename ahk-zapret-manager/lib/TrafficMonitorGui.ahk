; ============================================================================
; TrafficMonitorGui.ahk — Просмотр результатов тестов стратегий
; Читает файлы из utils/test results/
; ============================================================================
#Requires AutoHotkey v2.0

class TrafficMonitorGui
{
    static Show(utilsDir)
    {
        resultsDir := utilsDir . "\test results"

        dlg := Gui("+Resize", "Результаты тестов — Zapret Manager")
        dlg.MarginX := 10
        dlg.MarginY := 10

        dlg.Add("Text",, "Сохранённые результаты тестирования стратегий:")
        lv := dlg.Add("ListView", "x10 y30 w680 h220 Grid -Multi", ["Файл", "Дата", "Размер"])
        lv.ModifyCol(1, 380)
        lv.ModifyCol(2, 180)
        lv.ModifyCol(3, 100)

        edContent := dlg.Add("Edit", "x10 y260 w680 h200 ReadOnly +Multi +VScroll")

        dlg.Add("Text", "x10 y470 w680 h2 +0x10")
        btnOpen    := dlg.Add("Button", "x10 y480 w150 h28", "Открыть папку")
        btnClean   := dlg.Add("Button", "x172 y480 w160 h28", "Удалить старые (>5)")
        btnRefresh := dlg.Add("Button", "x344 y480 w100 h28", "Обновить")
        btnClose   := dlg.Add("Button", "x600 y480 w90 h28", "Закрыть")

        dlg.OnEvent("Close", (*) => dlg.Destroy())
        btnClose.OnEvent("Click", (*) => dlg.Destroy())
        btnOpen.OnEvent("Click", (*) => _OpenFolder())
        btnClean.OnEvent("Click", (*) => _Cleanup())
        btnRefresh.OnEvent("Click", (*) => _LoadFiles())
        ; Use fat-arrow wrapper — nested functions aren't directly valid callbacks
        ; in AHK v2 class method scope without explicit reference
        lv.OnEvent("ItemSelect", (ctrl, row) => _OnSelect(ctrl, row))

        _LoadFiles()
        dlg.Show("w700 h520")

        _LoadFiles() {
            lv.Delete()
            edContent.Value := ""
            if !DirExist(resultsDir)
                return

            files := []
            Loop Files, resultsDir . "\*.txt"
                files.Push({path: A_LoopFilePath, name: A_LoopFileName, modified: A_LoopFileTimeModified, size: A_LoopFileSize})

            ; Сортировка: новые первые
            Loop files.Length - 1
            {
                Loop files.Length - A_Index
                {
                    i := A_Index
                    if (files[i].modified < files[i + 1].modified)
                    {
                        tmp := files[i]
                        files[i] := files[i + 1]
                        files[i + 1] := tmp
                    }
                }
            }

            for f in files
            {
                dateStr := SubStr(f.modified, 1, 4) . "-" . SubStr(f.modified, 5, 2) . "-" . SubStr(f.modified, 7, 2)
                        . " " . SubStr(f.modified, 9, 2) . ":" . SubStr(f.modified, 11, 2)
                sizeStr := Format("{:.1f} KB", f.size / 1024)
                lv.Add("", f.name, dateStr, sizeStr)
            }
        }

        _OnSelect(ctrl, row) {
            if (row = 0)
                return
            fname := lv.GetText(row, 1)
            fpath := resultsDir . "\" . fname
            Try
            {
                content := FileRead(fpath, "UTF-8")
                edContent.Value := content
            }
            Catch as _e
            {
                edContent.Value := "Ошибка чтения файла"
            }
        }

        _OpenFolder() {
            DirCreate(resultsDir)
            Run("explorer.exe `"" . resultsDir . "`"")
        }

        _Cleanup() {
            if !DirExist(resultsDir)
                return
            files := []
            Loop Files, resultsDir . "\*.txt"
                files.Push({path: A_LoopFilePath, modified: A_LoopFileTimeModified})

            Loop files.Length - 1
            {
                Loop files.Length - A_Index
                {
                    i := A_Index
                    if (files[i].modified < files[i + 1].modified)
                    {
                        tmp := files[i]
                        files[i] := files[i + 1]
                        files[i + 1] := tmp
                    }
                }
            }

            deleted := 0
            Loop files.Length
            {
                if (A_Index > 5)
                {
                    FileDelete(files[A_Index].path)
                    deleted++
                }
            }
            MsgBox("Удалено файлов: " . deleted, "Очистка", 64)
            _LoadFiles()
        }
    }
}
