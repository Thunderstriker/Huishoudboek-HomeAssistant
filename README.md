# Huishoudboek voor Home Assistant

Een lokaal, Nederlandstalig huishoudboek in de Home Assistant-zijbalk.
Houd inkomsten, uitgaven, budgetten en spaargeld bij zonder cloudverbinding,
automatische bankkoppeling of aparte Windows-backend. Alle bedragen zijn in EUR.

Dit is een **Home Assistant-app/add-on**, geen HACS-integratie.

## Functies

- **Eerste week en wekelijkse controle:** een checklist voor rekeningen, beginsaldi,
  imports, banksaldocontrole, categorisatie en budgetten/vaste posten. Daarna een compacte
  review voor recente imports, uitzonderingen en betalingen over 7 dagen.
- **Licht mobiel gebruik:** snelle uitgave-/inkomsteninvoer en een telefoonvriendelijke
  transactielijst met uitklapbare bankgegevens en acties; desktop behoudt de tabel.
- **Maandoverzicht:** inkomsten, lasten, netto opzijgezet en het bedrag dat overblijft,
  met een compacte doughnutgrafiek per categorie en een budgettabel met overschrijdingen.
- **Maandprognose:** vergelijk werkelijke inkomsten, lasten en het resterende bedrag
  met de schatting voor het einde van de maand.
- **Verantwoord besteden:** geschatte bestedingsruimte tot het volgende inkomen,
  bekende betalingen over 7/30 dagen en dagelijkse saldi per betaalrekening,
  met het laagste saldo en zijn datum. Stel een minimale cashbuffer in en controleer
  een aankoopbedrag. Verouderde imports en ontbrekende gegevens krijgen waarschuwingen.
- **Vaste inkomsten en lasten:** plan maand-, kwartaal-, jaar- en eenmalige betalingen.
  Koppel een echte bankboeking aan één geplande betaling, ook bij afwijkende bedragen
  of een andere bankmaand. Voorstellen met instelbare herkenningsmarges zijn te bevestigen.
- **Jaaroverzicht:** inkomsten tegenover maandlasten, een uitgavendoughnut en de
  ontwikkeling van netto spaarbijdragen. Exacte bedragen en jaarbudgetten zijn uitklapbaar.
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
- **Transfers koppelen:** bevestig twee bestaande bankboekingen als één interne
  geldbeweging, zonder saldi of originele boekingen te wijzigen. Voorstellen en
  handmatig koppelen, met omkeerbare bevestigingen en afwijzingen.
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
- **Over** betekent inkomsten min lasten en netto opzijgezet. Het is geen rekeningsaldo.
- **Negatief opzijgezet** betekent dat er netto geld uit sparen of beleggen is opgenomen.
- **Budget 0** betekent geen ingestelde limiet. Jaarbudgetten beslaan alle twaalf
  maanden, ook wanneer het jaar nog niet voorbij is.
- Budgetten gelden voor het huishouden, ook wanneer u op een eigenaar filtert.
- Prognoses zijn schattingen op basis van geregistreerde boekingen, vaste posten
  en het waargenomen uitgavenpatroon. Onvolledige imports kunnen het beeld vertekenen.
- Bevestigde transferparen zijn geen inkomsten of uitgaven. Netto opzijgezet telt
  eenmaal op de bankdatum van de betaalrekening, ook als de ontvangst in een andere
  maand valt. Een eigenaarfilter neemt de hele spaarbijdrage mee als minstens één
  betrokken rekening onder het filter valt, zoals bij handmatige transfers.

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

```powershell
dotnet build huishoudboek/source/src/Huishoudboek.Service/Huishoudboek.Service.csproj
dotnet test huishoudboek/source/tests/Huishoudboek.Tests/Huishoudboek.Tests.csproj
```

Voor een nieuwe appversie moeten `version` in `huishoudboek/config.yaml` en het
`io.hass.version`-label in `huishoudboek/Dockerfile` gelijk zijn. Werk ook de changelog bij.
