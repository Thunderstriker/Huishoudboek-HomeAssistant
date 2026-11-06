# Huishoudboek voor Home Assistant

Een lokaal, Nederlandstalig huishoudboek in de Home Assistant-zijbalk.
Houd inkomsten, uitgaven, budgetten en spaargeld bij zonder cloudverbinding,
automatische bankkoppeling of aparte Windows-backend. Alle bedragen zijn in EUR.

Dit is een **Home Assistant-app/add-on**, geen HACS-integratie.

## Functies

- **Dashboard als startpagina:** saldi van vandaag per rekening, maandcijfers,
  uitgavencategorieën, budgetbewaking, betalingen over 7 dagen en recente boekingen.
  Kies zelf rekeningkaarten; standaard staan de betaalrekeningen (zichtrekeningen) aan.
  De keuze wordt per browser bewaard en verandert geen overzichten of financiële gegevens.
  Het volledige maandoverzicht blijft apart beschikbaar via **Maandoverzicht**.
- **Instellen en wekelijkse controle:** aparte pagina's met een checklist voor rekeningen, beginsaldi,
  imports, banksaldocontrole, categorisatie en budgetten/vaste posten. Daarna een compacte
  review voor recente imports, uitzonderingen en betalingen over 7 dagen. Instellen staat
  onder Beheer & import; de wekelijkse controle kunt u in deze browser verbergen en herstellen.
- **Licht mobiel gebruik:** snelle uitgave-/inkomsteninvoer en een telefoonvriendelijke
  transactielijst met uitklapbare bankgegevens en acties; desktop behoudt de tabel.
- **Maandoverzicht:** inkomsten, lasten, netto opzijgezet en het bedrag dat overblijft,
  direct bovenaan, met een compacte doughnutgrafiek per categorie en een budgettabel
  met overschrijdingen. Geen startchecklist of bestedingsplanner boven de cijfers.
- **Maandprognose:** vergelijk werkelijke inkomsten, lasten en het resterende bedrag
  met de schatting voor het einde van de maand.
- **Financiële vooruitblik:** maanduitleg en geschatte bestedingsruimte tot het volgende inkomen,
  bekende betalingen over 7/30 dagen en dagelijkse saldi per betaalrekening,
  met het laagste saldo en zijn datum. Stel een minimale cashbuffer in en controleer
  een aankoopbedrag. Verouderde imports en ontbrekende gegevens krijgen waarschuwingen.
- **Vaste inkomsten en lasten:** plan maand-, kwartaal-, jaar- en eenmalige betalingen.
  Herken vaste of wisselende bedragen, koppel vanuit een bankboeking en controleer
  voorstellen in één selectie. Een maandlijst onderscheidt herkende betalingen,
  controlepunten en wachten op bankgegevens; ontbrekende imports zijn geen betalingsachterstand.
- **Jaaroverzicht:** inkomsten tegenover maandlasten, een uitgavendoughnut en de
  ontwikkeling van netto spaarbijdragen. Exacte bedragen en jaarbudgetten zijn uitklapbaar.
- **Categorietrends:** bekijk historische uitgaven voor één categorie over 6, 12 of
  24 maanden of een eigen periode. Bedragstaven met een driemaandsgemiddelde,
  twee jaren over elkaar en een kalenderheatmap tonen trends en seizoenspatronen.
  Exacte bedragen blijven beschikbaar; onvolledige maanden en onbevestigde bankdekking
  zijn herkenbaar. Open de pagina vanuit het dashboard of klik op een categorietotaal.
- **Inzichten:** een financiële vrijheidskaart met essentiële maanden zonder inkomen,
  bufferdoel, bestedingsruimte, vaste verplichtingen en leefstijlbereik. Een geldvingerafdruk
  toont zes verklaarbare gedragspatronen en onderbouwde ontdekkingen met bronboekingen.
  Een compacte vrijheidskaart staat op het dashboard. Essentiële en uitzonderlijke
  categorieën, toegankelijke reserverekeningen en extra gereserveerd geld zijn instelbaar.
- **Transacties en budgetten:** categorieën, categorisatieregels, maandafwijkingen,
  vaste posten en filters per eigenaar.
- **Splitsingen, retouren en vergoedingen:** verdeel één bankboeking over categorieën,
  verwerk ontvangsten als vermindering van uitgaven en volg verwachte terugbetalingen
  voor werkgeverskosten of gedeelde aankopen. Oorspronkelijke bankboekingen blijven intact.
- **Rekeningen en vermogen:** betaal- en spaarrekeningen, beleggen, eigenaren en hypotheek.
- **Import en export:** Excel/CSV-import, automatische herkenning van Argenta-exports,
  kant-en-klare profielen voor ING (NL), Rabobank en KBC/CBC, CSV-export en volledige databaseback-ups met herstel.
- **Imports per rekening:** alleen de gekozen rekening wordt bijgewerkt, met de
  bedragen uit het bestand. Geen automatische koppeling tussen eigen rekeningen.
- **Sparen zonder transfers koppelen:** kies per categorie gewone inkomsten/uitgaven,
  sparen/beleggen of tussen eigen rekeningen. Spaarbijdragen staan apart van uitgaven
  en budgetoverschrijdingen; bestaande transfers blijven als historie behouden.
- **Bestaande administratie:** importeer een Huishoudboek-database (`.db`) of begin
  met een lege administratie.

De grafieken behouden tabellen of samenvattingen met exacte bedragen.
De grafieken gebruiken Chart.js (MIT-licentie), lokaal meegeleverd zonder CDN.
De doughnut toont de vijf grootste categorieën en groepeert de rest.
Negatieve spaarbijdragen en tekorten staan in bedragengrafieken onder de nullijn.
De grafieken passen zich aan de schermbreedte aan zonder horizontaal scrollen.
De spaarlijn toont bijdragen opgeteld sinds januari, geen saldo of beleggingsrendement.

## Installeren

1. Open in Home Assistant **Instellingen → Apps/Add-ons → Appwinkel → ⋮ → Repositories**.
2. Voeg deze repository toe:
   `https://github.com/Thunderstriker/Huishoudboek-HomeAssistant`.
3. Zoek **Huishoudboek** en kies **Installeren**.
4. Controleer de tijdzone, standaard `Europe/Amsterdam`, en kies **Starten**.
5. Open de webinterface of kies **Tonen in zijbalk**. Uw Home Assistant-aanmelding
   geeft toegang; een apart appwachtwoord of API-token is niet nodig.
6. Maak eigenaren en rekeningen aan, of kies **Bestaande database importeren**.

Home Assistant bouwt de app vanuit de broncode in deze repository. Er zijn geen
vooraf gebouwde container-images. Installatie en updates vragen daarom internet,
extra opslag en geheugen voor de .NET 10 SDK en NuGet-pakketten. Normaal gebruik
heeft daarna geen internet nodig. De Home Assistant-host moet blijven draaien.

### Architecturen

Gebruik Home Assistant OS met Supervisor, bij voorkeur op **amd64/x64**.
De configuratie vermeldt ook `aarch64`, `armhf`, `armv7` en `i386`.
Voor `aarch64` is de containerbuild ingericht, maar controleer de werking op uw
doelapparaat. De huidige Microsoft .NET 10-container-images ondersteunen geen
32-bit ARM of i386; installatie op `armhf`, `armv7` en `i386` zal daarmee mislukken.

## Overzichten Lezen

- Werkelijke bedragen bevatten alleen boekingen tot en met vandaag. De huidige
  maand is dus nog onvolledig; toekomstige maanden staan niet in de jaargrafiek.
- **Inkomsten min uitgaven** is het maandoverschot vóór sparen. **Maandresultaat na sparen**
  betekent inkomsten min lasten en netto opzijgezet. Het is geen rekeningsaldo.
- **Negatief opzijgezet** betekent dat er netto geld uit sparen of beleggen is opgenomen.
- **Budget 0** betekent geen ingestelde limiet. Jaarbudgetten beslaan alle twaalf
  maanden, ook wanneer het jaar nog niet voorbij is.
- Budgetten gelden voor het huishouden, ook wanneer u op een eigenaar filtert.
- Prognoses zijn schattingen op basis van geregistreerde boekingen, vaste posten
  en het waargenomen uitgavenpatroon. Onvolledige imports kunnen het beeld vertekenen.
- Categorietrends gebruikt een eigen periode (standaard 24 afgesloten maanden),
  onafhankelijk van de maand/het jaar in de werkbalk; het eigenaarfilter blijft gelden.
  Retouren en vergoedingen verlagen uitgaven op hun ontvangstdatum. De optionele
  huidige maand telt niet mee voor gemiddelden, de hoogste maand of de vergelijking
  van de laatste 12 afgesloten maanden met de vorige 12. Ontbrekende imports kunnen
  lijken op een daling; een lege maand is niet automatisch een bevestigde nul.
- Inzichten gebruikt de laatste 12 afgesloten maanden en de saldi van vandaag, los
  van de maandkeuze. Alleen volledige bevestigde afschriftmaanden tellen mee; minimaal
  3 voor schattingen en 6 opeenvolgende voor gedragsclaims. Markeer essentiële of
  uitzonderlijke categorieën bij Budgetten & categorieën. Uitzonderlijke uitgaven
  blijven in werkelijke cijfers en budgetten, maar niet in normale uitgavenramingen.
  Beleggingen tellen niet mee als direct beschikbare vrijheidsreserve. Het berekende
  bereik zonder inkomen is geen claim van financiële onafhankelijkheid.
- Zet bij **Budgetten & categorieën → Categorie instellen → Telt als** een categorie
  op **Sparen & beleggen**. Netto opzijgezet telt alleen op de betaalrekening, op
  de eigen bankdatum; een ontvangst daar verlaagt opzijgezet. Gebruik dezelfde
  verwerking op de spaarzijde als u die ook importeert. Geen tweede boeking nodig
  om de spaarbijdrage te tonen. **Tussen eigen rekeningen** telt nergens als inkomen,
  uitgave of sparen. Categoriseren verandert nooit saldi.
- Spaarbijdragen volgen bij een eigenaarfilter de betaalrekening; gedeelde rekeningen
  tellen eenmaal. Historische handmatige transfers en bevestigde paren behouden hun
  oorspronkelijke datum- en eigenaarverwerking, zonder herschrijven van bankboekingen.

## Gegevens en Back-ups

De administratie staat in de permanente private opslag van de Home Assistant-app.
Er is geen synchronisatie met een eventuele Windows-kopie.

Gebruik voor database-import bij voorkeur een back-up uit Huishoudboek. Sluit
anders zowel de Windows-app als de financiële Windows-service voordat u een
`.db` kopieert: een kopie van een actieve SQLite-database kan recente wijzigingen missen.
Database-import vervangt de volledige administratie en voegt geen databases samen.

Maak regelmatig een databaseback-up via **Beheer & import** en bewaar een kopie
buiten Home Assistant. CSV-export is geen volledige back-up. Databasebestanden
en back-ups worden niet door de app versleuteld; publiceer ze nooit in deze repository.

## Bijwerken

Maak eerst een back-up en werk vervolgens bij via de Home Assistant-appwinkel.
Normale updates behouden de permanente opslag. Verwijder de app niet als
updateprocedure en houd de repository-URL en app-slug gelijk.

Wijzigingen per versie staan in de [changelog](huishoudboek/CHANGELOG.md).

## Beveiliging

De webinterface is alleen bereikbaar via Home Assistant-ingress, zonder externe
app-poort. Alle toegelaten ingress-gebruikers hebben dezelfde volledige financiële
rechten. Geef daarom alleen vertrouwde personen toegang. Er is geen cloud,
telemetrie, MQTT of automatische bankverbinding.

Meer informatie over database-import, opslag en veiligheidsbeperkingen:
[documentatie](huishoudboek/DOCS.md).

## Ontwikkelen

De app gebruikt .NET 10, Blazor Interactive Server en SQLite. De broncode staat
onder `huishoudboek/source/src`, verdeeld over Core, Infrastructure, Web en Service.

### Lokaal bekijken zonder GitHub of Home Assistant

Installeer de **.NET 10 SDK** en start vanuit de repository in PowerShell:

```powershell
.\preview.ps1
```

Open **http://127.0.0.1:5099**. Er is **geen gebruikersnaam of wachtwoord** nodig.
De eerste start maakt fictieve demogegevens aan: eigenaren, rekeningen, budgetten,
vaste posten en twaalf maanden historie plus de huidige maand. De launcher
gebruikt `dotnet watch`:
ondersteunde codewijzigingen worden direct toegepast; andere wijzigingen starten
de app opnieuw. Vernieuw de browser als een wijziging niet meteen zichtbaar is.
Stop met **Ctrl+C**. De eerste start herstelt ook de benodigde NuGet-pakketten.

Instellingen en een aparte SQLite-administratie staan buiten de
repository in `%LOCALAPPDATA%\Huishoudboek\Preview\<checkout-id>`. Elke checkout
of worktree heeft zijn eigen map; dezelfde checkout behoudt zijn gegevens na een
herstart. De launcher toont het exacte pad. Ontbrekende bestaande gegevens worden
niet stilzwijgend opnieuw aangemaakt en een niet-lege map zonder previewconfiguratie
wordt niet overgenomen.

De preview luistert alleen op deze computer en opent direct de app, zonder
aanmeldscherm. Alleen de expliciete `--preview`-startoptie activeert dit; de normale
websiteaanmelding en Home Assistant-ingress blijven ongewijzigd. Verzoeken vanaf
andere computers of met een niet-lokale Host-header worden geweigerd. Gebruik
geen reverse proxy of tunnel voor deze niet-beveiligde demo. Eventueel kunt u via
**Beheer & import** een **back-upkopie** uit Home Assistant importeren. Dat vervangt alleen
de previewadministratie; er is geen synchronisatie met Home Assistant. Behandel
ook deze lokale kopie en eventuele back-ups als gevoelige financiële gegevens:
iedereen op uw computer die de preview kan openen heeft volledige toegang.
Controleer ingress, de Home Assistant-zijbalk en de containerbuild nog op Home Assistant.

Opties:

```powershell
.\preview.ps1 -Port 5100 # Andere lokale poort
.\preview.ps1 -NoWatch   # Eenmalig starten zonder bestandsbewaking
```

Als Windows het lokale script blokkeert, kunt u voor alleen deze PowerShell-sessie
`Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass` gebruiken; wijzig het
systeembeleid niet. Op andere besturingssystemen kunt u dezelfde preview expliciet
starten in een lege map buiten de repository:

```text
DOTNET_ENVIRONMENT=Development dotnet watch --non-interactive --project huishoudboek/source/src/Huishoudboek.Service -- --preview /absolute/path/to/preview 5099
```

### Bouwen en testen

```powershell
dotnet build huishoudboek/source/src/Huishoudboek.Service/Huishoudboek.Service.csproj
dotnet test huishoudboek/source/tests/Huishoudboek.Tests/Huishoudboek.Tests.csproj
```

Voor een nieuwe appversie moeten `version` in `huishoudboek/config.yaml` en het
`io.hass.version`-label in `huishoudboek/Dockerfile` gelijk zijn. Werk ook de changelog bij.
