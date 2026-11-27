# Regressiecontroles

Zonder extra testframework of NuGet-afhankelijkheden. Vanaf de repositorymap:

```powershell
dotnet run --project huishoudboek/source/tests/Huishoudboek.RegressionTests
```

Controleert bankboekingen zoeken buiten de 80 recentste resultaten, gecombineerde zoektermen,
maandnamen, datums, bedragen en behoud van de bankselectie; historische chequeontvangsten
apart opslaan vóór een aankoopbon, saldo op aankoopdatum en revisieconflicten;
decimale boninvoer tijdens het typen,
komma/punt, geldigheid en getalgrenzen,
de gedeelde maaltijdchequeregistratie, volledige bonkosten, categoriesplitsingen,
afronding, eigenaarfilters, retourkoppelingen, herhaald toepassen, verwijderen, importherstel,
CSV-export, atomaire opslag en migratie/herstel van oudere SQLite-schema's.
Transactieherkenning test wisselende AXA-omschrijvingen, onafhankelijke naam/categorie en
planning, overlappende regels, scopes, persoonlijke omschrijvingen, expliciete historische
selecties en importvoorbeeld/opslag. De automatische schema-16-upgrade wordt getest met
283 bestaande regels, 32 vaste posten en twee jaar boekingen, inclusief behoud van
bankhistorie, splitsingen, vergoedingen en bankdekking, idempotentie, veiligheidskopie,
rollback bij migratiefouten en herstel van oude/nieuwe back-ups.
Actiemenu's bij desktop-/mobiele transacties en transactieherkenning worden in beide talen
gerenderd, met unieke menukoppelingen, behoud van beschikbare acties en bonverwijzingen,
en zonder verwijderactie bij gedeelde vaste-postplanningen.
Daarnaast: gewogen effectenlots, FIFO-verkopen, kosten/TOB, historische versus huidige FX,
splitsingen, onvolledige historie, handmatige koersen, schema 15, private sleutelopslag,
blijvende gedeelde API-budgetten, providerfouten en authenticatie. Een tijdelijke lokale
webserver controleert de Nederlandse/Engelse prestatiekaarten, dashboardweergave en
gegroepeerde koersinstellingen met relevante tijdvelden voor iedere opvraagfrequentie.
Providerantwoorden zijn gesimuleerd: er is geen echte API-sleutel of externe aanvraag nodig.
Tijdelijke testdatabases worden in een eigen map onder de tijdelijke gebruikersmap aangemaakt
en na iedere controle verwijderd; echte administraties worden niet geopend.
Hypotheekcontroles vergelijken nominale en effectieve jaarrente met exacte maandrente,
behouden oude berekeningen en rente-precisie, toetsen invoer en SQLite-opslag en scheiden
het volledige theoretische schema van werkelijke aflossingen, inclusief een afwijkende
eerste bankbetaling. Beide talen van de hypotheekpagina worden lokaal gerenderd.

De financiële kalender en het lokale weekdagleren worden met synthetische aankopen
getest: aankoopdatums uit bankteksten, weekendboekingen op maandag, datumcorrecties
en bonvoorrang, complete stille weken versus ontbrekende afschriften, splitsingen
en maaltijdcheques, eenmalige acceptatie/pauze, behoud van het totale uitgavenniveau,
toekomstige bankboekingen, reserves en overgeslagen aankopen. Risicoprioriteit,
eigenaarfilters, schema-17-migratie/back-ups, revisieconflicten en Nederlandse/Engelse
kalender- en leercomponenten worden eveneens gecontroleerd. De eigen kalenderpagina
onder Overzichten wordt in beide talen getest, inclusief actieve navigatie, lege
rekeningen, eigenaarfilters en behoud van aankoopcontrole en tabellen op Financiële
vooruitblik. Persoonlijke spreadsheets zijn geen testafhankelijkheid en worden niet geïmporteerd.
Kalenderprognoses testen zichtbare geleerde bedragen en resterende dagramingen,
afboekingscorrecties, gegroepeerde totalen naast betalings-overloop, afzonderlijke
dagdetails en toegankelijke iconen/legenda in beide talen. Alleen werkelijk geraamde
bedragen verschijnen; geboekte en overgeslagen aankopen, historische dagen en
tijdelijke reserves blijven gescheiden. De extra dagraming verandert geen saldi,
gegevens of aantallen gedateerde betalingen voor risicokleuren en uitleg.

De UX-controles testen pagina-afhankelijke periodes, vaste categoriekleuren,
splitsings-/retourfilters, bankrichting per transferzijde, eigenaarbeperking,
indicatief budgettempo met bevestigde dekking, ongewijzigde prognosegegevens,
veldfouten/ARIA-koppelingen, importcontrolepunten en Nederlandse/Engelse weergave.

Bankimport test herhaalde en overlappende afschriften, voorkomen-aantallen van gelijke
betalingen, ontbrekende/afwijkende banksaldi, rekening/datum/bankrichting, handmatige
en onzekere matches, behoud van categorieën en splitsingen, expliciete dubbelbevestiging,
afschriftdekking met bestaande boekingen en intrekking na wijzigen/verwijderen/herstel
van bronboekingen, stille perioden, importherstel en revisieconflicten.

Categoriecontroles testen geneste subcategorieën, verplaatsen naar een andere ouder
of hoofdniveau, ongeldige ouders en kringverwijzingen, behoud van boekingen en
afzonderlijke budgetten, hernoemen en veilig omhoog schuiven bij verwijderen.
Nederlandse/Engelse invoer, categoriepaden, transactiezoektermen, schema-18-migratie,
veiligheidskopieën, oud/nieuw databaseherstel en revisieconflicten worden eveneens getest.

## Optionele browsercontroles

Gebruik uitsluitend een **aparte, tijdelijke lokale previewadministratie**. De
UI-tests kunnen synthetische bankboekingen en democontracten wijzigen; gebruik nooit een kopie met echte gegevens.
De scripts vereisen Node.js, Microsoft Edge en extern geïnstalleerde Playwright/axe-core.
Installeer die testhulpmiddelen buiten de repository, bijvoorbeeld:

```powershell
npm install --prefix "$env:TEMP/huishoudboek-ux-tools" playwright axe-core --no-audit --no-fund
$env:PLAYWRIGHT_MODULE = "file:///" + ("$env:TEMP/huishoudboek-ux-tools/node_modules/playwright/index.mjs" -replace '\\', '/')
$env:AXE_SCRIPT = "$env:TEMP/huishoudboek-ux-tools/node_modules/axe-core/axe.min.js"
```

Start in een ander venster de gebouwde service met een **nieuwe, lege** previewmap:

```powershell
dotnet huishoudboek/source/src/Huishoudboek.Service/bin/Debug/net10.0/Huishoudboek.Service.dll --preview "$env:TEMP/huishoudboek-ux-demo" 5199
```

Voer daarna de controles uit:

```powershell
node huishoudboek/source/tests/Huishoudboek.RegressionTests/ui-smoke.mjs
node huishoudboek/source/tests/Huishoudboek.RegressionTests/ui-import-duplicates.mjs
node huishoudboek/source/tests/Huishoudboek.RegressionTests/ui-category-hierarchy.mjs
node huishoudboek/source/tests/Huishoudboek.RegressionTests/ui-linked-contracts.mjs
node huishoudboek/source/tests/Huishoudboek.RegressionTests/ui-accessibility.mjs
```

`UI_PREVIEW_URL` kan een andere lokale poort kiezen; standaard is dit
`http://127.0.0.1:5199/`. Met `UI_SCREENSHOT_DIR` bewaart de UI-test schermafbeeldingen
in een map buiten de repository. De UI-test controleert invoer, conceptbeveiliging,
filters, eigenaarbehoud, grafieken, mobiel menu, themaopslag en begeleide import.
De routes via Te controleren, Vooruitblik en Meer en de directe importbeoordeling
met handmatige kolomkeuze voor onbekende bestanden worden ook getest.
De importcontrole toetst herhaalde en overlappende afschriften, extra gelijke betalingen,
onzekere matches, expliciete dubbelwaarschuwingen en afschriftdekking in beide talen.
De categoriecontrole maakt testcategorieën en bankboekingen aan, verplaatst geneste
indelingen naar andere ouders en hoofdniveau, toetst veilige ouderkeuzes en behoud
van categorie-id's/budgetten en verwijdert een ouder met behoud van haar kinderen.
Dit gebeurt in het Nederlands op desktop en in het Engels op mobiel.
De contractcontrole volgt de koppelingen vanuit Vaste posten en bankboekingen op
desktop en mobiel, ook nadat het gekoppelde democontract is opgezegd.
De aanvullende toegankelijkheidscontrole toetst 16 pagina's in beide thema's op
tekstcontrast, veldlabels en geldige ARIA-waarden. Alleen die diagnostische browsercontext
omzeilt CSP om axe te injecteren; de appbeveiliging en normale UI-test blijven ongewijzigd.
