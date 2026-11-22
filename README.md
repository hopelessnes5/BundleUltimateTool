# BundleUltimateTool

## Обзор

BundleUltimateTool — Windows-утилита для просмотра, извлечения и сборки архивов
`Need for Speed: Hot Pursuit (2010)` / `Burnout Paradise` (`.bundle`, `.bndl`, `.bin`).
Это новое приложение: оболочка и визуальный стиль перенесены из
**PrototypeUltimateTool**, а логика формата — порт **nfs-bundle-explorer**
(оригинал: iOrange, 2010; модернизация 2026 — @hopelessnes5 и Gemini).

Приложение работает в одном основном окне редактора: дерево записей, сетка свойств,
2D-предпросмотр текстур, строка состояния, светлая/тёмная темы и каталоги RU/EN.
Единственный дополнительный диалог — компактный сборщик архивов.

## Возможности

- Открытие архива (меню `Файл → Открыть`, `Ctrl+O` или перетаскивание файла в окно).
- Плоский список записей в дереве с поиском по тексту и фильтром по типу файла.
- Сетка свойств: тип, хеш, link ID, размеры (сырые и с маской), абсолютные смещения,
  CRC32 сжатых заголовка/тела, параметры текстур и моделей.
- Извлечение RAW `.nfs` (сжатый заголовок + тело после zlib) для одной или всех записей.
- Текстуры: предпросмотр, экспорт `.dds` и преобразование `.dds → .nfs`.
- Модели: экспорт геометрии в Wavefront `.obj` (глобальные 1-based индексы).
- STRINGS: сохранение таблицы строк в UTF-8 `.txt` (одна строка на линию).
- DEFS: компактная таблица в панели свойств (Field ID, Offset, Type).
- Сборщик архивов: замена записей по `.nfs`-файлам `file_0000.nfs`, запись во
  временный файл с последующей атомарной заменой цели (исправлен баг оригинала,
  который оставлял `.temp` и никогда не переименовывал результат).

## Стек и требования

- C# 13, .NET 9 (`net9.0-windows`), WinForms. **WPF и DX12 не используются.**
- Windows x64, .NET 9 Windows Desktop Runtime (устанавливается отдельно).
- Поставка — framework-dependent single-file EXE: один `.exe` плюс внешние каталоги
  `Languages/`; self-contained не требуется.

## Сборка, тесты, запуск

```powershell
dotnet build BundleUltimateTool.sln -c Release
dotnet run --project tests/BundleChecks/BundleChecks.csproj -c Release
.\src\BundleUltimateTool\bin\Release\net9.0-windows\BundleUltimateTool.exe
```

Архив можно передать аргументом командной строки (в том числе из Проводника):

```powershell
.\src\BundleUltimateTool\bin\Release\net9.0-windows\BundleUltimateTool.exe "F:\path\to\file.bundle"
```

Проверки используют эталонные файлы из `F:\repos\nfs-bundle-explorer\src\test`
(бинарные файлы не копируются в репозиторий). Настройки сохраняются в `settings.ini`
рядом с исполняемым файлом, журнал — в `%LOCALAPPDATA%\BundleUltimateTool\Logs`.

## Поддерживаемые типы и ограничения

- Поддерживаемые типы записей: текстуры (DXT1, DXT5, RGBA32), модели (mesh type 5),
  таблицы STRINGS (513) и DEFS (18); остальные типы остаются доступны как RAW-записи.
- 3D-вьюпорт отсутствует: предпросмотр только 2D.
- DXT3 не декодируется (в NFS-архивах не встречается); неподдерживаемый FourCC
  отклоняется с понятной ошибкой.
- Новые zlib-потоки корректны, но не побайтово совпадают с потоками старой версии zlib
  (например, 4885 B против 4448 B); это зафиксировано в тестах и не влияет на данные.
- STRINGS-таблицы нет в эталонных архивах, поэтому её разбор покрыт синтетическим тестом.
- Пересборка поверх исходного файла допускается только после явного подтверждения.

## Благодарности

iOrange (оригинальный nfs-bundle-explorer, 2010); @hopelessnes5 и Gemini (модернизация
2026); команда PrototypeUltimateTool — за оболочку редактора и визуальные решения.

---

## Overview

BundleUltimateTool is a Windows utility for browsing, extracting and rebuilding archives
of `Need for Speed: Hot Pursuit (2010)` / `Burnout Paradise` (`.bundle`, `.bndl`, `.bin`).
This is a new application: the UI shell and visual style are inherited from
**PrototypeUltimateTool**, while the file-format logic is a managed port of
**nfs-bundle-explorer** (original by iOrange, 2010; modernization 2026 by @hopelessnes5
and Gemini).

The editor stays in one main window: entry tree, property grid, 2D texture preview and
status bar, with light/dark themes and RU/EN catalogs. The only additional dialog is the
compact bundle assembler.

## Features

- Open an archive via `File → Open`, `Ctrl+O`, or by dropping a file onto the window.
- Flat entry tree with text search and file-type filter.
- Property grid: type, hash, link ID, raw and masked sizes, absolute offsets, CRC32 of
  compressed header/body, plus texture and model metadata when applicable.
- Raw `.nfs` extraction (compressed header + body after zlib) for selected or all entries.
- Textures: preview, DDS export and DDS→NFS conversion.
- Models: geometry export to Wavefront `.obj` with global 1-based indices.
- STRINGS: save the string table as UTF-8 `.txt`, one string per line.
- DEFS: compact table inside the property panel (Field ID, Offset, Type).
- Bundle assembler: replace entries from `file_0000.nfs`-style files; writes to a unique
  temp file and atomically replaces the target (fixing the original `.temp` bug).

## Stack and requirements

- C# 13, .NET 9 (`net9.0-windows`), WinForms. **No WPF, no Direct3D 12.**
- Windows x64 with the .NET 9 Windows Desktop Runtime installed separately.
- Framework-dependent single-file EXE: one `.exe` plus external `Languages/` catalogs.

## Build, test, run

```powershell
dotnet build BundleUltimateTool.sln -c Release
dotnet run --project tests/BundleChecks/BundleChecks.csproj -c Release
.\src\BundleUltimateTool\bin\Release\net9.0-windows\BundleUltimateTool.exe
```

A bundle may also be passed as a command-line argument (including from Windows Explorer):

```powershell
.\src\BundleUltimateTool\bin\Release\net9.0-windows\BundleUltimateTool.exe "F:\path\to\file.bundle"
```

Fixture checks read the reference files from `F:\repos\nfs-bundle-explorer\src\test`
(no binaries are copied into this repository). Settings live in `settings.ini` next to the
executable; logs go to `%LOCALAPPDATA%\BundleUltimateTool\Logs`.

## Supported types and known limitations

- Supported entry types: textures (DXT1, DXT5, RGBA32), models (mesh type 5), STRINGS
  (513) and DEFS (18); all other types remain available as raw entries.
- There is no 3D viewport — preview is 2D only.
- DXT3 is not decoded (it does not occur in these NFS archives); unsupported FourCC
  values are rejected with a clear error.
- Newly compressed zlib streams are valid but not byte-identical to legacy zlib output
  (e.g. 4885 B vs 4448 B); the difference is recorded by the checks and does not change data.
- No STRINGS table exists in the bundled fixtures, so its parser is covered by a
  synthetic test.
- Rebuilding in place requires explicit user confirmation.

## Credits

iOrange (original nfs-bundle-explorer, 2010); @hopelessnes5 and Gemini (2026
modernization); the PrototypeUltimateTool team for the editor shell and visual patterns.
