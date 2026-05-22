# Contributing to Zapret Manager

Спасибо за интерес к проекту! Ниже описано всё необходимое для участия в разработке.

---

## Требования для разработки

| Инструмент | Версия | Ссылка |
|-----------|--------|--------|
| .NET SDK | **8.0+** | [Скачать](https://dotnet.microsoft.com/download/dotnet/8.0) |
| Windows | **10 / 11** | — |
| Visual Studio | 2022+ (опционально) | — |
| JetBrains Rider | любая (опционально) | — |

---

## Быстрый старт

```bash
# 1. Клонировать репозиторий
git clone https://github.com/WildeSR98/12345.git
cd 12345

# 2. Восстановить зависимости
dotnet restore src/Zapret.sln

# 3. Собрать проект
dotnet build src/Zapret.sln -c Release

# 4. Запустить тесты
dotnet test src/ZapretManager.Tests/ZapretManager.Tests.csproj

# 5. Запустить менеджер (требуется запуск от администратора)
cd src/ZapretManager
dotnet run
```

---

## Структура проекта

```
12345/
├── .github/
│   └── workflows/ci.yml          # GitHub Actions CI
├── .editorconfig                  # Стиль кода
├── CHANGELOG.md                   # История релизов
├── CONTRIBUTING.md                # Этот файл
├── README.md                      # Документация
├── config.json                    # Конфигурация приложения
│
├── src/
│   ├── Zapret.sln
│   ├── ZapretManager/             # Основной проект
│   │   ├── Core/                  # AppConfig, Logger, HttpDomainGuard, AdminHelper
│   │   ├── UI/                    # ConsoleMenu, TrayManager, ToastNotifier
│   │   ├── Service/               # WinServiceManager, BackupManager, Watchdog, ...
│   │   ├── Diagnostics/           # StrategyTester, DpiChecker, FullDiagnostics, ...
│   │   ├── Lists/                 # ListMerger, ListDownloader, HostsUpdater
│   │   ├── Updates/               # UpdateChecker, GitHubUpdater, HashVerifier
│   │   └── Program.cs             # Точка входа + роутинг меню
│   │
│   └── ZapretManager.Tests/       # Unit-тесты (xUnit)
│       ├── Core/                  # AppConfigTests
│       ├── Lists/                 # ListMergerTests
│       ├── Service/               # BackupManagerTests
│       └── Updates/               # UpdateCheckerTests, HashVerifierTests
│
├── strategies/                    # .bat файлы стратегий
├── lists/                         # Списки доменов и IP
├── bin/                           # winws.exe и зависимости
└── logs/                          # Логи (авторотация 14 дней)
```

---

## Соглашения по коду

### Стиль

- Форматирование по `.editorconfig` в корне репозитория
- Перед коммитом запустить: `dotnet format src/Zapret.sln`
- Отступы — **4 пробела** (не табы)
- Кодировка файлов — **UTF-8 с BOM** для `.cs`, UTF-8 без BOM для `.json`/`.md`

### Правила

1. **Нет пустых `catch { }`** — всегда логировать через `Logger.Error/Warn`
2. **Нет `Thread.Sleep`** — использовать `await Task.Delay`
3. **Все HTTP-запросы** — проходить через `HttpDomainGuard.Validate(url)` перед отправкой
4. **Загрузка файлов** — всегда верифицировать через `HashVerifier.VerifyOrThrowAsync`
5. **Публичные методы и классы** — снабжать `/// <summary>` документацией

### Именование

| Что | Стиль | Пример |
|-----|-------|--------|
| Классы, методы, свойства | PascalCase | `BackupManager.CreateBackup` |
| Приватные поля | `_camelCase` | `_rootDir` |
| Локальные переменные | camelCase | `var zipPath` |
| Константы | UPPER_SNAKE или PascalCase | `BackupsDir` |

---

## Запуск тестов

```bash
# Запустить все тесты
dotnet test src/ZapretManager.Tests/

# С покрытием кода
dotnet test src/ZapretManager.Tests/ \
  --collect:"XPlat Code Coverage" \
  --results-directory ./TestResults

# Конкретный тест
dotnet test src/ZapretManager.Tests/ --filter "FullyQualifiedName~ListMergerTests"
```

Тесты используют временные директории — очищают за собой через `IDisposable`.  
**Не требуют** запуска от администратора.

---

## Как создать Pull Request

1. Создайте ветку от `main`:
   ```bash
   git checkout -b feat/my-feature
   ```

2. Внесите изменения, убедитесь что тесты проходят:
   ```bash
   dotnet test src/ZapretManager.Tests/
   ```

3. Сделайте коммит в [Conventional Commits](https://www.conventionalcommits.org/ru/) формате:
   ```
   feat: добавить проверку DNS при диагностике
   fix: исправить падение при отсутствии config.json
   docs: обновить README — добавить CLI reference
   refactor: вынести MenuBackup в отдельный класс
   test: покрыть ProfileManager тестами
   chore: добавить .editorconfig
   ```

4. Откройте Pull Request на GitHub, опишите что изменили и почему.

---

## Сборка релизного exe

```bash
# Self-contained (рекомендуется, ~84 MB)
dotnet publish src/ZapretManager/ZapretManager.csproj \
  -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true \
  -o ./publish-standalone

# Net8-required (~21 MB, требует .NET 8 Runtime)
dotnet publish src/ZapretManager/ZapretManager.csproj \
  -c Release -r win-x64 --self-contained false \
  -p:PublishSingleFile=true \
  -o ./publish-net8
```

---

## Вопросы

Создайте [Issue на GitHub](https://github.com/WildeSR98/12345/issues) с тегом `question`.
