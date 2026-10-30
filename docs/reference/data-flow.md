# Как ходят данные

```
WoW (аддон) ──SavedVariables\MountSheetSync.lua──► программа ──Sheets API / Code.gs──► Google-таблица
     ▲                                                │
     ├──────────── SheetData.lua («Осталось», попытки) ┤
     └──────────── Program.lua («программа запущена») ─┘
```

- **Игра → программа.** Аддон пишет `MountSheetSyncDB` в SavedVariables; игра сохраняет файл только при выходе
  на выбор персонажа, выходе из игры и `/reload` («Отправить в таблицу» = `/reload`). Программа видит изменение
  за ≤5 с. Аддоны WoW в сеть не выходят — по-другому никак.
- **Программа → таблица.** Записи, которых нет в `sent-ids…txt`, по 40 за запрос; ответ на каждую — статус
  (`marked`, `already`, `stale`, `not_found`, `wrong_difficulty`, `no_sheet`, `collected`, `planned`, `reset`) и
  попытки.
- **Таблица → игра.** `status` → `SheetData.lua` (`MountSheetSyncSheet`: `generated`, `version`, `spreadsheet`,
  `known` (ID сложностей), `resetAt`, `chars[{name, keys, items[]}]`, `mounts[]`; у строк `items`/`mounts` с 2.9.1 —
  `e` = номера боёв, `Encounters.IdsFor`: аддон 3.0.2 узнаёт убийство по `encounterID`, даже если имя другое) в папку аддона каждой найденной
  игры (`Interface\AddOns\MountSheetSync`). Игра читает его при входе и `/reload`; убитое после `generated` аддон
  отмечает сам по своему журналу.
- **«Программа запущена».** `Program.lua` раз в минуту; аддон считает программу запущенной, если отметка не
  старше 150 с на момент загрузки интерфейса (`ns.ProgramState`: online / nosheet / offline).
- **Атлас и коллекция.** Аддон кладёт в SavedVariables снимок коллекции маунтов и имён Атласа приключений;
  программа берёт из них названия «как в игре» и номера боёв (`GameData.Read`, `Encounters.Use`).
- **Статистика убийств (2.9).** `chars[…].stats` каждого персонажа → `GameCharacter.Stats` (из нескольких файлов
  SavedVariables — с самым свежим `statsAt`) → «Попыток» при сверке с коллекцией.

Запросы к Google на одно действие сейчас: загрузка таблицы (`GoogleBook.Load`) = 3 чтения (метаданные +
значения + формулы), её делает каждый вызов (`ping`, `kills`, `status`) — отправка убийств со списком ≈ 6 чтений
и 1–2 записи. Лимит проекта — 300 чтений и 300 записей в минуту (см. [decisions.md](decisions.md)).
