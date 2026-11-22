# BundleUltimateTool

## Обзор

Windows-утилита для полного цикла работы с архивами `Need for Speed: Hot Pursuit (2010)`
и `Burnout Paradise`: `.bundle`, `.bndl`, `.bin`. Приложение объединяет:

- **оболочку и визуальные решения PrototypeUltimateTool** (компактное одно окно, дерево,
  PropertyGrid, 2D/3D-предпросмотр, тёмная/светлая темы, RU/EN);
- **логику формата nfs-bundle-explorer**
- **DX12-рендерер и систему индексации PrototypeUltimateTool**, адаптированные под NFS-архивы.

Всё работает в одном основном окне. Единственный дополнительный диалог — компактный
сборщик архивов.

## Возможности

### Архивы и навигация

- Открытие архива: `Файл → Открыть`, `Ctrl+O`, перетаскивание файла в окно или аргумент
  командной строки.
- Выбор папки с игрой (`Файл → Выбрать папку игры`). При выборе папки приложение
  **само индексирует и открывает все найденные архивы** — двойной щелчок не нужен.
- Дерево показывает папки и архивы; записи внутри архива подгружаются лениво при раскрытии,
  один щелчок по `+` раскрывает, следующий сворачивает.
- Индексация по образцу Prototype: читаются только 48-байтовые заголовки архивов,
  строится компактный **type-histogram** (тип → количество) и кэшируется в бинарный
  sidecar со строгой проверкой identity (путь/размер/mtime), budgets и атомарной записью.
- Фильтр `Тип` строится по реальным форматам внутри архивов; в выпадающем списке видны
  `texture (1)`, `material (2)`, `model (5)`, `defs table (18)`, а также имена,
  определённые по структуре неизвестных типов (`D3D9 shader`, `challenge XML`,
  `name table`, `vehicle stats`, `hardpoints`, `cam spline`, `float4 array`, …).
- Восемь режимов сортировки из Prototype: по умолчанию, по имени ↑/↓, по размеру ↑/↓,
  по разрешению ↑/↓, по типу.

### Извлечение и конвертация

- Извлечение RAW `.nfs` (декомпрессированные заголовок + тело) для выбранных или всех записей.
- Текстуры: 2D-предпросмотр, экспорт `.dds`, конвертация `.dds → .nfs`.
- Модели: экспорт в Wavefront `.obj` (глобальные 1-based индексы); регрессионный тест
  экспортирует все 44 модели эталонного архива.
- STRINGS: сохранение таблицы строк в UTF-8 `.txt`; DEFS: таблица в панели свойств.
- Сборщик архивов: замена записей из `.nfs`, запись во временный файл с атомарной заменой
  цели (исправлен баг оригинала, оставлявший `.temp`).

### Просмотр

- **2D-вьювер** — панель Prototype: преsets `RGBA` / `RGB`, каналы `R G B A`, подпись
  разрешения справа. Обработка каналов — порт `DdsDecoder.ProcessChannels`.
- **3D-вьювер** — компактный DX12-рендерер Prototype (Vortice 3.8.3): орбитальная камера,
  gizmo, оверлей статистики FPS/CPU/GPU/draw calls, режимы каркас/сплошной/normals.
  Шейдер `Dx12Wireframe.hlsl` встроен в сборку.
- PropertyGrid показывает тип, хеш, link ID, размеры (сырые и с маской), абсолютные
  смещения, CRC32 сжатых данных, параметры текстур и моделей.
- В режиме «Файлы» любой файл можно посмотреть: текстовые — текстом (UTF-8 с fallback),
  остальные — HEX + ASCII (16 байт в строке).

## Стек и требования

- C# 13, .NET 9 (`net9.0-windows`), WinForms, Direct3D 12 через Vortice 3.8.3. **WPF не используется.**
- Windows x64, **.NET 9 Windows Desktop Runtime** (устанавливается отдельно).
- Поставка — framework-dependent single-file EXE: один `.exe` плюс внешняя папка `Languages/`.
  Self-contained сборка не используется.
- Данные приложения: `settings.ini` рядом с EXE; логи — `%LOCALAPPDATA%\BundleUltimateTool\Logs`;
  кэш индекса — `%LOCALAPPDATA%\BundleUltimateTool\IndexCache` и `…\Cache`.

## Сборка, тесты, запуск

```powershell
dotnet build BundleUltimateTool.sln -c Release
dotnet run --project tests/BundleChecks/BundleChecks.csproj -c Release
.\src\BundleUltimateTool\bin\Release\net9.0-windows\BundleUltimateTool.exe
```

Аргументы командной строки:

```powershell
... BundleUltimateTool.exe "F:\path\to\file.bundle"
... BundleUltimateTool.exe --game-folder "F:\Games\NFS HP"
... BundleUltimateTool.exe "F:\path\to\file.bundle" --select-entry 0   # отладочный выбор записи
```

Проверки читают эталонные файлы из `F:\repos\nfs-bundle-explorer\src\test`
(бинарники в репозиторий не копируются). Сейчас **337 проверок**, включая побайтовую
пересборку архивов, OBJ-экспорт всех моделей, кэш и incident-тесты битых таблиц.

## Поддерживаемые типы и ограничения

- Полностью поддерживаются: текстуры (DXT1, DXT5, RGBA32), модели (тип 5),
  STRINGS (513), DEFS (18), RAW-извлечение любых записей.
- Для неизвестных типов (~50 значений) определены и присвоены имена по структуре файлов, но
  **декодеры для большинства из них не реализованы** — они доступны как RAW и по имени
  в фильтре. DXT3 не декодируется (в NFS-архивах не встречается).
- 3D-вьювер работает в Solid/Wireframe/Normals; текстурированный pipeline подготовлен,
  но не привязан к текстурам архива — модели отображаются без текстур.
- Новые zlib-потоки корректны, но не побайтово совпадают со старой версией zlib
  (например, 4885 B против 4448 B); сборка без замен побайтово идентична оригиналу.
- STRINGS-таблицы нет в эталонных архивах — её разбор покрыт синтетическим тестом.
- Папка игры выбирается вручную; автоопределения установленных игр нет. Скрытые записи
  (`.svn`) показываются как обычные папки. Пересборка поверх исходника требует подтверждения.

## Overview

A Windows utility for the full cycle of working with `Need for Speed: Hot Pursuit (2010)`
and `Burnout Paradise` archives (`.bundle`, `.bndl`, `.bin`). It combines the compact
single-window shell and viewer patterns of **PrototypeUltimateTool** with a managed C#
port of the **nfs-bundle-explorer** format logic. 
A DX12 renderer and Prototype-style indexing are adapted for NFS bundles.

### Features

- Open a bundle via `File → Open`, `Ctrl+O`, drag-and-drop, or command line.
- Select a game folder: **all bundles are discovered and opened automatically**, with
  header-only indexing, a compact type histogram and a binary sidecar cache using strict
  identity checks. A single click on `+` expands a bundle, the next collapses it.
- Type filter built from actual archive contents; eight sorting modes ported from Prototype.
- Raw `.nfs` extraction, DDS export/conversion, OBJ export, STRINGS→TXT, DEFS table view,
  and an archive assembler with atomic replacement.
- Prototype-style 2D preview (`RGBA`/`RGB` presets, R/G/B/A channels, resolution label)
  and a compact DX12 3D viewer with orbit camera, gizmo and stats overlay.
- Property grid with hashes, sizes, offsets, CRC32 and texture/model metadata.

### Stack / limitations

C# 13, .NET 9, WinForms, Direct3D 12 via Vortice 3.8.3 (no WPF). Requires the .NET 9
Windows Desktop Runtime; shipped as a framework-dependent single-file EXE. Most non-core
file types are identified and named but not decoded (raw extraction only). 3D models are
shown untextured; new zlib streams are valid but not byte-identical to legacy output.
Game-folder discovery is manual.

### Build / test / run

```powershell
dotnet build BundleUltimateTool.sln -c Release
dotnet run --project tests/BundleChecks/BundleChecks.csproj -c Release
.\src\BundleUltimateTool\bin\Release\net9.0-windows\BundleUltimateTool.exe --game-folder "F:\Games\NFS HP"
```

337 checks pass, including byte-identical rebuilds, 44-model OBJ regression, cache
identity tests and corrupt-table cases.
