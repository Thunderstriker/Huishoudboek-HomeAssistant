# Huishoudboek voor Home Assistant

Een lokaal, Nederlandstalig huishoudboek in uw Home Assistant-zijbalk.
Beheer transacties, rekeningen, eigenaren, budgetten, vaste posten en hypotheek
zonder cloudverbinding of aparte Windows-backend. Alle bedragen zijn in EUR.

## Inzicht in Uw Financiën

- **Maandoverzicht:** compacte doughnutgrafiek met de grootste uitgavencategorieën
  en percentages, plus werkelijke bedragen tegenover de maandprognose.
  Budgetten en overschrijdingen staan in de bijbehorende tabel.
- **Hoeveel kan ik verantwoord uitgeven?** Bestedingsruimte tot het volgende inkomen,
  komende betalingen over 7/30 dagen en dagelijkse saldi per betaalrekening. Met
  minimale cashbuffer, aankoopcontrole, laagste saldo/datum en waarschuwingen bij oude imports.
- **Vaste posten:** maand-, kwartaal-, jaar- en eenmalige inkomsten/lasten vooraf
  plannen. Echte bankboekingen vervangen één schatting, ook met een afwijkend bedrag
  of datum. Herkenningsmarges en handmatig koppelen voorkomen dubbele prognoses.
- **Jaaroverzicht:** een inkomsten-en-lastentrend, een uitgavendoughnut en een
  spaarritmegrafiek met maandbijdragen en het totaal sinds januari, geen rekeningsaldo.
  Tabellen met exacte bedragen en jaarbudgetten zijn uitklapbaar.
- Negatieve bedragen staan onder de nullijn; exacte bedragen blijven beschikbaar
  in tabellen of samenvattingen. De grafieken passen zich aan de schermbreedte aan.
  Chart.js is lokaal meegeleverd onder de MIT-licentie.
- Excel/CSV-import met automatische Argenta-herkenning, categorisatieregels,
  CSV-export en volledige databaseback-ups met herstel.
- Elke bankimport werkt alleen de gekozen rekening bij, met de bedragen uit het
  bestand. Gebruik voor elke rekening het eigen bestand; geen automatische koppelingen.
- **Transfers koppelen:** bekijk en bevestig twee bestaande bankboekingen als één
  interne geldbeweging. Saldi en originele boekingen blijven intact; alleen de
  rapportage verandert. Bevestigingen en afwijzingen zijn omkeerbaar.

**Over** is inkomsten min lasten en netto opzijgezet, geen rekeningsaldo.
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
