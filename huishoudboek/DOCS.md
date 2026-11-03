# Installeren en gebruiken

## Voorwaarden

- Home Assistant OS met Supervisor, architectuur **amd64** (x64 VM).
- Een bijgewerkte Core en Supervisor die bij ingress een geauthenticeerd
  `X-Remote-User-Id` meesturen. Een onbekende gebruiker krijgt bewust geen toegang;
  werk een oudere installatie bij in plaats van deze controle uit te schakelen.
- Voldoende VM-opslag voor de database én veiligheidskopieën.
- Internet tijdens installatie voor het officiële Microsoft .NET 10-runtime-image.
  Bij installatie uit een broncoderepository zijn ook de SDK en NuGet-pakketten nodig.
  Normaal gebruik en berekeningen hebben daarna geen internet nodig.
- Geen Docker Desktop, .NET-runtime of Windows-financebackend nodig op de host.
  De Windows-host en VM moeten wel aan blijven; sluimerstand onderbreekt de app.

## Installeren via uw eigen GitHub-repository

De accountnaam en repositorynaam zijn vrij. Gebruik bij voorkeur een publieke
repository en upload nooit uw database of persoonlijke configuratie.

1. Genereer vanuit de Windows-bronmap `Publish-HuishoudboekHomeAssistantRepository.ps1`.
   Dit maakt `dist/HomeAssistant/Huishoudboek-HomeAssistant-Repository.zip`.
2. Pak uit en upload de **inhoud** naar de hoofdmap van uw repository:
   `repository.yaml` en `README.md` staan direct bovenaan, naast `huishoudboek/`.
   Publiceer dus niet alleen het ZIP-bestand als GitHub Release/download.
3. Ga naar **Instellingen → Apps/Add-ons → Appwinkel → ⋮ → Repositories**
   en voeg `https://github.com/UW-ACCOUNT/UW-REPOSITORY` toe.
4. Installeer **Huishoudboek**. Home Assistant bouwt zelf de meegeleverde broncode;
   geen GitHub-token, Actions-workflow of containerregister nodig. Dit duurt langer
   en vraagt meer geheugen/opslag dan de lokale voorgebouwde distributie hieronder.
5. Controleer de tijdzone, start, toon in de zijbalk en open de webinterface.
   Alleen de bestaande Home Assistant-aanmelding is nodig: geen apart wachtwoord,
   certificaat of API-token instellen.

De database-import en permanente opslag werken daarna precies hetzelfde als bij
de lokale installatie. Bewaar na installatie dezelfde repository-URL en app-slug:
een andere repository kan door Home Assistant als een andere app worden behandeld.
Voor een update: publiceer de aangepaste broncode en een hoger versienummer in
`huishoudboek/config.yaml` (met gelijk Dockerfile-label), en werk bij via de winkel.
Verwijder de app niet om bij te werken. De volledige publicatiestappen staan in de
README van het repositorypakket.

Overstappen van een lokale installatie naar een repository-app migreert de opslag
niet automatisch. Maak eerst een databaseback-up en importeer die expliciet in de
nieuwe app; bewerk niet beide kopieën verder.

## Alternatief: lokale installatie zonder GitHub

1. Pak `Huishoudboek-HomeAssistant-amd64.zip` uit op uw computer. U krijgt de map
   `huishoudboek`, met onder andere `config.yaml`, `Dockerfile` en `app/`.
2. Kopieer die **hele map** naar de lokale apps-/add-onsmap van Home Assistant:
   `/addons/huishoudboek`. Via de Samba-app is dit doorgaans de share `addons`.
   U kunt ook de bestaande SSH-/bestandsvoorziening van uw installatie gebruiken.
   Kopieer niet naar `/config/custom_components`: dat is voor integraties.
3. Open **Instellingen → Apps** (in oudere versies **Add-ons**) → **Appwinkel**.
   Kies via het menu **Controleren op updates / Opnieuw laden** en zoek bij
   **Lokale apps/add-ons** naar **Huishoudboek**.
4. Kies **Installeren**. Home Assistant bouwt lokaal het kleine container-image
   rond de al gepubliceerde app. Er hoeft geen C#-broncode in de VM te worden gebouwd.
5. Controleer **Configuratie → Tijdzone**: standaard `Europe/Amsterdam`.
   Bijvoorbeeld `Europe/Brussels` is ook mogelijk. Deze tijdzone bepaalt 'vandaag'
   voor werkelijke saldi en prognoses. Een ongeldige tijdzone blokkeert de start.
6. Kies **Starten**, eventueel **Starten bij opstarten** en **Tonen in zijbalk**.
   Open **Webinterface openen**. Uw Home Assistant-aanmelding geeft toegang;
   een afzonderlijk websitewachtwoord is niet nodig.

Deze lokale distributie voegt geen openbare repository toe en publiceert niets
naar GitHub of een containerregister. Alleen amd64 is in deze versie opgenomen.

## Uw bestaande database importeren

1. Gebruik bij voorkeur **Back-up maken** in de bestaande Windows-app/website.
   U kunt ook uw bestaande `%LOCALAPPDATA%\Huishoudboek\Data\huishoudboek.db`
   gebruiken, maar sluit dan **zowel de desktopapp als de financiële Windows-service**
   voordat u kopieert. Kopieer nooit alleen een actieve `.db`: recente wijzigingen
   kunnen nog in SQLite-WAL-bestanden staan. De app kan ontbrekende WAL-gegevens
   niet achteraf herkennen of terughalen.
2. Open in de Home Assistant-app **Bestaande database importeren** op het
   welkomstscherm, of **Beheer & import → Bestaande database importeren / back-up herstellen**.
3. Selecteer uw `.db`-bestand (maximaal **64 MB**) en kies
   **Bestaande database importeren…**. Selecteren alleen slaat niets op.
4. Controleer de waarschuwing en bevestig expliciet met **Database importeren**.
   Annuleren wijzigt niets. Een import vervangt de volledige administratie;
   het is geen samenvoeging van twee huishoudboeken.
5. De app valideert applicatiemarker, schema (versies 1–3), SQLite-integriteit en
   financiële verwijzingen. Vervolgens bewaart hij de huidige administratie als
   veiligheidskopie en slaat hij de geïmporteerde administratie atomair op.
   Een onbekende versie of ongeldig bestand wordt geweigerd, niet gereset.
6. Controleer na afloop rekeningen, transacties en bedragen. Uw bronbestand op
   Windows blijft ongewijzigd. Bestaande versies 1–2 worden bij import ingelezen
   naar de huidige structuur zonder het bronbestand te migreren.

Alle financiële gegevens blijven behouden: transacties en eigen omschrijvingen,
rekeningen/eigenaren, budgetten en maandafwijkingen, regels, vaste posten, hypotheek,
effectenwaarderingen, importprofielen en importgeschiedenis. Geen nieuwe bankimport
nodig. Inloggegevens uit een Windows-websiteconfiguratie zijn geen financiële data
en worden niet geïmporteerd.

Daarna is de Home Assistant-administratie de **enige actieve kopie**. Bewerk de
Windows-kopie niet verder: er is geen synchronisatie. De oude kopie kan als archief
worden bewaard. Een Windows-desktopverbinding naar deze app is niet inbegrepen;
er is standaard geen externe API-poort. De losse read-only sensorintegratie is een
ander product en is niet automatisch met deze ingress-only app verbonden.

## Opslag, back-ups en updates

- Actieve administratie: `/data/administration/huishoudboek.db`, met bijbehorende
  SQLite-bestanden, lease en service-marker. Configuratie: `/data/home-assistant.json`.
  `/data` is de private, permanente app-opslag van de Supervisor, geen Windows-pad.
- Normale herstarts en updates behouden `/data`. Een bestaande ontbrekende of
  beschadigde database wordt nooit automatisch vervangen door een lege database.
- Alleen een werkelijk nieuwe installatie begint met een lege administratie en
  standaardcategorieën, zonder fictieve bedragen. U kunt ook zelf rekeningen aanmaken.
- Download regelmatig een databaseback-up via **Beheer & import → Back-up maken**.
  CSV is geen volledige back-up. Bewaar ook een kopie buiten de VM.
- Neem deze app op in Home Assistant-back-ups. De instelling **cold backup** stopt
  de app tijdens de back-up, zodat SQLite-bestanden samen consistent worden vastgelegd.
  Dit onderbreekt tijdelijk de interface. Test herstel op een aparte installatie.
- Maak vóór een update een back-up. Bij een repository-installatie: publiceer een
  hoger versienummer en werk bij via de appwinkel. Bij een lokale installatie:
  stop de app, vervang de lokale installatiebestanden,
  laad de winkel opnieuw en werk de app bij (of gebruik **Opnieuw bouwen** bij een
  handmatig vervangen pakket met hetzelfde versienummer). Wis/verwijder de app niet
  als updateprocedure: verwijderen kan de permanente app-opslag verwijderen.
- Bij een afgebroken antwoord eerst vernieuwen en controleren vóór opnieuw opslaan:
  een wijziging kan al zijn opgeslagen. Conflicterende revisies worden geweigerd.

## Beveiliging en beperkingen

- Alleen de Supervisor-ingressproxy (`172.30.32.2`) krijgt toegang; losse verbindingen
  en ontbrekende/ongeldige ingress- of gebruikersheaders worden geweigerd.
- Geen externe poort, hostnetwerk, hostmappen, Docker-socket, apparaten of
  Supervisor-/Home Assistant-API-beheerdersrechten gevraagd. Laat beschermingsmodus aan.
- Vertrouw geen handmatig ingestelde proxyheaders van andere bronnen. De normale
  Windows-servicemodus houdt zijn HTTPS-, wachtwoord- en API-tokenbeveiliging.
- Alle toegelaten ingress-gebruikers hebben dezelfde volledige financiële rechten.
  De zijbalk is standaard voor beheerders; deze zichtbaarheid is **geen aparte
  financiële rollencontrole**. Geef alleen vertrouwde personen app-/ingresstoegang.
- Home Assistant beheert de toegang tot nieuwe verzoeken. Bestaande Blazor-verbindingen
  worden niet continu bij Home Assistant opnieuw geauthenticeerd; ze verlopen uiterlijk
  na 12 uur. Sluit de app/tabbladen bij afmelden. Reeds getoonde gegevens zijn niet
  op afstand te wissen. Geen persoonlijke bewerkingsaudit of afzonderlijke rollen.
- Database, configuratie en gedownloade databaseback-ups zijn **niet door deze app
  versleuteld**. Beveilig de Windows-host, VM-schijf en back-ups. Zet geen financiële
  bestanden of proxyheaders in gedeelde logs/screenshots.
- Geldberekeningen blijven decimal, schema blijft versie 3. Saldi zijn berekend uit
  geregistreerde gegevens, geen live bankgegevens. Prognoses blijven schattingen.
- Geen publieke internet-/Cloudflare-configuratie, automatische discovery of HACS.
  Dit is de volledige app, niet een HACS-integratie.

## Probleemoplossing

- Niet zichtbaar in de winkel: controleer dat `config.yaml` rechtstreeks in
  `/addons/huishoudboek` staat, dat `app/Huishoudboek.Service.dll` is meegekopieerd
  en dat uw architectuur amd64 is. Laad daarna de winkel opnieuw.
- HTTP 403: open via Home Assistant, niet via een zelf geopende containerpoort.
  Gebruik de standaard Supervisor-netwerkroute. Handmatig proxyvertrouwen uitbreiden
  of HTTP/HTTPS-controles uitzetten is geen oplossing.
- Verbindingsmelding na herstart: open de app opnieuw vanuit Home Assistant.
- Opstart weigert bestaande data: bewaar de map en logs zonder persoonlijke data;
  herstel een geldige back-up. Verwijder geen database of service-marker om dit te omzeilen.
- Grote database: de huidige uploadgrens is 64 MB. Niet verkleinen door transacties
  uit uw echte administratie te verwijderen; deze versie ondersteunt grotere imports niet.
