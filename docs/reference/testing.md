# Тесты

`bash tools/run_tests.sh` (или `--no-ui` без Xvfb) — всё; временные файлы в `$MSS_TEST_TMP`. Перед тестами
собираются тестовый exe (`tools/build_test_exe.sh`, `-define:TESTS`) и хосты `tools/cs_host/*.exe`, проверяются
переводы (`tools/i18n/build.py --check`, `build_sheet.py --check`). Нужны mono, python3 + lupa, node, Xvfb,
xdotool, stalonetray, imagemagick. В CI — то же (`.github/workflows/ci.yml`).

| Тест | Что проверяет |
| --- | --- |
| `test_addon2.py`, `test_addon_left.py`, `test_minimap.py`, `test_collection.py`, `test_locale.py` | аддон в имитации WoW (`wow_mock.lua`, lupa): окно, «Осталось», кнопки, коллекция, все ключи во всех языках |
| `test_offline.py` | пометка «офлайн» по `Program.lua` |
| `test_attempts_addon.py` | строка попытки в чате, подсказка босса, Атлас, «Сведения для отчёта» |
| `test_gamedata.py`, `test_collection_sync.py`, `test_layout22.py`, `test_attempts.py` | C# на таблице в памяти (`CollectionHost`, `BuilderHost`, `MemoryBook`): чтение игры, сверка, обновление вида, счётчик попыток |
| `e2e_google.py`, `e2e_collection.py`, `e2e_chars.py`, `e2e_lang.py` | exe против имитации Google (`mock_google.py`: Sheets/Drive/OAuth, проверка формул по локали, insertDimension…) |
| `e2e_script.py` | режим со скриптом: Code.gs в Node (`gas_harness.js`) |
| `e2e_update.py`, `ui_update.py` | обновление с имитации GitHub (вкл. пробные версии), меню и перезапуск в трее |
| `e2e_install.py` | самоустановка, «Установленные приложения», удаление |
| `ui_google.py`, `ui_lang.py` | окна программы под Xvfb |

Особенности: тесты переписывают `tools/tests/sv_collection.lua` и `status_left.json` (метки времени) — перед
коммитом вернуть (`git checkout`), версию в `status_left.json` оставлять актуальной. Под mono нет общих между
процессами именованных мьютексов и ярлыков «Пуск» — тесты это учитывают. Индексы колонок в e2e жёстко заданы:
при новых колонках таблицы их надо сдвинуть.
