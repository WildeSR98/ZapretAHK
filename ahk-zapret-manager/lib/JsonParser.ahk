; ============================================================================
; JsonParser.ahk — Рекурсивный JSON парсер / сериализатор (pure AHK v2)
; Поддержка: объекты (Map), массивы (Array), строки, числа, bool, null
; ============================================================================
#Requires AutoHotkey v2.0

class JsonParser
{
    ; ── Публичный API ─────────────────────────────────────────────────────────

    ; Парсинг JSON-строки → Map/Array/primitive
    static Parse(jsonStr)
    {
        p := JsonParser._Parser(Trim(jsonStr))
        return p.ParseValue()
    }

    ; Парсинг из файла
    static ParseFile(path)
    {
        if !FileExist(path)
            return Map()
        Try
        {
            content := FileRead(path, "UTF-8")
            return JsonParser.Parse(content)
        }
        Catch
            return Map()
    }

    ; Сериализация Map/Array/primitive → JSON строка
    static Stringify(val, indent := 2, _level := 0)
    {
        pad  := indent > 0 ? "`n" . _Repeat(" ", indent * _level) : ""
        pad1 := indent > 0 ? "`n" . _Repeat(" ", indent * (_level + 1)) : ""
        sep  := indent > 0 ? " " : ""

        if val is Map
        {
            if (val.Count = 0)
                return "{}"
            parts := []
            for k, v in val
                parts.Push(Chr(34) . k . Chr(34) . ":" . sep . JsonParser.Stringify(v, indent, _level + 1))
            return "{" . pad1 . _Join(parts, "," . pad1) . pad . "}"
        }
        else if val is Array
        {
            if (val.Length = 0)
                return "[]"
            parts := []
            for v in val
                parts.Push(JsonParser.Stringify(v, indent, _level + 1))
            return "[" . pad1 . _Join(parts, "," . pad1) . pad . "]"
        }
        else if (val = true)
            return "true"
        else if (val = false)
            return "false"
        else if (val = "")
            return "null"
        else if IsNumber(val)
            return val
        else
        {
            ; Escape string
            s := StrReplace(val, "\",  "\\")
            s := StrReplace(s,   Chr(34), "\" . Chr(34))
            s := StrReplace(s,   "`n",  "\n")
            s := StrReplace(s,   "`r",  "\r")
            s := StrReplace(s,   "`t",  "\t")
            return Chr(34) . s . Chr(34)
        }

        _Repeat(str, n) {
            r := ""
            Loop n
                r .= str
            return r
        }

        _Join(arr, sep) {
            r := ""
            for i, v in arr
                r .= (i > 1 ? sep : "") . v
            return r
        }
    }

    ; ── Вспомогательный парсер-класс ──────────────────────────────────────────
    class _Parser
    {
        __New(str)
        {
            this.str := str
            this.pos := 1
            this.len := StrLen(str)
        }

        ; Текущий символ
        Cur()
        {
            if (this.pos > this.len)
                return ""
            return SubStr(this.str, this.pos, 1)
        }

        ; Сдвинуться вперёд
        Advance()
        {
            this.pos++
        }

        ; Пропустить пробелы/перевод строки
        SkipWS()
        {
            while (this.pos <= this.len)
            {
                c := SubStr(this.str, this.pos, 1)
                if (c = " " || c = "`t" || c = "`n" || c = "`r")
                    this.pos++
                else
                    break
            }
        }

        ; Парсинг произвольного значения
        ParseValue()
        {
            this.SkipWS()
            c := this.Cur()

            if (c = "{")
                return this.ParseObject()
            if (c = "[")
                return this.ParseArray()
            if (c = Chr(34))
                return this.ParseString()
            if (c = "t")
                return this.ParseLiteral("true", true)
            if (c = "f")
                return this.ParseLiteral("false", false)
            if (c = "n")
                return this.ParseLiteral("null", "")
            if (c = "-" || (c >= "0" && c <= "9"))
                return this.ParseNumber()

            ; Ошибка — вернуть пустую строку
            return ""
        }

        ; Парсинг объекта { "key": value, ... } → Map
        ParseObject()
        {
            this.Advance()  ; пропустить {
            result := Map()

            this.SkipWS()
            if (this.Cur() = "}")
            {
                this.Advance()
                return result
            }

            Loop
            {
                this.SkipWS()
                key := this.ParseString()
                this.SkipWS()
                if (this.Cur() = ":")
                    this.Advance()
                this.SkipWS()
                val := this.ParseValue()
                result[key] := val

                this.SkipWS()
                c := this.Cur()
                if (c = ",")
                {
                    this.Advance()
                    continue
                }
                if (c = "}")
                {
                    this.Advance()
                    break
                }
                break  ; Ошибка разбора
            }
            return result
        }

        ; Парсинг массива [ v1, v2, ... ] → Array
        ParseArray()
        {
            this.Advance()  ; пропустить [
            result := []

            this.SkipWS()
            if (this.Cur() = "]")
            {
                this.Advance()
                return result
            }

            Loop
            {
                this.SkipWS()
                val := this.ParseValue()
                result.Push(val)

                this.SkipWS()
                c := this.Cur()
                if (c = ",")
                {
                    this.Advance()
                    continue
                }
                if (c = "]")
                {
                    this.Advance()
                    break
                }
                break  ; Ошибка разбора
            }
            return result
        }

        ; Парсинг строки "..." (с поддержкой escape)
        ParseString()
        {
            if (this.Cur() != Chr(34))
                return ""
            this.Advance()  ; пропустить открывающую "

            result := ""
            while (this.pos <= this.len)
            {
                c := SubStr(this.str, this.pos, 1)
                if (c = "\")
                {
                    this.pos++
                    esc := SubStr(this.str, this.pos, 1)
                    switch esc
                    {
                        case Chr(34): result .= Chr(34)
                        case "\": result .= "\"
                        case "/": result .= "/"
                        case "n": result .= "`n"
                        case "r": result .= "`r"
                        case "t": result .= "`t"
                        case "b": result .= "`b"
                        case "f": result .= "`f"
                        case "u":
                            ; \uXXXX → Unicode
                            hex := SubStr(this.str, this.pos + 1, 4)
                            this.pos += 4
                            result .= Chr(Integer("0x" . hex))
                        default: result .= esc
                    }
                    this.pos++
                }
                else if (c = Chr(34))
                {
                    this.pos++  ; пропустить закрывающую "
                    break
                }
                else
                {
                    result .= c
                    this.pos++
                }
            }
            return result
        }

        ; Парсинг числа
        ParseNumber()
        {
            start := this.pos
            if (this.Cur() = "-")
                this.pos++

            while (this.pos <= this.len)
            {
                c := SubStr(this.str, this.pos, 1)
                if (c >= "0" && c <= "9") || c = "." || c = "e" || c = "E" || c = "+" || c = "-"
                    this.pos++
                else
                    break
            }

            numStr := SubStr(this.str, start, this.pos - start)
            return IsInteger(numStr) ? Integer(numStr) : Float(numStr)
        }

        ; Парсинг литерала (true/false/null)
        ParseLiteral(literal, retVal)
        {
            this.pos += StrLen(literal)
            return retVal
        }
    }
}
