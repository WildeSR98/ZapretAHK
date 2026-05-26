; ============================================================================
; NicSelectorGui.ahk — Выбор сетевого адаптера для winws --iface
; По образцу NicSelector.cs (WMI via COM)
; ============================================================================
#Requires AutoHotkey v2.0

class NicSelectorGui
{
    static CONFIG_FILE := "selected_nic.json"

    static Show(utilsDir)
    {
        nics := NicSelectorGui._GetNics()
        saved := NicSelectorGui._LoadSelected(utilsDir)

        dlg := Gui("+AlwaysOnTop", "Сетевой адаптер — Zapret Manager")
        dlg.MarginX := 14
        dlg.MarginY := 12

        dlg.Add("Text",, "Выберите сетевой адаптер для winws --iface:")
        dlg.Add("Text",, "(Авто — winws выберет интерфейс сам)")
        dlg.Add("Text",, "")

        lv := dlg.Add("ListView", "x10 y70 w500 h260 Grid -Multi", ["Адаптер", "Тип", "IP-адрес", "Статус"])
        lv.ModifyCol(1, 210)
        lv.ModifyCol(2, 80)
        lv.ModifyCol(3, 120)
        lv.ModifyCol(4, 70)

        currentRow := 0
        for i, nic in nics
        {
            row := lv.Add("", nic.name, nic.type, nic.ip, nic.status)
            if (saved != "" && saved = nic.name)
            {
                lv.Modify(row, "Select Focus")
                currentRow := row
            }
        }

        dlg.Add("Text",, "")
        dlg.Add("Text", "x10 y342 w200", "Текущий выбор:")
        lblCurrent := dlg.Add("Text", "x220 y342 w290 cBlue", (saved = "" ? "(Авто)" : saved))

        dlg.Add("Text", "x10 y360 w500 h1 +0xEtched")
        btnAuto  := dlg.Add("Button", "x10 y372 w100 h30", "Авто (сброс)")
        btnSave  := dlg.Add("Button", "x122 y372 w120 h30 Default", "Сохранить")
        btnClose := dlg.Add("Button", "x402 y372 w108 h30", "Закрыть")

        dlg.OnEvent("Close", (*) => dlg.Destroy())
        btnClose.OnEvent("Click", (*) => dlg.Destroy())

        btnAuto.OnEvent("Click", _SetAuto)
        btnSave.OnEvent("Click", _Save)

        dlg.Show("w524 h412")

        _Save(*) {
            row := lv.GetNext()
            if (row = 0)
                return MsgBox("Выберите адаптер в списке", "Zapret Manager", 48)
            name := lv.GetText(row, 1)
            NicSelectorGui._SaveSelected(utilsDir, name)
            lblCurrent.Text := name
            Logger_Info("NIC выбран: " . name)
            MsgBox("Адаптер сохранён: " . name . "`nПри следующей установке службы будет добавлен параметр --iface", "Zapret Manager", 64)
        }

        _SetAuto(*) {
            NicSelectorGui._ClearSelected(utilsDir)
            lblCurrent.Text := "(Авто)"
            lv.Modify(0, "-Select")
            Logger_Info("NIC: сброшен в авто")
            MsgBox("Адаптер: Авто (без привязки)", "Zapret Manager", 64)
        }
    }

    ; ── Private ───────────────────────────────────────────────────────────────

    static _GetNics()
    {
        result := []
        Try
        {
            wmi := ComObjGet("winmgmts:")
            query := wmi.ExecQuery("SELECT Name, AdapterTypeId, MACAddress, NetConnectionStatus FROM Win32_NetworkAdapter WHERE NetEnabled=TRUE")
            for nic in query
            {
                status := nic.NetConnectionStatus = 2 ? "Подключён" : "Отключён"
                type   := nic.AdapterTypeId = 0 ? "Ethernet" : (nic.AdapterTypeId = 9 ? "WiFi" : "Другой")
                ip     := NicSelectorGui._GetIpForMac(wmi, nic.MACAddress)
                result.Push({name: nic.Name, type: type, ip: ip, status: status})
            }
        }
        Catch as _e {}
        return result
    }

    static _GetIpForMac(wmi, mac)
    {
        Try
        {
            q := wmi.ExecQuery("SELECT IPAddress FROM Win32_NetworkAdapterConfiguration WHERE MACAddress='" . mac . "'")
            for cfg in q
            {
                if cfg.IPAddress
                {
                    ips := cfg.IPAddress
                    for ip in ips
                        return ip
                }
            }
        }
        Catch as _e {}
        return "—"
    }

    static _LoadSelected(utilsDir)
    {
        path := utilsDir . "\" . NicSelectorGui.CONFIG_FILE
        if !FileExist(path)
            return ""
        Try
        {
            data := JsonParser.ParseFile(path)
            if (data is Map) && data.Has("name")
                return data["name"]
        }
        Catch as _e {}
        return ""
    }

    static _SaveSelected(utilsDir, name)
    {
        DirCreate(utilsDir)
        path := utilsDir . "\" . NicSelectorGui.CONFIG_FILE
        s := Map()
        s["name"] := name
        Try
        {
            FileDelete(path)
            FileAppend(JsonParser.Stringify(s), path, "UTF-8")
        }
        Catch as _e {}
    }

    static _ClearSelected(utilsDir)
    {
        path := utilsDir . "\" . NicSelectorGui.CONFIG_FILE
        if FileExist(path)
            FileDelete(path)
    }
}
