# Тесты

`bash tools/run_tests.sh` (или `--no-ui` без Xvfb) — всё; временные файлы в `$MSS_TEST_TMP`. Перед тестами
собираются тестовый exe (`tools/build_test_exe.sh`, `-define:TESTS`) и хосты `tools/cs_host/*.exe`, проверяются
переводы (`tools/i18n/build.py --check`, `build_sheet.py --check`) и список статистик (`tools/catalog/statistic_ids.py`:
колонка каталога и `Statistics.lua` совпадают с ним). Нужны mono, python3 + lupa, node, Xvfb,
xdotool, stalonetray, imagemagick. В CI — то же (`.github/workflows/ci.yml`).

| Тест | Что проверяет |
| --- | --- |
| `test_addon2.py`, `test_addon_left.py`, `test_minimap.py`, `test_collection.py`, `test_locale.py` | аддон в имитации WoW (`wow_mock.lua`, lupa): окно (персонажи слева, вкладки «Осталось» / «Все персонажи» / «Попытки» / «Журнал», шестерёнка → настройки), «Осталось» любого персонажа, коллекция и статистика убийств, все ключи во всех языках и без повторов в `Locales.lua`; скорость с 20+ персонажами и что повторное открытие окна ничего не перестраивает |
| `test_offline.py` | пометка «офлайн» по `Program.lua`, «нужен аддон новее» / «программа устарела», порядок версий |
| `test_release_plan.py` | какие теги и выпуски делает `tools/release_plan.sh` (программа / аддон / оба, release / alpha / beta) |
| `test_attempts_addon.py` | строка попытки в чате, подсказка босса (и её кеш), Атлас, вкладка «Попытки», «Сведения для отчёта» |
| `profile_addon.py` | не тест: время и мусор горячих мест аддона на настоящих данных игрока (аргументы — SavedVariables и SheetData.lua) |
| `test_gamedata.py`, `test_collection_sync.py`, `test_layout22.py`, `test_attempts.py` | C# на таблице в памяти (`CollectionHost`, `BuilderHost`, `MemoryBook`): чтение игры, сверка, обновление вида, счётчик попыток |
| `e2e_google.py`, `e2e_collection.py`, `e2e_chars.py`, `e2e_lang.py` | exe против имитации Google (`mock_google.py`: Sheets/Drive/OAuth, проверка формул по локали, insertDimension…) |
| `e2e_script.py` | режим со скриптом: Code.gs в Node (`gas_harness.js`) |
| `e2e_update.py`, `ui_update.py` | обновление с имитации GitHub (вкл. пробные версии), меню и перезапуск в трее |
| `e2e_install.py` | самоустановка, «Установленные приложения», удаление |
| `ui_google.py`, `ui_lang.py` | окна программы под Xvfb; в ui_google ещё — `SheetData.lua` возвращается сам после «обновления аддона», «обновите аддон» при старом `addonVersion` |

ScrollBox в `wow_mock.lua` упрощён: `SetDataProvider` вызывает инициализатор для каждой строки (в игре — только для
видимых) на кадрах из `box._pool`; `box._inits` считает вызовы, `box._view._extent` отличает список (16) от
персонажей (20). Строки списка в тестах читаются из `_pool`, а не по координатам FontString.

Особенности: тесты переписывают `tools/tests/sv_collection.lua` и `status_left.json` (метки времени) — перед
коммитом вернуть (`git checkout`), версию в `status_left.json` оставлять актуальной. Под mono нет общих между
процессами именованных мьютексов и ярлыков «Пуск» — тесты это учитывают. Индексы колонок в e2e жёстко заданы:
при новых колонках таблицы их надо сдвинуть.

На Windows (Python + lupa, без mono): `paths.py` ставит локаль `C` — иначе Lua `string.lower` по системной cp1251
портит кириллицу в UTF-8 (в игре локаль `C`). Идут `test_addon2`, `test_minimap`, `test_offline`, `test_locale`,
`test_collection`; `test_addon_left` — с готовым `SheetData.lua` первым аргументом (Node-скрипт таблицы под Windows
не работает; дата списка тогда старая — одна проверка «сегодня в» не проходит). `test_attempts_addon` — только в CI.

## Скриншоты окон аддона

`test_addon_left.py` выгружает разметку окна (кадры, размеры, якоря, тексты, текстуры) в `tools/layout_left.json`,
`layout_all.json`, `layout_log.json`, `layout_settings.json`; `python tools/screens/render.py` рисует их в браузере
(playwright, Chromium) в `docs/screens/addon-*.png`. Персонажи — примерные из `wb_left.json`, классы — из
`MountSheetSyncDB.chars` в тесте. ScrollBox, его полоса (только когда список не влезает), текстуры с цветом и
шестерёнка рисуются по разметке; слой HIGHLIGHT не рисуется.
