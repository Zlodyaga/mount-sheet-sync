"""Build a workbook fixture from the real «Таблица для выбивания маунтов» contents."""
import json
import sys

RAID_HEADER = ["Пытался на этой неделе?", "Аддон", "Рейд", "Маунт", "Босс / достижение / сундук",
               "Сложность / режим", "Скип есть?"]
RAID_ROWS = [
    ["Да", "Cataclysm", "Firelands", "Пламенное крыло Алисразор", "Алисразор", "Normal/Heroic", "Нет"],
    ["Да", "Cataclysm", "Dragon Soul", "Эксперимент 12-Б", "Ультраксион", "Normal/Heroic", "Нет"],
    ["Да", "Cataclysm", "Dragon Soul", "Пылающий дракон", "Безумие Смертокрыла", "Normal/Heroic", "Нет"],
    ["Да", "Cataclysm", "Dragon Soul", "Служанка Хранительницы Жизни", "Безумие Смертокрыла", "Heroic", "Нет"],
    ["Нет", "Battle for Azeroth", "Ny'alotha, the Waking City", "Всевидец из Ни'алоты", "Н'Зот Заразитель", "Mythic", "Да"],
    ["Нет", "Shadowlands", "Sanctum of Domination", "Месть", "Сильвана Ветрокрылая", "Mythic", "Да"],
    ["Нет", "Shadowlands", "Sanctum of Domination", "Темный скакун Святилища", "Девятка", "Normal/Heroic/Mythic", "Да, но не для этого"],
    ["Нет", "Shadowlands", "Sepulcher of the First Ones", "Смотритель Зерета", "Тюремщик", "Mythic", "Да"],
    ["Нет", "Dragonflight", "Amirdrassil, the Dream's Hope", "Ану'релос, Наставление Пламени", "Фиракк", "Mythic", "Да"],
]


def raid_sheet(name, overrides=None):
    rows = [list(r) for r in RAID_ROWS]
    for idx, val in (overrides or {}).items():
        rows[idx][0] = val
    return {"name": name, "values": [RAID_HEADER] + rows}


DUNGEON_HEADER = ["Пытался на этой неделе?", "Аддон", "Подземелье / активность", "Маунт",
                  "Босс / достижение / условие", "Сложность / режим", "Скип / маршрут"]
DUNGEON_ROWS = [
    ["Timewalking", "Any Timewalking Dungeon", "Попиратель времен из рода Бесконечности", "Любой босс подземелий путешествий во времени", "Timewalking", "Нет постоянного маршрута"],
    ["Classic", "Stratholme", "Поводья коня смерти", "Лорд Аурий Ривендер", "Normal", "Короткий маршрут к Baron/Rivendare"],
    ["The Burning Crusade", "Sethekk Halls", "Поводья Повелителя воронов", "Анзу", "Heroic", "Беги к Anzu"],
    ["Wrath of the Lich King", "The Culling of Stratholme", "Поводья бронзового дракона", "Осквернитель из рода Бесконечности", "Heroic timed run", "Успей в таймер"],
    ["Wrath of the Lich King", "Utgarde Pinnacle", "Поводья синего протодракона", "Скади Безжалостный", "Heroic", "Маршрут до Skadi"],
    ["Wrath of the Lich King", "WotLK Heroic Dungeons", "Поводья красного протодракона", "Слава герою", "Heroic achievements", "Разные подземелья WotLK"],
    ["Cataclysm", "The Stonecore", "Поводья камнекожего дракона", "Камнедроб", "Normal/Heroic", "Беги до Slabhide"],
    ["Cataclysm", "The Vortex Pinnacle", "Поводья дракона северного ветра", "Альтаирий", "Normal/Heroic", "Беги до Altairus"],
    ["Cataclysm", "Zul'Gurub", "Бронированный ящер Раззаши", "Мандокир Повелитель Крови", "Heroic", "Маршрут до Mandokir"],
    ["Cataclysm", "Zul'Gurub", "Стремительная зулианская пантера", "Верховная жрица Килнара", "Heroic", "Маршрут до Kilnara"],
    ["Cataclysm", "Zul'Aman", "Аманийский боевой медведь (Cataclysm)", "Мешок Каши", "Heroic timed run", "Успей спасти пленников"],
    ["Legion", "Return to Karazhan", "Пылающий угольный змей", "Ночная Погибель", "Mythic", "Собери кристаллы для Nightbane"],
    ["Legion", "Return to Karazhan", "Полночь", "Ловчий Аттумен", "Mythic", "Attumen early route"],
    ["Battle for Azeroth", "King's Rest", "Подкрадывающийся к гробницам", "Король Дазар / Сундук претендента", "Mythic / Mythic+", "Финальный босс"],
    ["Battle for Azeroth", "The Underrot", "Крог Подгнилья", "Неупокоенное чудовище / Сундук претендента", "Mythic / Challenge cache", "Финальный босс"],
    ["Battle for Azeroth", "Operation: Mechagon", "Воздушное судно R-21/X", "Король Мехагон", "Mythic, hard mode", "Финальная часть Mechagon"],
    ["Shadowlands", "Torghast, Tower of the Damned", "Исполинская темная утробная крыса", "Боссы Торгаста", "Layers 13+", "Фарм Torghast bosses"],
    ["Midnight", "Magisters' Terrace (Midnight)", "Светящийся крылобег", "Дегентрий", "Mythic / Mythic+", "Финальный/указанный босс"],
    ["Midnight", "Windrunner Spire", "Призрачный крылобег", "Неспокойное Сердце", "Mythic / Mythic+", "Указанный босс"],
    ["Midnight", "Mythic+ Season 2", "Дыхание Чумы", "Мастер ключей Midnight: 2-й сезон", "2000+ M+ rating", "M+ рейтинг"],
]

WORLD_HEADER = ["Собрано?", "Аддон", "Локация", "Маунт", "Босс / достижение / сундук", "Сброс / частота",
                "Осталось попыток выбить", "Совет по фарму", "Шанс выпадения", "Источник",
                "Эйра", "Элтара", "Эллин", "Каэлия (Лок)", "Каэлиия (ДК)", "Мирна"]
WORLD_ROWS = [
    ["Mists of Pandaria", "Kun-Lai Summit", "Небесный ониксовый облачный змей", "Ша Злости"],
    ["Mists of Pandaria", "Valley of the Four Winds", "Сын Галлеона", "Галлеон"],
    ["Mists of Pandaria", "Isle of Giants", "Кобальтовый изначальный дикорог", "Ундаста"],
    ["Mists of Pandaria", "Isle of Thunder", "Грозовой кобальтовый облачный змей", "Налак"],
    ["Warlords of Draenor", "Spires of Arak", "Солнечный ястреб пиков", "Рухмар"],
    ["Battle for Azeroth", "Vol'dun", "Молли", "Песчаный проглот Кролок"],
    ["Battle for Azeroth", "Uldum", "Дракон Четырех Ветров", "Исхак Повелитель Четырех Ветров"],
]

MASTER_HEADER = ["Собрано?", "Аддон", "Рейд", "Маунт", "Тип источника", "Босс / достижение / сундук",
                 "Сложность / режим", "Сброс / частота", "Осталось попыток выбить", "Скип есть?"]
MASTER_ROWS = [
    ["Да", "The Burning Crusade", "The Eye, Tempest Keep", "Пепел Ал'ара", "Босс-дроп", "Кель'тас Солнечный Скиталец", "Обычный рейд", "Еженедельно", 0, "Нет"],
    ["Нет", "Wrath of the Lich King", "Eye of Eternity", "Лазурный дракон", "Босс-дроп", "Малигос", "10/25", "Еженедельно", 0, "Нет"],
    ["Нет", "Cataclysm", "Firelands", "Пламенное крыло Алисразор", "Босс-дроп", "Алисразор", "Normal/Heroic", "Еженедельно", 0, "Нет"],
    ["Нет", "Battle for Azeroth", "Ny'alotha, the Waking City", "Всевидец из Ни'алоты", "Босс-дроп", "Н'Зот Заразитель", "Mythic", "Еженедельно", 5, "Да"],
]


def build(variant):
    mirna = {2: "Нет", 3: "Нет"} if variant == "deathwing" else None
    sheets = [
        {"name": "Рейды", "values": [MASTER_HEADER] + MASTER_ROWS},
        raid_sheet("Рейды Эйра", {4: "Да"}),
        raid_sheet("Рейды Элтара"),
        raid_sheet("Рейды Эллин"),
        raid_sheet("Рейды Каэлия (Лок)"),
        raid_sheet("Рейды Каэлиия (ДК)"),
        raid_sheet("Рейды Мирна", mirna),
        {"name": "Подземелья", "values": [
            ["Собрано?", "Аддон", "Подземелье / активность", "Маунт", "Тип источника", "Босс / достижение / условие", "Сложность / режим"],
            ["Нет", "Wrath of the Lich King", "Utgarde Pinnacle", "Поводья синего протодракона", "Босс-дроп", "Скади Безжалостный", "Heroic"],
        ]},
        {"name": "Подземелья Эйра", "values": [DUNGEON_HEADER] + [["Нет"] + r for r in DUNGEON_ROWS]},
        {"name": "Сводка", "values": [["WoW Retail / Midnight — импорт", "", "", ""], ["Обновлено", "2026-10-01", "", ""]]},
        {"name": "Мировые боссы", "values": [WORLD_HEADER] + [["Нет"] + r + ["Еженедельно", "6", "Респ 15 минут", "1%", "https://www.warcraftmounts.com/loot.php"] + [False] * 6 for r in WORLD_ROWS]},
    ]
    return {"name": "Таблица для выбивания маунтов", "sheets": sheets}


if __name__ == "__main__":
    variant = sys.argv[2] if len(sys.argv) > 2 else "real"
    with open(sys.argv[1], "w", encoding="utf-8") as f:
        json.dump(build(variant), f, ensure_ascii=False, indent=1)
