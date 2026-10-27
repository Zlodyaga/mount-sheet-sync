# Выпуски и инфраструктура

## Где что лежит

| Что | Где |
| --- | --- |
| Код | GitHub `Zlodyaga/mount-sheet-sync` (ветка `main`) |
| Сайт | GitHub Pages `Zlodyaga/zlodyaga.github.io` → https://zlodyaga.github.io/ (`index.html` — кнопки «Download for Windows» = `releases/latest/download/MountSheetSync.exe` и «Addon on CurseForge», шаги на EN и RU; `privacy.html`; `code-signing.html`) |
| Аддон | CurseForge, проект 1761349 (`mount-sheet-sync`); описание — `docs/curseforge-description.md`, правится в консоли автора CurseForge (поле react-mde) |
| Выпуски программы | GitHub Releases: `MountSheetSync-<версия>.zip` (всё), `MountSheetSync.exe` (одна программа), `MountSheetSync-addon-only-<тег>.zip` |
| Google | проект Google Cloud `mount-sheet-sync`: OAuth-клиент (Desktop), экран согласия External / In production |

## Как выпустить (программа и аддон — отдельно, с 2.8.0)

У программы и аддона свои версии и свои выпуски:

| Что | Версия в файле | Тег | Куда уходит |
| --- | --- | --- | --- |
| программа | `Program.Version` (+ `AssemblyInfo.cs`, тесты: `e2e_install.py`, `tools/screens/program.py`) | `v<версия>` | выпуск GitHub: exe + полный архив `MountSheetSync-<версия программы>.zip` → автообновление, кнопка сайта. CurseForge не трогается |
| аддон | `## Version` в toc и `VERSION` в MountSheetSync.lua (+ шапки .lua, `test_collection.py`, `status_left.json`) | `addon-v<версия>` | только CurseForge (выпуска GitHub нет — «latest» остаётся выпуском программы) |

1. Поднять версию той части, что меняется, и написать раздел `## <версия>`: программа — в `CHANGELOG.md` (станет
   описанием выпуска GitHub), аддон — в `CHANGELOG-addon.md` (его показывает CurseForge, `.pkgmeta`).
2. Выпуск — кнопкой: GitHub → **Actions → Release → Run workflow**: `what` = program / addon / both,
   `kind` = release / alpha / beta. Номера и теги подбирает `tools/release_plan.sh`: обычный — `v2.9.3` /
   `addon-v2.9.0` (уже выпущенный номер — отказ, «поднимите версию»), пробный — следующий свободный
   `…-alphaN` / `-betaN` (пре-релиз на GitHub, Alpha/Beta на CurseForge; сборка программы знает суффикс —
   `Program.Prerelease`). Можно и тегом руками (`git push origin v2.9.3` / `addon-v2.9.0`).
   Через «Releases → Draft a new release» аддон **не** выпускать: так появится выпуск GitHub без exe.
3. Совместимость: программа пишет в `Program.lua` `addon = Program.MinAddon`, аддон пишет `db.addonVersion`; аддон
   знает `MIN_PROGRAM`. Новой возможности нужна свежая другая половина — поднять `MinAddon` / `MIN_PROGRAM`:
   аддон покажет серую строку «нужен аддон новее» / «программа устарела», программа — строку в меню и одно
   уведомление «обновите аддон в CurseForge».

`.github/workflows/release.yml`: **plan** (`release_plan.sh`) → **exe** (Windows: суффикс в Prerelease,
`program\build.cmd`, подпись SignPath — включится сама, когда будут переменные `SIGNPATH_ORGANIZATION_ID`,
`SIGNPATH_PROJECT_SLUG` и секрет `SIGNPATH_API_TOKEN`) → **archive** (`tools/package.sh`: README.txt, 1-addon с
меткой `installed-from-archive.txt`, 2-program с исходниками, 3-old-way-script; выпуск GitHub с заметками из
CHANGELOG.md, тег ставит softprops); отдельно **addon** (ставит тег `addon-v…`, убирает с коммита остальные теги,
BigWigs packager без `GITHUB_OAUTH`, секрет `CF_API_KEY`). `ci.yml` — тесты на каждый push (около 3 мин).
2.8.0 — последняя общая версия (теги `v2.8.0` и `addon-v2.8.0` на одном коммите).

## Подпись и доверие

- exe пока **без подписи** → SmartScreen «Windows защитила ваш компьютер» («Подробнее → Выполнить в любом
  случае»). Ждём ответа SignPath Foundation (бесплатно для open source); политика — `code-signing.html`.
- Google: права обычные (`drive.file`, `openid`, `email`), проверка приложения не нужна; лимита 100
  пользователей нет (он только для чувствительных прав). Логотип из бренда убран (27.10.2026; заявку на
  подтверждение бренда просим закрыть), поэтому подтверждение бренда не требуется; сайт подтверждён в Search Console.

## Квоты Google

Sheets API: 300 чтений и 300 записей в минуту на проект, 60 в минуту на пользователя; сверх — 429 (программа
повторяет). Обычное использование бесплатно; Google пишет, что позже в 2026 превышение квоты может стать
платным для проекта с платёжным аккаунтом (у нас его нет). Расчёт и планы — [decisions.md](decisions.md).

## Правила площадок

- CurseForge: в описании нельзя ссылок на скачивание файлов вне CurseForge — только на сайт проекта
  («Get the Windows program — project website»), внизу описания.
- Blizzard UI Add-On Policy: без рекламы и просьб о деньгах, без спама в чат. Адрес сайта аддон показывает только
  там, где без программы чего-то нет (пустая «Осталось», ответ `/mss left`).
