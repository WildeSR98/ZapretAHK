# 🚀 zapret-manager v3.0.0

> Крупный рефакторинг архитектуры, новый UI на Spectre.Console и исправление ключевых багов.

---

## 📦 Файлы релиза

| Файл | Описание | Требования |
|------|----------|------------|
| `zapret-manager-v3.0.0-standalone.zip` | Самодостаточный exe (~159 МБ) | Ничего не нужно |
| `zapret-manager-v3.0.0-net8-required.zip` | Лёгкий exe (~2 МБ) | [.NET 8 Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) |

---

## ✨ Новое в v3.0.0

### 🏗️ Архитектура — рефакторинг `Program.cs`
- `Program.cs` разбит на отдельные классы меню: `ServiceMenu`, `SettingsMenu`, `UpdateMenu`, `DiagnosticsMenu`, `BackupMenu`, `MonitorMenu`, `TgProxyMenu`, **`AdvancedMenu`** (новый)
- `AdvancedMenu` содержит: Watchdog (п.17), Speed-тест (п.18), Редактор стратегий (п.19), Определение провайдера (п.20), Управление доменами (п.21), Выбор NIC (п.22), Экспорт/импорт настроек (п.23)
- `Program.cs` сокращён с ~2000 до ~1566 строк

### 🎨 UI — Spectre.Console
- `ConsoleMenu.PickFromList<T>` — интерактивный `SelectionPrompt` с fallback для неинтерактивных терминалов
- `ConsoleMenu.PickMultiple<T>` — `MultiSelectionPrompt` с fallback
- `ConsoleMenu.Prompt(validator, validationError)` — перегрузка с встроенной валидацией ввода
- Выбор стратегии при установке службы теперь через стрелки/Enter

### 🔒 Безопасность
- **`HttpService.ValidateUrl()`** — whitelist разрешённых хостов (GitHub, Cloudflare CDN). Блокирует запросы к произвольным серверам
- **`HashVerifier.cs`** — SHA256 верификация загружаемых архивов (уже включена)

### 🐛 Исправления багов
- **`Ping: Timeout` везде** — критический баг: `System.Net.NetworkInformation.Ping` использовался одним экземпляром для параллельных запросов (не потокобезопасен). Теперь каждый запрос получает свой экземпляр
- **Crash `InvalidOperationException: Could not find color or style 'выкл'`** — русскоязычные строки в меню не экранировались через `Markup.Escape()` перед передачей в Spectre
- **Crash `Unescaped ']' token`** — конструкция `[{переменная}]` в Spectre-разметке. Исправлено на `[[{...}]]` (буквальные скобки)
- **Crash `Unbalanced markup stack`** — в `BackupMenu` пункты `[1]`, `[2]` воспринимались как теги стилей. Исправлено на `[[1]]`, `[[2]]`
- **Прогресс-бар** — `[{bar}]` (символы `█░`) также вызывал ошибку. Исправлено через `[[{Markup.Escape(bar)}]]`

### 🧹 Качество кода
- **Пустые `catch {}`** → `catch (Exception ex) { Logger.Error(...) }` во всём проекте (~58 блоков в 23 файлах)
- **`Thread.Sleep`** → `await Task.Delay` в async-методах. `CheckServiceHealth()` и `DiscordCacheCleaner.Clean()` стали `async`
- **`using ZapretManager.Core`** добавлен во все файлы где использовался `Logger`

### ✅ Тесты
- 45 unit-тестов — все проходят (`ZapretManager.Tests`, xUnit + FluentAssertions)
- GitHub Actions CI — автосборка и тесты при push/PR

---

## ⬆️ Как обновить

1. Скачайте нужный архив
2. Закройте старый `zapret-manager.exe`
3. Распакуйте в папку с существующей установкой **с заменой**
4. Запустите `zapret-manager.exe`

> [!WARNING]
> Конфигурация, стратегии и листы (`config.json`, `strategies/`, `lists/`) **не затрагиваются** при обновлении.

---

## 🔧 Системные требования

- Windows 10/11 (x64)
- Права администратора (для управления службой)
- .NET 8 Runtime — только для варианта `net8-required`
