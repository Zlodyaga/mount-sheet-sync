"""Farming tips of the rare sheet in German, French, Spanish and Portuguese, made from the English tip sentence by
sentence. Zone and creature names come from the game's tables (UiMap) and Wowhead tooltips (npc_names.json), the rest
is translated here by hand (machine-quality: worth a look from a native speaker). An English sentence that is not
known here stops the build, so a new tip cannot slip through untranslated."""
import re

LANGS = ("de", "fr", "es", "pt")

# fixed sentences; {z:ID} is a zone (UiMap ID), {n:Name} an English creature name — both put in the tip's language
FIXED = {
    "Loot once a day per character.": {
        "de": "Beute einmal am Tag pro Charakter.", "fr": "Butin une fois par jour et par personnage.",
        "es": "Botín una vez al día por personaje.", "pt": "Saque uma vez por dia por personagem."},
    "Each one gives loot once a day per character, so the row has no cooldown of its own.": {
        "de": "Jeder gibt einmal am Tag pro Charakter Beute, daher hat die Zeile keine eigene Abklingzeit.",
        "fr": "Chacun donne du butin une fois par jour et par personnage : la ligne n'a donc pas de délai propre.",
        "es": "Cada uno da botín una vez al día por personaje, así que la fila no tiene reutilización propia.",
        "pt": "Cada um dá saque uma vez por dia por personagem, então a linha não tem recarga própria."},
    "The mount always drops.": {
        "de": "Das Reittier fällt immer.", "fr": "La monture tombe toujours.",
        "es": "La montura siempre cae.", "pt": "A montaria sempre cai."},
    "The mount almost always drops.": {
        "de": "Das Reittier fällt fast immer.", "fr": "La monture tombe presque toujours.",
        "es": "La montura cae casi siempre.", "pt": "A montaria cai quase sempre."},
    "Appears during N'Zoth assaults.": {
        "de": "Erscheint während der Angriffe von N'Zoth.", "fr": "Apparaît pendant les assauts de N'Zoth.",
        "es": "Aparece durante los asaltos de N'Zoth.", "pt": "Aparece durante os ataques de N'Zoth."},
    "No cooldown: every kill is an attempt.": {
        "de": "Keine Abklingzeit: jeder Kill ist ein Versuch.", "fr": "Pas de délai : chaque victime est une tentative.",
        "es": "Sin reutilización: cada muerte es un intento.", "pt": "Sem recarga: cada abate é uma tentativa."},
    "Appears in several zones of Pandaria.": {
        "de": "Erscheint in mehreren Zonen von {z:424}.", "fr": "Apparaît dans plusieurs zones de {z:424}.",
        "es": "Aparece en varias zonas de {z:424}.", "pt": "Aparece em várias zonas de {z:424}."},
    "The three Warbringers look the same, each has its own direhorn; the number is only for the sheet, the addon tells them apart.": {
        "de": "Die drei sehen gleich aus, jeder hat sein eigenes Terrorhorn; die Nummer ist nur für die Tabelle, das Addon unterscheidet sie.",
        "fr": "Les trois se ressemblent, chacun a sa propre corne-fourche ; le numéro ne sert qu'au tableau, l'addon les distingue.",
        "es": "Los tres parecen iguales, cada uno tiene su propio cuernoatroz; el número es solo para la hoja, el accesorio los distingue.",
        "pt": "Os três parecem iguais, cada um tem seu próprio chifre-terror; o número é só para a planilha, o addon os distingue."},
    "No loot cooldown.": {
        "de": "Keine Abklingzeit für Beute.", "fr": "Pas de délai de butin.",
        "es": "Sin reutilización de botín.", "pt": "Sem recarga de saque."},
    "Four rares of Tanaan Jungle; the mount drops from any of them.": {
        "de": "Vier seltene Gegner im {z:534}; das Reittier fällt bei jedem von ihnen.",
        "fr": "Quatre monstres rares de {z:534} ; la monture tombe sur n'importe lequel.",
        "es": "Cuatro raros de {z:534}; la montura cae de cualquiera de ellos.",
        "pt": "Quatro raros de {z:534}; a montaria cai de qualquer um deles."},
    "Can also be bought from a vendor.": {
        "de": "Kann auch bei einem Händler gekauft werden.", "fr": "Peut aussi s'acheter chez un marchand.",
        "es": "También se puede comprar a un vendedor.", "pt": "Também pode ser comprada de um vendedor."},
    "Frostfire Ridge, appears rarely.": {
        "de": "{z:525}, erscheint selten.", "fr": "{z:525}, apparaît rarement.",
        "es": "{z:525}, aparece rara vez.", "pt": "{z:525}, aparece raramente."},
    "Nagrand (Draenor), appears once in many hours.": {
        "de": "{z:550} (Draenor), erscheint nur alle paar Stunden.", "fr": "{z:550} (Draenor), apparaît une fois toutes les nombreuses heures.",
        "es": "{z:550} (Draenor), aparece una vez cada muchas horas.", "pt": "{z:550} (Draenor), aparece uma vez a cada muitas horas."},
    "Gorgrond, appears once in many hours.": {
        "de": "{z:543}, erscheint nur alle paar Stunden.", "fr": "{z:543}, apparaît une fois toutes les nombreuses heures.",
        "es": "{z:543}, aparece una vez cada muchas horas.", "pt": "{z:543}, aparece uma vez a cada muitas horas."},
    "Talador, appears once in many hours.": {
        "de": "{z:535}, erscheint nur alle paar Stunden.", "fr": "{z:535}, apparaît une fois toutes les nombreuses heures.",
        "es": "{z:535}, aparece una vez cada muchas horas.", "pt": "{z:535}, aparece uma vez a cada muitas horas."},
    "Shadowmoon Valley (Draenor), appears once in many hours.": {
        "de": "{z:539} (Draenor), erscheint nur alle paar Stunden.", "fr": "{z:539} (Draenor), apparaît une fois toutes les nombreuses heures.",
        "es": "{z:539} (Draenor), aparece una vez cada muchas horas.", "pt": "{z:539} (Draenor), aparece uma vez a cada muitas horas."},
    "Flies around the Storm Peaks, appears every 6-24 hours (Vyragosa may come instead).": {
        "de": "Fliegt über die {z:120}, erscheint alle 6-24 Stunden (stattdessen kann {n:Vyragosa} kommen).",
        "fr": "Vole autour des {z:120}, apparaît toutes les 6 à 24 heures ({n:Vyragosa} peut venir à sa place).",
        "es": "Vuela por {z:120}, aparece cada 6-24 horas (en su lugar puede venir {n:Vyragosa}).",
        "pt": "Voa por {z:120}, aparece a cada 6-24 horas ({n:Vyragosa} pode vir no lugar)."},
    "Flies around Deepholm; appears every few hours, you have to mount it and win.": {
        "de": "Fliegt über {z:207}; erscheint alle paar Stunden, man muss ihn besteigen und besiegen.",
        "fr": "Vole au-dessus du {z:207} ; apparaît toutes les quelques heures, il faut le chevaucher et gagner.",
        "es": "Vuela por {z:207}; aparece cada pocas horas, hay que montarlo y vencer.",
        "pt": "Voa por {z:207}; aparece a cada poucas horas, é preciso montá-lo e vencer."},
    "First find a camel figurine in Uldum (it appears rarely, in different places): it takes you to Dormus.": {
        "de": "Zuerst eine Kamelfigur in {z:249} finden (erscheint selten und an verschiedenen Orten): sie bringt dich zu {n:Dormus the Camel-Hoarder}.",
        "fr": "Trouver d'abord une figurine de chameau en {z:249} (rare, à divers endroits) : elle vous emmène chez {n:Dormus the Camel-Hoarder}.",
        "es": "Primero hay que encontrar una figurilla de camello en {z:249} (aparece rara vez y en distintos lugares): te lleva con {n:Dormus the Camel-Hoarder}.",
        "pt": "Primeiro encontre uma estatueta de camelo em {z:249} (aparece raramente, em lugares diferentes): ela leva você até {n:Dormus the Camel-Hoarder}."},
    "Swims around Vashj'ir (Shimmering Expanse, Abyssal Depths), appears every few hours.": {
        "de": "Schwimmt durch {z:203} ({z:205}, {z:204}), erscheint alle paar Stunden.",
        "fr": "Nage dans {z:203} ({z:205}, {z:204}), apparaît toutes les quelques heures.",
        "es": "Nada por {z:203} ({z:205}, {z:204}), aparece cada pocas horas.",
        "pt": "Nada por {z:203} ({z:205}, {z:204}), aparece a cada poucas horas."},
    "Vale of Eternal Blossoms (the Pandaria phase): a Sky Crystal made of 10 Skyshards, dropped by the Vale's mobs, takes down her shield.": {
        "de": "{z:390} (Pandaria-Phase): ein Himmelskristall (Sky Crystal) aus 10 Himmelssplittern (Skyshard) von den Gegnern des Tals nimmt ihr den Schild.",
        "fr": "{z:390} (phase de Pandarie) : un cristal céleste (Sky Crystal) fait de 10 éclats célestes (Skyshard), lâchés par les monstres du val, brise son bouclier.",
        "es": "{z:390} (fase de Pandaria): un cristal celeste (Sky Crystal) hecho de 10 fragmentos celestes (Skyshard), que sueltan los enemigos del valle, le quita el escudo.",
        "pt": "{z:390} (fase de Pandária): um cristal celeste (Sky Crystal) feito de 10 fragmentos celestes (Skyshard), que os inimigos do vale deixam cair, tira o escudo dela."},
    "Alliance only.": {"de": "Nur für die Allianz.", "fr": "Alliance uniquement.", "es": "Solo para la Alianza.", "pt": "Só para a Aliança."},
    "Horde only.": {"de": "Nur für die Horde.", "fr": "Horde uniquement.", "es": "Solo para la Horda.", "pt": "Só para a Horda."},
    "The cave entrance is at the point.": {
        "de": "Der Höhleneingang ist am Punkt.", "fr": "L'entrée de la grotte est au point indiqué.",
        "es": "La entrada de la cueva está en el punto.", "pt": "A entrada da caverna fica no ponto."},
    "It has to be summoned (details on Wowhead).": {
        "de": "Er muss beschworen werden (Details auf Wowhead).", "fr": "Il faut l'invoquer (détails sur Wowhead).",
        "es": "Hay que invocarlo (detalles en Wowhead).", "pt": "É preciso invocá-lo (detalhes no Wowhead)."},
    "Grows out of a Rapidly Growing Mushroom (details on Wowhead).": {
        "de": "Wächst aus einem {n:Rapidly Growing Mushroom} (Details auf Wowhead).",
        "fr": "Sort d'un {n:Rapidly Growing Mushroom} (détails sur Wowhead).",
        "es": "Sale de un {n:Rapidly Growing Mushroom} (detalles en Wowhead).",
        "pt": "Cresce de um {n:Rapidly Growing Mushroom} (detalhes no Wowhead)."},
    "Ardenweald: appears after a short quest chain (details on Wowhead).": {
        "de": "{z:1565}: erscheint nach einer kurzen Questreihe (Details auf Wowhead).",
        "fr": "{z:1565} : apparaît après une courte suite de quêtes (détails sur Wowhead).",
        "es": "{z:1565}: aparece tras una breve cadena de misiones (detalles en Wowhead).",
        "pt": "{z:1565}: aparece depois de uma curta série de missões (detalhes no Wowhead)."},
    "The Theater of Pain champions in Maldraxxus come out to the arena in turn; the mount drops from any of them.": {
        "de": "Die Champions des Theaters der Schmerzen in {z:1536} betreten nacheinander die Arena; das Reittier fällt bei jedem von ihnen.",
        "fr": "Les champions du Théâtre de la Souffrance à {z:1536} entrent tour à tour dans l'arène ; la monture tombe sur n'importe lequel.",
        "es": "Los campeones del Teatro del Dolor en {z:1536} salen a la arena por turnos; la montura cae de cualquiera de ellos.",
        "pt": "Os campeões do Teatro da Dor em {z:1536} entram na arena um após o outro; a montaria cai de qualquer um deles."},
    "Drops only while the construct Neena is your active companion (Necrolords).": {
        "de": "Fällt nur, solange das Konstrukt Neena dein aktiver Begleiter ist (Nekrolords).",
        "fr": "Tombe seulement si la construction Neena est votre compagnon actif (Nécro-seigneurs).",
        "es": "Solo cae si el ensamblaje Neena es tu compañero activo (Necroseñores).",
        "pt": "Só cai enquanto o constructo Neena for seu companheiro ativo (Necrossenhores)."},
    "Comes only when summoned with a lure (Aurelid Lure; details on Wowhead).": {
        "de": "Kommt nur, wenn er mit einem Köder beschworen wird (Aurelid Lure; Details auf Wowhead).",
        "fr": "Ne vient que s'il est invoqué avec un appât (Aurelid Lure ; détails sur Wowhead).",
        "es": "Solo aparece si se invoca con un cebo (Aurelid Lure; detalles en Wowhead).",
        "pt": "Só aparece quando invocado com uma isca (Aurelid Lure; detalhes no Wowhead)."},
    "Appears on the Isle of Dorn now and then (details on Wowhead).": {
        "de": "Erscheint ab und zu auf der {z:2248} (Details auf Wowhead).",
        "fr": "Apparaît de temps en temps sur l'{z:2248} (détails sur Wowhead).",
        "es": "Aparece de vez en cuando en {z:2248} (detalles en Wowhead).",
        "pt": "Aparece de vez em quando em {z:2248} (detalhes no Wowhead)."},
    "It has to be summoned: five players pull the five Inconspicuous Levers around the Ringing Deeps within about 10 seconds; it appears at the lake a minute later.": {
        "de": "Er muss beschworen werden: fünf Spieler ziehen innerhalb von etwa 10 Sekunden die fünf Hebel (Inconspicuous Lever) in den {z:2214}; eine Minute später erscheint er am See.",
        "fr": "Il faut l'invoquer : cinq joueurs actionnent les cinq leviers (Inconspicuous Lever) des {z:2214} en 10 secondes environ ; il apparaît au lac une minute plus tard.",
        "es": "Hay que invocarlo: cinco jugadores accionan las cinco palancas (Inconspicuous Lever) de {z:2214} en unos 10 segundos; aparece en el lago un minuto después.",
        "pt": "É preciso invocá-lo: cinco jogadores puxam as cinco alavancas (Inconspicuous Lever) de {z:2214} em cerca de 10 segundos; ele aparece no lago um minuto depois."},
}

# sentences with a number, a zone or creature names in them
TEMPLATES = [
    (r"A rare in (?P<zone>.+?), at (?P<x>[\d.]+), (?P<y>[\d.]+)\.", {
        "de": "Rar: {zone}, Punkt {x}, {y}.", "fr": "Rare : {zone}, point {x}, {y}.",
        "es": "Raro: {zone}, punto {x}, {y}.", "pt": "Raro: {zone}, ponto {x}, {y}."}),
    (r"A rare in (?P<zone>.+?), several spawn points\.", {
        "de": "Rar: {zone}, mehrere Fundorte.", "fr": "Rare : {zone}, plusieurs points d'apparition.",
        "es": "Raro: {zone}, varios puntos de aparición.", "pt": "Raro: {zone}, vários pontos de surgimento."}),
    (r"A rare in (?P<zone>.+?) during N'Zoth assaults; loot once a day per character\.", {
        "de": "Rar: {zone}, während der Angriffe von N'Zoth; Beute einmal am Tag pro Charakter.",
        "fr": "Rare : {zone}, pendant les assauts de N'Zoth ; butin une fois par jour et par personnage.",
        "es": "Raro: {zone}, durante los asaltos de N'Zoth; botín una vez al día por personaje.",
        "pt": "Raro: {zone}, durante os ataques de N'Zoth; saque uma vez por dia por personagem."}),
    (r"Alliance: (?P<a>.+?), Horde: (?P<h>.+?)\.", {
        "de": "Allianz: {a}, Horde: {h}.", "fr": "Alliance : {a}, Horde : {h}.",
        "es": "Alianza: {a}, Horda: {h}.", "pt": "Aliança: {a}, Horda: {h}."}),
    (r"Drops from any rare of the zone \((?P<num>\d+) of them\)\.", {
        "de": "Fällt bei jedem seltenen Gegner der Zone (es gibt {num}).",
        "fr": "Tombe sur n'importe quel monstre rare de la zone (il y en a {num}).",
        "es": "Cae de cualquier raro de la zona (hay {num}).", "pt": "Cai de qualquer raro da zona (são {num})."}),
    (r"Drops from any rare of the isle \((?P<num>\d+) of them\), very rarely\.", {
        "de": "Fällt bei jedem seltenen Gegner der Insel (es gibt {num}), sehr selten.",
        "fr": "Tombe sur n'importe quel monstre rare de l'île (il y en a {num}), très rarement.",
        "es": "Cae de cualquier raro de la isla (hay {num}), muy rara vez.",
        "pt": "Cai de qualquer raro da ilha (são {num}), muito raramente."}),
    (r"Drops from the zone's common mobs \((?P<num>\d+) kinds\), e\.g\. (?P<list>.+?)\.", {
        "de": "Fällt bei gewöhnlichen Gegnern der Zone ({num} Arten), z. B. {list}.",
        "fr": "Tombe sur les monstres communs de la zone ({num} sortes), par ex. {list}.",
        "es": "Cae de los enemigos comunes de la zona ({num} tipos), p. ej. {list}.",
        "pt": "Cai dos inimigos comuns da zona ({num} tipos), por ex. {list}."}),
]
NAMES = {"a", "h", "list"}   # groups that hold creature names (a list: comma-separated)


def split(en):
    en = en.replace("e.g. ", "e.g.\0")
    return [s.replace("\0", " ") for s in re.split(r"(?<=[.!?])\s+", en.strip()) if s]


class Tips:
    def __init__(self, uimap, npc_names):
        self.uimap = uimap                       # lang -> {UiMap ID: name}
        self.zone_id = {}
        for i, n in uimap["en"].items():
            self.zone_id.setdefault(n, i)
        self.npc = {}                            # English creature name -> {lang: name}
        for v in npc_names.values():
            if v.get("en"):
                self.npc.setdefault(v["en"], v)

    def zone(self, en, lang):
        i = self.zone_id.get(en)
        if i is None:
            raise SystemExit("tip_l10n: unknown zone %r" % en)
        return self.uimap[lang].get(i) or en

    def creature(self, en, lang):
        v = self.npc.get(en)
        if v is None:
            raise SystemExit("tip_l10n: unknown creature %r" % en)
        return v.get(lang) or en

    def fill(self, text, lang):
        text = re.sub(r"\{z:(\d+)\}", lambda m: self.uimap[lang].get(int(m.group(1))) or self.uimap["en"][int(m.group(1))], text)
        return re.sub(r"\{n:([^}]+)\}", lambda m: self.creature(m.group(1), lang), text)

    def sentence(self, s, lang):
        if s in FIXED:
            return self.fill(FIXED[s][lang], lang)
        for pattern, out in TEMPLATES:
            m = re.fullmatch(pattern, s)
            if m:
                parts = {}
                for k, v in m.groupdict().items():
                    if k == "zone":
                        parts[k] = self.zone(v, lang)
                    elif k in NAMES:
                        parts[k] = ", ".join(self.creature(x.strip(), lang) for x in v.split(", "))
                    else:
                        parts[k] = v
                return out[lang].format(**parts)
        raise SystemExit("tip_l10n: no translation for the sentence %r — add it to FIXED or TEMPLATES" % s)

    def translate(self, en):
        return {lang: " ".join(self.sentence(s, lang) for s in split(en)) for lang in LANGS}
