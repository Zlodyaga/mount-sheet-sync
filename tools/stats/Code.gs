/**
 * Mount Sheet Sync — приёмник анонимной статистики (Google Apps Script, веб-приложение автора).
 *
 * Программа раз в сутки шлёт сюда одно сообщение JSON (program/Stats.cs), только если игрок согласился.
 * Скрипт кладёт его строкой на лист «data» этой таблицы. IP отправителя скрипту не виден и нигде не хранится.
 * Строки старше 12 месяцев убирает prune() — по ежедневному триггеру (ставится один раз: setup()).
 *
 * Установка — tools/stats/README.md.
 */

var SHEET = 'data';
var KEEP_DAYS = 365;
var HEADER = ['Получено', 'Номер установки', 'Программа', 'Аддон', 'Windows', 'Язык программы', 'Язык игры', 'Где данные',
  'Персонажей', 'Mapzeroth', 'День счётчиков', 'Счётчики'];
/** Счётчики, которые знает аддон; другие имена отбрасываются. */
var COUNTERS = ['open', 'route', 'manual', 'tab_left', 'tab_ach', 'tab_attempts', 'tab_log', 'tab_drops'];
/** Что может стоять в каждом поле: всё остальное становится пустым. */
var RULES = {
  id: /^[0-9a-f]{32}$/,
  program: /^[0-9A-Za-z.\-]{1,20}$/,
  addon: /^[0-9A-Za-z.\-]{1,20}$/,
  windows: /^[0-9]{1,3}$/,
  lang: /^[a-z]{2,5}$/,
  game: /^[a-z]{2}[A-Z]{2}$/,
  mode: /^(local|google)$/,
  chars: /^(0|1|2-5|6-10|11\+)$/,
  day: /^\d{4}-\d{2}-\d{2}$/
};

function sheet_() {
  var book = SpreadsheetApp.getActiveSpreadsheet();
  var sheet = book.getSheetByName(SHEET) || book.insertSheet(SHEET);
  if (sheet.getLastRow() === 0) {
    sheet.appendRow(HEADER);
    sheet.setFrozenRows(1);
  }
  return sheet;
}

function clean_(message, field) {
  var value = message[field];
  value = value === undefined || value === null ? '' : String(value);
  return RULES[field].test(value) ? value : '';
}

function doPost(e) {
  var answer = { ok: false };
  try {
    var text = e && e.postData && e.postData.contents || '';
    if (text.length > 0 && text.length <= 2000) {
      var message = JSON.parse(text);
      if (message && message.v === 1 && clean_(message, 'id')) {
        var use = {};
        var given = message.use && typeof message.use === 'object' ? message.use : {};
        COUNTERS.forEach(function (name) {
          var n = Number(given[name]);
          if (n > 0 && n <= 100000) {
            use[name] = Math.floor(n);
          }
        });
        // день без времени: по времени строки игрока не сопоставить с чем-то ещё
        var today = Utilities.formatDate(new Date(), 'UTC', 'yyyy-MM-dd');
        // апостроф впереди: иначе таблица читает «2.10.2» как дату, а номер из одних цифр — как число
        var text_ = function (field) { return "'" + clean_(message, field); };
        sheet_().appendRow(["'" + today, text_('id'), text_('program'), text_('addon'),
          text_('windows'), text_('lang'), text_('game'), text_('mode'),
          text_('chars'), message.mapzeroth === true ? "'1" : message.mapzeroth === false ? "'0" : '',
          text_('day'), "'" + JSON.stringify(use)]);
        answer.ok = true;
      }
    }
  } catch (err) {
    answer.ok = false;
  }
  return ContentService.createTextOutput(JSON.stringify(answer)).setMimeType(ContentService.MimeType.JSON);
}

/** Убрать строки старше KEEP_DAYS. Строки идут по порядку получения, старые — сверху. */
function prune() {
  var sheet = sheet_();
  var last = sheet.getLastRow();
  if (last < 2) {
    return;
  }
  var limit = Utilities.formatDate(new Date(Date.now() - KEEP_DAYS * 86400000), 'UTC', 'yyyy-MM-dd');
  var days = sheet.getRange(2, 1, last - 1, 1).getDisplayValues();
  var old = 0;
  while (old < days.length && days[old][0] < limit) {
    old++;
  }
  if (old > 0) {
    sheet.deleteRows(2, old);
  }
}

/** Запустить один раз руками: лист «data» и ежедневная уборка. */
function setup() {
  sheet_();
  ScriptApp.getProjectTriggers().forEach(function (t) {
    if (t.getHandlerFunction() === 'prune') {
      ScriptApp.deleteTrigger(t);
    }
  });
  ScriptApp.newTrigger('prune').timeBased().everyDays(1).atHour(4).create();
}
