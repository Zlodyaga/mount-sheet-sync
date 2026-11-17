--[[
Mount Sheet Sync — праздничные маунты (3.0.7)

Маунты с праздничных боссов (раз в день, через поиск подземелий). Их нет в каталоге программы: список — здесь,
номера маунтов из таблицы игры Mount (wago.tools). Праздник идёт, если его подземелье можно выбрать в поиске
(IsLFGDungeonJoinable); «сегодня уже» — по ежедневной награде (GetLFGDungeonRewards, первое значение). Название
босса — из игры (GetLFGDungeonInfo), маунты — из журнала маунтов, поэтому переводов нет.

Видно игроку: строка при входе (вместе с «что осталось», та же галочка) и « - праздник: …» в шапке «Осталось».
]]

local _, ns = ...
local L = ns.L

-- lfg — подземелье праздника в поиске; mounts — номера маунтов (журнал), которые с него падают
local HOLIDAYS = {
  { lfg = 285, mounts = { 219, 2623 } },        -- Тыквовин: Всадник без головы
  { lfg = 287, mounts = { 202, 226, 2640 } },   -- Хмельной фестиваль: Корен Худовар
  { lfg = 288, mounts = { 352, 2328 } },        -- Любовная лихорадка: Аптекарь Хаммел
}
ns.HOLIDAYS = HOLIDAYS

local function Missing(ids)
  local out = {}
  local journal = C_MountJournal
  for _, id in ipairs(ids) do
    if journal and journal.GetMountInfoByID then
      local name, _, _, _, _, _, _, _, _, _, collected = journal.GetMountInfoByID(id)
      if name and not collected then
        out[#out + 1] = name
      end
    end
  end
  return out
end

--- Праздники, которые идут сейчас и где есть несобранные маунты: { { name, mounts = { имена }, done } }.
function ns.HolidayNow()
  local out = {}
  if not IsLFGDungeonJoinable then
    return out
  end
  for _, h in ipairs(HOLIDAYS) do
    local ok, open = pcall(IsLFGDungeonJoinable, h.lfg)
    if ok and open then
      local mounts = Missing(h.mounts)
      if #mounts > 0 then
        local name = GetLFGDungeonInfo and select(1, GetLFGDungeonInfo(h.lfg)) or nil
        local done = GetLFGDungeonRewards and select(1, GetLFGDungeonRewards(h.lfg)) or false
        out[#out + 1] = { name = tostring(name or "?"), mounts = mounts, done = done and true or false, lfg = h.lfg }
      end
    end
  end
  return out
end

--- « - праздник: Корен Худовар» для шапки «Осталось»: только то, что сегодня ещё не пройдено.
function ns.HolidayHead()
  local names = {}
  for _, h in ipairs(ns.HolidayNow()) do
    if not h.done then
      names[#names + 1] = h.name
    end
  end
  return #names > 0 and (" - " .. string.format(L.holiday_head, table.concat(names, ", "))) or ""
end

--- Строка при входе (через 12 с; галочка «При входе - что осталось», «Не писать в чат»).
function ns.HolidayNote()
  local db = ns.GetDB()
  if not db or db.quiet or db.loginNote == false then
    return
  end
  for _, h in ipairs(ns.HolidayNow()) do
    ns.Print(string.format(h.done and L.holiday_done or L.holiday_left, h.name, table.concat(h.mounts, ", ")))
  end
end
