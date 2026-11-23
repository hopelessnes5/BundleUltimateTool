# Vehicle IDs → names (NFS: Hot Pursuit)

Метод: `VEHICLES\VEHICLELIST.BUNDLE` (тип 261) содержит 153 записи по 92 байта и хвост
из 257 uint32. В записях и хвосте лежат числовые ID файлов `VEH_<id>_*.BIN`; рядом в тех же
записях находятся числовые ID строк из STRINGS-таблиц (`UI\LANGUAGE\*.BNDL`, тип 513,
8194 строки на таблицу, 13 языков). STRINGS не имеют текстовых ключей — только
`id -> UTF-16LE value`, поэтому имя восстанавливается по числовому ID из записи.

 Все 134 найденных `VEH_<id>` имеют 513-backed имя. `high` = короткое имя с брендом в самой
строке, `medium` = только модель/вариант в этой строке (бренд виден в соседних записях).

## Mapping

| VEH ID | Name | Conf |
|---:|---|:---:|
| 97331 | MURCIÉLAGO LP 640 | medium |
| 118158 | BUGATTI VEYRON | high |
| 118196 | BOXSTER SPYDER | medium |
| 118240 | BUGATTI VEYRON | high |
| 118454 | SHELBY GT500 | medium |
| 118492 | KOENIGSEGG CCX | high |
| 118530 | LANCER EVOLUTION X | medium |
| 118570 | NISSAN GT-R SPECV | high |
| 118608 | CAYMAN S | medium |
| 118889 | AUDI R8 V10 | high |
| 118940 | 8C SPIDER | medium |
| 118984 | AUDI R8 SPYDER | high |
| 121050 | MURCIÉLAGO LP 640 | medium |
| 172237 | NISSAN 370Z | high |
| 172279 | KOENIGSEGG CCXR | high |
| 172321 | GALLARDO LP 560-4 | medium |
| 172363 | MURCIÉLAGO LP 670-4 SV | medium |
| 172460 | F1 | medium |
| 172502 | MCLAREN F1 | high |
| 172544 | SLR MCLAREN 722 | high |
| 172586 | SLR MCLAREN STIRLING MOSS | high |
| 172670 | MAZDA RX-8 | high |
| 172714 | IMPREZA WRX STI | medium |
| 172756 | BMW M6 | high |
| 172798 | MASERATI QUATTROPORTE | high |
| 172840 | DODGE VIPER SRT10 | high |
| 172882 | DODGE VIPER SRT10 ACR | high |
| 172966 | BMW Z4 SDRIVE35IS | high |
| 210008 | AUDI R8 V10 | high |
| 210456 | TRAFFIC AUDI A4 | high |
| 210620 | TRAFFIC PORSCHE CAYENNE TURBO | high |
| 292004 | BMW M3 | high |
| 292069 | CAMARO SS | medium |
| 292146 | DODGE CHALLENGER SRT8 | high |
| 293008 | LAMBORGHINI REVENTÓN | high |
| 293124 | 8C COMPETIZIONE | medium |
| 293191 | FORD GT | high |
| 321070 | 911 TARGA 4S | medium |
| 329012 | SL65 AMG BLACK | medium |
| 329054 | PANAMERA TURBO | medium |
| 329096 | CARRERA GT | medium |
| 345203 | SHELBY GT500 | medium |
| 380028 | SHELBY GT500 | medium |
| 380071 | BUGATTI GRAND SPORT | high |
| 380114 | CORVETTE GRAND SPORT | medium |
| 380157 | CORVETTE Z06 | medium |
| 380200 | CORVETTE ZR1 | medium |
| 380243 | POLICE INTERCEPTOR | medium |
| 380286 | DODGE CHARGER SRT8 | high |
| 380329 | SHELBY GT500 SUPER SNAKE | medium |
| 380372 | FORD POLICE INTERCEPTOR CONCEPT | high |
| 380415 | GALLARDO LP 550-2 | medium |
| 380501 | REVENTÓN ROADSTER | medium |
| 380544 | MASERATI GRANCABRIO | high |
| 380587 | MASERATI GRANTURISMO | high |
| 380630 | ZONDA CINQUE | medium |
| 380673 | CINQUE ROADSTER | medium |
| 380802 | 911 GT3 RS | medium |
| 380845 | 911 TURBO S CABRIOLET | medium |
| 380888 | SLS AMG | medium |
| 380931 | NISSAN 370Z ROADSTER | high |
| 380982 | ASTON MARTIN DBS | high |
| 383026 | DBS VOLANTE | medium |
| 383069 | V12 VANTAGE | medium |
| 383112 | V12 VANTAGE | medium |
| 383155 | BENTLEY CONTINENTAL | high |
| 383198 | JAGUAR XKR | high |
| 383242 | KOENIGSEGG CCX | high |
| 383249 | GALLARDO LP 560-4 SPYDER | medium |
| 383297 | 8C COMPETIZIONE | medium |
| 383311 | BMW M3 | high |
| 383326 | CAMARO SS | medium |
| 383333 | DODGE CHALLENGER SRT8 | high |
| 383340 | FORD GT | high |
| 383347 | GALLARDO LP 560-4 | medium |
| 383354 | LAMBORGHINI REVENTÓN | high |
| 383361 | MCLAREN F1 | high |
| 383375 | SLR MCLAREN 722 | high |
| 383382 | LANCER EVOLUTION X | medium |
| 383389 | NISSAN GT-R SPECV | high |
| 383396 | 911 TARGA 4S | medium |
| 383403 | CAYMAN S | medium |
| 383410 | PANAMERA TURBO | medium |
| 383425 | MCLAREN MP4-12C | high |
| 383432 | 918 SPYDER | medium |
| 383480 | 918 SPYDER | medium |
| 383527 | ONE-77 | medium |
| 383613 | ONE-77 | medium |
| 383620 | IMPREZA WRX STI | medium |
| 383627 | NISSAN 370Z | high |
| 383641 | DODGE CHARGER SRT8 | high |
| 383658 | V12 VANTAGE | medium |
| 383686 | CARRERA GT | medium |
| 383693 | 911 GT3 RS | medium |
| 383707 | ZONDA CINQUE | medium |
| 383714 | SL65 AMG BLACK | medium |
| 383728 | MURCIÉLAGO LP 670-4 SV | medium |
| 383735 | JAGUAR XKR | high |
| 383742 | SHELBY GT500 SUPER SNAKE | medium |
| 383749 | CORVETTE Z06 | medium |
| 383756 | CORVETTE ZR1 | medium |
| 383808 | AGERA | medium |
| 383853 | KOENIGSEGG AGERA | high |
| 459267 | GALLARDO LP 550-2 | medium |
| 459298 | TRAFFIC FORD MUSTANG SHELBY GT500 INTERCEPTOR | high |
| 529376 | BENTLEY CONTINENTAL | high |
| 529397 | AUDI TT RS | high |
| 529411 | ASTON MARTIN DBS | high |
| 529425 | ONE-77 | medium |
| 529453 | DODGE VIPER SRT10 | high |
| 529460 | DODGE VIPER SRT10 ACR | high |
| 529481 | KOENIGSEGG AGERA | high |
| 529488 | KOENIGSEGG CCXR | high |
| 529502 | GALLARDO LP 570-4 SUPERLEGGERA | medium |
| 529516 | MASERATI GRANTURISMO | high |
| 529523 | MASERATI QUATTROPORTE | high |
| 529537 | SLS AMG | medium |
| 553384 | TRAFFIC PORSCHE CAYENNE TURBO COP | high |
| 553409 | TRAFFIC PORSCHE CAYENNE TURBO COP | high |
| 553768 | TRAFFIC CHEVROLET EXPRESSVAN | high |
| 607177 | TRAFFIC DODGE RAM1500 | high |
| 607689 | TRAFFIC GMC TOPKICK | medium |
| 686357 | TRAFFIC CADILLAC CTSV | medium |
| 686399 | TRAFFIC GM CHEVROLET COBALT | high |
| 686576 | TRAFFIC DODGE GRANDCARAVAN | high |
| 686663 | TRAFFIC DODGE MAGNUM RT | high |
| 686736 | TRAFFIC DODGE CALIBER | high |
| 686914 | TRAFFIC NISSAN FRONTIER | high |
| 710030 | TRAFFIC NISSAN VERSA | high |
| 745251 | ZONDA CINQUE NFS EDITION | medium |
| 745296 | ZONDA CINQUE ROADSTER NFS EDITION | medium |
| 1065045 | CONTINENTAL SUPERSPORTS CONVERTIBLE | medium |
| 1065326 | DODGE VIPER SRT10 CONVERTIBLE | high |
| 1065371 | MURCIÉLAGO LP650-4 ROADSTER | medium |

## Дополнительные источники имён

- `GAMELOGIC\TESTINGSCRIPTS.BNDL`, entry 73 (type 112, challenge XML) — отладочный roster:
  `GALLARDO LP 560-4 SPYDER`, `GALLARDO LP 570-4 SUPERLEGGERA`, `911 TARGA 4S`,
  `911 TURBO S CABRIOLET`, `911 GT3 RS`, `ZONDA CINQUE ROADSTER`,
  `CHARGER SRT8 INTERCEPTOR`, `TAURUS SHO`, `LAMBORGHINI REVENTON INTERCEPTOR`.
- `GAMELOGIC\GAMEPLAY.BNDL` (type 19) — entitlement/internal labels, например
  `DrPepper - Dodge Viper SRT10`, `DrPepper - Porsche Cayman S`.
- Type 522 (`CHARACTERLIST.BNDL`) и type 530 (`CUTSCENES.BNDL`) содержат только
  имена персонажей/сцен и не дают vehicle-ID → name.

Повторяющиеся имена у разных ID — норма: racer/cop/traffic/NFS-edition варианты.
25 из 153 записей VEHICLELIST не содержат VEH-файловый ID (шаблоны/варианты).
