# Выпуски и инфраструктура

## Где что лежит

| Что | Где |
| --- | --- |
| Код | GitHub `Zlodyaga/mount-sheet-sync` (ветка `main`) |
| Сайт | GitHub Pages `Zlodyaga/zlodyaga.github.io` → https://zlodyaga.github.io/ (`index.html` — кнопки «Download for Windows» = `releases/latest/download/MountSheetSync.exe` и «Addon on CurseForge», шаги на EN и RU; `privacy.html`; `code-signing.html`) |
| Аддон | CurseForge, проект 1761349 (`mount-sheet-sync`); описание — `docs/curseforge-description.md`, правится в консоли автора CurseForge (поле react-mde) |
| Выпуски программы | GitHub Releases: `MountSheetSync-<версия>.zip` (всё), `MountSheetSync.exe` (одна программа), `MountSheetSync-addon-only-<тег>.zip` |
| Google | проект Google Cloud `mount-sheet-sync`: OAuth-клиент (Desktop), экран согласия External / In production |

## Как выпустить

1. Версия одна на всё: `## Version` в toc, `VERSION` в MountSheetSync.lua (и шапки .lua), `Program.Version`,
   `AssemblyInfo.cs`, тесты (`test_collection.py`, `e2e_install.py`, `status_left.json`, `tools/screens/program.py`).
2. Раздел `## <версия>` в CHANGELOG.md — он станет описанием выпуска.
3. Тег (создаёт пользователь сам, обычно через GitHub → Releases → Draft a new release):
   - `v2.8.0` — обычный выпуск: «Latest» на GitHub, Release на CurseForge, на него обновляются все.
   - `v2.8.0-alpha1` / `v2.8.0-beta1` — пробный: пре-релиз на GitHub (галочка «Set as a pre-release»),
     Alpha / Beta на CurseForge (BigWigs packager читает слово в теге), автообновление и сайт его не видят,
     кроме программ с «Обновляться до пробных версий». Сборка знает свой суффикс (`Program.Prerelease`).

`.github/workflows/release.yml` по тегу `v*`: **exe** (Windows: проверка тег = toc и Program.cs, суффикс в
Prerelease, `program\build.cmd`, подпись SignPath — включится сама, когда будут переменные
`SIGNPATH_ORGANIZATION_ID`, `SIGNPATH_PROJECT_SLUG` и секрет `SIGNPATH_API_TOKEN`) → **archive** (`tools/package.sh`:
README.txt, 1-addon с меткой `installed-from-archive.txt`, 2-program с исходниками, 3-old-way-script; выпуск
GitHub с заметками из CHANGELOG, `prerelease` по «-» в теге) → **addon** (BigWigs packager, `.pkgmeta`, секрет
`CF_API_KEY`). `ci.yml` — тесты на каждый push (около 3 мин).

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
