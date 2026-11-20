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
