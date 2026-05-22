# Zapret Manager

<div align="center">

[![CI](https://github.com/WildeSR98/12345/actions/workflows/ci.yml/badge.svg)](https://github.com/WildeSR98/12345/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/WildeSR98/12345?label=release&color=brightgreen)](https://github.com/WildeSR98/12345/releases/latest)
[![Downloads](https://img.shields.io/github/downloads/WildeSR98/12345/total?color=blue)](https://github.com/WildeSR98/12345/releases)
[![Windows](https://img.shields.io/badge/Windows-10%2F11-0078d4?logo=windows)](https://www.microsoft.com/windows)

**Удобный менеджер для обхода блокировок Discord, YouTube и других сайтов на Windows**

</div>

---

## 📥 Скачать

Перейдите на страницу [Releases](https://github.com/WildeSR98/12345/releases/latest) и скачайте один из архивов:

| Архив | Описание |
|-------|----------|
| `zapret-manager-v*-standalone.zip` | **Рекомендуется.** Работает сразу, ничего не нужно устанавливать |
| `zapret-manager-v*-net8-required.zip` | Лёгкий вариант — требует [.NET 8 Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) |

---

## 🚀 Быстрый старт

1. Распакуйте архив в любую папку (например, `C:\zapret\`)
2. Запустите `zapret-manager.exe` **от имени администратора**
3. Следуйте подсказкам — менеджер сам всё настроит

> Если Discord работает, а YouTube — нет: включите **Secure DNS** в браузере.
> - **Chrome/Edge**: Настройки → Конфиденциальность → Безопасный DNS → Google
> - **Firefox**: Настройки → Параметры сети → DNS через HTTPS → Google

---

## 🖥 Что умеет

- **Установка и управление службой** — запустить, остановить, удалить
- **Выбор стратегии** — интерактивный список, стрелки + Enter
- **Автоматический тест стратегий** — проверяет каждую и выбирает лучшую
- **Иконка в трее** — быстрый доступ без открытия консоли
- **Watchdog** — автоматически меняет стратегию если сайты перестали открываться
- **Обновления** — менеджер и ядро zapret обновляются одной кнопкой
- **Диагностика** — показывает что именно не работает (HTTP/TLS/Ping)
- **Бэкап и профили** — сохраните и восстановите любые настройки
- **Speed-тест, мониторинг трафика, игровой фильтр** и многое другое

---

## ❓ Частые проблемы

**YouTube не работает после установки:**
1. Включите **Secure DNS** в браузере (см. выше)
2. В меню → пункт **[11] Тест стратегий** — найдёт рабочую стратегию

**Лагают онлайн-игры:**
Включите **[4] Игровой фильтр** — обход будет работать только для нужных сайтов

**Перестало работать через время:**
1. **[9]** Проверить обновления
2. **[7]** Обновить IPSet
3. **[11]** Снова протестировать стратегии
4. **[17]** Включить Watchdog — он сделает это автоматически

**Как полностью удалить:**
1. В меню → **[2] Удалить службы**
2. Удалите папку с программой

---

## 🔧 Сборка из исходников

Требуется [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```bash
# Standalone (~84 MB, не требует .NET у пользователя)
dotnet publish src/ZapretManager/ZapretManager.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish/standalone

# С .NET Runtime (~21 MB)
dotnet publish src/ZapretManager/ZapretManager.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish/net8

# Тесты
dotnet test src/ZapretManager.Tests/
```

---

## 📄 Благодарности

- [zapret](https://github.com/bol-van/zapret) — ядро обхода DPI
- [zapret-discord-youtube](https://github.com/Flowseal/zapret-discord-youtube) — стратегии и списки
- [Spectre.Console](https://spectreconsole.net/) — консольный UI

## ⚠️ Дисклеймер

Программа предоставляется «как есть» для образовательных целей. Используйте на свой страх и риск.
