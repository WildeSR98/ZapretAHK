# Zapret Manager — AutoHotkey v2 Edition

Перенос проекта Zapret Manager с C# .NET на AutoHotkey v2 для Windows.

## 📁 Структура проекта

```
ahk-zapret-manager/
├── ZapretManager.ahk          # Главный файл приложения (GUI)
├── config.json                # Конфигурация (копия из корня)
│
├── lib/                       # Библиотеки функций
│   ├── WinService.ahk         # Управление службами Windows (advapi32.dll)
│   ├── Logger.ahk             # Система логирования
│   ├── AppConfig.ahk          # Загрузка config.json
│   ├── AdminHelper.ahk        # Проверка прав администратора
│   └── HttpService.ahk        # HTTP-запросы (WinHttpRequest)
│
├── service/                   # Модули управления службами
│   ├── ProcessManager.ahk     # Управление процессами
│   ├── TgProxyManager.ahk     # TG Proxy управление
│   ├── BackupManager.ahk      # Бэкапы
│   ├── ProfileManager.ahk     # Профили
│   ├── StrategyEditor.ahk     # Редактор стратегий
│   ├── StrategyReader.ahk     # Парсинг .bat стратегий
│   ├── Watchdog.ahk           # Авто-ротация стратегий
│   └── NicSelector.ahk        # Выбор сетевого адаптера
│
├── ui/                        # Пользовательский интерфейс
│   ├── ToastNotifier.ahk      # Уведомления Windows
│   └── Dialogs.ahk            # Диалоговые окна
│
├── menus/                     # Меню действий
│   ├── SettingsMenu.ahk       # Настройки
│   ├── UpdateMenu.ahk         # Обновления
│   ├── DiagnosticsMenu.ahk    # Диагностика
│   ├── TgProxyMenu.ahk        # Меню TG Proxy
│   └── BackupMenu.ahk         # Бэкап/восстановление
│
├── diagnostics/               # Диагностика
│   ├── AccessChecker.ahk      # Проверка доступа
│   ├── DnsChecker.ahk         # DNS проверка
│   ├── IspDetector.ahk        # Определение провайдера
│   ├── SpeedTester.ahk        # Speed-тест
│   └── FullDiagnostics.ahk    # Полная диагностика
│
├── lists/                     # Работа со списками
│   ├── ListDownloader.ahk     # Загрузка списков
│   ├── ListMerger.ahk         # Слияние списков
│   └── HostsUpdater.ahk       # Обновление hosts
│
└── updates/                   # Обновления
    ├── UpdateChecker.ahk      # Проверка обновлений
    └── Updater.ahk            # Загрузчик обновлений
```

## ✅ Реализовано

| Компонент | Статус | Описание |
|-----------|--------|----------|
| GUI приложение | ✅ | Основное окно с меню и кнопками |
| Управление службой | ✅ | Установка/удаление/запуск/остановка через advapi32.dll |
| Tray-иконка | ✅ | Контекстное меню в трее |
| Логирование | ✅ | Запись логов в файлы |
| Загрузка конфигов | ✅ | Парсинг JSON |
| HTTP запросы | ✅ | WinHttpRequest для API и загрузок |
| Права админа | ✅ | Авто-перезапуск от администратора |

## 🔧 Требования

- **Windows 10/11** (AHK работает только на Windows)
- **AutoHotkey v2.0+** (обязательно вторая версия)
- **Права администратора** (для управления службами)

## 🚀 Запуск

```bash
# Установите AutoHotkey v2: https://www.autohotkey.com/v2/

# Запуск скрипта
AutoHotkey64.exe ZapretManager.ahk

# Или создайте ярлык с параметром /restart (для прав админа)
```

## 📝 Особенности реализации

### Управление службами Windows
Используется прямой вызов API через `advapi32.dll`:
- `OpenSCManager`, `CreateService`, `StartService`
- `DeleteService`, `ControlService`, `QueryServiceStatus`
- Политика восстановления (restart on failure)

### Асинхронность
AHK v2 не имеет нативной async/await, но использует:
- `SetTimer` для фоновых задач
- Отдельные процессы для долгих операций
- Неблокирующий GUI (Gui + AlwaysOnTop)

### Сетевые операции
- `WinHttp.WinHttpRequest.5.1` для HTTP/HTTPS
- Поддержка SSL/TLS через системные библиотеки
- Загрузка файлов через `ADODB.Stream`

## ⚠️ Ограничения AHK

| Что нельзя сделать | Решение |
|-------------------|---------|
| Нет async/await | SetTimer, отдельные процессы |
| Нет сложных сокетов | WinHttpRequest, RunWait с curl |
| Только Windows | Это целевая платформа |
| Нет многопоточности | Critical секции, таймеры |

## 📋 План разработки

1. **Базовый функционал** (готово)
   - GUI окно, меню, трей
   - Управление службой
   - Логирование

2. **Диагностика** (в процессе)
   - Ping, nslookup через RunWait
   - Проверка доступа к доменам
   - DPI тесты

3. **Обновления**
   - Проверка GitHub API
   - Загрузка новых версий
   - Hash verification

4. **TG Proxy**
   - Запуск/остановка
   - Мониторинг статуса

5. **Бэкапы**
   - Copy/Zip архивация
   - Восстановление из бэкапа

## 🔗 Ссылки

- [AutoHotkey v2 Documentation](https://www.autohotkey.com/docs/v2/)
- [Оригинальный проект на C#](../src/ZapretManager/)
- [Zapret Core](https://github.com/Flowseal/zapret-discord-youtube)
