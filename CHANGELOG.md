# Changelog

Все заметные изменения в проекте задокументированы здесь.  
Формат основан на [Keep a Changelog](https://keepachangelog.com/ru/1.1.0/).  
Версионирование следует [Semantic Versioning](https://semver.org/lang/ru/).

---

## [Unreleased]

### Added
- **Unit-тесты** (`ZapretManager.Tests`) — покрытие `ListMerger`, `AppConfig`, `BackupManager`, `UpdateChecker`, `HashVerifier` (xUnit + FluentAssertions + coverlet)
- **GitHub Actions CI** — автоматическая сборка и запуск тестов при каждом push/PR (`.github/workflows/ci.yml`)
- **`.editorconfig`** — единый стиль форматирования кода (C#, JSON, YAML, Markdown)
- **`HashVerifier.cs`** — SHA256 верификация загружаемых архивов (всегда включена)
- **`HttpDomainGuard.cs`** — проверка URL по whitelist перед каждым HTTP-запросом
- **`Spectre.Console`** — добавлена зависимость для современного консольного UI

### Changed
- `Thread.Sleep(1500)` → `await Task.Delay(1500)` в `MenuTgProxy` (Program.cs L991)
- Пустые блоки `catch { }` заменены на логирующие `catch (Exception ex)` во всём проекте
- `TgProxyManager.Stop()` — теперь логирует ошибку завершения процесса вместо молчаливого игнора
- `Logger.Write()`, `Logger.Dispose()`, `Logger.RotateLogs()` — все ошибки выводятся в stderr

### Security
- Добавлен `HttpDomainGuard` — все HTTP-запросы проходят валидацию по whitelist (GitHub + Cloudflare)
- SHA256 верификация архивов — `hash_verification` флаг убран, проверка теперь обязательная

---

## [2.5.0] — 2026-03-xx

### Added
- **Watchdog** (п.17) — фоновый мониторинг + авторотация стратегий при сбоях
- **Speed-тест** (п.18) — измерение скорости до/после обхода через Cloudflare  
- **Редактор стратегий** (п.19) — создание и редактирование `.bat` файлов стратегий
- **Определение провайдера ISP** (п.20) — автоопределение ISP + рекомендуемые стратегии
- **Управление доменами** (п.21) — Whitelist/Blacklist доменов через меню
- **Сетевой адаптер NIC** (п.22) — выбор сетевого адаптера для winws  
- **Экспорт/Импорт настроек** (п.23) — полный бэкап настроек в ZIP
- **Toast уведомления** — уведомления Windows при появлении обновлений
- **Фоновый UpdateChecker** — проверка обновлений в фоне (авторотация, режимы auto/manual)
- **`--check-updates`** аргумент — тихая проверка через Task Scheduler
- **Автозапуск трея** через Task Scheduler (Настройки)
- **Профили** (п.15) — сохранение/загрузка комбинаций настроек
- **Мониторинг трафика** (п.16) — live rx/tx + информация о winws

### Changed
- Меню расширено до 23 пунктов
- `ConsoleMenu` обновлён: прогресс-бар, спиннер с unicode-символами

---

## [2.3.0] — предыдущий релиз

- Начальная публичная версия
- Базовые функции: установка, удаление, переустановка, диагностика, тест стратегий
- Системный трей с иконкой статуса службы
- Бэкап/Восстановление конфигурации
