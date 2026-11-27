# Changelog

## 0.4.3

- Eenvoudigere navigatie op desktop en mobiel: banktaken bij Transacties, komende
  betalingen bij Vooruitblik en minder gebruikte onderdelen onder Meer.
- Financiële kalender staat als eerste onder Overzichten. De samenvatting, legenda
  en maandkalender staan bovenaan; waarschuwingen en instellingen zijn eronder uitklapbaar.
- **Te controleren** op het dashboard opent de wekelijkse controle met rechtstreekse
  links naar de juiste rekening, saldocontrole en boekingen zonder categorie.
- Herkende bankafschriften direct beoordelen na rekening- en bestandskeuze. Periode
  en kolommen zijn aanpasbaar; dubbels, afschriftdekking en saldocontrole blijven bewaakt.
- Geregistreerd saldo, maandresultaat na sparen en geschatte bestedingsruimte zijn
  duidelijker onderscheiden, met waarschuwingen bij onvolledige prognosegegevens
  en beter leesbare betaaldatums op het dashboard.
- Bevat ook de elektriciteitsverbeteringen uit de sectie 0.4.2.1.
- App- en containerversie bijgewerkt naar 0.4.3.

## 0.4.2.1

- Alle geïmporteerde kwartiermetingen en dagtotalen wissen voor één gekozen EAN,
  met controle vóór bevestiging. Andere aansluitingen en contractgegevens blijven bewaard.
- Fluviuskosten en overheidsheffingen per EAN en maand invoeren, los van de vaste
  leveranciersvergoeding. Het bedrag telt één keer mee in het maandsubtotaal;
  schema 23 bewaart de invoer in volledige back-ups.
- Elektriciteitskosten per maand vergelijken in een jaartabel met afname, injectie,
  vaste kosten en meetdekking. Open een maand voor de uitsplitsing per dag.
- De pdf-import herkent ook Luminus BasicFix-tariefkaarten met de tabelkoppen
  Dag/Nacht en zonder regiokop. Netkosten en voorbeeldprijzen worden niet overgenomen.
- Fluvius-maandpieken importeren uit de CSV Piekvermogen. Het geschatte
  capaciteitstarief gebruikt de beschikbare pieken, met zichtbare dekking en voorlopige waarden.
- Groene stroom en WKK, variabele netkosten, heffingen en databeheer per tariefperiode
  berekenen, met een invulvoorstel voor Luminus en Fluvius Antwerpen in 2026.
  Een handmatig maandbedrag voorkomt dubbeltelling; schema 24 bewaart toeslagen en pieken.

## 0.4.2

- Fluvius-dagtotalen importeren naast kwartiermetingen, met behoud van dag-/nachtregisters
  voor afname en injectie. Dagtotalen vullen ontbrekende kwartierdekking aan zonder
  dubbeltelling; dynamische afnamekosten blijven gebaseerd op gemeten kwartieren.
  Schema 22 bewaart beide resoluties in back-ups en behoudt bestaande metingen.
- Eerdere kwartiermetingen wissen per EAN en datumbereik, met controle vóór bevestiging.
  Dagtotalen en kwartiermetingen buiten de selectie blijven bewaard.
- App- en containerversie bijgewerkt naar 0.4.2.

## 0.4.1

- Gekoppelde contracten rechtstreeks openen vanuit Vaste posten en de bijbehorende
  bankboekingen, ook op mobiel. Bedragen en betalingsplanning blijven ongewijzigd.
- App- en containerversie bijgewerkt naar 0.4.1.

## 0.4

- Abonnementen & contracten bij Planning, met aanbieders, productnamen, prijzen,
  proefperiodes, kortingen, verlengdatums en opzegvoorwaarden. Expliciete deadlines
  gaan vóór berekende datums; onbekende voorwaarden blijven controlepunten.
- Contracten buiten geregistreerde rekeningen bijhouden, met aparte prijstotalen
  en zonder bankboekingen, saldo-, budget- of prognosewijzigingen. Bestaande vaste
  posten zijn optioneel te koppelen of als invoervoorstel over te nemen.
- Mobiele/internetvoorwaarden en streamingkenmerken bewaren; zoeken, type- en
  eigenaarfilters, contractlinks, notities en opgezegde/verlopen historie beschikbaar.
  Dashboard en wekelijkse controle tonen komende datums en te controleren opzeggingen.
- Databaseschema 19 bewaart contracten in volledige back-ups, met veiligheidskopie
  vóór migratie en behoud van oudere administraties. Automatische prijsvergelijkingen
  uitgesteld tot een betrouwbare gegevensbron met passende gebruiksrechten beschikbaar is.
- Elektriciteit voor Vlaanderen bij Planning: tariefhistorie, enkelvoudige of
  dag-/nachttarieven, injectie, btw en vaste vergoedingen. Luminus BasicFix-pdf's
  geven lokaal een te controleren invulvoorstel; het origineel bewaren is optioneel.
- Handmatige Fluvius-kwartierimport met controle op dubbels, correcties en zomer-/wintertijd.
  Dag- en maandkosten tonen gegevensdekking en een deelsubtotaal, zonder bankboekingen
  te wijzigen. Belgische day-aheadprijzen zijn alleen beschikbaar bij dynamische afname.
- Databaseschema 20 neemt energiecontracten, tarieven, metingen en prijzen mee in
  de volledige back-up, met een veiligheidskopie vóór migratie.
- Contracten kunnen op **Onbepaalde duur** staan, zonder waarschuwingen voor ontbrekende
  verleng- of opzegdatums. Ingevulde datums blijven controlepunten; schema 21 bewaart
  de keuze zonder bestaande contracten automatisch aan te passen.
- App- en containerversie bijgewerkt naar 0.4 voor installatie en updates via Home Assistant.

## 0.3.4

- Subcategorieën aanmaken en bestaande categorieën onder een andere categorie of
  terug op hoofdniveau plaatsen, met herkenbare categoriepaden en behoud van
  boekingen en afzonderlijke budgetten. Bij verwijderen schuiven subcategorieën
  één niveau omhoog; schema 18 bewaart de indeling met veiligheidskopie vóór migratie.
- Bankimport slaat overeenkomende eerdere boekingen standaard over, telt gelijke
  betalingen per voorkomen en vraagt controle bij onzekere overeenkomsten.
  Herhaalde afschriften kunnen zonder nieuwe boekingen worden afgerond; bevestigde
  afschriftdekking houdt rekening met al geïmporteerde regels en vervalt bij wijzigen
  of terugdraaien van de oorspronkelijke overlappende bankgegevens.
- Periodekeuze afgestemd op de pagina, met duidelijke peildatums en een snelkoppeling
  naar de huidige maand; jaaroverzicht toont alleen de jaarkeuze.
- Compact mobiel menu en vaste snelle navigatie, met direct uitgaven toevoegen.
  Dashboard biedt snelle uitgave-/inkomsteninvoer en bankimport.
- Recente boekingen, rekeningen en categorietotalen openen direct de bijbehorende
  bankboekingen met behoud van periode en eigenaar; trends blijven apart bereikbaar.
- Transacties filteren op categorie, bankrichting, bedrag en eigen periode, met
  actieve filters, eenvoudig wissen en duidelijk onderscheiden banktotalen.
- Budgetten tonen gebruikspercentages en een indicatief maandtempo bij bevestigde
  afschriftdekking; vaste posten en onvolledige gegevens krijgen passende uitleg.
- Financiële vooruitblik toont verwacht saldoverloop over 7, 30 of 90 dagen, met
  bufferlijn, laagste dagsaldi en gedateerde betaalmarkeringen naast de exacte tabel.
- Bankimport begeleidt vier stappen, met ingeklapte technische instellingen,
  controlepuntenfilter, mobiele boekingskaarten en een blijvende selectiesamenvatting.
- Gedeelde invoervensters tonen fouten bij de velden en waarschuwen bij het verlaten
  van gewijzigde invoer; niet-opgeslagen imports krijgen eveneens een waarschuwing.
- Samenhangende weergave met licht/donker/systeemkeuze, beter leesbare labels en
  vaste categoriekleuren onafhankelijk van rangorde, maand of overzicht.
- App- en containerversie bijgewerkt naar 0.3.4 voor installatie en updates via Home Assistant.

## 0.3.3.3

- Automatische uitgavenramingen beperken tot gekozen categorieën, standaard Voeding
  en Medische kosten. Uitgesloten categorieën tellen ook niet meer mee in de
  dagreserve; bekende betalingen, inkomsten en vaste posten blijven behouden.
- Terugkerende aankoopdagen herkennen op basis van herhaling, met afzonderlijke ramingen
  per weekdag. Incidentele aankopen worden geen wekelijks patroon; geleerde bedragen
  vervangen hun deel van de dagraming zonder dubbele aftrek.
- Kalender en Financiële vooruitblik tonen de ramingsscope; leerpatronen tonen
  herkende dagen en concrete uitleg bij ontbrekende dekking, aankoopdatums of
  acceptatie. Categorievoorkeuren blijven bewaard in databaseback-ups.
- App- en containerversie bijgewerkt naar 0.3.3.3 voor installatie en updates via Home Assistant.

## 0.3.3.2

- App- en containerversie bijgewerkt naar 0.3.3.2 voor installatie en updates via Home Assistant.

## 0.3.3.1

- Financiële kalender toont geraamde uitgaven bij toekomstige saldi, met aparte
  iconen voor geleerde bestedingspatronen en overige dagelijkse ramingen. Compacte
  bedragen blijven ook op mobiel zichtbaar; dagdetails tonen de afzonderlijke posten.
  Saldoberekening, risicokleuren en automatisch leren blijven ongewijzigd.
- Geraamde kalenderuitgaven hebben een lavendelkleurige achtergrond en paarse
  onderbroken rand, ook in legenda en dagdetails, zodat ze niet op gewone betalingen lijken.

## 0.3.3

- Financiële kalender bij Financiële vooruitblik met vaste posten, inkomsten,
  verwachte bestedingen en dagelijkse saldi tot 90 dagen vooruit. Kleuren en
  tekst tonen betaaldruk, bufferproblemen en mogelijke tekorten per rekening.
- Financiële kalender en geleerde bestedingspatronen op een eigen pagina onder
  Overzichten, met een koppeling vanuit Financiële vooruitblik. Aankoopcontrole
  en gedetailleerde prognosetabellen blijven bij Financiële vooruitblik.
- Lokale leerpatronen per categorie en betaalrekening, met aankoopdatums uit
  bankteksten of gecontroleerde bonnen. Na eenmalige acceptatie leren bedragen
  en weekdagen verder; eigen dagen/bedragen, pauzeren en losse aankopen overslaan
  blijven mogelijk. Onvolledige afschriften gelden niet als stille weken.
- Bankdatums en werkelijke bedragen blijven behouden. Geleerde bestedingen
  vervangen hun deel van de dagelijkse raming; splitsingen, maaltijdcheques,
  geboekte aankopen en onzekere reserves blijven gescheiden van werkelijke bankkosten.
- Databaseschema 17 bewaart leerinstellingen en aankoopdatumcorrecties in
  databaseback-ups, met veiligheidskopie vóór migratie en behoud van oudere back-ups.
- App- en containerversie bijgewerkt naar 0.3.3 voor installatie en updates via Home Assistant.

## 0.3.2

- Compact actiemenu via de ⋯-knop bij transacties en transactieherkenning, ook op mobiel.
  Bestaande acties en verwijderbevestigingen blijven behouden; het menu is met het
  toetsenbord te bedienen en sluit bij een keuze, buiten het menu klikken of Escape.
- App- en containerversie bijgewerkt naar 0.3.2 voor installatie en updates via Home Assistant.

## 0.3.1

- Eén herkenningsinstelling voor een leesbare transactienaam, optionele categorie en
  optionele vaste-postplanning. Herkenning blijft werken bij afwijkende bedragen of
  bankdatums; alleen de planningskoppeling kan controle vereisen.
- Herkende namen zichtbaar bij import en transacties, met behoud van de oorspronkelijke
  banktekst en voorrang voor eigen omschrijvingen. Historische wijzigingen alleen na
  een voorbeeld met expliciet geselecteerde boekingen; bestaande categorieën en koppelingen blijven behouden.
- Automatische database-upgrade naar schema 16 met veiligheidskopie vooraf. Bestaande
  regels, volgorde, vaste posten en transactiehistorie blijven behouden; overlappende
  instellingen worden niet stilzwijgend samengevoegd.
- App- en containerversie bijgewerkt naar 0.3.1 voor installatie en updates via Home Assistant.

## 0.3.0.4

- Hypotheekrente expliciet instelbaar als nominale jaarrente, effectieve jaarrente
  of exacte periodieke maandrente; bestaande hypotheken behouden hun berekening.
- Hypotheekschema standaard vanaf de peildatum, met aparte prognose vanaf vandaag.
  Theoretische aflossingen blijven gescheiden van werkelijke schuld; uitleg verduidelijkt
  afgeronde rentetarieven en afwijkende eerste betalingen met dagrente.
- App- en containerversie bijgewerkt naar 0.3.0.4 voor installatie en updates via Home Assistant.

## 0.3.0.3

- Een losse premiumverwijzing in een providermelding wordt niet langer als ontbrekende
  betaalde gegevensrechten uitgelegd. Concrete sleutel- en quotafouten krijgen voorrang;
  een onbekende melding blijft als onbekende oorzaak zichtbaar.
- API-resultaten tonen de werkelijk gebruikte, veilige aanvraagparameters en een vaste
  herkenningsreden, inclusief een weggelaten of ingestelde entitlement. Sleutels, zoektermen,
  aanvraag-URL's en onbewerkte providerantwoorden blijven buiten de diagnostiek.
- Nieuwe AMZN-basistest met één slotkoersaanvraag vanaf de app-server, zonder entitlement
  of voorafgaande FX-aanvraag. De test respecteert lokale limieten en wijzigt geen portefeuillekoersen.
- App- en containerversie bijgewerkt naar 0.3.0.3 voor installatie en updates via Home Assistant.

## 0.3.0.2

- Een gemelde API-providerlimiet veroorzaakt niet langer een vaste blokkering van
  24 uur; ook bestaande blokkeringen vervallen zonder koersen, sleutel of verbruik te wissen.
- Bij providerlimieten verschijnt een afteller met hervattijd: een opgegeven
  Retry-After wordt gerespecteerd, anders is één minuut wachten alleen een duidelijk
  gemarkeerd advies en blijft handmatig opnieuw proberen mogelijk binnen de lokale limieten.
  Het lokale dagbudget toont apart wanneer het om 00:00 UTC opnieuw beschikbaar is.
- App- en containerversie bijgewerkt naar 0.3.0.2 voor installatie en updates via Home Assistant.

## 0.3.0.1

- Koersaanvragen tonen naast de knoppen voortgang en een blijvend resultaat per
  effect en valutapaar, met concrete foutcodes, vervolgstappen en eventuele HTTP-status.
  Zichtbaar is welke gegevens zijn vernieuwd, behouden of nog ontbreken; sleutels,
  aanvraag-URL's en onbewerkte providerantwoorden blijven buiten de diagnostiek.
- Ontbrekende gegevensrechten voor een afzonderlijke wisselkoers blokkeren niet langer
  een beschikbare aandelenkoers. Limieten en verbindingsfouten stoppen verdere aanvragen;
  een volledig mislukte update wordt niet meer als gedeeltelijk geslaagd gemeld.
- De provider-time-out geldt ook tijdens het lezen van de antwoordinhoud, zodat een
  vastgelopen antwoord de koersaanvraag niet onbeperkt bezet houdt.
- Documentatie aangevuld met veilige handmatige USD→EUR- en AMZN-testaanvragen en de
  bevestiging dat beide basisaanvragen werken; de oorspronkelijke app-oorzaak is nog onbekend.
- App- en containerversie bijgewerkt naar 0.3.0.1 voor installatie en updates via Home Assistant.

## 0.3.0

- Koersinstellingen overzichtelijk gegroepeerd in verbinding, updateschema en API-limieten,
  met korte labels, aparte hulpteksten en een mobiele indeling. Alleen relevante
  tijdvelden voor de gekozen opvraagfrequentie worden getoond.
- Beleggingen met prestatiekaarten per aandeel of ETF: koersverschil sinds de vorige
  handelsafsluiting en gewogen resultaat sinds aankoop, zonder grafieken. Compacte
  samenvatting op het dashboard; eigenaarfilters blijven gelden.
- Historische aankopen, verkopen, openingsposities en splitsingen per beleggingsrekening,
  met fractionele aantallen, kosten, TOB en valuta. FIFO voor verkopen; werkelijke
  EUR-afrekeningen of expliciete historische wisselkoersen behouden oude aankoopkosten.
- Optionele Alpha Vantage-koppeling met een eigen, lokaal versleutelde API-sleutel,
  dagelijkse updates als standaard en instelbare frequentie, budgetten en betaalde
  gegevensrechten. Tests, zoekacties, FX en handmatig vernieuwen delen blijvende limieten;
  mislukte aanvragen behouden de laatste koers en worden niet als nul opgeslagen.
- Portefeuilleschattingen blijven apart van handmatige effectenwaarderingen en
  bankboekingen. Databaseschema 15 met veiligheidskopie vóór migratie; effecten,
  transacties en handmatige koersen gaan mee in databaseback-ups, API-sleutels niet.
- App- en containerversie bijgewerkt naar 0.3.0 voor installatie en updates via Home Assistant.

## 0.2.3.2

- Bankboekingen zoeken bij Aankopen & garanties toont alle overeenkomsten uit de
  volledige historie van de gekozen rekening(en), ook buiten de 80 recentste boekingen.
  Combineer banktekst, datum, Nederlandse of Engelse maandnaam en bedrag met komma of punt.
- Historische maaltijdchequeontvangsten en een eventueel beginsaldo direct bij een
  aankoopbon vastleggen, met de aankoopdatum als voorstel. Het gezamenlijke saldo wordt
  meteen bijgewerkt zonder boninvoer te verliezen; de cheque-invoer blijft apart bewaard.
- App- en containerversie bijgewerkt naar 0.2.3.2 voor installatie en updates via Home Assistant.

## 0.2.3.1

- Bedragvelden bij Aankopen & garanties werken totalen direct tijdens het typen bij,
  met komma of punt als decimaalteken. Geen pijltjesknoppen of onbedoelde wijzigingen
  door scrollen; ongeldige invoer blijft zichtbaar en blokkeert opslaan.
- App- en containerversie bijgewerkt naar 0.2.3.1 voor installatie en updates via Home Assistant.

## 0.2.3

- Standaardcategorie per aankoopbon, met afwijkende categorieën per productregel.
  Handmatige keuzes blijven behouden bij een gewijzigde standaard; zoeken en
  categorievoorbeelden gebruiken de gekozen bon- en regelcategorieën. Categorieën
  verwijderen wist ook bonverwijzingen, zonder aankoopgegevens te verwijderen.
- Maaltijdcheques als aparte betaling vastleggen zonder negatieve productregel:
  het volledige bontotaal blijft controleerbaar en alleen het resterende bedrag
  wordt met de bankboeking vergeleken. Duidelijk benoemde betalingen worden uit
  bontekst herkend; volledig met maaltijdcheques betaalde bonnen kunnen zonder bankkoppeling.
- Databaseschema 13 met veiligheidskopie vóór migratie en behoud van bestaande
  aankoopbonnen en oudere databaseback-ups.
- Eén gezamenlijke maaltijdchequeregistratie met ontvangstdatum, aantal, waarde per
  cheque en optioneel beginsaldo. Ontvangsten tellen als extra inkomen; het beginsaldo
  niet. Bestedingen verlagen het tegoed, zonder banksaldi of cashreserves te verhogen.
- Gecontroleerde bonnen expliciet opnemen in budgetten, grafieken en categorietrends:
  de volledige aankoop telt per productcategorie mee, ook bij gedeeltelijke of volledige
  betaling met maaltijdcheques. De gekoppelde bankbetaling wordt niet dubbel geteld.
- Bonsplitsingen met aansluitende centen, bescherming van bestaande splitsingen en
  retourkoppelingen, en veilig opnieuw toepassen of verwijderen. Gedeelde cheque-inkomsten
  en -kosten staan bij het hele huishouden; eigenaarfilters behouden alleen hun bankdeel.
- CSV-export uitgebreid met maaltijdcheque-inkomsten, -uitgaven en bonverwijzingen.
  Databaseschema 14 bewaart ontvangsten en toegepaste bonnen, met veiligheidskopie vóór
  migratie en behoud van oudere back-ups.
- App- en containerversie bijgewerkt naar 0.2.3 voor installatie en updates via Home Assistant.

## 0.2.2

- Nieuwe pagina Aankopen & garanties met lokale bonherkenning uit foto's en PDF's:
  productregels, aantallen/gewichten, prijzen, kortingen en statiegeld controleren,
  aanvullen en per regel een bestaande uitgavencategorie kiezen. Verschillen blijven
  zichtbaar; categoriesplitsingen worden nog niet op bankboekingen toegepast.
- Bonbestand bewaren is optioneel en staat bij nieuwe bonnen uit. Zonder vinkje
  blijven alleen aankoopgegevens en productregels bewaard, zonder afbeelding of PDF.
  Bewaarde originelen gaan mee in volledige databaseback-ups.
- Aankopen aan bankboekingen koppelen zonder bedragen te wijzigen; producten zoeken,
  serienummers, retour- en garantiedatums vastleggen en komende termijnen bekijken.
  Bestaande vergoedingsverwachtingen zijn koppelbaar; datums worden niet geraden.
- Databaseschema 12 met veiligheidskopie vóór migratie, behoud van oudere database-imports
  en bonbijlagen buiten gewone gegevensmomentopnamen. Back-updownloads worden gestreamd;
  databaseherstel ondersteunt bestanden tot 256 MB.
- App- en containerversie bijgewerkt naar 0.2.2 voor installatie en updates via Home Assistant.

## 0.2.1

- Extra Home Assistant-kaarten voor budgetbewaking, komende betalingen en volgende
  inkomensdatums, gegevensstatus en categorietrends. Budgetten, onzekerheden en
  eigenaarfilters volgen de bestaande berekeningen; dashboard en kaarten delen de weergave.
- Importkaart voor betaalrekeningen met laatste actieve import, ouderdom per rekening
  en een wekelijks streefritme. Groen tot zeven dagen, via geelgroen, amber en roodoranje
  naar donkerrood na dertig dagen; onbekende importdatums blijven expliciet onbekend.
  De oudste of ontbrekende import bepaalt de kop; bankactualiteit staat apart.
- Categorietrendkaart met 6, 12 of 24 afgesloten maanden, driemaandsgemiddelde,
  negatieve bedragen onder nul en exacte maandwaarden. Statische SVG zonder scripts
  in de kaart; ontbrekende afschriftdekking blijft zichtbaar en is geen nuluitgave.
- Lokale galerij en installatiehulp uitgebreid voor alle acht kaarten, met categorie-id's,
  trendinstellingen en YAML-voorbeelden. Applicatieversies blijven ongewijzigd.
- Ongeldige vervangende kaartinstellingen verbergen direct de vorige financiële
  weergave; een oude eigenaar- of categoriecontext blijft niet in beeld.
- Proefversie van losse Home Assistant-dashboardkaarten voor het maandoverzicht,
  de uitgavenverdeling en de compacte financiële vrijheidskaart. Hergebruikt de
  bestaande berekeningen via ingress, zonder extra poort, API-token of bewerkknoppen.
- Lichtgewicht kaartpagina's zonder Blazor-verbinding per kaart, met automatisch
  vernieuwen, passende hoogte, lichte/donkere weergave en Nederlandse/Engelse teksten.
  Bankactualiteit en ontbrekende historie blijven zichtbaar; eigenaarfilters geven
  geen extra toegangsrechten. Kaartbestand en installatiehulp staan bij Beheer & import.
- Realistische lokale kaartgalerij met desktop-, tablet- en telefoonindeling,
  lichte/donkere weergave, taal-, maand- en eigenaarkeuze en YAML-voorbeelden.
  Alleen in de loopback-preview; de echte Home Assistant-aanmelding blijft ongewijzigd.
- Uitleg binnen de uitgavenring benut de beschikbare ruimte, zonder onnodige
  woordafbreking op smalle Home Assistant-kaarten.
- Aparte handleiding `HOME-ASSISTANT-CARDS.md` met installatie, architectuur,
  beveiligingsgrenzen, mogelijke vervolgkaarten en overdracht naar een nieuwe werksessie.

## 0.2.0

- Lokale preview blijft opgemaakt na CSS-wijzigingen en opnieuw bouwen: stylesheets
  worden rechtstreeks geladen, zonder verouderde gecomprimeerde bestanden uit de bouwcache.
- Overal dezelfde diepgroene zijbalk met zes hoofdkeuzes en uitklapbare submenu's
  voor overzichten, transacties, planning en beheer. De actieve pagina opent automatisch
  de juiste groep; de voorkeur voor wekelijkse controle blijft behouden.
- Automatische tests en bijbehorende testafhankelijkheden verwijderd; de bouwinstructies
  verwijzen alleen nog naar de applicatie.
- Bovenste dashboardkaarten compacter: maandresultaat en maandcijfers staan op brede
  schermen naast elkaar; rekeningsaldi verschijnen als korte regels. Minder witruimte,
  kleinere bedragen en minder geneste vlakken laten uitgaven en betalingen eerder zien.
- Dashboard visueel verfijnd met een diepgroene maandkaart, zachtere achtergrond,
  grotere bedragen, gekleurde rekeningkaarten en een uitgavenring naast de categoriebalken.
  Bijpassende navigatie, subtiele schaduwen en betere mobiele verhoudingen; geen extra
  introductietekst of actieknoppen. Bedragen, filters en bankwaarschuwingen blijven intact.
- Minimalistischer dashboard zonder introductiekop, ondertitel of actieknoppen bovenaan.
  Rustige kaarten met gelijkmatige kolommen, minder herhaalde labels en compacte
  navigatielinks. Alleen echte aandachtspunten of instelhulp tonen een actiebalk;
  bankwaarschuwingen, berekeningsuitleg en rekeningkeuze blijven behouden.
- Opnieuw ontworpen dashboard met een centrale financiële momentopname, compacte
  rekeningsaldi en direct daaronder de uitgavenverdeling en komende betalingen.
  Eén gerichte volgende stap krijgt voorrang; overige aandachtspunten en uitleg
  zijn uitklapbaar. Geen geruststellende status op basis van ontbrekende bankgegevens.
- Uitgaven als direct leesbare categoriebalken met bedragen, percentages en trendlinks.
  Budgetten en recente boekingen blijven beschikbaar, met een compactere mobiele indeling.
- Financiële vrijheidskaart van het dashboard verwijderd; de volledige analyse blijft
  beschikbaar onder Inzichten. Vernieuwde mobiele weergave en Nederlandse/Engelse teksten.
- Lokale preview herstart na bronwijzigingen, zodat gewijzigde dashboardvertalingen
  niet als interne sleutelnamen verschijnen door verouderde Hot Reload-resources.
  Extra regressiecontroles voor ontbrekende Nederlandse en Engelse dashboardteksten.

## 0.1.16

- Bankperioden rechtstreeks per betaal- of spaarrekening bevestigen, ook zonder import
  en voor meerdere maanden of jaren tegelijk. Bevestigingen veranderen geen boekingen
  of saldi en blijven bewaard in databaseback-ups. Gewijzigde bankgegevens vragen om
  een nieuwe controle; categoriseren trekt een bevestiging niet in.
- Inzichten toont ontbrekende rekeningen en datumbereiken per maand, een concrete
  uitleg bij onvoldoende historie en duidelijke meldingen in plaats van lege ringen.
  Bevestigde imports krijgen een bevestigingsstatus en de actie Afschriftperiode wijzigen.
- Databaseschema 11 met een veiligheidskopie vóór migratie van schema's 1–10.

## 0.1.15

- Nieuwe pagina Inzichten met een financiële vrijheidskaart: maanden essentiële
  uitgaven zonder inkomen, bufferdoel, bestedingsruimte, vaste verplichtingen en
  leefstijlbereik. Compacte kaart op het dashboard, exacte bedragen en uitleg per ring.
- Categorieën als essentieel of uitzonderlijk markeren. Uitzonderlijke kosten blijven
  in werkelijke saldi, budgetten en maand-/jaaroverzichten, maar verstoren geen gewone
  uitgavenschattingen of gedragspatronen. Toekomstige betalingen blijven gereserveerd.
- Geldvingerafdruk met zes verklaarbare dimensies, daggemiddelde-vergelijkingen en
  bronboekingen. Alleen voldoende bevestigde bankhistorie ondersteunt ontdekkingen;
  ontbrekende gegevens worden nooit als nul of een perfecte score gepresenteerd.
- Instelbare toegankelijke reserverekeningen, extra gereserveerd geld en een bufferdoel.
  Beleggingen tellen niet mee als liquide noodreserve; bereik is geen financiële
  onafhankelijkheid. Nederlands/Engels, responsive SVG's zonder externe diensten.
- Databaseschema 10 met veiligheidskopie vóór migratie van schema's 1–9 en veilige
  standaardinstellingen. Nieuwe regressietests voor berekeningen, migratie en weergave.

## 0.1.14

- Nieuwe pagina Categorietrends: historische maanduitgaven per categorie met
  kleurrijke bedragstaven, een voortschrijdend gemiddelde over drie maanden,
  een vergelijking van twee jaren en een interactieve kalenderheatmap.
- Kies 6, 12 of 24 afgesloten maanden of een eigen periode tot tien jaar.
  De huidige maand is optioneel en telt niet mee voor gemiddelden en jaarvergelijkingen.
  Eigenaarfilters, splitsingen, retouren en vergoedingen volgen de bestaande boekhoudregels.
- Exacte bedragen blijven uitklapbaar beschikbaar. Ontbrekende afschriftbevestigingen
  worden zichtbaar gemeld; geen boekingen betekent niet automatisch geen uitgaven.
- Rechtstreeks bereikbaar vanaf het dashboard, de navigatie en categorietotalen in
  maand-/jaaroverzichten. Beide talen, mobiele weergave en verminderde animatie ondersteund.

## 0.1.13

- Nieuw dashboard als startpagina met individuele rekeningsaldi van vandaag,
  compacte maandcijfers, uitgavencategorieën en budgetbewaking, komende betalingen,
  volgende inkomensdatums, recente boekingen en gerichte aandachtspunten.
- Rekeningkaarten kiezen per browser; standaard de betaalrekeningen/zichtrekeningen.
  Spaar- en beleggingsrekeningen zijn optioneel, met cash en effecten apart vermeld.
  De kaartkeuze verandert geen rapporten; eigenaarfilters blijven overal gelden.
- Het bestaande maandoverzicht blijft behouden als aparte pagina op `/maand`.
  Dashboard en maandoverzicht hebben elk een eigen navigatielink.
- Nederlandse/Engelse teksten, documentatie en regressietests voor de nieuwe startpagina,
  lokale voorkeuren, bankactualiteit, eigenaarfilters en onzekere betalingen.

## 0.1.12

- Rustiger maandoverzicht: maandcijfers direct bovenaan, zonder eerste-weekchecklist,
  wekelijkse controle, uitgebreide maanduitleg of bestedingsplanner erboven.
  Berekeningsdetails zijn uitklapbaar; compacte waarschuwingen verwijzen naar de uitleg.
- Aparte pagina's voor Financiële vooruitblik, Instellen en Wekelijkse controle.
  Instellen staat onder Beheer & import. De wekelijkse menulink is per browser te
  verbergen en daar te herstellen, zonder financiële gegevens of controles te wijzigen.
- Nederlandse/Engelse teksten, documentatie en regressietests bijgewerkt voor de
  nieuwe routes, Home Assistant-ingress en blijvende navigatievoorkeuren.

- Vaste posten met een maandlijst: verwacht, wacht op bankgegevens, herkend, te
  controleren of niet gevonden. Ontbrekende bankimports leveren geen waarschuwing
  per betaling meer op; de financiële vooruitblik groepeert onzekerheid per rekening.
- Expliciet volledige afschriftperioden bevestigen bij import, ook zonder activiteit.
  Oudere imports zijn onder Beheer & import te bevestigen zonder opnieuw te importeren.
  Gaten, rekeninggrenzen en import-undo blijven gerespecteerd; gewijzigde/verwijderde
  bankgegevens laten de bijbehorende bevestiging vervallen.
- Herkenning van vaste of wisselende bedragen, onschuldige tekstformatering en exact
  eerder gekoppelde omschrijvingen. Afwijkende vaste bedragen en nabije afwijkende
  bankdatums blijven controlevoorstellen, nooit automatische koppelingen.
- Vanuit een transactie een vaste post koppelen of maken; historische matches met
  datums, bedragen en selectievakjes bekijken en samen bevestigen. Dubbelzinnige
  boekingen, rekeninggrenzen en expliciete verwerking blijven beschermd.
- Onbevestigde uitgaven blijven een aparte, onzekere planningsreserve, geen als
  ‘morgen te betalen’ gepresenteerde rekening. Ze staan niet in gedateerde 7/30-dagentotalen.
  Werkelijke bankboekingen, bedragen, datums en saldi worden niet aangepast.
- Databaseschema 9 met veiligheidskopie vóór migratie van schema's 1–8. Bestaande
  vaste posten blijven standaard op vaste bedragen staan; oude importdekking wordt
  niet geraden. Nieuwe instellingen en afschriftperioden blijven in volledige back-ups bewaard.

## 0.1.11

- Eenvoudiger sparen zonder transfers koppelen: categorieën tellen als gewone
  inkomsten/uitgaven, sparen/beleggen of bewegingen tussen eigen rekeningen.
  Spaarbijdragen tellen alleen op de betaalrekening, zonder verplichte tweede import.
  Terugontvangsten verlagen opzijgezet, niet inkomsten; de spaarzijde telt niet dubbel.
- Maandoverzicht met overschot vóór sparen, apart opzijgezet en maandresultaat na sparen.
  Uitgaven boven inkomen, categoriebudgetoverschrijdingen en sparen boven het overschot
  krijgen verschillende verklaringen. Cashbuffer- en rekeningcontroles blijven actief.
- Budgetpagina splitst uitgavenbudgetten, spaardoelen en eigen rekeningbewegingen.
  Een gehaald spaardoel is geen rode budgetoverschrijding. Splitsingen, vaste posten,
  maand-/jaaroverzichten, CSV en prognoses gebruiken dezelfde categorie-instellingen.
- Transfermatching en het nieuwe handmatige transfertype uit dagelijks gebruik verwijderd.
  Oude transfers blijven ongewijzigd als historie, met beschermde financiële velden.
  Nieuwe bankboekingen wijzigen alleen hun eigen rekening; categoriseren nooit saldi.
- Databaseschema 8 met veiligheidskopie vóór migratie van schema's 1–7. Bestaande
  bankboekingen, transferbeslissingen en categorienamen worden niet herschreven.
  Beide talen en tests uitgebreid voor sparen, opnames, splitsingen, prognoses,
  eigenaarfilters, jaargrenzen en compatibel back-up/herstel.

## 0.1.10

- Automatische koppeling van vaste posten bij bankimport respecteert nu de ingestelde
  bedrag- en datummarges, zonder verborgen limiet van 1 cent of 5 dagen. De geselecteerde
  regels worden samen beoordeeld: meerdere mogelijke boekingen of geplande betalingen
  blijven ter controle. Werkelijke bankbedragen en volgende schattingen blijven intact.
- Automatische dubbelcontrole bij bankimports verwijderd: alle geldige regels binnen
  de gekozen periode zijn standaard geselecteerd, ook identieke en eerder geïmporteerde
  boekingen. Handmatig uitvinken en invoervalidatie blijven behouden. Een afschrift
  opnieuw importeren voegt de geselecteerde boekingen opnieuw toe.

## 0.1.9

- Eerste-weekchecklist: eigenaren en rekeningen → beginsaldi → afschriften importeren
  → banksaldi controleren → categoriseren → budgetten en vaste posten. Nieuwe gebruikers
  starten zonder database-import; voortgang volgt echte gegevens en expliciete saldocontroles.
- Snelle uitgave-/inkomsteninvoer met een positief bedrag en alleen de noodzakelijke velden.
  De uitgebreide editor toont tegenrekening en aflossing alleen bij het bijbehorende type.
- Telefoonvriendelijke transactielijst met datum, rekening, categorie en bedrag;
  banktekst, koppelingen en bewerken/verwerken/verwijderen zijn uitklapbaar. Desktop behoudt de tabel.
- Kant-en-klare CSV-profielen en automatische herkenning voor ING (NL), Rabobank en
  KBC/CBC, naast Argenta en het bestaande banktemplate. Profielen blijven aanpasbaar;
  importvoorbeeld, EUR- en rekeningcontrole blijven verplicht.
- Compacte wekelijkse controle voor recente imports, categorisatie-/koppelvoorstellen,
  ongecontroleerde saldi en ongekoppelde rekeningen over 7 dagen, ook over maandgrenzen.
- Expliciete beginsaldo- en banksaldocontroles worden in back-ups bewaard. Wijzigingen
  aan cashgegevens of nieuwe werkelijke boekingen heropenen de saldocontrole; geen automatische correctieboekingen.
- Bij toevoegen of opslaan van een vaste post verschijnt het aantal eenduidige
  historische matches binnen de ingestelde marges. Eén bevestiging koppelt ze
  allemaal; opslaan zonder koppelen blijft mogelijk. Dubbelzinnige matches blijven
  ter controle; bankgegevens, saldi, categorieën, bestaande koppelingen en expliciete
  transactieverwerking blijven intact. De planning en batch worden samen opgeslagen.
- Compacte taalkeuze `NL - EN` onder Beheer & import in de zijbalk, zonder keuzelijst
  of aparte bevestigingsknop. De zijbalk blijft staan tijdens het scrollen van de pagina.
- Pagina's, formulieren, bevestigingen, grafieken en bestedingsruimte gebruiken de
  gekozen interfacetaal. Bedraginvoer volgt het decimaalteken van de taal; eigen namen
  en bankteksten blijven ongewijzigd. Tests controleren taalwisselingen in beide
  richtingen, ook achter het Home Assistant-ingresspad.
- Bankboekingen verdelen over meerdere categorieën met bedragen die exact aansluiten
  op het oorspronkelijke bankbedrag. De banktekst, datum, importgegevens en saldi blijven intact.
- Expliciete retouren en vergoedingen verlagen uitgaven in de ontvangstmaand, nooit
  inkomsten. Koppel de oorspronkelijke aankoop of splitsing, of kies alleen een categorie.
- Verwachte vergoedingen en gedeelde aankopen bijhouden per betaler, met optionele
  betaaldatum, deelbetalingen en sluiten van een verwachting. Voorschotten blijven
  uitgaven totdat terugbetaling is ontvangen; verwachtingen zijn geen beschikbaar geld.
- Eén ontvangst kan meerdere aankopen vergoeden of salaris en vergoeding combineren.
  Openstaande bedragen en ontvangen betalingen zijn zichtbaar bij Transacties.
- Budgetten, maand-/jaaroverzichten en prognoses gebruiken dezelfde verwerking.
  Negatieve uitgavencategorieën blijven zichtbaar; de cirkelgrafiek licht het verschil toe.
- Afhankelijke koppelingen beschermen tegen onbedoeld wijzigen/verwijderen en import-undo.
  Categorisatieregels en transferkoppelingen overschrijven geen expliciete splitsingen.
- Databaseschema 7, met veilige migratie van schema's 1–6 en een veiligheidskopie vooraf.
  Volledige back-ups bewaren alle gegevens; CSV blijft één rij per bankboeking en
  bevat splitsingen en vergoedingsverwachtingen als JSON-kolommen.
- Tests voor splitsingen, retouren, deelbetalingen, maandgrenzen, cashveiligheid,
  eigenaarfilters, koppelingen, migratie, back-up/herstel, export en beide interfacetalen.

## 0.1.8

- Taalkeuze in de werkbalk: Nederlands (standaard) en Engels, per browser onthouden.
  Wijzigen van de taal herlaadt de pagina; sla open formulieren eerst op.
- Vertaalbasis met standaard .NET-`.resx`-bestanden, benoemde sleutels en
  `IStringLocalizer`. Navigatie, werkbalk, gedeelde knoppen, paginering en
  verbindingsmeldingen zijn vertaald. Overige paginateksten zijn nog Nederlandstalig;
  de Engelse vertaling is dus nog niet volledig.
- Maandnamen en getoonde bedragen volgen de gekozen taal; de valuta blijft EUR.
  Rekeningnamen, categorieën en bankomschrijvingen blijven ongewijzigd.
- Lokalisatietests voor beide resourcebestanden, standaardtaal, ondersteunde
  browservoorkeuren, veilige terugkeeradressen, EUR-notatie en Razor-weergave.
- Bestedingsruimte vanaf vandaag per betaalrekening, tot het volgende inkomen,
  na bekende betalingen, dagelijkse uitgavenschatting en een instelbare cashbuffer.
- Aankoopcontrole (bijvoorbeeld € 150), betalingen over 7/30 dagen, dagelijkse
  saldotabel en het laagste verwachte saldo met datum. Geen automatische dekking
  door geld op een andere rekening; kosten gaan voorzichtig vóór ontvangsten op dezelfde dag.
- Waarschuwingen bij oude/ontbrekende imports, beperkte historie, onbekende betaaldag
  en vervallen, niet-gekoppelde posten. Achterstallig inkomen wordt niet aangenomen.
- Vaste posten uitgebreid met kwartaal-, jaar- en eenmalige betalingen. Bankboekingen
  zijn aan een specifieke geplande betaling te koppelen, ook over maandgrenzen en
  met een afwijkend bedrag. Volgende schattingen en oorspronkelijke boekingen blijven intact.
- Controlevoorstellen met instelbare bedrag-/datummarges, handmatig koppelen en
  ontkoppelen. Automatische herkenning blijft strikt (± 1 cent, maximaal 5 dagen).
- Databaseschema 5 bewaart buffers, frequenties en geplande betaaldatums, met veilige
  migratie van schema's 1–4 en een veiligheidskopie vooraf. CSV-export bevat vaste-postkoppelingen.
- Tests voor betaaldagtekorten, rekeninggrenzen, dagelijkse uitgaven, importactualiteit,
  frequenties, koppelingen, maandgrenzen, migratie/back-ups en gerenderde schermen.

## 0.1.7

- Nieuwe werkruimte **Transfers koppelen**: mogelijke paren van bestaande af- en
  bijboekingen bekijken, expliciet bevestigen, afwijzen of handmatig koppelen.
- Transferkoppelingen wijzigen alleen rapportage: beide bankboekingen, bedragen,
  datums, omschrijvingen en rekeningsaldi blijven intact. Geen automatische
  tegenboekingen of koppelingen tijdens import.
- Bevestigde paren uitgesloten van inkomsten, uitgaven, categoriebestedingen en
  variabele uitgavenprognose. Netto spaar-/beleggingsbijdragen tellen één keer op
  de bankdatum van de betaalrekening, ook bij verschillende maanden.
- Koppelingen en afwijzingen zijn omkeerbaar. Financiële wijzigingen, verwijderen
  van een boeking en terugdraaien van een import verwijderen betrokken beslissingen.
- Databaseschema 4 bewaart transferbeslissingen in volledige back-ups. Home
  Assistant werkt ondersteunde eerdere schema's bij onder de schrijfvergrendeling,
  na validatie en een SQLite-bewuste veiligheidskopie. Onbekende versies worden geweigerd.
- CSV-export aangevuld met transactie-ID's en bevestigde transferkoppelingen.
- Tests voor saldobehoud, rapportage, prognoses, maandgrenzen, eigenaarfilters,
  conflicterende voorstellen, wijzigingen, import-undo en migratie/back-up/herstel.

## 0.1.6

- Excel/CSV-imports boeken uitsluitend op de gekozen rekening en behouden het
  oorspronkelijke bedrag, ook bij betalingen tussen eigen rekeningen.
- Automatische tegenrekeningkoppeling en de tegenrekeningkeuze in het importvoorbeeld
  verwijderd. Elke rekening gebruikt het eigen importbestand; boekingen op andere
  rekeningen worden niet als dubbelen gebruikt.
- Bestaande opgeslagen overboekingen blijven ongewijzigd; controleer en corrigeer
  eerder gekoppelde imports zo nodig handmatig. Importprofielen blijven compatibel.
- Regressietests toegevoegd voor rekeningonafhankelijke imports, bedragen,
  dubbelen, saldocontrole en het weigeren van tegenrekeningkoppelingen via de API.

## 0.1.5

- Blazor-JavaScript-koppeling voor maand- en jaargrafieken hersteld: geen dubbele
  objectreferenties meer, zodat grafiekupdates de verbinding niet verbreken.
- Productiepublicatie gecontroleerd met gesimuleerde Home Assistant-ingress,
  inclusief laden van scripts, navigatie en wijzigen van het jaarfilter.

## 0.1.4

- Maandoverzicht opnieuw ingericht met een compacte uitgavendoughnut en een
  vergelijking van werkelijke bedragen met de maandprognose.
- Jaaroverzicht vernieuwd met een inkomsten-en-lastentrend, een uitgavendoughnut
  en netto spaarbijdragen per maand en opgeteld sinds januari.
- Grootste vijf uitgavencategorieën zichtbaar; kleinere categorieën samengevoegd.
- Jaartabellen met exacte bedragen en budgetten uitklapbaar gemaakt.
- Chart.js 4.5.1 lokaal meegeleverd onder de MIT-licentie, zonder CDN-afhankelijkheid.
- Responsieve grafieken zonder horizontaal scrollen, met afhandeling van lege
  gegevens en veilige opruiming bij navigatie en filterwijzigingen.

## 0.1.3

- Maandoverzicht uitgebreid met uitgavengrafieken per categorie, budgetmarkeringen
  en zichtbare budgetoverschrijdingen.
- Werkelijke inkomsten, lasten en het resterende bedrag visueel vergelijken met
  de prognose voor het einde van de maand.
- Jaargrafiek toont inkomsten, lasten, netto opzijgezet en het resterende bedrag
  met negatieve bedragen onder de nullijn; toekomstige maanden worden niet getekend.
- Jaaruitgaven per categorie vergelijken met het volledige jaarbudget.
- Grafieken met lege toestanden, toegankelijke labels en horizontaal scrollen
  op kleine schermen; exacte bedragen blijven beschikbaar.
- README-bestanden vernieuwd met installatie, gebruik, grafiekuitleg en beperkingen.

## 0.1.2

- Automatische herkenning van het Argenta-exportformaat (Boekingsdatum,
  Rekening tegenpartij, Munt, Mededeling) bij importeren.

## 0.1.1

- Add-on niet meer als experimenteel gemarkeerd, zodat installatie geen
  Advanced Mode meer vereist.
- `arch` uitgebreid naar aarch64, amd64, armhf, armv7 en i386.
  Let op: Microsoft publiceert geen .NET 10 SDK/ASP.NET-images voor
  armhf/armv7 (32-bit ARM) of i386, dus de build zal op die
  architecturen mislukken totdat een alternatieve .NET-runtime of
  build-aanpak beschikbaar is.

## 0.1.0

- Eerste lokale amd64 Home Assistant-app met volledige Nederlandstalige Blazor-interface.
- Aanmelding via vertrouwde Supervisor-ingress, zonder externe netwerkpoort.
- Bestaande Huishoudboek-database importeren met validatie en veiligheidskopie.
- Permanente private opslag, instelbare tijdzone en cold backups.
- Accountonafhankelijk GitHub-repositorypakket met on-device broncodebuild,
  zonder GitHub Actions, containerregister of vooraf ingestelde repository-URL.
- Experimenteel: uiteindelijke installatie en ingress in een echte Home Assistant OS-VM
  moeten nog in de doelomgeving worden geverifieerd.
