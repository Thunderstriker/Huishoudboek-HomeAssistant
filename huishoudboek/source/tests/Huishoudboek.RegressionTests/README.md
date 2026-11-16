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
Tijdelijke testdatabases worden in een eigen map onder de tijdelijke gebruikersmap aangemaakt
en na iedere controle verwijderd; echte administraties worden niet geopend.
