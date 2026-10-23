/** @OnlyCurrentDoc */
/**
 * Mount Sheet Sync 1.4.0 — приёмник для аддона World of Warcraft.
 *
 * Получает от программы Mount Sheet Sync (MountSheetSync.exe) список убитых боссов и:
 *   • на листах «Рейды <Имя>» и «Подземелья <Имя>» меняет «Нет» → «Да»
 *     в колонке «Пытался на этой неделе?»;
 *   • на листе «Мировые боссы» ставит галочку в колонке персонажа;
 *   • когда в игре появился новый маунт — ставит «Да» в колонке «Собрано?»
 *     строк с этим маунтом («Рейды», «Подземелья», «Мировые боссы»).
 * По запросу отдаёт список «Осталось на этой неделе» для окна аддона.
 * «Да» никогда не меняется обратно на «Нет» — кроме кнопки «Сброс недели»
 * в окне аддона: она ставит «Нет» всем персонажам (только в колонке
 * «Пытался на этой неделе?» и в галочках персонажей на «Мировых боссах»).
 *
 * Установка (подробно — в README.txt):
 *   Расширения → Apps Script → вставить этот код → Сохранить →
 *   Развернуть → Новое развертывание → Веб-приложение
 *   (Запуск от имени: Я, Доступ: Все) → скопировать URL веб-приложения.
 * После любых правок кода: Развернуть → Управление развертываниями →
 *   ✎ (изменить) → Версия: «Новая версия» → Развернуть. URL не изменится.
 */

// ============================ НАСТРОЙКИ ============================

/** Должен совпадать с полем «Ключ» в настройках программы Mount Sheet Sync. */
const SECRET = 'snel1Y-KWGZbBii0vKrBNGfT';

/** Что писать в ячейку при отметке и при сбросе недели. */
const YES = 'Да';
const NO = 'Нет';

/** Листы персонажей: «Рейды Эйра», «Подземелья Эйра», «Рейды Каэлия (Лок)». */
const SHEET_PREFIXES = { raid: 'Рейды', dungeon: 'Подземелья' };

/** Лист с галочками по персонажам. */
const WORLD_SHEET = 'Мировые боссы';

/**
 * Если имя персонажа в игре не совпадает с названием листа:
 *   'Имя в игре': 'Имя как в названии листа',
 * Можно указать вместе с сервером: 'Эйра-Ревущий фьорд': 'Эйра',
 */
const CHARACTER_ALIASES = {
};

/**
 * Если босс в игре называется иначе, чем в колонке «Босс»:
 *   'Имя в игре': 'Имя в таблице',
 * Английские имена уже вписаны — на случай английского клиента игры.
 */
const BOSS_ALIASES = {
  // Рейды
  'Alysrazor': 'Алисразор',
  'Ultraxion': 'Ультраксион',
  'Madness of Deathwing': 'Безумие Смертокрыла',
  "N'Zoth the Corruptor": "Н'Зот Заразитель",
  'Sylvanas Windrunner': 'Сильвана Ветрокрылая',
  'The Nine': 'Девятка',
  'The Jailer': 'Тюремщик',
  'Fyrakk the Blazing': 'Фиракк',
  "Kael'thas Sunstrider": "Кель'тас Солнечный Скиталец",
  'Attumen the Huntsman': 'Ловчий Аттумен',
  'Malygos': 'Малигос',
  'Sartharion': 'Сартарион',
  'Onyxia': 'Ониксия',
  'Yogg-Saron': 'Йогг-Сарон',
  'The Lich King': 'Король-лич',
  "Al'Akir": "Ал'акир",
  'Ragnaros': 'Рагнарос',
  'Elegon': 'Элегон',
  'Horridon': 'Хорридон',
  'Ji-Kun': 'Цзи-Кунь',
  'Garrosh Hellscream': 'Гаррош Адский Крик',
  'Blackhand': 'Чернорук',
  'Archimonde': 'Архимонд',
  "Gul'dan": "Гул'дан",
  "Mistress Sassz'ine": "Госпожа Сашж'ин",
  'Felhounds of Sargeras': 'Гончие Саргераса',
  'Argus the Unmaker': 'Аргус Порабощенный',
  'High Tinker Mekkatorque': 'Главный механик Меггакрут',
  'Lady Jaina Proudmoore': 'Леди Джайна Праудмур',
  'Queen Ansurek': 'Королева Ансурек',
  'Chrome King Gallywix': 'Хромовый король Галливикс',
  'Dimensius': 'Дименсий',
  // Подземелья
  'Lord Aurius Rivendare': 'Лорд Аурий Ривендер',
  'Anzu': 'Анзу',
  'Infinite Corruptor': 'Осквернитель из рода Бесконечности',
  'Skadi the Ruthless': 'Скади Безжалостный',
  'Slabhide': 'Камнедроб',
  'Altairus': 'Альтаирий',
  'Bloodlord Mandokir': 'Мандокир Повелитель Крови',
  'High Priestess Kilnara': 'Верховная жрица Килнара',
  'Nightbane': 'Ночная Погибель',
  'Dazar, The First King': 'Король Дазар',
  'Unbound Abomination': 'Неупокоенное чудовище',
  'Harlan Sweete': 'Красавчик Харлан',
  'King Mechagon': 'Король Мехагон',
  'HK-8 Aerial Oppression Unit': 'Воздушное судно подавления HK-8',
  'Nalthor the Rimebinder': 'Налтор Призыватель Зимы',
  "So'leah": "Со'лея",
  'Void Speaker Eirich': 'Верховный оратор Эйрих',
  'The Darkness': 'Тьма',
  // Мировые боссы
  'Sha of Anger': 'Ша Злости',
  'Galleon': 'Галлеон',
  'Oondasta': 'Ундаста',
  'Nalak': 'Налак',
  'Rukhmar': 'Рухмар',
};

// ===================================================================

const VERSION = '1.4.0';

/** Проверка в браузере: откройте URL веб-приложения — увидите ответ. */
function doGet() {
  return json_({
    ok: true,
    app: 'Mount Sheet Sync',
    version: VERSION,
    message: 'Приёмник работает. Данные присылает программа Mount Sheet Sync (MountSheetSync.exe).',
  });
}

function doPost(e) {
  let req;
  try {
    req = JSON.parse((e && e.postData && e.postData.contents) || '{}');
  } catch (err) {
    return json_({ ok: false, error: 'Запрос не является JSON.' });
  }
  if (!req || req.secret !== SECRET) {
    return json_({ ok: false, error: 'Неверный secret: ключ в настройках программы Mount Sheet Sync должен совпадать с SECRET в Code.gs.' });
  }

  const lock = LockService.getScriptLock();
  if (!lock.tryLock(30000)) {
    return json_({ ok: false, error: 'Таблица занята другим запросом, повторите позже.' });
  }
  try {
    const ss = SpreadsheetApp.getActiveSpreadsheet();
    if (!ss) {
      return json_({
        ok: false,
        error: 'Скрипт не привязан к таблице: откройте его из самой таблицы (Расширения → Apps Script).',
      });
    }
    const book = new Book_(ss);
    if (req.action === 'ping') {
      return json_(ping_(book, req));
    }
    if (req.action === 'status') {
      return json_(status_(book));
    }
    const kills = Array.isArray(req.kills) ? req.kills : [];
    const results = kills.map(kill => processKill_(book, kill, req.client));
    if (!req.dryRun) {
      book.flush();
      if (results.some(r => r.status === 'reset')) {
        rememberReset_();
      }
    }
    return json_({ ok: true, version: VERSION, results: results });
  } catch (err) {
    return json_({ ok: false, error: String(err && err.message ? err.message : err) });
  } finally {
    lock.releaseLock();
  }
}

// ------------------------------------------------------------------
// Обработка одного убийства
// ------------------------------------------------------------------

/**
 * Статусы: marked — поставлено «Да»; already — уже было «Да»; reset — сброс недели;
 * wrong_difficulty — босс есть, но для другой сложности; not_found — босса
 * нет на листах персонажа; no_sheet — нет листа персонажа; stale — убийство
 * прошлой недели; invalid — неполная запись.
 * Для нового маунта (kind = 'mount'): collected — «Собрано?» стало «Да»;
 * already — уже было «Да»; not_found — маунта нет в колонке «Маунт».
 */
function processKill_(book, kill, client) {
  const out = {
    id: kill && kill.id,
    status: 'not_found',
    char: kill && kill.char,
    boss: kill && kill.boss,
    cells: [],
    message: '',
  };
  if (!kill || !kill.id || !kill.boss || !kill.char) {
    out.status = 'invalid';
    out.message = 'в записи нет id, персонажа или босса';
    return out;
  }
  const kind = String(kill.kind || 'manual');
  if (kind === 'mount') {
    return markCollected_(book, kill, out); // собранный маунт остаётся собранным и после сброса недели
  }
  const weekEnd = Number(kill.weekEnd);
  if (weekEnd > 0 && weekEnd * 1000 <= Date.now()) {
    out.status = 'stale';
    out.message = 'убийство до еженедельного сброса — пропущено';
    return out;
  }

  if (kind === 'reset') {
    if (isOldClient_(client)) {
      // старый скрипт не умеет заново отмечать убийства недели после сброса
      out.status = 'needs_update';
      out.message = 'обновите программу синхронизации и нажмите «Сброс недели» ещё раз';
      return out;
    }
    return resetWeek_(book, out);
  }
  const charKeys = characterKeys_(kill);
  const names = bossNames_(kill.boss);
  const ctx = { cells: out.cells, wrongDifficulty: [] };

  const lists = [];
  if (kind === 'raid' || kind === 'manual') {
    lists.push(...book.ownedBy(SHEET_PREFIXES.raid, charKeys));
  }
  if (kind === 'party' || kind === 'scenario' || kind === 'manual') {
    lists.push(...book.ownedBy(SHEET_PREFIXES.dungeon, charKeys));
  }
  lists.forEach(entry => markInList_(book, entry, kill, names, ctx));

  let worldFound = false;
  if (kind === 'world' || kind === 'manual') {
    worldFound = markInWorld_(book, kill, charKeys, names, ctx);
  }

  if (out.cells.some(c => c.changed)) {
    out.status = 'marked';
  } else if (out.cells.length) {
    out.status = 'already';
  } else if (!lists.length && !worldFound) {
    out.status = 'no_sheet';
    out.message = noSheetMessage_(kill, kind);
  } else if (ctx.wrongDifficulty.length) {
    out.status = 'wrong_difficulty';
    out.message = 'в таблице этот босс только для сложности: ' + unique_(ctx.wrongDifficulty).join('; ');
  } else {
    out.message = 'такого босса нет на листах персонажа';
  }
  return out;
}

/**
 * Сброс недели: «Нет» в колонке «Пытался на этой неделе?» на всех листах
 * персонажей и снятые галочки в колонках персонажей на «Мировых боссах».
 * Другие колонки («Собрано?», «Скип есть?» и т. д.) и общие листы не трогаются.
 */
function resetWeek_(book, out) {
  let count = 0;
  const known = new Set();
  book.entries.forEach(entry => {
    const owner = sheetOwner_(entry.name, SHEET_PREFIXES.raid) || sheetOwner_(entry.name, SHEET_PREFIXES.dungeon);
    if (!owner) {
      return;
    }
    known.add(norm_(owner));
    const data = book.values(entry);
    const cStatus = data.length ? findCol_(data[0], /пытался/, -1) : -1;
    if (cStatus < 0) {
      return; // без заголовка «Пытался…» лист не трогаем
    }
    for (let r = 1; r < data.length; r++) {
      count += resetCell_(book, entry, r, cStatus);
    }
  });

  const world = book.byName(WORLD_SHEET);
  if (world) {
    const data = book.values(world);
    const header = data[0] || [];
    for (let c = 0; c < header.length; c++) {
      if (!isCharacterColumn_(data, c, known)) {
        continue;
      }
      for (let r = 1; r < data.length; r++) {
        count += resetCell_(book, world, r, c);
      }
    }
  }
  out.status = 'reset';
  out.count = count;
  out.message = '«Нет» поставлено, ячеек: ' + count;
  return out;
}

/** «ps-1.0.0» и старше не поддерживают сброс недели. */
function isOldClient_(client) {
  const m = /^ps-(\d+)\.(\d+)/.exec(String(client || ''));
  return !!m && (Number(m[1]) < 1 || (Number(m[1]) === 1 && Number(m[2]) < 1));
}

function resetCell_(book, entry, r, c) {
  const value = book.values(entry)[r][c];
  if (value === true) {
    book.set(entry, r, c, false);
    return 1;
  }
  if (typeof value === 'string' && isYes_(value)) {
    book.set(entry, r, c, NO);
    return 1;
  }
  return 0;
}

// ------------------------------------------------------------------
// Новый маунт → «Собрано?»
// ------------------------------------------------------------------

/**
 * Маунт появился в игре: «Собрано?» → «Да» во всех строках с ним на листах,
 * где есть колонки «Собрано?» и «Маунт» («Рейды», «Подземелья», «Мировые боссы»).
 * Листы персонажей таких колонок не имеют и не меняются.
 */
function markCollected_(book, kill, out) {
  out.kind = 'mount';
  const name = String(kill.mount || kill.boss || '');
  const from = String(kill.from || '');
  book.entries.forEach(entry => {
    const data = book.values(entry);
    if (!data.length) {
      return;
    }
    const header = data[0];
    const cCollected = findCol_(header, /собрано/, -1);
    const cMount = findCol_(header, /маунт/, -1);
    if (cCollected < 0 || cMount < 0) {
      return;
    }
    const cBoss = findCol_(header, /босс/, -1);
    for (let r = 1; r < data.length; r++) {
      const row = data[r];
      if (!mountMatches_(name, row[cMount], from, cBoss >= 0 ? row[cBoss] : '')) {
        continue;
      }
      if (book.isFormula(entry, r, cCollected)) {
        continue; // значение считает формула — его не трогаем
      }
      const current = row[cCollected];
      let changed = false;
      if (isNo_(current)) {
        book.set(entry, r, cCollected, current === false ? true : YES);
        changed = true;
      } else if (!isYes_(current)) {
        continue;
      }
      out.cells.push({ sheet: entry.name, row: r + 1, mount: String(row[cMount] || ''), changed: changed });
    }
  });
  if (out.cells.some(c => c.changed)) {
    out.status = 'collected';
  } else if (out.cells.length) {
    out.status = 'already';
  } else {
    out.status = 'not_found';
    out.message = 'маунта «' + name + '» нет в колонке «Маунт» — отметьте «Собрано?» вручную';
  }
  return out;
}

/** Слова, по которым игра и таблица называют предмет («Поводья …») и сам маунт. */
const ITEM_WORDS = new Set(['поводья', 'повод', 'уздечка', 'свисток', 'reins']);

function mountWords_(value) {
  const text = String(value == null ? '' : value).replace(/\([^)]*\)|\[[^\]]*\]/g, ' ');
  return norm_(text).split(' ').filter(w => w && !ITEM_WORDS.has(w));
}

/** Одно слово в разных падежах: «синий» ↔ «синего», «протодракон» ↔ «протодракона». */
function sameStem_(a, b) {
  if (a === b) {
    return true;
  }
  let i = 0;
  while (i < a.length && i < b.length && a[i] === b[i]) {
    i++;
  }
  return i >= 3 && a.length - i <= 3 && b.length - i <= 3;
}

/** Каждому слову из xs нашлось своё слово в ys. */
function coversWords_(xs, ys) {
  const used = ys.map(() => false);
  return xs.every(x => {
    const j = ys.findIndex((y, k) => !used[k] && sameStem_(x, y));
    if (j < 0) {
      return false;
    }
    used[j] = true;
    return true;
  });
}

/**
 * Имя маунта из игры ↔ ячейка «Маунт». Совпадают все слова (с учётом падежей:
 * «Синий протодракон» ↔ «Поводья синего протодракона») или — если источник маунта
 * в игре называет босса этой строки — все слова одного имени есть в другом
 * («Конь смерти Ривендера» ↔ «Поводья коня смерти», босс «Лорд Аурий Ривендер»).
 */
function mountMatches_(gameName, cell, sourceText, bossCell) {
  const a = mountWords_(gameName);
  const b = mountWords_(cell);
  if (!a.length || !b.length) {
    return false;
  }
  if (a.length === b.length && coversWords_(a, b)) {
    return true;
  }
  return bossNamed_(sourceText, bossCell) && (coversWords_(b, a) || coversWords_(a, b));
}

/** Текст источника («Добыча: Скади Безжалостный / Подземелье: …») называет босса строки. */
function bossNamed_(sourceText, bossCell) {
  const words = new Set(norm_(sourceText).split(' ').filter(Boolean));
  if (!words.size) {
    return false;
  }
  return String(bossCell == null ? '' : bossCell).split('/').some(alt => {
    const need = norm_(alt).split(' ').filter(Boolean);
    return need.length > 0 && need.every(w => words.has(w));
  });
}

/** Колонка персонажа на «Мировых боссах»: по имени из листов или колонка из одних галочек. */
function isCharacterColumn_(data, c, known) {
  const head = norm_(stripNote_(data[0][c]));
  if (head && known.has(head)) {
    return true;
  }
  let checkboxes = 0;
  for (let r = 1; r < data.length; r++) {
    const value = data[r][c];
    if (value === '' || value == null) {
      continue;
    }
    if (typeof value !== 'boolean') {
      return false;
    }
    checkboxes++;
  }
  return checkboxes > 0;
}

/** Листы «Рейды <Имя>» / «Подземелья <Имя>». */
function markInList_(book, entry, kill, names, ctx) {
  const data = book.values(entry);
  if (!data.length) {
    return;
  }
  const header = data[0];
  const cStatus = findCol_(header, /пытался/, 0);
  const cBoss = findCol_(header, /босс/, 4);
  const cDiff = findCol_(header, /сложност|режим/, 5);
  const cMount = findCol_(header, /маунт/, 3);
  const cPlace = findCol_(header, /^рейд|подземел/, 2);

  for (let r = 1; r < data.length; r++) {
    const row = data[r];
    const match = rowMatch_(kill, names, row[cBoss], row[cDiff], row[cPlace]);
    if (match === 'difficulty') {
      ctx.wrongDifficulty.push(String(row[cDiff]));
      continue;
    }
    if (match !== true) {
      continue;
    }
    const current = row[cStatus];
    let changed = false;
    if (isNo_(current)) {
      book.set(entry, r, cStatus, current === false ? true : YES);
      changed = true;
    } else if (!isYes_(current)) {
      continue; // «Недоступно» и прочие значения не трогаем
    }
    ctx.cells.push({ sheet: entry.name, row: r + 1, mount: String(row[cMount] || ''), changed: changed });
  }
}

/** Лист «Мировые боссы»: галочка в колонке персонажа. */
function markInWorld_(book, kill, charKeys, names, ctx) {
  const entry = book.byName(WORLD_SHEET);
  if (!entry) {
    return false;
  }
  const data = book.values(entry);
  if (!data.length) {
    return false;
  }
  const header = data[0];
  const cBoss = findCol_(header, /босс/, 4);
  const cMount = findCol_(header, /маунт/, 3);
  let cChar = -1;
  header.forEach((h, i) => {
    if (cChar < 0 && charKeys.has(norm_(stripNote_(h)))) {
      cChar = i;
    }
  });
  if (cChar < 0) {
    return false;
  }

  for (let r = 1; r < data.length; r++) {
    const row = data[r];
    const text = String(row[cBoss] == null ? '' : row[cBoss]);
    const loose = kill.kind === 'manual';
    const hit = text.trim() && text.split('/').some(alt => names.some(n => nameMatches_(n, alt, loose)));
    if (!hit) {
      continue;
    }
    const current = row[cChar];
    let changed = false;
    if (isNo_(current)) {
      book.set(entry, r, cChar, current === false ? true : YES);
      changed = true;
    } else if (!isYes_(current)) {
      continue;
    }
    ctx.cells.push({ sheet: entry.name, row: r + 1, mount: String(row[cMount] || ''), changed: changed });
  }
  return true;
}

/** true — строка подходит; 'difficulty' — босс тот, сложность другая; false — нет. */
function rowMatch_(kill, names, bossCell, diffCell, placeCell) {
  const text = String(bossCell == null ? '' : bossCell).trim();
  if (!text) {
    return false;
  }
  const manual = kill.kind === 'manual';
  let named = false;
  let anyBoss = false;
  text.split('/').forEach(alt => {
    if (isAnyBoss_(alt)) {
      if (!manual && anyBossApplies_(kill, alt, diffCell, placeCell)) {
        anyBoss = true;
      }
    } else if (names.some(n => nameMatches_(n, alt, manual))) {
      named = true;
    }
  });
  if (!named && !anyBoss) {
    return false;
  }
  if (manual) {
    return true; // отметка вручную — сложность не проверяем
  }
  if (named && difficultyMatches_(kill, diffCell, false)) {
    return true;
  }
  // «Любой босс …» засчитываем только при точно известной подходящей сложности
  if (anyBoss && difficultyMatches_(kill, diffCell, true)) {
    return true;
  }
  return named ? 'difficulty' : false;
}

// ------------------------------------------------------------------
// Сравнение имён
// ------------------------------------------------------------------

function norm_(value) {
  return String(value == null ? '' : value)
    .toLowerCase()
    .replace(/ё/g, 'е')
    .replace(/['’‘`ʼ´]/g, '')
    .replace(/[^\p{L}\p{N}]+/gu, ' ')
    .trim();
}

function stripNote_(value) {
  return String(value == null ? '' : value).replace(/\([^)]*\)/g, ' ').replace(/\s+/g, ' ').trim();
}

/**
 * «Фиракк» в таблице совпадает с «Фиракк Пылающий» из игры, а
 * «Инструктор Разувий» из игры — с «Инструктор Разувий (секрет …)» в таблице.
 * loose (ручная отметка /mss add): хватает одного слова — «Сильвана».
 */
function nameMatches_(gameName, tableName, loose) {
  const a = norm_(gameName);
  const b = norm_(tableName);
  if (!a || !b) {
    return false;
  }
  if (a === b) {
    return true;
  }
  const wa = a.split(' ');
  const wb = b.split(' ');
  const sa = new Set(wa);
  const sb = new Set(wb);
  if (b.length >= 4 && wb.every(w => sa.has(w))) {
    return true;
  }
  const minWords = loose && a.length >= 4 ? 1 : 2;
  return wa.length >= minWords && wa.every(w => sb.has(w));
}

function bossNames_(boss) {
  const names = [String(boss)];
  Object.keys(BOSS_ALIASES).forEach(key => {
    if (nameMatches_(boss, key)) {
      names.push(BOSS_ALIASES[key]);
    }
  });
  return names;
}

function characterKeys_(kill) {
  const name = String((kill && kill.char) || '').trim();
  const realm = String((kill && kill.realm) || '').trim();
  const keys = new Set();
  const add = v => {
    const n = norm_(v);
    if (n) {
      keys.add(n);
    }
  };
  add(name);
  if (name && realm) {
    add(name + '-' + realm);
    add(name + '-' + realm.replace(/\s+/g, ''));
  }
  Object.keys(CHARACTER_ALIASES).forEach(key => {
    if (keys.has(norm_(key))) {
      add(CHARACTER_ALIASES[key]);
    }
  });
  return keys;
}

/** «Рейды Каэлия (Лок)» → «Каэлия»; общий лист «Рейды» → null. */
function sheetOwner_(sheetName, prefix) {
  const name = String(sheetName).trim();
  if (name.slice(0, prefix.length).toLowerCase() !== prefix.toLowerCase()) {
    return null;
  }
  const rest = name.slice(prefix.length);
  if (!/^\s/.test(rest)) {
    return null;
  }
  return stripNote_(rest) || null;
}

/** «Любой босс …» и «Боссы …» — строки, которые подходят к любому боссу места. */
function isAnyBoss_(text) {
  return /^(любой|любые|все) босс|^any boss|^боссы /.test(norm_(text));
}

function anyBossApplies_(kill, text, diffCell, placeCell) {
  if (parseDifficulty_(diffCell).kinds.has('timewalking') || /путешеств|timewalking/.test(norm_(text))) {
    return true; // «Любой босс подземелий путешествий во времени» — проверим только режим
  }
  return placeMentioned_(kill.instance, text) || placeMentioned_(kill.instance, placeCell);
}

const PLACE_STOP_WORDS = new Set([
  'любой', 'любые', 'все', 'всех', 'босс', 'боссы', 'боссов', 'any', 'boss', 'bosses', 'the',
]);

function placeWords_(text) {
  return norm_(text).split(' ').filter(w => w.length >= 3 && !PLACE_STOP_WORDS.has(w));
}

/**
 * Подземелье названо в тексте (с учётом падежей): «Склеп Аркавона» ↔
 * «Любой босс Склепа Аркавона», «Торгаст, Башня Проклятых» ↔ «Боссы Торгаста».
 */
function placeMentioned_(place, text) {
  const a = placeWords_(place);
  const b = placeWords_(text);
  if (!a.length || !b.length) {
    return false;
  }
  const covers = (xs, ys) => xs.every(x => ys.some(y => sameWord_(x, y)));
  return covers(a, b) || covers(b, a);
}

function sameWord_(a, b) {
  if (a === b) {
    return true;
  }
  let i = 0;
  while (i < a.length && i < b.length && a[i] === b[i]) {
    i++;
  }
  return i >= Math.max(4, Math.min(a.length, b.length) - 2);
}

// ------------------------------------------------------------------
// Сложность
// ------------------------------------------------------------------

/** DifficultyID из игры → вид и размер рейда. */
const DIFFICULTY = {
  1: { kind: 'normal' },
  2: { kind: 'heroic' },
  3: { kind: 'normal', size: 10 },
  4: { kind: 'normal', size: 25 },
  5: { kind: 'heroic', size: 10 },
  6: { kind: 'heroic', size: 25 },
  7: { kind: 'lfr' },
  8: { kind: 'mythicplus' },
  9: { kind: 'normal', size: 40 },
  14: { kind: 'normal' },
  15: { kind: 'heroic' },
  16: { kind: 'mythic' },
  17: { kind: 'lfr' },
  23: { kind: 'mythic' },
  24: { kind: 'timewalking' },
  33: { kind: 'timewalking' },
  150: { kind: 'normal' },
  151: { kind: 'timewalking' },
  205: { kind: 'follower' },
  220: { kind: 'story' },
};

/** Разбор текста «Сложность / режим»: Normal/Heroic, Mythic, Heroic 25, 10/25, Любая… */
function parseDifficulty_(text) {
  const s = String(text == null ? '' : text).toLowerCase().replace(/ё/g, 'е');
  const req = { any: false, kinds: new Set(), sizes: new Set() };
  if (!s.trim()) {
    return req;
  }
  if (/любая|любой|any(?![a-z])|все сложности/.test(s)) {
    req.any = true;
  }
  const mythicPlus = /(mythic|мифик|эпохал[а-я]*)\s*\+|(^|[^a-z])m\+|ключ|keystone|challenge/;
  if (mythicPlus.test(s)) {
    req.kinds.add('mythicplus');
  }
  const rest = s.replace(/(mythic|мифик|эпохал[а-я]*)\s*\+/g, ' ').replace(/(^|[^a-z])m\+/g, ' ');
  if (/mythic|мифик|эпохал/.test(rest)) {
    req.kinds.add('mythic');
  }
  if (/heroic|героич/.test(s)) {
    req.kinds.add('heroic');
  }
  if (/normal|обычн|нормал/.test(s)) {
    req.kinds.add('normal');
  }
  if (/(^|[^a-z])lfr([^a-z]|$)|поиск рейда|raid finder/.test(s)) {
    req.kinds.add('lfr');
  }
  if (/timewalking|путешеств/.test(s)) {
    req.kinds.add('timewalking');
  }
  [10, 25, 40].forEach(n => {
    if (new RegExp('(^|\\D)' + n + '(\\D|$)').test(s)) {
      req.sizes.add(n);
    }
  });
  return req;
}

/** strict: неизвестная сложность не подходит (для строк «Любой босс …»). */
function difficultyMatches_(kill, diffCell, strict) {
  const req = parseDifficulty_(diffCell);
  if (req.any || (!req.kinds.size && !req.sizes.size)) {
    return true;
  }
  const d = DIFFICULTY[Number(kill.difficultyID)];
  if (!d) {
    return !strict; // неизвестная сложность — обычную строку не блокируем
  }
  if (req.kinds.size && !req.kinds.has(d.kind) && !(d.kind === 'mythicplus' && req.kinds.has('mythic'))) {
    return false;
  }
  return !(req.sizes.size && d.size && !req.sizes.has(d.size));
}

// ------------------------------------------------------------------
// Проверка связи
// ------------------------------------------------------------------

function ping_(book, req) {
  const chars = new Map();
  book.entries.forEach(e => {
    Object.keys(SHEET_PREFIXES).forEach(k => {
      const owner = sheetOwner_(e.name, SHEET_PREFIXES[k]);
      if (owner) {
        const key = norm_(owner);
        if (!chars.has(key)) {
          chars.set(key, { name: owner, sheets: [] });
        }
        chars.get(key).sheets.push(e.name);
      }
    });
  });
  const world = book.byName(WORLD_SHEET);
  if (world) {
    (book.values(world)[0] || []).forEach(h => {
      const key = norm_(stripNote_(h));
      if (chars.has(key)) {
        chars.get(key).sheets.push(world.name);
      }
    });
  }
  const list = Array.from(chars.values());
  const asked = Array.isArray(req.chars) ? req.chars : [];
  const checked = asked.map(c => {
    const keys = characterKeys_(c);
    const found = list.filter(x => keys.has(norm_(x.name)));
    return {
      char: c && c.char,
      realm: c && c.realm,
      found: found.length > 0,
      sheets: found.reduce((all, x) => all.concat(x.sheets), []),
    };
  });
  return {
    ok: true,
    version: VERSION,
    spreadsheet: book.ss.getName(),
    url: spreadsheetUrl_(book.ss),
    characters: list,
    checked: checked,
  };
}

/** Ссылка на таблицу — для пункта «Открыть таблицу» в меню программы. */
function spreadsheetUrl_(ss) {
  try {
    return ss.getUrl();
  } catch (err) {
    return '';
  }
}

// ------------------------------------------------------------------
// «Осталось на этой неделе» — для окна аддона
// ------------------------------------------------------------------

/**
 * Для каждого персонажа — строки, где в «Пытался на этой неделе?» стоит «Нет»
 * или «Да» (у них y: true), и галочки персонажа на «Мировых боссах»; маунты,
 * у которых в общих листах «Собрано?» = «Да», и строки «Недоступно»
 * пропускаются. К строке приложены имена босса (как их сравнивает этот скрипт)
 * и подходящие DifficultyID: по ним аддон сам отмечает боссов, убитых после
 * того, как список попал в игру. Строки «Да» аддону нужны после «Сброса недели»:
 * тогда они снова «Нет». resetAt — когда в таблице последний раз был сброс недели.
 */
function status_(book) {
  const collected = collectedMounts_(book);
  const chars = new Map();
  const charFor = owner => {
    const key = norm_(owner);
    if (!chars.has(key)) {
      chars.set(key, { name: owner, keys: ownerKeys_(owner), items: [] });
    }
    return chars.get(key);
  };

  book.entries.forEach(entry => {
    Object.keys(SHEET_PREFIXES).forEach(kind => {
      const owner = sheetOwner_(entry.name, SHEET_PREFIXES[kind]);
      if (!owner) {
        return;
      }
      const ch = charFor(owner);
      const data = book.values(entry);
      if (!data.length) {
        return;
      }
      const header = data[0];
      const cStatus = findCol_(header, /пытался/, -1);
      if (cStatus < 0) {
        return;
      }
      const cBoss = findCol_(header, /босс/, 4);
      const cDiff = findCol_(header, /сложност|режим/, 5);
      const cMount = findCol_(header, /маунт/, 3);
      const cPlace = findCol_(header, /^рейд|подземел/, 2);
      for (let r = 1; r < data.length; r++) {
        const row = data[r];
        const boss = cellText_(row[cBoss]);
        const mount = cellText_(row[cMount]);
        const yes = isYes_(row[cStatus]);
        if ((!boss && !mount) || (!yes && !isNo_(row[cStatus])) || collected.has(norm_(mount))) {
          continue;
        }
        ch.items.push(statusItem_(kind, entry.name, r, row[cPlace], boss, row[cDiff], mount, yes));
      }
    });
  });

  const world = book.byName(WORLD_SHEET);
  if (world) {
    const data = book.values(world);
    const header = data[0] || [];
    const cBoss = findCol_(header, /босс/, 4);
    const cMount = findCol_(header, /маунт/, 3);
    const cPlace = findCol_(header, /локац/, 2);
    const cCollected = findCol_(header, /собрано/, -1);
    const known = new Set(chars.keys());
    for (let c = 0; c < header.length; c++) {
      if (c === cCollected || !isCharacterColumn_(data, c, known)) {
        continue;
      }
      const owner = stripNote_(header[c]);
      if (!owner) {
        continue;
      }
      const ch = charFor(owner);
      for (let r = 1; r < data.length; r++) {
        const row = data[r];
        const boss = cellText_(row[cBoss]);
        const mount = cellText_(row[cMount]);
        const yes = isYes_(row[c]);
        if ((!boss && !mount) || (!yes && !isNo_(row[c]))) {
          continue;
        }
        if ((cCollected >= 0 && isYes_(row[cCollected])) || collected.has(norm_(mount))) {
          continue;
        }
        ch.items.push(statusItem_('world', world.name, r, row[cPlace], boss, '', mount, yes));
      }
    }
  }

  return {
    ok: true,
    version: VERSION,
    spreadsheet: book.ss.getName(),
    generated: Math.floor(Date.now() / 1000),
    resetAt: lastReset_(),
    known: Object.keys(DIFFICULTY).map(Number),
    chars: Array.from(chars.values()),
  };
}

const RESET_PROPERTY = 'lastWeekReset';

/** Когда «Сброс недели» дошёл до таблицы — чтобы аддон знал, была ли неделя уже сброшена. */
function rememberReset_() {
  try {
    PropertiesService.getScriptProperties().setProperty(RESET_PROPERTY, String(Math.floor(Date.now() / 1000)));
  } catch (err) {
    // не страшно: аддон тогда смотрит на сбросы в своём журнале
  }
}

function lastReset_() {
  try {
    const value = Number(PropertiesService.getScriptProperties().getProperty(RESET_PROPERTY));
    return value > 0 ? value : undefined;
  } catch (err) {
    return undefined;
  }
}

/**
 * Строка списка. names — как norm_ называет босса (и английские имена из BOSS_ALIASES);
 * y — в таблице уже «Да».
 */
function statusItem_(kind, sheet, r, place, boss, diff, mount, yes) {
  const alts = String(boss).split('/').map(s => s.trim()).filter(Boolean);
  const anyAlt = alts.find(alt => isAnyBoss_(alt));
  const names = [];
  alts.forEach(alt => {
    if (isAnyBoss_(alt)) {
      return;
    }
    names.push(norm_(alt));
    Object.keys(BOSS_ALIASES).forEach(key => {
      if (nameMatches_(BOSS_ALIASES[key], alt)) {
        names.push(norm_(key));
      }
    });
  });
  const item = {
    kind: kind,
    boss: boss,
    diff: cellText_(diff),
    mount: mount,
    place: cellText_(place),
    sheet: sheet,
    row: r + 1,
    names: unique_(names.filter(Boolean)),
    d: difficultyIds_(diff),
  };
  if (yes) {
    item.y = true;
  }
  if (anyAlt) {
    // «Любой босс …»: как в anyBossApplies_ — режим путешествий во времени или упомянутое место
    item.any = true;
    item.tw = parseDifficulty_(diff).kinds.has('timewalking') || /путешеств|timewalking/.test(norm_(anyAlt));
    item.aw = [placeWords_(anyAlt), placeWords_(place)];
  }
  return item;
}

/** DifficultyID, при которых строка засчитывается; пустой список — любая сложность. */
function difficultyIds_(diff) {
  const all = Object.keys(DIFFICULTY).map(Number);
  const ok = all.filter(id => difficultyMatches_({ difficultyID: id }, diff, false));
  return ok.length === all.length ? [] : ok;
}

/** Маунты, у которых в общих листах «Собрано?» = «Да» (по тексту ячейки «Маунт»). */
function collectedMounts_(book) {
  const set = new Set();
  book.entries.forEach(entry => {
    const data = book.values(entry);
    if (!data.length) {
      return;
    }
    const cCollected = findCol_(data[0], /собрано/, -1);
    const cMount = findCol_(data[0], /маунт/, -1);
    if (cCollected < 0 || cMount < 0) {
      return;
    }
    for (let r = 1; r < data.length; r++) {
      const key = norm_(data[r][cMount]);
      if (key && isYes_(data[r][cCollected])) {
        set.add(key);
      }
    }
  });
  return set;
}

/** Как аддон найдёт своего персонажа: имя листа и имена из CHARACTER_ALIASES. */
function ownerKeys_(owner) {
  const keys = new Set([norm_(owner)]);
  Object.keys(CHARACTER_ALIASES).forEach(game => {
    if (norm_(stripNote_(CHARACTER_ALIASES[game])) === norm_(owner)) {
      keys.add(norm_(game));
    }
  });
  return Array.from(keys).filter(Boolean);
}

function cellText_(value) {
  return String(value == null ? '' : value).trim();
}

// ------------------------------------------------------------------
// Служебное
// ------------------------------------------------------------------

class Book_ {
  constructor(ss) {
    this.ss = ss;
    this.entries = ss.getSheets().map(sheet => ({ sheet: sheet, name: sheet.getName(), data: null }));
    this.writes = [];
  }

  values(entry) {
    if (!entry.data) {
      entry.data = entry.sheet.getDataRange().getValues();
    }
    return entry.data;
  }

  byName(name) {
    const n = norm_(name);
    return this.entries.find(e => norm_(e.name) === n) || null;
  }

  /** Есть ли в ячейке формула (её значение не перезаписываем). */
  isFormula(entry, r, c) {
    if (!entry.formulas) {
      entry.formulas = entry.sheet.getDataRange().getFormulas();
    }
    const row = entry.formulas[r];
    return !!(row && row[c]);
  }

  ownedBy(prefix, charKeys) {
    return this.entries.filter(e => {
      const owner = sheetOwner_(e.name, prefix);
      return owner !== null && charKeys.has(norm_(owner));
    });
  }

  set(entry, r, c, value) {
    this.values(entry)[r][c] = value;
    this.writes.push({ entry: entry, r: r, c: c, value: value });
  }

  /** Пишет только изменённые ячейки; соседние строки одной колонки — одним вызовом. */
  flush() {
    const last = new Map();
    this.writes.forEach(w => last.set(w.entry.name + '|' + w.r + '|' + w.c, w));
    const columns = new Map();
    last.forEach(w => {
      const key = w.entry.name + '|' + w.c;
      if (!columns.has(key)) {
        columns.set(key, []);
      }
      columns.get(key).push(w);
    });
    columns.forEach(list => {
      list.sort((a, b) => a.r - b.r);
      let start = 0;
      for (let i = 1; i <= list.length; i++) {
        if (i === list.length || list[i].r !== list[i - 1].r + 1) {
          const run = list.slice(start, i);
          run[0].entry.sheet.getRange(run[0].r + 1, run[0].c + 1, run.length, 1).setValues(run.map(w => [w.value]));
          start = i;
        }
      }
    });
    if (last.size) {
      SpreadsheetApp.flush();
    }
    this.writes = [];
  }
}

function findCol_(header, re, fallback) {
  for (let i = 0; i < header.length; i++) {
    if (re.test(norm_(header[i]))) {
      return i;
    }
  }
  return fallback;
}

function isNo_(value) {
  return value === false || value === '' || /^(нет|no|false)$/.test(norm_(value));
}

function isYes_(value) {
  return value === true || /^(да|yes|true)$/.test(norm_(value));
}

function unique_(list) {
  return Array.from(new Set(list));
}

function noSheetMessage_(kill, kind) {
  const who = String(kill.char);
  if (kind === 'raid') {
    return 'нет листа «' + SHEET_PREFIXES.raid + ' ' + who + '»';
  }
  if (kind === 'party' || kind === 'scenario') {
    return 'нет листа «' + SHEET_PREFIXES.dungeon + ' ' + who + '»';
  }
  if (kind === 'world') {
    return 'нет колонки «' + who + '» на листе «' + WORLD_SHEET + '»';
  }
  return 'нет листов для персонажа «' + who + '»';
}

function json_(obj) {
  return ContentService.createTextOutput(JSON.stringify(obj)).setMimeType(ContentService.MimeType.JSON);
}
