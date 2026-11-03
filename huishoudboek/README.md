# Huishoudboek voor Home Assistant

Een lokaal, Nederlandstalig huishoudboek in uw Home Assistant-zijbalk.
Beheer transacties, rekeningen, eigenaren, budgetten, vaste posten en hypotheek
zonder cloudverbinding of aparte Windows-backend. Alle bedragen zijn in EUR.

## Inzicht in Uw Financiën

- **Maandoverzicht:** uitgaven per categorie met budgetmarkeringen, percentages
  en overschrijdingen, plus werkelijke bedragen tegenover de maandprognose.
- **Jaaroverzicht:** maandelijkse inkomsten, lasten, netto opzijgezet en het bedrag
  dat overblijft, plus categorie-uitgaven tegenover het volledige jaarbudget.
- Negatieve bedragen staan onder de nullijn; exacte bedragen blijven beschikbaar
  in tabellen of samenvattingen. Grafieken zijn op kleine schermen horizontaal verschuifbaar.
- Excel/CSV-import met automatische Argenta-herkenning, categorisatieregels,
  CSV-export en volledige databaseback-ups met herstel.

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
