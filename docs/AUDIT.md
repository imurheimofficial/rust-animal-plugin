# Změny verze 1.2.1 (2026-10-11)

Opravy z tvého testu verze 1.2.0. Ve hře jsem je nezkoušel. Kód jsem zkontroloval překladačem bez knihoven Rustu, jako minule.
Některé příčiny jsem ověřil v datech hry, která jsou v repu (`docs/_data`).

| Co jsi hlásil | Proč se to dělo | Jak to je teď |
|---|---|---|
| Kůň (hráč-kůň) zrychluje, už když držíš W. | Kůň neměl vlastní rychlosti chůze. Jeho „chůze“ byla 5,5 m/s, tedy rychlý klus až cval, takže to vypadalo, že s W zrychluje. | Kůň má svoje kroky: chůze 1,9, klus 4,3, cval 7,5, trysk 11, plný trysk 14 m/s (config Horse → FormGaits). W jen jde, každý Shift přidá jeden krok. Podkovy a ostatní vybavení koně rychlost mění. |
| Zvíře jede sprintem jen s W. | Zvolený krok zůstával i po zastavení: po sprintu a zastavení jelo W hned zase sprintem. | Když zvíře zastaví, další W jde zase krokem. Rychlost přidává Shift. |
| Útok na zvíře často netrefí a neteče krev. | Zásah se měřil přímou vzdáleností od čumáku (0,6 m), takže menší nebo níž stojící zvíře minul. Krev se nepouštěla, jen zvuk úderu. | Dosah je 1 m před čumákem a měří se vodorovně, s tolerancí na výšku. Při každém zásahu živého těla (útok, náraz, kůň) teče krev. Je to herní efekt zásahu do masa. |
| Spawnutý kůň tě divně pronásleduje. | Agresivní kůň útočil pořád dokola, dokud jsi byl do 14 m, a utéct mu nešlo. | Agresivní kůň zaútočí jako beran: krátký rozběh (nejvýš 6 s), jeden náraz, proběhne a pak tě 25 s nechá být. |
| Ctrl posouvá kameru dolů. | Hra vracela tvé přikrčení zpátky do kamery dřív, než ho plugin stihl zrušit. Navíc minulá úprava (zvednutí o 0,6 m) přidávala poskakování. | Přikrčení se ruší hned, jak tvůj tik přijde na server, dřív než ho hra použije. Zvedání je vypnuté (Camera → DuckLift = 0). |
| Stamina lišta: pryč nápis, velikost jako herní lišty. | Lišta byla širší a měla nápis. | Jen žlutá výplň a číslo. Velikost je 192 × 24 px jako herní lišty a jde nastavit (Stamina → BarRight, BarBottom, BarWidth, BarHeight). |
| Kůň píše „Occupied“. | Tohle píše sama hra na koni bez sedla (nemá volné místo). Plugin text hry změnit nemůže. | Pod nápisem teď vidíš, jak daleko je ochočení (viz další řádek). Jakmile má kůň sedlo, hra píše jízdu. |
| Kůň se ochočí za 20 s, když stojíš u něj. | Bylo potřeba 300 s a pak stisknout E. | Stůj do 6 m od klidného divokého koně 20 s (Horse → BondSeconds) a je tvůj, bez klávesy. Na obrazovce běží „Taming the horse: 12 / 20 s“. Plachý kůň utíká, agresivního nejdřív uspi šipkou. |
| Dveře šly otevřít přes okno. | E hledalo věci v kouli kolem hlavy bez kontroly, co je mezi nimi. | E platí jen pro věci přímo před hlavou (do 1,5 m, do 50°). Mezi hlavou a věcí nesmí být nic pevného (zeď, rám okna, podlaha). Platí i pro nápovědu na obrazovce. |
| Jezdec je posunutý. | Sedadlo koně je v hře připevněné na kost páteře. Plugin dával na páteř místo, kde má jezdec kořen těla, takže seděl níž a jinde. Místo na zádech se navíc bralo u nejvyšší kosti, ta bývá u lopatek. | Sedadlo je na páteři jako u koně, kousek před středem zad. Na všech zvířatech má jezdec stejné sedadlo jako na koni a druhý jezdec sedadlo koně pro zadního jezdce. Doladíš `/bm seat up/down/forward/back` (5 cm, uloží se pro druh) nebo tlačítky Seat na stránce Controls. |
| Saddle bag nejde dát zvířatům, nenabízí inventář. | Inventář měla jen ochočená zvířata a zvířata hráčů. Divoká a nově spawnutá ho neměla vůbec. | Inventář má každé zvíře, které nosí výbavu, i divoké: Ctrl + E, podržené E → Inventory, nebo E u zvířete, které nejde ochočit klávesou. Věci zůstanou zvířeti i po restartu. Po ochočení jsou majitele, po smrti spadnou na zem. |
| Sedla nejsou vidět na zádech. | Sedla šla vidět jen u ochočených zvířat. A pokud hra nezná název kosti páteře, hra hráče připevnila sedlo jinam. | Sedlo se ukáže u každého zvířete se sedlem v inventáři. Připevní se na kost jen tehdy, když ji hra zná jménem, jinak na tělo. `/bm saddle` (admin, při pohledu na zvíře) napíše, kolik sedel má v inventáři, jestli je model na zádech a na čem drží. |

**Druhá kontrola kódu** našla a opravil jsem:
* **Ochočení koně stáním u něj nefunguje u:**
  * koně, kterého hráč jako zvíře právě opustil;
  * bosse (ten se ochočuje svým sedlem);
  * hráče bez práva ochočovat nebo s plným počtem zvířat (dřív mu chodila zpráva 4× za sekundu).
* **Shift ve stoje** si vybraný krok podrží, nevrací se na chůzi.
* **Divoké zvíře, na kterém někdo jede,** nepřijde o inventář, sedlo na zádech ani armor.
* **Propuštění zvířete s otevřeným inventářem:** věci se nejdřív uloží, neztratí se.
* **Ochočení jelena nebo bosse:** nepřepíše sedlo, které zvíře už mělo v divokém inventáři. Sedlo navíc dostaneš zpátky.
* **Spící zvíře odhlášeného hráče** se už nebere jako divoké s druhým, prázdným inventářem.
* **Restart a zmizelé divoké zvíře:** jeho věci se už nesmažou, spadnou tam, kde bylo naposled.
* **Podržené E u divokého zvířete,** kde má E jinou práci (ochočení, boss, past, uspaný jelen), dělá tu práci. Inventář je tam přes Ctrl + E.
* **Prázdný inventář divokého zvířete,** které jen někdo otevřel, se zapomene.

**Po nahrání:** nahraj `dist/Beastmaster.cs`, smaž `oxide/config/Beastmaster.json` a `oxide/lang/en/Beastmaster.json`, zadej `oxide.reload Beastmaster`.
V konzoli musí být `Beastmaster 1.2.1 ready`. Bez smazání configu zůstane staré sedadlo, starý dosah útoku a stará lišta.

**Co otestovat:**
1. Kůň: W jde krokem, Shift přidává, po zastavení zase krok.
2. Útok zvířetem na zvíře zblízka: trefí a teče krev.
3. Spawn divokého koně: agresivní jednou narazí a odejde, klidný se po 20 s u něj ochočí.
4. Ctrl jako zvíře: kamera se nesmí hnout.
5. Dveře za oknem nejdou otevřít, dveře přímo před hlavou ano.
6. Jezdec na krávě, ovci a jelenovi:
   * jak sedí;
   * když je posunutý, dolaď `/bm seat forward/back/up/down`.
7. Divoké nebo spawnuté zvíře:
   * Ctrl + E otevře inventář;
   * dej sedlo a podívej se na záda;
   * když sedlo není vidět, napiš mi, co ukáže `/bm saddle`.

---

# Změny verze 1.2.0 (2026-10-10)

Tahle verze řeší všechno z tvého testu verze 1.1.0 a tři doplňky, které jsi poslal během práce: inventář jako u koně
(saddle bag, podkovy a další věci koně), horse armor jen jako buff a staminu jako u koně.

**Jak je to ověřené:** kód jsem prošel a zkontroloval překladačem C# (Roslyn). Ten najde chyby v kódu pluginu, ale ne chyby v
názvech věcí ze hry, protože knihovny serveru tu nemám. Ve hře jsem nic nespouštěl. Věci, u kterých hra mohla rozhodnout jinak,
než čekám, jsou dole v části „Co je nejisté“.

## Ovládání zvířete a kamera

| Co jsi hlásil | Proč se to dělo | Jak to je teď |
|---|---|---|
| Ctrl snižuje kameru jako při dřepu. | Hra snižuje kameru přikrčeného hráče, i když je skrytý. | Při držení Ctrl se skryté tělo zvedne přesně o tolik, o kolik hra kameru sníží (config Camera → DuckLift, 0,6 m). |
| Kamera se při nárazu do zdi nebo v klidu otočí jinam. | Animace zvířete otáčely tělem, na kterém visí kamera. | Natočení těla se drží po každém snímku animace, kamera se nehne. |
| Útok byl skoro vždy zadní kop. | Plugin četl směr pohledu, který hra u diváka neobnovuje. Byl to starý směr. | Plugin čte čerstvý směr kamery z ovládání. Přední půlkruh kamery = přední útok, zadní půlkruh = kop dozadu. |
| Kopnout dozadu má i kůň, jelen, beran a ovce. | Chybělo. | Kop dozadu má kráva, býk, tele, ovce, beran, jehně, jelen a kůň. Kůň nemá animaci kopu, zásah a zvuk proběhnou bez ní. |
| Kráva při běhu rovně kouká doprava. | Hlava sledovala starý směr pohledu. | Hlava sleduje kameru: ve stoji do 70°, v plném běhu do 35°. Když se díváš za zvíře, hlava kouká dopředu. |
| Zásah útoku je metr před zvířetem. | Zásah se měřil od středu těla. | Zásah se měří od čumáku (u kopu od zadku) a sahá 0,6 m (Combat → HitReach). |
| Náraz do zvířete přijde moc brzo. | Náraz se počítal od středu těla a v jakékoli rychlosti. | Náraz platí jen v nejvyšším sprintu a jen při dotyku čumáku (0,35 m, Combat → ChargeContact). Stejně tak náraz do zdi. |
| Idle animace se dohrává, i když jdeš. | Plugin zastavoval idle jen v prvních 8 sekundách. | Idle animace se při pohybu zastaví vždy, podle skutečné délky klipu. |
| Skok na kameny propadne. | Místo dopadu se nekontrolovalo, zvíře mohlo dopadnout do kamene. | Dopad se hledá paprskem shora a kontroluje se, jestli se tam vejde celé tělo. Když ne, zvíře narazí a zůstane dole. |
| E Feed bez animace. | Chybělo. | Kráva, býk, ovce, beran sklopí hlavu a pasou se nebo pijí (stejná póza jako ve hře). Ostatní druhy přehrají klip jídla. Pohybem to skončí. |

## Inventář jako u koně

| Co jsi chtěl | Jak to je teď |
|---|---|
| Inventář zvířete nesmí být smíchaný s lidským. | Každé jezditelné zvíře má vlastní inventář koně: slot 1 sedlo, 2 podkovy, 3 saddle bag, 4 horse armor, od slotu 5 úložiště. Lidský inventář se ho netýká. |
| Saddle bag jako u koně. | Saddle bag dá 12 slotů úložiště a zpomalí o 1 m/s. Bez saddle bagu zvíře nic nenese. Co zvíře sebere (E) nebo co mu kdo dá (`/bm give`), jde do saddle bagu. |
| Podkovy pro rychlost. | Basic +1,5 m/s, high quality +2,5 m/s (čísla koně). |
| Horse armor: buffy ano, vidět ne. | Dřevěná: −25 % poškození, +6 slotů, −2,25 m/s. Roadsign: −40 % poškození, −4,5 m/s. Na zvířeti vidět není, jak jsi chtěl. Kůň má svoje. |
| Sedlo na všech zvířatech a jízda jen se sedlem. | Bez sedla ve slotu 1 se na zvíře nenasedne (adminové mohou kvůli testu). Platí i pro zvíře, které jsi opustil (X). |
| Double saddle na koně, býka, krávu, berana, ovci a jelena. | Double saddle se vejde jen do těchto druhů. Na druhé místo si sedne druhý hráč (E na zvíře, na kterém někdo jede). |
| Hraju za zvíře: nasednout na mě jde jen se sedlem. | Single sedlo = 1 jezdec, double = 2. Kůň používá svoje herní sedlo a sedadla. |
| Sedlo vidět na zádech, podle velikosti zvířete, single a double jinak. | Na zádech je herní model sedla (single a double vypadají jinak), připevněný k páteři a zmenšený nebo zvětšený podle délky zvířete. |
| Ctrl + E batoh neotvírá. | Přesnou příčinu bez hry nevím. Dvě pravděpodobné jsem opravil: skrytá bedna pod zemí, přes kterou se panel otevírá, mohla zmizet jako věc bez podkladu (teď nezmizí), a u koně E zároveň nasedlo (teď se u koně se sedlem použije herní menu koně). Jako zvíře viz „Co je nejisté“. |

**Jak se inventář otvírá (jako člověk):**
* **E na zvíře:** se sedlem nasedneš, bez sedla se otevře jeho inventář, abys sedlo mohl dát.
* **Ctrl + E:** vždy inventář. U zvířete, za které hraje jiný hráč, Ctrl + E vezme nebo pustí vodítko.
* **Podržené E:** malé menu s tím, co jde právě udělat. Například Ride, Ride behind, Inventory, Lead / Let go, Follow, Wait, Guard.
* **Kůň:** E a podržené E jsou herní (nasednutí, menu koně s inventářem). Inventář koně, za kterého hraje hráč, smí otevřít ten, kdo s ním smí zacházet.
* **`/bm bag`:** inventář zvířete, na které se díváš.

## Proměna

| Co jsi hlásil | Jak to je teď |
|---|---|
| Efekt a zvuk se při spamování X někdy nepřehraje. | Další X se přijme až 1,2 s po posledním, takže se každá proměna dohraje. Efekt jde každému hráči v okolí i tobě, každému jednou. |
| Při proměně se znovu načítá okolí a textury. | Příčina: plugin nutil hru znovu přihlásit hráče k okolí, a hra pak okolí načetla znovu. Ve výchozím stavu je to vypnuté (Camera → RefreshSubscriptions = false). |
| Proměna do koně nejde. | `/bm form` funguje i na koně. Rituál najde i tělo koně, které hra ukládá jinak. |
| Proměna do mláďat. | Tele a jehně jdou přes `/bm form` i rituál, pokud je hra má jako samostatná zvířata. `bm.selftest` u nich ukáže OK. |
| Beran musí být beran, kráva není býk. | Hra pohlaví a vzhled losuje až po vytvoření zvířete. Plugin je proto nastaví po vytvoření a znovu po 0,5 s: beran a býk samec, ovce a kráva samice. |

## Koně

* **Povaha:** klidní (nechají tě být), plaší (utečou) a agresivní. Agresivní kůň se rozběhne, narazí do tebe, dá poškození jako beran a proběhne dál. Žádné stavění na zadní ani divná animace.
* **Kop dozadu:** jako hráč koně kopneš dozadu, když je kamera za ním.
* **Zvuky koně:** zůstávají herní.
* **Animace krávy a berana na koni:** nejde. Kůň má jinou kostru a jejich animace na něm hra neumí přehrát.

## Pády a nárazy

* **Náraz do zdi v nejvyšším sprintu:** spadne každý druh.
  * Kráva, býk a tele mají herní animaci pádu.
  * Ovce, beran, jelen a divočák padnou a lehnou si, za chvíli vstanou.
  * Kůň padne jako ve hře.
* **Plný sprint z prudkého kopce:** zvíře spadne stejně.
* **Jezdec:** při nárazu nebo pádu spadne ze zvířete jako z koně (odletí, malé poškození).

## Jízda

| Co jsi hlásil | Jak to je teď |
|---|---|
| `/bm seat down` se při jízdě samo vrací nahoru. | Plugin zvedal sedadlo vždy, když bylo nízko nad terénem. Teď ho zvedne jen tehdy, kdyby hráč skončil hluboko pod terénem (to hra trestá smrtí), a hned ho vrátí. Nastavení z `/bm seat` se drží. |
| Sedět jako na koni. | Sedadlo je herní sedlo koně: stejná póza nohou jako na koni. Kdyby ho tvoje verze hry neměla, použije se náhradní a `/bm seat <číslo>` vybere jiné. |
| Houpat se se zády, pevně. | Sedadlo je připevněné ke kosti páteře, takže se hýbe se zády. |
| Nejde se otočit úplně dozadu. | Hlavou se otočíš do stran nejvýš o 100° (Riding → ViewClamp). Dál ti hra hlavu vrátí. |
| Stamina jako u koně. | Plný sprint vydrží asi 15 s, sprint déle, klus skoro pořád. Chůze a stání staminu vrací. Bez staminy zvíře jen klusá a neskáče, dokud nenabere 25 %. Žlutá lišta je nad herními lištami. Při jízdě je nad ní i zdraví zvířete. Platí pro jízdu i pro hraní za zvíře (config Stamina). |

## Další

* **Neviditelné vodítko:** když jako zvíře vedeš jiné zvíře, žádné lano není vidět. Lano ze hry, které zůstalo na skrytém těle, plugin sundá a zvíře jde dál po neviditelném vodítku.
* **Divoká zvířata neskáčou přes překážky.** Skáče jen zvíře, které ovládá hráč. Plugin vypíná skoky vypínači, které má hra u zvířat. Které našel, ukáže `bm.selftest` (řádek „Life“).
* **Loot z mrtvých zvířat:** jako ve hře. Chráněné zůstávají jen tělo hořícího rituálu a náhradní tělo.
* **Nápověda E:** jako zvíře vidíš na obrazovce, co E udělá: Open, Close, Loot, Eat, Pick up, Drink, Lead.
* **Výkaly:** kůň, kráva, býk, tele, ovce, beran a jehně za hráče jednou za 4 až 9 minut (config Life).
* **Vejce:** slepice za hráče má velmi malou šanci snést vejce, někdy 2 nebo 3 naráz (2 % za minutu).
* **Ostatní hráči:**
  * vodí zvíře hráče na vodítku (Ctrl + E), u všech druhů včetně jelena, koně a divočáka;
  * podojí krávu a ostříhají ovci nebo berana nožem po svém, jako ve hře. Plugin jim dá důvěru zvířete.
* **Divoká zvířata:** zvíře, které zaútočilo, už hned neuteče. Rozhodnutí bránit se nebo utéct se drží, dokud útok neskončí.
* **`/bm saddle` (admin):** `lift`, `scale`, `turn`, `reset` doladí sedlo na zádech pro druh, na který se díváš, jedeš na něm nebo za něj hraješ. Uloží se.

## Druhá kontrola kódu

Po dokončení jsem nechal kód projít ještě jednou nezávislou kontrolou a opravil, co našla:

* **Ztráta věcí:**
  * Zvíře, které jsi opustil (X), po smrti nic neupustilo.
  * Inventář zvířete, ve kterém ses odhlásil, zmizel, když zvíře umřelo. Ve stejné relaci se navíc objevil u tvé další proměny.
  * Teď všechno spadne na zem.
* **Uložené věci** se při otevření inventáře už nikdy nesmažou, ani když v configu ubereš sloty: vrátí se hráči.
* **Sedlo, podkovy a armor** si drží skin i opotřebení. Vyndáním se už zadarmo neopraví.
* **Ochočené zvíře,** za které jsi hrál a opustil ho, ukazovalo prázdný inventář. Teď ukazuje svůj.
* **Náraz koně do zdi** se nikdy nespustil, protože se měřila už nastavená rychlost. Opraveno.
* **Ovce nebo beran,** který shodil jezdce, zůstal ležet. Teď vstane, až pád skončí. Stejně skončí póza pasení.
* **Spolujezdci** na hráči-zvířeti při nárazu spadnou také.
* **Menu podrženého E** po vypnutí pluginu nezůstane na obrazovce.
* **Neviditelný panel** hráče-zvířete neblokuje inventář ostatním ani sbírání věcí. Po 2 minutách se sám uloží a zavře.
* **Zvíře naložené na transport** se otáčí s vozidlem.
* **Skoky divokých zvířat:**
  * po vypnutí pluginu se vrátí přesně původní nastavení, po zapnutí se zase vypnou;
  * mrtvá zvířata se nehromadí v paměti.
* **Bez sedla jezdit (config):** když v configu vypneš povinné sedlo, na hráče-zvíře jde nasednout i bez sedla.
* **Inventář koně** přes `/bm bag` a Ctrl + E se ptá na stejná pravidla vlastnictví jako herní menu.

## Co je nejisté (ověříš jen ve hře)

* **Ctrl + E jako zvíře (vlastní inventář):** hra v pohledu zvířete (divák) blokuje Tab inventář a nejspíš i tento panel. Když se nic neukáže:
  * jiný hráč ti ho otevře (E nebo podržené E na tebe),
  * nebo dej X a Ctrl + E na své zvíře.
* **Sedlo na zádech:** je to model sedla ze hry, který je udělaný pro koně. Natočení a velikost u ostatních druhů jsem nemohl vidět. Když nesedí, dolaď ho `/bm saddle`.
* **Houpání jezdce:** funguje, pokud hra u zvířete hýbe kostí páteře i u sedadla. Když se jezdec chová divně, vypni Riding → SeatOnSpine.
* **Proměna bez načítání okolí:** kdybys jako zvíře neviděl ostatní hráče, zapni Camera → RefreshSubscriptions. Okolí se pak ale bude znovu načítat.
* **Skoky divokých zvířat:** záleží na tom, jaké vypínače hra má. Když `bm.selftest` napíše, že žádné nenašel, divoká zvířata budou skákat dál.
* **Dojení a stříhání hráče-zvířete:** běží herní kód. Plugin jen přidá důvěru zvířete.
* **Vejce:** předmět se hledá pod jménem `egg`. Když ho hra nemá, `bm.selftest` napíše MISSING.

## Po nahrání na wespner

1. Nahraj `dist/Beastmaster.cs` do `oxide/plugins` (Carbon: `carbon/plugins`).
2. **Smaž `oxide/config/Beastmaster.json` a `oxide/lang/en/Beastmaster.json`.** Jinak zůstanou staré výchozí hodnoty a staré texty (sedadlo, stamina, obnova okolí).
3. `oxide.reload Beastmaster` (Carbon: `c.reload Beastmaster`). V konzoli musí být `Beastmaster 1.2.0 ready`.
4. `bm.selftest`. Podívej se na řádky `seat`, `Gear` a `Life`.

## Co otestovat

1. **Ovládání zvířete:** drž Ctrl, kamera se nesníží. Narazit do zdi a stát v klidu: kamera se nehne.
2. **Útoky:** kamera dopředu a F = přední útok, kamera za zvíře a F = kop dozadu. Zkus krávu, ovci, jelena a koně.
3. **Zásahy a nárazy:**
   * útok trefí jen těsně před čumákem nebo za zadkem;
   * náraz do zvířete nebo zdi jen v nejvyšším sprintu;
   * jezdec při nárazu spadne.
4. **Skoky:** skoč na kámen a na plošinu, nepropadneš.
5. **Krmení:** E na trávu nebo vodu jako kráva: pasení nebo pití, pohybem skončí.
6. **Inventář ochočeného zvířete** (E bez sedla, Ctrl + E, podržené E):
   * dej sedlo, podkovy, saddle bag a armor, zavři a otevři znovu;
   * sedlo musí být vidět na zádech;
   * se saddle bagem se otevře úložiště.
7. **Jízda:** E se sedlem nasedne.
   * Sedíš jako na koni a houpeš se.
   * Hlavou se nejde otočit úplně dozadu.
   * `/bm seat down` se drží.
8. **Stamina:** plný sprint asi 15 s, pak jen klus, po odpočinku zase běh. Žlutá lišta vpravo dole.
9. **Za zvíře se sedlem:** druhý hráč nasedne, bez sedla ne. Double saddle = 2 jezdci.
10. **Proměna:** spamuj X, efekt i zvuk pokaždé. Okolí se nenačítá znovu. Ostatní hráče vidíš.
11. **Kůň:** proměna do koně (`/bm form`, rituál).
12. **Mláďata a pohlaví:** proměna do telete a jehněte, beran je beran.
13. **Vodítko:** jako zvíře veď jiné zvíře, žádné lano.
14. **Nápověda E** jako zvíře u dveří, bedny a vody.
15. **Výkaly a vejce:** po pár minutách výkal za zvířetem. Slepice jen výjimečně vejce.
16. **Divoká zvířata:** ověř, že nepřeskakují ploty.

---

# Audit verze 1.1.0 (2026-10-10)

Prošel jsem celý kód (`src/*.cs`, 29 modulů, ~16 000 řádků) a porovnal ho s tím, co jsi během vývoje požadoval
(historie v `2026-10-10-224531-.../txt` a `docs/SPEC.md`). Níže je mapa nalezených chyb a nedodělků, u každé příčina a oprava.

**Jak je to ověřené:** kód jsem kontroloval čtením a překladačem C# (Roslyn), který odhalí syntaktické chyby a chyby v kódu
pluginu samotném. Knihovny serveru Rust jsem tu neměl, takže plný překlad proti hře udělej u sebe:
`powershell -File tools/build.ps1`. Ve hře jsem nic nespouštěl.

## Chyby, které jsem opravil

| # | Co se dělo | Příčina | Oprava |
|---|---|---|---|
| 1 | Býk a kráva spadli na zem, když narazili do jiného zvířete. | Při nárazu se přehrála animace „hurt“, a ta je u nich pád (knockdown). | Při nárazu se přehraje útok hlavou (charge). Zvíře nespadne, cíl dostane poškození. |
| 2 | Držený Space skákal znovu a znovu (každých 1,2 s). | Plugin bere z ovládání diváka Space a Ctrl, takže hra viděla drženou klávesu jako nový stisk. | Plugin si stisk hlídá sám. Jeden stisk je jeden skok. |
| 3 | Ctrl + R v podobě zvířete neotevřelo menu. Při jízdě se menu otevřelo a hned zavřelo. | Ctrl zmizel dřív, než ho menu přečetlo. Při jízdě reagovaly dvě obsluhy najednou. | Ctrl + R obsluhuje jedno místo. Funguje s prázdnýma rukama, při jízdě i jako zvíře. |
| 4 | Zvíře na nízké plošině (základ, schod, bedna) propadlo skrz ni. | Plugin vracel zvíře na navigační síť (navmesh) terénu hned vedle plošiny a stáhl ho dolů. | Zvíře se na navmesh vrátí jen tam, kde opravdu stojí na terénu. |
| 5 | Zvíře, ze kterého jsi odešel (X) na plošině nebo ve skoku, zamrzlo nebo se pohybovalo mimo svou pozici. | Navigace zvířete zůstala vypnutá. | Při konci ovládání se navigace zapne a zvíře se postaví na nejbližší navmesh. |
| 6 | Po návratu do člověka (X) uvnitř base jsi mohl skončit pod podlahou nebo v základu. | Výška se brala jen z terénu. | Hledá se povrch, na kterém zvíře stojí (podlaha, střecha, kámen, terén). |
| 7 | Po seskočení ze zvířete jsi mohl zůstat stát na jeho zádech. | Po sesednutí tě nikdo neodsunul. | Postavíš se vedle zvířete. |
| 8 | Zvuk a efekt proměny „někdy ano, někdy ne“. | Efekt mizení šel jen tobě, a to dvakrát; ostatní ho neviděli. Efekt objevení u zvířete se k tobě nedostal, protože ses v tu chvíli přepínal do pohledu zvířete. | Efekt se posílá přímo každému hráči v okolí i tobě, každému jednou. Proměna má stejný efekt a zvuk jako opuštění zvířete. |
| 9 | Rituál měl dvojitý zvuk proměny. | Navíc se pouštěl kouř u těla. | Rituál používá jen efekty proměny. |
| 10 | Jako zvíře šla zapnout první osoba přes `/bm view first`, `/bm preset c/d` a tlačítka v menu. | Chyběl zákaz. | Podoba zvířete je jen ve třetí osobě, jízda má oba pohledy. |
| 11 | Jízda začínala vždy v první osobě. Presety při jízdě nedělaly nic. | Ignorovala se tvoje volba. | Jízda si pamatuje poslední pohled (G, R, menu). |
| 12 | E na koně, kterého jsi opustil (X), psalo hlášky o ochočování místo nasednutí. | Chyběla podmínka pro opuštěného koně. | E nasedne, Ctrl + E otevře jeho batoh. |
| 13 | Nápověda slibovala druhého jezdce s dvojitým sedlem, ale nebylo to naprogramované. | Nedodělek. | E na zvíře, na kterém někdo jede s dvojitým sedlem: sedneš za něj (vlastník a tým). Space = sesednout. |
| 14 | Sebraná věc jako zvíře mohla nechat na zemi prázdný předmět a nic se nenapsalo. | Neúplné sebrání. | Věc zmizí ze země a jde do batohu zvířete se zprávou. Při plném batohu zůstane ležet. |
| 15 | Odhlášení ve zvířeti ztratilo jeho hlad a žízeň a lidské jídlo a vodu. | Staty se smazaly s odpojením. | Drží se do návratu. |
| 16 | Hráčovi koně se sedlem (sedlo ve vybavení) by po načtení pluginu přišli o sedadla, takže by na nich nešlo jezdit. | Divokými se dělali všichni koně ve světě. | Kůň se sedlem ve vybavení zůstává hráčův. Ostatní koně jsou divocí a bez sedla, jak jsi chtěl. |
| 17 | Zpráva při množení: „The you are pregnant“. | Překlep ve skládání věty. | Opraveno. |
| 18 | `/bm debug abc` nebo `/bm clear abc` skončily chybou. | Chybějící kontrola čísla. | Ošetřeno. |
| 19 | Přepínač nápověd kláves v menu se projevil až za 20 s. | Neprovedlo se překreslení. | Projeví se hned. |
| 20 | Nápověda, `/bm keys` a stránka Controls popisovaly staré ovládání (G/R jako pohled zvířete, F5 a H „automaticky“, presety C/D, noční vidění „funguje samo“). | Texty se neaktualizovaly. | Přepsáno podle současného ovládání. |
| 21 | `README.md` obsahoval nevyřešený merge konflikt (`<<<<<<< HEAD`). | Chyba při commitu. | Přepsáno. |
| 22 | Jelen nevydával zvuky při útoku a zranění. | Hra jelenovi žádné zvuky nedává (jeho animace zvuk nemají). | Plugin zkusí najít zvuk jelena v souborech hry. V configu (Combat → Stag ... sound) jde nastavit vlastní efekt. Když žádný není, ozve se aspoň zvuk zásahu. Ostatní druhy při zásahu přehrají lehkou reakci (nikdy pád). |
| 23 | V configu zůstala sekce „Inventory“ s obrázky, kterou jsi zrušil. | Pozůstatek. | Odstraněna. |

## Co server udělat nedokáže (nezměněno)

* **Skrytí HUD hráče:** plugin hru požádá o `graphics.hud 0`, ale hra to nemusí přijmout. Lišty zdraví, jídla a vody proto ukazují hodnoty zvířete.
* **Klávesy:** server nemůže přiřadit klávesy. Ctrl + R a `/bm` fungují vždy. F5 a H fungují po jednorázovém `bind f5 bm.menu` a `bind h bm.nv` v konzoli F1.
* **Inventář (Tab) jako zvíře:** pohled diváka ho v klientovi blokuje. Věci jdou do batohu zvířete.
* **Výška sedla podle druhu:** nejde odhadnout bez hry. Admin si sedne na zvíře a zadá `/bm seat up` nebo `/bm seat down` (nebo tlačítka Seat up/down na stránce Controls). Uloží se pro druh a pro všechny.

## Po nahrání na wespner

1. Nahraj `dist/Beastmaster.cs` do `oxide/plugins` (Carbon: `carbon/plugins`).
2. **Smaž `oxide/config/Beastmaster.json` a `oxide/lang/en/Beastmaster.json`**, jinak zůstanou staré hodnoty a staré texty nápovědy.
3. `oxide.reload Beastmaster` (Carbon: `c.reload Beastmaster`). V konzoli musí být `Beastmaster 1.1.0 ready`.

## Co otestovat (hlavně opravené věci)

1. Rozběhni se jako býk nebo kráva do jiného zvířete: nespadneš, druhé zvíře dostane poškození.
2. Drž Space: skočí jen jednou.
3. Jako zvíře a při jízdě stiskni Ctrl + R: menu se otevře, dalším Ctrl + R zavře.
4. Vyskoč na nízký základ, schod nebo bednu a projdi se po nich: nepropadneš. Sejdi nebo seskoč dolů.
5. Na plošině stiskni X: postavíš se vedle zvířete na podlahu, ne pod ni. Odejdi a vrať se k němu: nesmí stát zamrzlé.
6. Proměna (rituál i `/bm form`): pokaždé efekt a zvuk, tvůj i druhého hráče vedle.
7. Jízda: G nebo R přepne pohled, sesednutí a nové nasednutí si pohled pamatuje.
8. X u koně, kterého jsi opustil: E nasedne, Ctrl + E otevře batoh.
9. Ochočené zvíře s dvojitým sedlem: jeden hráč jede, druhý dá E a sedne za něj.
10. Jako zvíře seber věc (E): přijde zpráva, po X ji najdeš přes Ctrl + E v batohu zvířete.
11. Jelen: útok a zásah, zda je slyšet zvuk.
12. Výška sedla u druhů, kde sedíš špatně: `/bm seat up|down`.
