# Huishoudboek voor Home Assistant

Een lokaal, Nederlandstalig huishoudboek in uw Home Assistant-zijbalk.
Beheer transacties, rekeningen, eigenaren, budgetten, vaste posten en hypotheek
zonder cloudverbinding of aparte Windows-backend. Alle bedragen zijn in EUR.

## Inzicht in Uw Financiën

- **Dashboard:** nieuwe startpagina met saldi van vandaag per rekening, compacte
  maandcijfers, uitgaven per categorie, budgetbewaking, komende betalingen en recente
  boekingen. Via **Rekeningen kiezen** bepaalt u welke rekeningkaarten zichtbaar zijn;
  standaard de betaalrekeningen (zichtrekeningen). De keuze blijft in deze browser
  en verandert geen rapportfilters. Het volledige maandoverzicht blijft een aparte pagina.
- **Maandoverzicht:** compacte doughnutgrafiek met de grootste uitgavencategorieën
  en percentages, plus werkelijke bedragen tegenover de maandprognose. De maandcijfers
  staan direct bovenaan, zonder startchecklist of bestedingsplanner erboven.
  Budgetten en overschrijdingen staan in de bijbehorende tabel.
- **Instellen en wekelijkse controle:** aparte pagina's; Instellen staat onder Beheer & import.
  De wekelijkse controle is per browser te verbergen en daar ook weer te herstellen.
- **Financiële vooruitblik:** maanduitleg en bestedingsruimte tot het volgende inkomen,
  komende betalingen over 7/30 dagen en dagelijkse saldi per betaalrekening. Met
  minimale cashbuffer, aankoopcontrole, laagste saldo/datum en waarschuwingen bij oude imports.
- **Vaste posten:** maand-, kwartaal-, jaar- en eenmalige inkomsten/lasten vooraf
  plannen, met vaste of wisselende bedragen. Koppelen of een vaste post maken kan
  direct vanuit een bankboeking; historische voorstellen zijn samen te controleren.
  De maandlijst onderscheidt wachten op bankgegevens van betalingen die controle vragen.
- **Jaaroverzicht:** een inkomsten-en-lastentrend, een uitgavendoughnut en een
  spaarritmegrafiek met maandbijdragen en het totaal sinds januari, geen rekeningsaldo.
  Tabellen met exacte bedragen en jaarbudgetten zijn uitklapbaar.
- **Categorietrends:** historische uitgaven per categorie met bedragstaven, een
  driemaandsgemiddelde, twee jaren over elkaar en een kalenderheatmap. Kies 6, 12 of
  24 afgesloten maanden of een eigen periode; voeg de huidige maand optioneel toe.
  Open vanuit het dashboard of klik op een categorietotaal in het maand-/jaaroverzicht.
  Het eigenaarfilter blijft gelden; de pagina heeft een eigen periodekeuze.
- Negatieve bedragen staan onder de nullijn; exacte bedragen blijven beschikbaar
  in tabellen of samenvattingen. De grafieken passen zich aan de schermbreedte aan.
  Chart.js is lokaal meegeleverd onder de MIT-licentie.
- Excel/CSV-import met automatische Argenta-herkenning, categorisatieregels,
  CSV-export en volledige databaseback-ups met herstel.
- Elke bankimport werkt alleen de gekozen rekening bij, met de bedragen uit het
  bestand. Gebruik voor elke rekening het eigen bestand; geen automatische koppelingen.
- **Sparen zonder koppelen:** categorieën tellen als gewone inkomsten/uitgaven,
  sparen/beleggen of tussen eigen rekeningen. Spaargeld staat apart van uitgaven,
  met optionele maanddoelen; boven een spaardoel is geen budgetoverschrijding.
  De betaalrekening bepaalt de spaarbijdrage, zonder verplichte tweede bankboeking.
- **Splitsingen, retouren en vergoedingen:** verdeel één bankboeking over meerdere
  categorieën en verwerk terugbetalingen als lagere uitgaven, nooit als inkomsten.
  Houd werkgeversvergoedingen en gedeelde aankopen bij, met deelbetalingen en
  openstaande bedragen. Bankboekingen en saldi blijven intact.

**Inkomsten min uitgaven** is het overschot vóór sparen. **Maandresultaat na sparen**
is inkomsten min lasten en netto opzijgezet, geen rekeningsaldo.
Gebruik **Budgetten & categorieën → Categorie instellen → Telt als** om sparen apart
te tonen. Categoriseren verandert nooit saldi. Eerder vastgelegde transfers behouden
hun oorspronkelijke werking en blijven als historie bewaard.
Prognoses zijn schattingen; werkelijke bedragen bevatten alleen boekingen tot
en met vandaag. Budget 0 betekent geen limiet. Jaarbudgetten beslaan twaalf maanden.

## Beginnen

Installeer Huishoudboek via de Home Assistant-appwinkel met deze repository:
`https://github.com/Thunderstriker/Huishoudboek-HomeAssistant`.
Controleer de tijdzone, start de app en open de webinterface via Home Assistant.
Een apart appwachtwoord of API-token is niet nodig.

Begin met een lege administratie of importeer uw bestaande Huishoudboek-database
(`.db`) via **Beheer & import**. Importeren vervangt de volledige administratie.
De actieve database blijft daarna in de permanente opslag van deze app.
Er is geen synchronisatie met een Windows-kopie.

Home Assistant bouwt de app vanuit de meegeleverde broncode. Gebruik bij voorkeur
amd64/x64; aarch64 is ook geconfigureerd. De vermelde 32-bit architecturen
armhf, armv7 en i386 werken niet met de huidige .NET 10-container-images.

## Privacy en Back-ups

Geen MQTT, cloud, telemetrie of automatische bankverbinding. Alle toegelaten
ingress-gebruikers hebben volledige financiële rechten. Database en back-ups
zijn niet door de app versleuteld.

Maak regelmatig een databaseback-up en altijd vóór een update. Werk bij via de
appwinkel; verwijder de app niet om bij te werken. CSV-export is geen volledige back-up.

Zie de [repository-README](../README.md) voor installatie en ontwikkeling,
[documentatie](DOCS.md) voor database-import en veiligheidsbeperkingen en de
[changelog](CHANGELOG.md) voor wijzigingen per versie.
