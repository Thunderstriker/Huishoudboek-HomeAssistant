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
