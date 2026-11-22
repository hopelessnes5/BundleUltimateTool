# Format fixtures — empirical golden data

Источник: `F:\repos\nfs-bundle-explorer\src\test\*`. Все значения — little-endian `uint32`,
считанные напрямую из файлов главным агентом (не из C++-кода).

## Структуры (эталон C++)

- `S_NFSBndlHeader` = 48 байт, 12 × uint32: magic (`bnd2`), version, u2, bundleSize,
  numFiles, tableOffset, headersOffset, bodysOffset, dataEnd, bundleSize2, bundleFlags, zero.
- `S_NFSTableEntry` = 80 байт, 20 × uint32, первого поля `hash`. Индексы uint32 внутри
  записи: 0 hash, 1 zero1, 2 linkID, 3 u1, 4 hdrUncompSize, 5 dataUncompSize,
  6-7 zero2, 8 hdrCompSize, 9 dataCompSize, 10-11 zero3, 12 hdrOffset, 13 bodyOffset,
  14-15 zero4, 16 dataEnd, 17 fileType, 18 numLinks, 19 zero5.
- Маска размеров `0x00ffffff`; выравнивание 16 (заголовки) и 128 (тела).

## VEHICLELIST.BUNDLE (4608 байт)

```
magic=bnd2 version=3 u2=1 bundleSize=4608 numFiles=1
tableOffset=48 headersOffset=128 bodysOffset=4608 dataEnd=4608 bundleSize2=4608 flags=1 zero=0
entry[0] @48: hash=0x6CB9F778 zero1=0 linkID=0 u1=0
  hdrUncompSize=0x40003A00 (mask -> 0x003A00 = 14848)
  dataUncompSize=0
  hdrCompSize=4448 dataCompSize=0
  hdrOffset=0 bodyOffset=0 dataEnd=0 fileType=261 numLinks=0 zero5=0
bytes @128: 78 DA ... (zlib stream)
```

## VEHICLETEX.BIN (5 420 672 байт)

```
magic=bnd2 version=3 u2=1 bundleSize=5420672 numFiles=51
tableOffset=48 headersOffset=4128 bodysOffset=5760 dataEnd=5420672 bundleSize2=5420672 flags=1
entry[0] @48: hash=0x0160E1F9
  hdrUncompSize=0x20000020 (mask -> 32)
  dataUncompSize=0x4002AB00 (mask -> 174848)
  hdrCompSize=29 dataCompSize=40569
  hdrOffset=0 bodyOffset=0 dataEnd=0 fileType=1 (texture) numLinks=0
entry[1] @128: hdrUncompSize=0x20000020 (mask -> 32), dataUncompSize=0x400AAB00 (mask -> 699136),
  hdrCompSize=28 dataCompSize=81599, hdrOffset=32, bodyOffset=40576, fileType=? (проверить)
```

## VEH_118158_MS.BIN (3 579 264 байт)

```
magic=bnd2 version=3 u2=1 bundleSize=3579264 numFiles=538
tableOffset=48 headersOffset=43088 (= 48 + 538*80) bodysOffset=158592
dataEnd=3579264 bundleSize2=3579264 flags=0x27 (39) zero=0
entry[0] @48: hash=0x016FDEA2 zero1=0 linkID=0x6179A9A2 u1=0
  hdrUncompSize=0x400000B0 (mask -> 176)
  dataUncompSize=0x40000100 (mask -> 256)
  hdrCompSize=93 dataCompSize=157
  hdrOffset=0 bodyOffset=0 dataEnd=160 fileType=5 (model) numLinks=1 zero5=0
```

Замечание: `headersOffset` точно равен `48 + numFiles*80` во всех трёх файлах —
таблица действительно начинается сразу после 48-байтового заголовка.

## Распределение типов записей (подсчитано по таблицам)

- `VEHICLELIST.BUNDLE`: тип 261 — 1 шт.
- `VEHICLETEX.BIN`: тип 1 (texture) — 51 шт.
- `VEH_118158_MS.BIN`: 1=40, 2=60, 5=44, 7=5, 18=105, 19=94, 81=10, 129=170,
  176=3, 178=1, 262=1, 528=1, 771=4.

Типа 513 (STRINGS) в тестовых файлах НЕТ — тест парсера строк нужно строить на
синтетических данных. Тип 528 не описан в `E_NFS_FILE_TYPE` оригинала (есть 527 и 532);
в порту он должен оставаться «неизвестным», но не вызывать ошибку.
Первые записи типа 18 (DEFS): индексы 159, 163, 168, 169, 173.

## Выводы для порта

- Поля размеров содержат старшие биты-флаги: обязательна маскировка `0x00ffffff`
  перед использованием как длины.
- `tableOffset` в C++ не используется: таблица читается сразу после 48-байтного заголовка.
  Порт должен либо подтвердить это на всех трёх файлах, либо явно обработать расхождение.
- Тест S1 обязан сверять перечисленные числа как golden values и падать при расхождении.
