# Справочник Mount Sheet Sync

Рабочая документация проекта: что умеют аддон, программа и таблица, как всё устроено, как выпускается и
проверяется, какие решения уже приняты и почему. Пользовательская инструкция — отдельно
([docs/README.ru.txt](../README.ru.txt), она же `README.txt` в архиве выпуска); здесь — для того, кто меняет проект.

| Файл | О чём |
| --- | --- |
| [addon.md](addon.md) | Аддон WoW: файлы, события, записи журнала, окно, команды, настройки, подсказки, языки |
| [program.md](program.md) | Программа в трее: режимы, меню, цикл синхронизации, файлы, установка, обновления, командная строка |
| [sheet.md](sheet.md) | Google-таблица: листы, колонки, формулы, версии вида (layout), языки, счётчик попыток |
| [data-flow.md](data-flow.md) | Как данные ходят между игрой, программой и таблицей (SavedVariables, SheetData.lua, Program.lua) |
| [release.md](release.md) | Выпуски: теги, GitHub Actions, CurseForge, сайт, подпись, Google Cloud, лимиты |
| [testing.md](testing.md) | Тесты: что где проверяется, имитации, как запускать |
| [decisions.md](decisions.md) | Принятые решения и ограничения (правила CurseForge/Blizzard, квоты Google и т. п.) |

Картинки — в [docs/screens](../screens): `sheet.png`, `program-menu.png`, `program-settings.png`,
`addon-log.png`, `addon-remaining.png`, `addon-settings.png` (их же использует README и страница CurseForge).

## Правила ведения

- Меняешь поведение — в том же коммите правишь нужный файл справочника и список изменений: программа —
  CHANGELOG.md, аддон — CHANGELOG-addon.md (у них отдельные версии и выпуски).
- Пишем факты о текущей версии; история — в CHANGELOG.md, причины — в decisions.md.
- Имена из кода — как в коде (`SheetLogic.ProcessKill`, `MountSheetSyncDB.kills`), чтобы их можно было найти grep'ом.
- В справочнике нет секретов: ни ключей, ни токенов, ни client secret, ни личной почты.

Версии: программа **2.9.0**, аддон **2.9.0** (выпущены 28.10.2026). Выходят отдельно (см. release.md).
