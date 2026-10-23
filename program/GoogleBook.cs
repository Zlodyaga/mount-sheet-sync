// Таблица через Google Sheets API для SheetLogic (та же логика, что в Code.gs, но без скрипта в таблице)
// и выбор, куда программа отправляет записи: в таблицу через вход Google или в скрипт Code.gs.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace MountSheetSync
{
    /// <summary>Куда уходят запросы ping / status / kills — ответ одинаковый.</summary>
    interface ISheetService
    {
        Dictionary<string, object> Call(Dictionary<string, object> payload);
    }

    /// <summary>Старый способ: веб-приложение Apps Script (Code.gs) в таблице.</summary>
    sealed class ScriptSheetService : ISheetService
    {
        readonly string url;

        public ScriptSheetService(string url)
        {
            this.url = url;
        }

        public Dictionary<string, object> Call(Dictionary<string, object> payload)
        {
            return SheetClient.Call(url, payload);
        }
    }

    /// <summary>Таблица, созданная программой: читаем её, решаем всё здесь и пишем обратно.</summary>
    sealed class GoogleSheetService : ISheetService
    {
        readonly string spreadsheetId;

        public GoogleSheetService(string spreadsheetId)
        {
            this.spreadsheetId = spreadsheetId;
        }

        public Dictionary<string, object> Call(Dictionary<string, object> payload)
        {
            // Тот же вид, что получает скрипт: через JSON (числа — double, списки — List<object>).
            Dictionary<string, object> request = (Dictionary<string, object>)Json.Parse(Json.Write(payload, false));
            GoogleBook book = GoogleBook.Load(spreadsheetId);
            bool dry = J.Truthy(request, "dryRun");
            Dictionary<string, object> resp;
            try
            {
                if (!dry)
                {
                    Upgrade(book);
                    RemoveCollected(book);   // «Да» поставили руками — список «Осталось» уже без этих строк
                }
                resp = SheetLogic.Handle(book, request);
                if (!dry)
                {
                    RemoveCollected(book);   // новые маунты из этой отправки
                }
            }
            catch (SyncException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log.Write(Tr.L("Ошибка при разборе таблицы: {0}", ex));
                throw new SyncException(Tr.F("внутренняя ошибка при работе с таблицей: {0}", ex.Message));
            }
            if (resp == null || !J.Truthy(resp, "ok"))
            {
                throw new SyncException(Tr.F("таблица не приняла запрос: {0}", J.Str(resp, "error")));
            }
            return resp;
        }

        /// <summary>Таблица прошлой версии — к нынешнему виду. Ошибка здесь не мешает отправке записей.</summary>
        public static void Upgrade(GoogleBook book)
        {
            try
            {
                SheetUpgrade.Ensure(book);
            }
            catch (SyncException ex)
            {
                Log.Write(Tr.L("Обновить вид таблицы не получилось: {0}. Попробую в следующий раз.", ex.Message));
            }
        }

        /// <summary>Строки собранных маунтов — с листов персонажей. Ошибка здесь не мешает отправке записей.</summary>
        static void RemoveCollected(GoogleBook book)
        {
            try
            {
                CollectionSync.RemoveCollected(book);
            }
            catch (SyncException ex)
            {
                Log.Write(Tr.L("Убрать строки собранных маунтов с листов персонажей не получилось: {0}. Попробую в следующий раз.", ex.Message));
            }
        }

        /// <summary>
        /// «Сверить с коллекцией». force = false — только если снимок коллекции новее того, с которым
        /// таблица уже сверялась (свойство таблицы). null — сверять не понадобилось.
        /// </summary>
        public CollectionResult SyncCollection(GameSnapshot game, bool force)
        {
            if (game.CollectionUpdated <= 0)
            {
                return null;
            }
            GoogleBook book = GoogleBook.Load(spreadsheetId);
            string stamp = game.CollectionUpdated.ToString(CultureInfo.InvariantCulture);
            if (!force && book.GetProperty(CollectionSync.CheckedProperty) == stamp)
            {
                return null;
            }
            Upgrade(book);
            CollectionResult res;
            try
            {
                res = CollectionSync.Run(book, game, MountIndex.For(Catalog.Embedded()));
            }
            catch (SyncException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log.Write(Tr.L("Ошибка при сверке с коллекцией: {0}", ex));
                throw new SyncException(Tr.F("внутренняя ошибка при сверке с коллекцией: {0}", ex.Message));
            }
            book.SetProperty(CollectionSync.CheckedProperty, stamp);
            return res;
        }
    }

    /// <summary>IBook поверх Google Sheets API: все листы в память, изменения — одним запросом.</summary>
    sealed class GoogleBook : IStructuredBook
    {
        public const string PropertyPrefix = "mountSheetSync.";

        readonly string id;
        string title = "";
        string url = "";
        string locale = "";
        string creatorApp = "";
        readonly List<SheetEntry> entries = new List<SheetEntry>();
        readonly List<CellRun.Cell> pending = new List<CellRun.Cell>();
        readonly Dictionary<string, KeyValuePair<long, string>> properties =
            new Dictionary<string, KeyValuePair<long, string>>(StringComparer.Ordinal);

        /// <summary>Сколько запросов на запись ушло (для журнала и проверок).</summary>
        public int WriteCalls;

        GoogleBook(string id)
        {
            this.id = id;
        }

        public string Title { get { return title; } }
        public string Url { get { return url; } }
        public List<SheetEntry> Entries { get { return entries; } }
        public string Id { get { return id; } }
        public string Locale { get { return locale; } }
        public string CreatorApp { get { return creatorApp; } }

        public static string A1Sheet(string name)
        {
            return "'" + name.Replace("'", "''") + "'";
        }

        public static GoogleBook Load(string spreadsheetId)
        {
            GoogleBook b = new GoogleBook(spreadsheetId);
            // только простые пути через точку: их понимает и маска полей Sheets API, и параметр fields
            Dictionary<string, object> meta = GoogleSheets.Get(spreadsheetId,
                "properties.title,properties.locale,spreadsheetUrl,sheets.properties.sheetId,sheets.properties.title,"
                + "sheets.properties.sheetType,sheets.properties.gridProperties.columnCount,"
                + "developerMetadata.metadataId,developerMetadata.metadataKey,developerMetadata.metadataValue");
            b.title = J.Str(J.Obj(J.Get(meta, "properties")), "title");
            b.locale = J.Str(J.Obj(J.Get(meta, "properties")), "locale");
            b.url = J.Str(meta, "spreadsheetUrl");
            if (b.url.Length == 0)
            {
                b.url = "https://docs.google.com/spreadsheets/d/" + spreadsheetId + "/edit";
            }
            foreach (object o in J.Arr(J.Get(meta, "developerMetadata")))
            {
                Dictionary<string, object> m = J.Obj(o);
                string key = J.Str(m, "metadataKey");
                if (key.StartsWith(PropertyPrefix, StringComparison.Ordinal))
                {
                    b.properties[key.Substring(PropertyPrefix.Length)] =
                        new KeyValuePair<long, string>(J.Long(m, "metadataId"), J.Str(m, "metadataValue"));
                }
                else if (key == SheetBuilder.MetadataKey)
                {
                    try
                    {
                        b.creatorApp = J.Str(Json.Parse(J.Str(m, "metadataValue")) as Dictionary<string, object>, "app");
                    }
                    catch (Exception)
                    {
                    }
                }
            }
            foreach (object o in J.Arr(J.Get(meta, "sheets")))
            {
                Dictionary<string, object> p = J.Obj(J.Get(J.Obj(o), "properties"));
                string type = J.Str(p, "sheetType");
                if (type.Length > 0 && type != "GRID")
                {
                    continue;
                }
                SheetEntry e = new SheetEntry();
                e.Name = J.Str(p, "title");
                e.SheetId = J.Long(p, "sheetId");
                e.GridColumns = (int)J.Long(J.Obj(J.Get(p, "gridProperties")), "columnCount");
                b.entries.Add(e);
            }
            if (b.entries.Count == 0)
            {
                return b;
            }
            List<string> ranges = b.entries.Select(e => A1Sheet(e.Name)).ToList();
            List<object> values = GoogleSheets.BatchGetValues(spreadsheetId, ranges, "UNFORMATTED_VALUE");
            List<object> formulas = GoogleSheets.BatchGetValues(spreadsheetId, ranges, "FORMULA");
            for (int i = 0; i < b.entries.Count; i++)
            {
                List<object> rows = i < values.Count ? J.Arr(J.Get(J.Obj(values[i]), "values")) : new List<object>();
                List<object> frows = i < formulas.Count ? J.Arr(J.Get(J.Obj(formulas[i]), "values")) : new List<object>();
                Fill(b.entries[i], rows, frows);
            }
            return b;
        }

        static void Fill(SheetEntry e, List<object> rows, List<object> frows)
        {
            int width = 0;
            foreach (object r in rows)
            {
                width = Math.Max(width, J.Arr(r).Count);
            }
            foreach (object r in rows)
            {
                List<object> src = J.Arr(r);
                object[] row = new object[width];
                for (int c = 0; c < width; c++)
                {
                    object v = c < src.Count ? src[c] : null;
                    row[c] = v ?? "";
                }
                e.Data.Add(row);
            }
            for (int r = 0; r < frows.Count && r < e.Data.Count; r++)
            {
                List<object> src = J.Arr(frows[r]);
                for (int c = 0; c < src.Count && c < width; c++)
                {
                    string f = src[c] as string;
                    if (f == null || !f.StartsWith("=", StringComparison.Ordinal))
                    {
                        continue;
                    }
                    // текст «=…» (введённый с апострофом) — не формула: значение то же самое
                    string v = e.Data[r][c] as string;
                    if (v != null && v == f)
                    {
                        continue;
                    }
                    e.Formulas.Add(SheetLogic.Key(r, c));
                }
            }
        }

        public void Set(SheetEntry entry, int r, int c, object value)
        {
            entry.Data[r][c] = value;
            pending.Add(new CellRun.Cell(entry, r, c));
        }

        public void Flush()
        {
            if (pending.Count == 0)
            {
                return;
            }
            List<object> data = new List<object>();
            foreach (CellRun run in CellRun.Group(pending))
            {
                string col = SheetBuilder.ColumnName(run.Col);
                string range = A1Sheet(run.Entry.Name) + "!" + col + (run.Row + 1).ToString(CultureInfo.InvariantCulture)
                    + ":" + col + (run.Row + run.Rows).ToString(CultureInfo.InvariantCulture);
                List<object> rows = new List<object>();
                for (int i = 0; i < run.Rows; i++)
                {
                    rows.Add(new List<object> { run.Entry.Data[run.Row + i][run.Col] });
                }
                Dictionary<string, object> d = new Dictionary<string, object>();
                d["range"] = range;
                d["majorDimension"] = "ROWS";
                d["values"] = rows;
                data.Add(d);
            }
            pending.Clear();
            GoogleSheets.BatchUpdateValues(id, data);
            WriteCalls++;
        }

        public void AppendColumn(SheetEntry entry, List<object> values, bool hidden)
        {
            Flush();
            int col = entry.Data.Count > 0 ? entry.Data[0].Length : 0;
            int grid = entry.GridColumns > 0 ? entry.GridColumns : col;
            GoogleSheets.BatchUpdate(id, SheetBuilder.AppendColumnRequests((int)entry.SheetId, col, grid, values, hidden));
            WriteCalls++;
            if (col >= grid)
            {
                entry.GridColumns = col + 1;
            }
            BookEdit.AppendColumn(entry, values);
        }

        public void DeleteRows(Dictionary<SheetEntry, List<int>> rows)
        {
            Flush();
            List<object> req = new List<object>();
            foreach (KeyValuePair<SheetEntry, List<int>> kv in rows)
            {
                req.AddRange(BookEdit.DeleteRequests(kv.Key, kv.Value));
            }
            if (req.Count == 0)
            {
                return;
            }
            GoogleSheets.BatchUpdate(id, req);
            WriteCalls++;
            foreach (KeyValuePair<SheetEntry, List<int>> kv in rows)
            {
                BookEdit.DeleteRows(kv.Key, kv.Value);
            }
        }

        public void Apply(List<object> requests, string propertyKey, string propertyValue)
        {
            Flush();
            List<object> req = new List<object>(requests);
            SheetBuilder.LocalizeFormulas(req, locale);
            if (propertyKey != null)
            {
                req.Add(PropertyRequest(propertyKey, propertyValue));
            }
            if (req.Count == 0)
            {
                return;
            }
            Dictionary<string, object> resp = GoogleSheets.BatchUpdate(id, req);
            WriteCalls++;
            if (propertyKey != null)
            {
                RememberProperty(propertyKey, propertyValue, resp);
            }
        }

        Dictionary<string, object> PropertyRequest(string key, string value)
        {
            KeyValuePair<long, string> old;
            if (properties.TryGetValue(key, out old) && old.Key > 0)
            {
                return SheetBuilder.O("updateDeveloperMetadata", SheetBuilder.O(
                    "dataFilters", new List<object> { SheetBuilder.O("developerMetadataLookup", SheetBuilder.O("metadataId", old.Key)) },
                    "developerMetadata", SheetBuilder.O("metadataValue", value),
                    "fields", "metadataValue"));
            }
            return SheetBuilder.O("createDeveloperMetadata", SheetBuilder.O("developerMetadata", SheetBuilder.O(
                "metadataKey", PropertyPrefix + key,
                "metadataValue", value,
                "location", SheetBuilder.O("spreadsheet", true),
                "visibility", "DOCUMENT")));
        }

        void RememberProperty(string key, string value, Dictionary<string, object> resp)
        {
            KeyValuePair<long, string> old;
            properties.TryGetValue(key, out old);
            long newId = old.Key;
            foreach (object o in J.Arr(J.Get(resp, "replies")))
            {
                Dictionary<string, object> created = J.Obj(J.Get(J.Obj(o), "createDeveloperMetadata"));
                if (created != null)
                {
                    Dictionary<string, object> m = J.Obj(J.Get(created, "developerMetadata"));
                    if (J.Str(m, "metadataKey") == PropertyPrefix + key)
                    {
                        newId = J.Long(m, "metadataId");
                    }
                }
            }
            properties[key] = new KeyValuePair<long, string>(newId, value);
        }

        public string GetProperty(string key)
        {
            KeyValuePair<long, string> v;
            return properties.TryGetValue(key, out v) ? v.Value : null;
        }

        public void SetProperty(string key, string value)
        {
            Dictionary<string, object> resp = GoogleSheets.BatchUpdate(id, new List<object> { PropertyRequest(key, value) });
            RememberProperty(key, value, resp);
        }
    }
}
