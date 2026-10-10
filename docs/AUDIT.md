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
