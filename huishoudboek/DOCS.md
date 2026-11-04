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
5. De app valideert applicatiemarker, schema (versies 1–5), SQLite-integriteit en
   financiële verwijzingen. Vervolgens bewaart hij de huidige administratie als
   veiligheidskopie en slaat hij de geïmporteerde administratie atomair op.
   Een onbekende versie of ongeldig bestand wordt geweigerd, niet gereset.
6. Controleer na afloop rekeningen, transacties en bedragen. Uw bronbestand op
   Windows blijft ongewijzigd. Bestaande versies 1–4 worden bij import ingelezen
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

## Excel/CSV importeren per rekening

- Een bankimport boekt uitsluitend op de gekozen **Importrekening**. Bedragen
  blijven positief of negatief zoals in het bestand; er wordt geen boeking op een
  andere rekening aangemaakt en geen tegenrekening automatisch gekoppeld.
- Importeer voor zicht-/betaal-, spaar- en beleggingsrekeningen ieder het eigen
  bestand, of voer de eigen transacties handmatig in. Ook beide zijden van een
  betaling tussen eigen rekeningen worden zo afzonderlijk geregistreerd.
- Zonder bevestigde transferkoppeling zijn deze boekingen gewone inkomsten of
  uitgaven in de overzichten. Een categorie is alleen een label en koppelt geen
  rekeningen. Gebruik **Transfers koppelen** om twee bestaande bankboekingen voor
  rapportage te koppelen. Handmatig aangemaakte interne overboekingen werken wel
  beide rekeningen bij; combineer die niet met twee afzonderlijke statementboekingen.
- Dubbelen worden gecontroleerd op rekening, datum, bedrag en omschrijving.
  Boekingen op andere rekeningen houden een eigen import niet tegen.
- De update wijzigt bestaande opgeslagen overboekingen niet. Maak eerst een
  databaseback-up. Corrigeer een oude automatisch gekoppelde import via
  **Transacties → Bewerken**: kies de oorspronkelijke importrekening, het type
  **Inkomst / uitgave** en het bedrag met het teken uit het bankbestand.
  Bijvoorbeeld een ontvangst van € 27.234,13 op de zichtrekening blijft daar
  **+€ 27.234,13**, zonder spaarrekeningkoppeling. Controleer daarna beide saldi
  en eventuele dubbelen. U kunt ook de betreffende import terugdraaien en opnieuw
  importeren; daarmee verwijdert u ook eventuele handmatige wijzigingen aan die import.

## Transfers tussen eigen rekeningen koppelen

1. Importeer het eigen afschrift van iedere betrokken rekening. Open daarna
   **Transfers koppelen**, ook bereikbaar vanuit Transacties en na een import.
2. Controleer mogelijke paren: even grote tegengestelde bedragen op verschillende
   eigen rekeningen, tot en met vandaag en maximaal vijf dagen datumverschil.
   Een rekeningnummer in de opgeslagen bankomschrijving ondersteunt het voorstel;
   gelijke bedragen en nabijgelegen datums alleen bewijzen geen transfer.
   Bij meerdere kandidaten kiest de app niet automatisch.
3. Controleer rekening, originele datum, omschrijving en bedrag van beide zijden.
   Kies **Transfer bevestigen…** en bevestig de getoonde rapportagewijziging.
   Beide bankboekingen blijven apart opgeslagen en werken uitsluitend hun eigen
   rekening bij. Er wordt geen extra ontvangst, saldoaanpassing of boeking aangemaakt.
4. Bevestigde paren tellen niet als inkomen, uitgaven, categoriebestedingen of
   variabele uitgavenprognose. Betaalrekening → sparen/beleggen telt als positief
   opzijgezet; de omgekeerde richting als negatief. Tussen twee betaalrekeningen
   of twee spaar-/beleggingsrekeningen is de netto spaarbijdrage nul.
5. Netto opzijgezet telt één keer op de originele bankdatum van de **betaalrekening**,
   ook bij maand- of jaargrenzen. Beide bankdatums blijven intact. Zoals bij
   handmatige transfers telt de volledige bijdrage mee als minstens één rekening
   onder het eigenaarfilter valt. De werkruimte toont de andere zijde ter controle;
   een eigenaarfilter is geen toegangsbeperking.
6. Een onjuist voorstel kunt u als **Geen transfer** afwijzen; alleen dat specifieke
   paar wordt verborgen. Bij **Bevestigde koppelingen** of **Geen transfer** kunt u
   de beslissing weer verwijderen. Ontkoppelen wijzigt geen saldi; de boekingen
   tellen weer als gewone inkomsten/uitgaven.
7. Voor een groter datumverschil gebruikt u **Handmatig twee bestaande boekingen
   koppelen**. Zonder tweede zijde blijft de bestaande rapportage ongewijzigd:
   importeer eerst de andere rekening. Vaste-postkoppelingen, hypotheekboekingen,
   bestaande handmatige transfers en reeds bevestigde boekingen zijn uitgesloten.

Wijziging van bedrag, datum, rekening, type of vaste-postkoppeling verwijdert
betrokken transferbeslissingen. Categorieën en eigen omschrijvingen wijzigen alleen
labels. Verwijderen van een bankboeking of terugdraaien van een import verwijdert
de betrokken beslissing, niet de andere bankboeking. Controleer die overgebleven
zijde vervolgens opnieuw. Zet een gekoppelde bankboeking niet zomaar om naar het
oude handmatige transfertype: dat type werkt beide rekeningen bij en kan een al
geïmporteerde ontvangst dubbel tellen.

## Bestedingsruimte en vaste inkomsten/lasten

Op **Maandoverzicht → Hoeveel kan ik verantwoord uitgeven?** staat een schatting
vanaf vandaag, onafhankelijk van het geselecteerde maandfilter. De berekening
blijft per betaalrekening: gedeelde rekeningen tellen eenmaal, maar geld op een
andere rekening of spaarrekening dekt een rekeninggebonden betaling niet automatisch.

1. Voeg via **Vaste posten → + Vaste post** bekende inkomsten en lasten toe.
   Positief is inkomen, negatief is een uitgave. Kies maandelijks, per kwartaal,
   jaarlijks of eenmalig, een bedrag per betaling en de juiste rekening.
   Kwartaal/jaar volgen de startmaand en ingestelde dag; betalingen vóór de startdatum
   worden overgeslagen. Dag 29–31 wordt in korte maanden ingekort. Eenmalig gebruikt
   de startdatum als betaaldatum. Een planning maakt geen echte bankboeking aan.
2. Stel herkenningstekst en een passende bedrag-/datummarge in, bijvoorbeeld € 10
   voor een salaris dat enkele euro's varieert. Automatische herkenning blijft strikt
   (± 1 cent en maximaal 5 dagen, één kandidaat). Ruimere marges leveren voorstellen
   in **Bankboekingen koppelen aan een geplande betaling**; controleer en bevestig.
3. Gebruik **Handmatig koppelen** als de omschrijving of het bedrag niet past.
   Kies de bankboeking, vaste post en de **geplande** betaaldatum. Die mag in een
   andere maand vallen dan de bankdatum. Eén bankboeking vervangt één schatting,
   met het echte bedrag en de echte datum. Bankbedragen, omschrijvingen en saldi
   blijven intact; het ingestelde bedrag van volgende betalingen verandert niet.
   Extra of deelbetalingen blijven ongekoppeld. Koppelingen zijn te verwijderen.
4. Stel via **Buffer instellen** of **Rekeningen → Bewerken** een minimale
   cashbuffer per rekening in. De bestedingsruimte is het laagste verwachte saldo
   tot en met de volgende inkomensdag minus deze buffer, minimaal 0.
5. Kies de rekening van uw aankoop en voer bijvoorbeeld `150,00` in. De controle
   kijkt naar alle geprojecteerde dagen: minstens 30, of tot het volgende inkomen
   als dat later valt (zoekhorizon maximaal 366 dagen). De dagelijkse saldotabel
   toont ook het laagste saldo en zijn datum. Kosten gaan voorzichtig vóór ontvangsten
   op dezelfde dag, zodat een positief maandultimo een eerder tekort niet verbergt.

De komende betalingen over 7/30 dagen omvatten vaste-postschattingen en geregistreerde
toekomstige afboekingen, inclusief expliciete overboekingen. Extra variabele uitgaven
worden apart geschat met maximaal 90 dagen historie tot de laatste geïmporteerde
bankdatum (zonder import: vandaag). Gekoppelde vaste lasten, hypotheekbetalingen
en bevestigde transfers vallen buiten dat gemiddelde. Geplande variabele boekingen
vullen het gemiddelde per dag in, zonder dubbele laag. Het eindsaldo van de
betaalrekeningen gebruikt deze dagelijkse schatting; het maandresultaat behoudt zijn
maandgebonden daggemiddelde en is daarom niet dezelfde prognose.

De laatste vervallen, niet-gekoppelde uitgave per post wordt voor morgen gereserveerd;
oudere ontbrekende betalingen worden niet opgeteld. Koppel echte betalingen om
dubbeltelling te voorkomen. Achterstallig inkomen wordt niet als beschikbaar
aangenomen. Zonder volgende inkomensdatum wordt voor 30 dagen gereserveerd met
een waarschuwing. Oude databases behouden hun maandgebonden koppelingen.

Imports zijn na 7 dagen verouderd. Ook een recente import met alleen oude bankdatums
geeft geen zekerheid over het huidige saldo. Ontbrekende imports, beperkte historie,
een buffer van 0 en niet-gekoppelde vervallen posten krijgen waarschuwingen; een
aankoop die rekenkundig past krijgt dan geen stellige bevestiging. Onverwachte kosten,
gewijzigde facturen en niet-geregistreerde overboekingen ontbreken. Dit blijft een
schatting, geen bankverbinding of garantie. Maak regelmatig een volledige back-up.

## Opslag, back-ups en updates

- Actieve administratie: `/data/administration/huishoudboek.db`, met bijbehorende
  SQLite-bestanden, lease en service-marker. Configuratie: `/data/home-assistant.json`.
  `/data` is de private, permanente app-opslag van de Supervisor, geen Windows-pad.
- Normale herstarts en updates behouden `/data`. Een bestaande ontbrekende of
  beschadigde database wordt nooit automatisch vervangen door een lege database.
- Bij een Home Assistant-update worden ondersteunde schema's 1–4 na validatie
  onder de exclusieve schrijfvergrendeling bijgewerkt naar schema 5. Vooraf wordt
  een SQLite-bewuste `.voor-versie-5-….db`-veiligheidskopie naast de database gemaakt.
  Onbekende schema's worden geweigerd. Teruggaan naar een oude appversie vereist
  herstel van een passende oude back-up; schema-5-back-ups zijn niet leesbaar in
  oudere apps. De normale Windows-service blijft automatische upgrades weigeren.
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
- Geldberekeningen blijven decimal, schema is versie 5. Saldi zijn berekend uit
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
