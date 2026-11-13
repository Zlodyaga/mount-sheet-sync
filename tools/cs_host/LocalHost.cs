// Test host: LocalBook, the data file of the «on this computer» mode. args[0] is an empty folder; prints JSON with what happened.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace MountSheetSync
{
    static class LocalHost
    {
        static LocalBook Fresh(string file)
        {
            GameSnapshot game = new GameSnapshot();
            GameCharacter c = new GameCharacter();
            c.Name = "Эйра";
            c.Realm = "Ревущий фьорд";
            game.Characters.Add(c);
            return LocalBook.Create(file, Program.PlanFor(game, "Тест", "ru"));
        }

        static SheetEntry Sheet(IBook book, string name)
        {
            return book.Entries.First(e => e.Name == name);
        }

        static int Main(string[] args)
        {
            Tr.Use("ru");
            AppPaths.Init(args[0]);
            Log.Init(AppPaths.LogFile, false);
            string file = Path.Combine(args[0], "data.json");
            Dictionary<string, object> r = new Dictionary<string, object>();

            // пустые данные: ничего не пишут и не живут дольше одного вызова
            LocalBook empty = LocalBook.Empty(file);
            empty.Flush();
            r["emptySheets"] = empty.Entries.Count;
            r["emptyReusable"] = empty.Reusable;
            r["emptyWroteFile"] = File.Exists(file);

            // создание и чтение обратно
            LocalBook made = Fresh(file);
            r["madeReusable"] = made.Reusable;
            r["creator"] = made.CreatorApp;
            r["langAfterLoad"] = LocalBook.Load(file).Lang.Code;
            r["titleAfterLoad"] = LocalBook.Load(file).Title;
            r["layout"] = LocalBook.Load(file).GetProperty(SheetUpgrade.Property);
            SheetEntry raid = Sheet(made, "Рейды Эйра");
            r["sameShapeAfterLoad"] = Sheet(LocalBook.Load(file), "Рейды Эйра").Data.Count == raid.Data.Count
                && Sheet(LocalBook.Load(file), "Рейды Эйра").Data[1].Length == raid.Data[1].Length;
            r["typesKept"] = Sheet(LocalBook.Load(file), "Рейды Эйра").Data[1].Last() is bool;

            // копия в памяти устаревает, когда файл изменили
            LocalBook a = LocalBook.Load(file);
            LocalBook b = LocalBook.Load(file);
            Thread.Sleep(20);
            b.Set(Sheet(b, "Рейды Эйра"), 1, 0, "Да");
            r["dirtyNotReusable"] = !b.Reusable;
            b.Flush();
            r["writerReusable"] = b.Reusable;
            r["otherCopyStale"] = !a.Reusable;
            r["valueSaved"] = SheetLogic.CellText(Sheet(LocalBook.Load(file), "Рейды Эйра").Data[1][0]);

            // проба: правит только память
            LocalBook trial = LocalBook.Load(file);
            trial.ReadOnly = true;
            trial.Set(Sheet(trial, "Рейды Эйра"), 1, 0, "Нет");
            trial.Flush();
            trial.SetProperty("trial", "1");
            r["trialValueOnDisk"] = SheetLogic.CellText(Sheet(LocalBook.Load(file), "Рейды Эйра").Data[1][0]);
            r["trialPropertyOnDisk"] = LocalBook.Load(file).GetProperty("trial") ?? "";
            r["trialNotReusable"] = !trial.Reusable;

            // служебные значения, колонка с формулой, удаление строк
            LocalBook c = LocalBook.Load(file);
            c.SetProperty("key", "значение");
            r["propertySaved"] = LocalBook.Load(file).GetProperty("key");
            SheetEntry e = Sheet(c, "Рейды Эйра");
            int rows = e.Data.Count;
            int width = e.Data[0].Length;
            List<object> col = new List<object> { "Новая", new SheetBuilder.Formula("=1+1") };
            c.AppendColumn(e, col, true);
            SheetEntry saved = Sheet(LocalBook.Load(file), "Рейды Эйра");
            r["columnSaved"] = SheetLogic.CellText(saved.Data[0][width]);
            r["formulaCellEmpty"] = SheetLogic.CellText(saved.Data[1][width]) == "" && saved.Formulas.Contains(SheetLogic.Key(1, width));
            Dictionary<SheetEntry, List<int>> gone = new Dictionary<SheetEntry, List<int>>();
            gone[e] = new List<int> { 1 };
            c.DeleteRows(gone);
            saved = Sheet(LocalBook.Load(file), "Рейды Эйра");
            r["rowDeleted"] = saved.Data.Count == rows - 1;
            r["formulaGoneWithRow"] = !saved.Formulas.Contains(SheetLogic.Key(1, width));

            // запросы листов: новый лист, ячейки, удаление колонки со сдвигом формул, удаление листа
            LocalBook d = LocalBook.Load(file);
            d.Run(new List<object>
            {
                SheetBuilder.O("addSheet", SheetBuilder.O("properties", SheetBuilder.O("sheetId", 900, "title", "Новый", "index", 0))),
                SheetBuilder.UpdateCells(900, 0, 0, new List<object[]>
                {
                    new object[] { "A", "B", "C" },
                    new object[] { 1.0, new SheetBuilder.Formula("=1"), true },
                }),
                SheetBuilder.O("updateCells", SheetBuilder.O("range", SheetBuilder.O("sheetId", 900), "rows", new List<object>(), "fields", "userEnteredFormat")),
                SheetBuilder.O("deleteDimension", SheetBuilder.O("range", SheetBuilder.O("sheetId", 900, "dimension", "COLUMNS", "startIndex", 0, "endIndex", 1))),
            });
            d.Flush();
            SheetEntry added = LocalBook.Load(file).Entries[0];
            r["addedFirst"] = added.Name;
            r["columnRemoved"] = SheetLogic.CellText(added.Data[0][0]);
            r["formulaShifted"] = added.Formulas.Contains(SheetLogic.Key(1, 0));
            r["boolKept"] = added.Data[1][1] is bool;
            d.Run(new List<object> { SheetBuilder.O("deleteSheet", SheetBuilder.O("sheetId", 900)) });
            d.Flush();
            r["sheetRemoved"] = !LocalBook.Load(file).Entries.Any(x => x.Name == "Новый");

            // очередь: второй писатель ждёт первого
            bool secondIn = false;
            bool inWhileHeld;
            using (LocalBook.Exclusive(file))
            {
                Thread other = new Thread(delegate()
                {
                    using (LocalBook.Exclusive(file))
                    {
                        secondIn = true;
                    }
                });
                other.Start();
                Thread.Sleep(400);
                inWhileHeld = secondIn;
                using (LocalBook.Exclusive(file))
                {
                    r["sameThreadAgain"] = true;
                }
                Thread.Sleep(10);
                ThreadPool.QueueUserWorkItem(delegate { });
                inWhileHeld = inWhileHeld || secondIn;
                exclusiveThread = other;
            }
            exclusiveThread.Join(5000);
            r["secondWaited"] = !inWhileHeld;
            r["secondGotIn"] = secondIn;

            // повреждённый файл: копия становится основной, повреждённый отложен, копия не затирается
            File.WriteAllText(file, "{ битый", new UTF8Encoding(false));
            LocalBook healed = LocalBook.Load(file);
            r["healedFromBackup"] = healed.Entries.Count > 0;
            r["damagedKept"] = File.ReadAllText(file + ".damaged").StartsWith("{ битый", StringComparison.Ordinal);
            healed.SetProperty("after", "1");
            r["backupGood"] = !File.ReadAllText(file + ".bak").StartsWith("{ битый", StringComparison.Ordinal);
            // дневные копии: одна за сегодня, сколько бы раз ни писали; старше семи — убираются
            string backups = Path.Combine(Path.GetDirectoryName(file), "backups");
            r["dailyToday"] = Directory.GetFiles(backups).Length;
            for (int day = 1; day <= 9; day++)
            {
                File.WriteAllText(Path.Combine(backups, Path.GetFileNameWithoutExtension(file) + "-2020-01-0" + day + ".json"), "{}");
            }
            File.Delete(Directory.GetFiles(backups).OrderBy(x => x, StringComparer.Ordinal).Last());   // сегодняшней нет — запись сделает новую
            healed.SetProperty("after", "2");
            string[] kept = Directory.GetFiles(backups).OrderBy(x => x, StringComparer.Ordinal).ToArray();
            r["dailyKept"] = kept.Length;
            r["dailyOldestGone"] = !kept.Any(x => x.EndsWith("-2020-01-01.json", StringComparison.Ordinal));

            // вернуть данные из дневной копии: нынешние остаются рядом, испорченную копию не берём
            string today = LocalBook.DailyCopies(file)[0].Value;
            r["copiesNewestFirst"] = LocalBook.DailyCopies(file).Count == 7
                && string.CompareOrdinal(LocalBook.DailyCopies(file)[0].Key, LocalBook.DailyCopies(file)[1].Key) > 0;
            string wasNow = File.ReadAllText(file), wasCopy = File.ReadAllText(today);
            LocalBook.Restore(file, today);
            r["restored"] = File.ReadAllText(file) == wasCopy && wasCopy != wasNow;
            r["restoreKeptCurrent"] = File.ReadAllText(LocalBook.BeforeRestoreFile(file)) == wasNow;
            r["restoreNotACopy"] = LocalBook.DailyCopies(file).Count == 7;
            bool refused = false;
            try
            {
                LocalBook.Restore(file, LocalBook.DailyCopies(file)[1].Value);   // «{}» — не данные
            }
            catch (Exception)
            {
                refused = true;
            }
            r["restoreBadRefused"] = refused && File.ReadAllText(file) == wasCopy;

            // ни файла, ни копии: понятная ошибка
            string missing = Path.Combine(args[0], "none.json");
            try
            {
                LocalBook.Load(missing);
                r["missing"] = "loaded";
            }
            catch (SyncException ex)
            {
                r["missing"] = ex.Message;
            }
            Console.OutputEncoding = new UTF8Encoding(false);
            Console.WriteLine(Json.Write(r, false));
            return 0;
        }

        static Thread exclusiveThread;
    }
}
