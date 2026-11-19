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

## Hypotheek: rente en aflossingsschema

Het maandbedrag is de volledige annuïteit: rente plus kapitaalaflossing. Kies bij
**Hypotheek instellen / bewerken** de renteberekening uit het kredietcontract:

- **Nominale jaarrente**: maandrente = jaarrente / 12. Bestaande hypotheken behouden
  deze berekening totdat u expliciet een andere keuze opslaat.
- **Effectieve jaarrente**: maandrente = `(1 + jaarrente / 100)^(1/12) - 1`.
  Een effectieve jaarrente van 3,34% geeft ongeveer 0,2741612771% per maand,
  niet 3,34% / 12. Gebruik hiervoor de contractrente, niet een jaarlijks kostenpercentage.
- **Exacte maandrente**: neem de periodieke maandrente met alle beschikbare decimalen
  over van de kredietgever. Een afgeronde 0,27% is niet genoeg voor een vergelijking
  tot op de cent; de app leidt geen nauwkeuriger tarief af uit een banksaldo.

Het schema toont standaard de theoretische maandbetalingen vanaf de peildatum,
met de eerste betaling eind volgende maand. Bij een peildatum in mei begint het
dus in juni, ook wanneer u de pagina pas in november opent. Dit schema verwerkt
geen geregistreerde betalingen of extra aflossingen. De aparte **Prognose vanaf
vandaag** begint bij de geregistreerde schuld en toont toekomstige betalingen.

De **werkelijke schuld** daalt uitsluitend met geregistreerde kapitaalaflossingen.
Een afwijkende eerste betaling door een gedeeltelijke maand, uitstel of dagrente
valt buiten het theoretische maandschema. Registreer het werkelijke bankbedrag en
de afzonderlijke kapitaalaflossing uit het bankoverzicht; het volledige bankbedrag
is geen kapitaalaflossing. Zonder de exacte start-/betaaldatums, renteconventie en
banksplitsing kan de app die eerste betaling niet betrouwbaar reconstrueren.

## Beleggingen en Alpha Vantage

Open **Overzichten → Beleggingen** (`/beleggingen`). De startpagina toont bij aanwezige
effectenregistraties automatisch een compacte samenvatting en maximaal drie positiekaarten.
Beide gebruiken de posities van vandaag en het eigenaarfilter, onafhankelijk van de maandkeuze.
Er zijn bewust **geen beleggingsgrafieken**.

### Historische aankopen invoeren

1. Maak bij **Rekeningen** een effecten-/beleggingsrekening met de juiste eigenaar aan.
2. Kies bij Beleggingen **Effect toevoegen**. Vul naam, beurs en handelsvaluta in,
   bijvoorbeeld EUR of USD; gebruik waar mogelijk ook de ISIN en aandelenklasse.
3. Zonder providersymbool werkt het effect met een handmatige koers en koersdatum.
   Een optionele vorige handelsafsluiting maakt het dagpercentage beschikbaar.
   Een uitdrukkelijk ingevoerde handmatige nulkoers is mogelijk voor een waardeloos
   effect; een ontbrekende of mislukte API-koers wordt daarentegen nooit nul.
4. Kies **Historische transactie toevoegen**. Registreer de uitvoeringsdatum, het
   aantal (ook fractioneel), de prijs per aandeel en eventuele kosten, TOB en valutakosten.
   Oude transacties mogen ouder zijn dan de openingsdatum van de bankadministratie.
5. Geef transacties voor hetzelfde effect op dezelfde rekening en dag een eigen
   volgordenummer. Dit bepaalt de uitvoeringsvolgorde, ook rond verkopen en splitsingen.

Alle prijs- en kostenvelden staan in de **handelsvaluta**. Zijn brokerkosten in een
andere valuta afgerekend, reken ze dan eerst met de werkelijke historische wisselkoers
om voordat u ze bij de handelsvalutakosten optelt. Voer daarnaast bij voorkeur het
**werkelijke totale EUR-afrekentotaal** in: aankoop inclusief alle kosten, verkoop netto
na kosten. Dat totaal wordt niet nogmaals met de losse kosten verhoogd of verlaagd.
Bij EUR-transacties moet het afrekentotaal aansluiten op aantal × prijs en kosten.

Ontbreekt een werkelijke EUR-afrekening voor een USD-aankoop, dan kunt u expliciet
een historische koers invoeren in **EUR per 1 USD** (bijvoorbeeld 0,92). De EUR-kosten
worden dan als geschat gemarkeerd. Zonder beide blijft het historische EUR-resultaat
onbekend; de app gebruikt nooit de huidige wisselkoers om oude kosten te verzinnen.
Voor de huidige positiewaarde wordt een afzonderlijke actuele API-wisselkoers gebruikt,
of een nieuwere handmatige wisselkoers op de ingevulde handmatige koersdatum.

### Percentages en FIFO

- **Sinds vorige handelsafsluiting** = `(laatste koers / vorige slotkoers − 1) × 100`.
  Dit is een koersbeweging, geen persoonlijk dagresultaat en niet per se sinds gisteren:
  op weekends en beursfeestdagen blijft de laatste handelsdag zichtbaar.
- **Sinds aankopen** = `(huidige waarde − resterende aankoopkosten) /
  resterende aankoopkosten × 100`. Iedere aankoop blijft een apart lot; kosten worden
  naar aantallen gewogen, nooit door de losse aankooppercentages te middelen.
- Voorbeeld zonder kosten: 10 × €100 en 5 × €120 kosten samen €1.600. Bij €110 per
  aandeel is de waarde €1.650 en het resultaat **+3,125%**, niet het gemiddelde van
  de twee afzonderlijke rendementen.
- **FIFO**: een verkoop neemt eerst aandelen en evenredige kosten uit de oudste lots.
  Verkoopkosten verlagen de gerealiseerde opbrengst. Het resterende percentage hoort
  bij de nog aanwezige lots; gerealiseerd resultaat staat apart.
- Bij USD-effecten staan het resultaat in USD en het persoonlijke EUR-resultaat apart;
  dat laatste bevat ook valuta-effecten. Gemengde EUR/USD-bedragen worden nooit gemiddeld.
- **Splitsing**: voer de verhouding nieuw/oud in, bijvoorbeeld 2 voor 2-op-1, of 0,1
  voor 1-op-10. Aantallen wijzigen, totale kosten niet. Leg de actie op elke betrokken
  rekening vast. Koersen van vóór de laatste splitsing worden niet gebruikt; het
  dagpercentage op de splitsingsdag blijft verborgen om een onjuist sprongeffect te vermijden.
- **Openingspositie / ontbrekende historie**: voor een positie zonder complete aankoop-
  of overdrachtsgegevens. Laat onbekende kosten leeg in plaats van een marktwaarde als
  aankoopprijs te gebruiken. Resultaten blijven onbekend zolang onbekende lots meetellen.

De berekeningen zijn portefeuille-informatie, geen belastingaangifte of geannualiseerd
rendement. Dividenden worden hier niet meegerekend; accumulerende ETF's krijgen geen
fictief cashdividend. Er is nog geen Bolero-bestandsimporter of bulkimport van marktprijshistorie.

### Eigen Alpha Vantage-sleutel instellen

1. Open **Koersgegevens en eigen API-sleutel** onderaan Beleggingen.
2. Voer **uw eigen** sleutel in, vink koersaanvragen aan en sla op. Deel de sleutel niet
   in chat, broncode, screenshots of foutmeldingen. De opgeslagen sleutel wordt niet
   teruggestuurd naar de browser; een leeg veld bij opslaan behoudt de bestaande sleutel.
3. Gebruik **Verbinding testen** (één aanvraag voor IBM). Dit bewijst niet dat iedere
   Europese ETF-notering door uw abonnement wordt ondersteund.
4. Bewerk een effect en zoek desgewenst met de expliciete zoekknop (één aanvraag).
   Controleer bij uw broker naam/ISIN, aandelenklasse, beurs en valuta en bevestig de koppeling.
   EODHD-symbolen uit de oude onderzoeksnotitie zijn geen Alpha Vantage-symbolen.
   Een zoekresultaat bewijst nog geen bruikbare koersdekking; gebruik **Koersen nu opvragen**.
5. Ontbreekt een notering, laat die dan handmatig gewaardeerd. De app kiest nooit zelf
   een andere aandelenklasse, beurs, token of vervangend fonds.

Na testen, zoeken of **Koersen nu opvragen** verschijnt naast de knoppen het
**Resultaat laatste API-actie**. Dit blijft bewaard bij herladen en toont afzonderlijk
de aandelenkoers (bijvoorbeeld AMZN) en wisselkoers (bijvoorbeeld USD→EUR), met oorzaak,
vervolgstap, vaste foutcode en eventuele HTTP-statuscode. HTTP 200 kan nog steeds een
providerfout bevatten, zoals ontbrekende gegevensrechten of een limiet. Het oplopen van
de aanvraagteller bewijst daarom niet dat een bruikbare koers is ontvangen.

Klap bij een resultaat **Aanvraagparameters (zonder geheimen)** open om de gebruikte
`function`, het `symbol` of valutapaar en bij quotes de `entitlement` te controleren.
Bij een basisaanvraag staat expliciet **weggelaten (basisaanvraag/slotkoers)**. Dit zijn
de parameters van die verzendpoging, niet de huidige of nog niet opgeslagen formulierkeuze.
Ze worden pas vastgelegd nadat de lokale controles een verzendpoging toelaten; ze bewijzen
zonder HTTP-resultaat niet dat een antwoord is ontvangen. Oude resultaten of niet-verstuurde
aanvragen tonen geen verzendparameters. Zoektermen en de API-sleutel worden nooit opgenomen.

Bij een providerfout staat ook een vaste **Herkenningsreden**, zonder onbewerkte providertekst.
Een losse vermelding van premium of een upgradeadvies bewijst geen ontbrekende gegevensrechten.
Concrete sleutel- of quotafouten krijgen voorrang op zulke adviezen; alleen een expliciete
premium-eindpunt-/rechtenmelding wordt `EntitlementRequired`. Een niet-herkende melding blijft
`ProviderRejected`, zonder te beweren dat een betaald abonnement nodig is. Oudere foutresultaten
worden niet achteraf opnieuw geïnterpreteerd: voer voor nieuwe diagnostiek een nieuwe actie uit.

De gegevensstatus vermeldt of gegevens in deze actie zijn opgehaald, eerder opgeslagen
gegevens zijn behouden of gegevens ontbreken, inclusief de beschikbare koersdatum.
Een mislukte afzonderlijke FX-aanvraag wegens ontbrekende gegevensrechten verhindert
niet dat een beschikbare aandelenkoers in de handelsvaluta wordt opgehaald; zonder
bruikbare FX kan de EUR-waardering wel ontbreken. Limieten, sleutel-/toegangsfouten en
verbindingsfouten stoppen verdere aanvragen; overgeslagen aanvragen worden gemarkeerd.
Na zoeken of testen geldt minimaal 30 seconden wachttijd voor een volledige update.
Wachten op het minuutbudget wordt ook als voortgang aangegeven. Voor diagnose kunt u
de foutcode en de betreffende notering/het valutapaar delen, nooit uw API-sleutel of de
installatiegegevensmap. Onbewerkte providerantwoorden en aanvraag-URL's worden niet bewaard.

Standaard: **dagelijks om 07:00 in de ingestelde lokale tijdzone**, met slotkoersen.
Andere keuzes: alleen handmatig, een aantal gelijkmatig verdeelde updates per 24 uur,
elk uur, of realtime polling met minimaal 60 seconden tussen volledige updates.
Een herstart speelt gemiste updates niet in een inhaalreeks af.

Gratis Alpha Vantage is momenteel begrensd op **25 aanvragen per dag**; de app hanteert
daarnaast conservatief maximaal 5/minuut. De teller gebruikt UTC-dagen. Quotes worden
gedeeld voor dezelfde notering/valuta en FX per valutapaar; een effect op twee rekeningen
vraagt geen twee identieke koersen. Zoeken, verbindingstests, FX, mislukte aanvragen en
handmatig vernieuwen delen hetzelfde blijvende budget. Sleutelvervanging, herstart en
financieel databaseherstel wissen de teller niet. Is het budget te klein, dan stopt de
app voordat een extra aanvraag wordt verstuurd; de raming in de instellingen toont dit.
Aanvragen met dezelfde sleutel vanuit andere apps zijn niet zichtbaar in deze lokale teller.

Bij een betaalde sleutel stelt u zelf het dag- en minuutbudget in. Dagbudget 0 is dan
onbeperkt, maar het minuutbudget blijft gelden. **Vaker opvragen maakt slotkoersen niet
realtime.** Vertraagde/realtime US-data vereist ook de juiste provider-/beursrechten;
die worden niet uit de sleutel geraden. Andere noteringen blijven slotkoersen. De kaart
toont de aangevraagde gegevenssoort, koersdatum en apart de ophaaltijd; die ophaaltijd
is geen exact tijdstip van een beurstransactie. Polling is geen tick-voor-tick stream.

Zonder internet of bij een providerfout blijven de laatste geldige koersen behouden;
onbekende koersen worden nooit nul. Datums en ontbrekende gegevens blijven zichtbaar.
Koersen/FX ouder dan vier kalenderdagen krijgen een waarschuwing (ook een beursvakantie
kan hiervan de oorzaak zijn). Een gemelde providerlimiet veroorzaakt **geen vaste
blokkering van 24 uur** meer. Bestaande blokkeringen uit oudere versies vervallen bij
de upgrade; opgeslagen koersen, de sleutel en de lokale verbruiksteller blijven behouden.

Bij een providerlimiet verschijnt naast de API-acties een **afteller met hervattijd**,
die zonder herladen iedere seconde bijwerkt en ook na herladen/herstart beschikbaar is:

- Geeft de provider een geldige HTTP-header `Retry-After` mee (aantal seconden of een
  HTTP-datum), dan verstuurt de app vóór dat tijdstip geen aanvragen. Een tussentijdse
  poging toont `ProviderRetryWait` zonder HTTP-status: de app heeft niets verstuurd.
- Zonder hervattijd toont de app een **geadviseerde wachttijd van één minuut**. Dit is
  geen bevestigde reset van een minuut- of daglimiet bij Alpha Vantage en blokkeert geen
  handmatige poging; de normale wachttijd en lokale budgetten blijven wel gelden.
- Na afloop mag u opnieuw proberen, maar alleen de provider kan bevestigen dat er weer
  budget is. Een nieuwe limietmelding behoudt de laatste geldige koersen en toont een
  nieuwe wachttijd. Er zijn geen automatische herhaalpogingen binnen dezelfde update;
  geplande updates respecteren de opgegeven of geadviseerde wachttijd.
- Het **lokale API-dagbudget** blijft een aparte bescherming. Als dat is verbruikt,
  toont een eigen afteller de reset om **00:00 UTC**, weergegeven in de lokale tijdzone.
  Dit is niet noodzakelijk het resettijdstip van de provider. Sleutelvervanging en
  herstart maken geen nieuw lokaal budget vrij.

Officiële bronnen: [limieten](https://www.alphavantage.co/support/#support),
[API-documentatie](https://www.alphavantage.co/documentation/),
[betaalde gegevensrechten](https://www.alphavantage.co/premium/).
Exacte dekking van uw noteringen moet u lokaal met de eigen sleutel controleren.

### Koersaanvragen handmatig controleren

Dezelfde basisaanvragen kunt u buiten de app testen. Vervang `YOUR_API_KEY` alleen
lokaal door uw eigen sleutel; deel of commit nooit de ingevulde URL. Iedere handmatige
aanvraag verbruikt providerbudget, maar verhoogt de lokale aanvraagteller van de app niet.

**USD naar EUR** (EUR per 1 USD):

```text
https://www.alphavantage.co/query?function=CURRENCY_EXCHANGE_RATE&from_currency=USD&to_currency=EUR&apikey=YOUR_API_KEY
```

Een bruikbaar antwoord bevat `Realtime Currency Exchange Rate`, met USD als
`1. From_Currency Code`, EUR als `3. To_Currency Code`, een positieve `5. Exchange Rate`,
een geldig tijdstip in `6. Last Refreshed` en `7. Time Zone` gelijk aan `UTC`.

**AMZN-slotkoers**:

```text
https://www.alphavantage.co/query?function=GLOBAL_QUOTE&symbol=AMZN&apikey=YOUR_API_KEY
```

Een bruikbaar antwoord bevat `Global Quote`, met `01. symbol` gelijk aan `AMZN`, een
positieve `05. price` en een geldige `07. latest trading day`. `08. previous close`
wordt gebruikt voor het verschil sinds de vorige handelsafsluiting. Een leeg
`Global Quote`-object of een veld `Information`, `Note` of `Error Message` kan ook bij
HTTP 200 op een mislukte aanvraag wijzen.

Voor een US-notering voegt de app bij vertraagde of realtime gegevens respectievelijk
`entitlement=delayed` of `entitlement=realtime` toe. Die rechten moeten apart beschikbaar
zijn. Een geslaagde basisaanvraag zonder deze parameter bewijst niet dat de varianten met
deze parameter werken. Vergelijk daarom ook de opgeslagen app-instellingen.

#### AMZN-basistest vanuit Home Assistant

Bij verschil tussen de browser en de app gebruikt u **AMZN-basistest (1 aanvraag)** onder
Koersgegevens. Wacht minstens 30 seconden na de laatste aanvraag; anders verschijnt
`DiagnosticCooldown` zonder dat budget wordt verbruikt. De test verstuurt vanaf de app-server
uitsluitend `function=GLOBAL_QUOTE` en `symbol=AMZN`, met de opgeslagen sleutel, **zonder
`entitlement` en zonder FX-aanvraag**. Dit blijft zo als vertraagde/realtimegegevens zijn
ingesteld. De test gebruikt dezelfde aanvraagcoördinatie, dag-/minuutbudgetten en eventuele
expliciete providerwachttijd als andere acties. Er zijn geen automatische herhaalpogingen.

Het resultaat toont één diagnostische quote met de aanvraagparameters, eventuele HTTP-status
en vaste herkenningsreden. Een geslaagde test bevestigt een geldig antwoord vanaf deze app-server
op dat moment, niet de oorzaak van een eerder verschil of betaalde gegevensrechten. De test
wijzigt geen opgeslagen portefeuillekoers, FX, effectenregistratie of banksaldo; gebruik daarna
zo nodig **Koersen nu opvragen**. Ook het onbewerkte testantwoord wordt niet bewaard.

**Diagnosenotitie, 18 november 2026:** de gebruiker heeft bevestigd dat zowel de losse
USD→EUR-aanvraag als de AMZN-basisaanvraag met de eigen sleutel werkt. Er zijn geen
sleutels, actuele koersen of onbewerkte antwoorden vastgelegd. Ontbrekende dekking van
deze twee basisaanvragen is daarmee op dat testmoment niet aangetoond; de oorzaak van
de ontbrekende koersweergave in de app is nog niet vastgesteld. Controleer het
**Resultaat laatste API-actie**, de ingestelde gegevensrechten, het lokale budget en de
bereikbaarheid vanuit de app-server. Een geslaagde test vanaf een ander apparaat
bewijst niet dat de app-server dezelfde netwerktoegang heeft.

De gebruiker bevestigde bij de vervolgdiagnose dat **Slotkoers** al was ingesteld,
terwijl AMZN in de app `EntitlementRequired` met HTTP 200 gaf. De oude herkenning van
iedere premiumverwijzing was te ruim, maar zonder het oorspronkelijke providerantwoord
is niet vastgesteld of juist die herkenning deze concrete melding veroorzaakte.
Versie 0.3.0.3 voegt daarom de veilige parameters, herkenningsreden en losse AMZN-basistest
toe; een werkend antwoord vanaf de persoonlijke Home Assistant-installatie moet daar
nog worden gecontroleerd. Er zijn geen persoonlijke API-aanvragen vanuit de ontwikkeling gedaan.

### Boekhouding, privacy en back-ups

Dit is voorlopig een **aparte portefeuilleschatting**: bestaande handmatige accountwaarderingen,
bankboekingen, banksaldi, budgetten en spaarbijdragen blijven ongewijzigd. De portefeuillevermelding
op het dashboard wordt niet nogmaals bij het vermogen opgeteld.

Alleen effectensymbolen, zoektermen en valutaparen gaan via HTTPS met de eigen sleutel
naar Alpha Vantage; geen aantallen, aankoopprijzen, rekeningnummers of saldi. Het gebruik
is persoonlijk. Alle tot de app toegelaten Home Assistant-gebruikers hebben dezelfde
rechten; eigenaarfilters zijn geen aparte toegangsbeveiliging of providerlicentie.

De financiële databaseback-up bevat effecten, transacties, splitsingen en handmatige
koersen (schema 15), maar **geen API-sleutel of koerscache**. Gewone bank-CSV-export is
geen volledige beleggingsback-up. Database-upgrades maken eerst een veiligheidskopie;
oudere back-ups kunnen worden hersteld met lege beleggingsregistraties.

De installatie-lokale map `administration/market-data/` bevat configuratie, quota en de
laatste quote-/FX-cache. De API-sleutel is versleuteld met ASP.NET Data Protection;
beveiligingsmateriaal staat in de private submap `keys/`, op Windows extra beschermd
met DPAPI. Op Linux beschermen private map- en bestandsrechten de lokale sleutels;
dit is geen bescherming tegen een beheerder van de host. Een **volledige Home Assistant-
back-up kan zowel het versleutelde geheim als de beveiligingssleutels bevatten**.
Beveilig die back-up en publiceer nooit de gegevensmap. Financieel databaseherstel
vervangt deze installatieconfiguratie niet; op een andere installatie voert u de sleutel
opnieuw in. **Sleutel verwijderen en koppeling uitschakelen** verwijdert het opgeslagen
geheim; eerdere quotes en de verbruiksteller blijven bestaan.

## Dashboard, maandoverzicht en aparte werkpagina's

De zijbalk is op elke pagina diepgroen en bevat zes hoofdkeuzes:

- **Dashboard**: direct naar de startpagina.
- **Overzichten**: maandoverzicht, jaaroverzicht, categorietrends, inzichten en beleggingen.
- **Transacties**: transacties, aankopen & garanties, categoriseren en wekelijkse controle (tenzij verborgen).
- **Planning**: financiële vooruitblik, budgetten, vaste posten en hypotheek.
- **Rekeningen**: direct naar de rekeningen.
- **Beheer**: beheer & import, Excel/CSV importeren en instellen.

Klik op een groep om het submenu te openen of te sluiten; er staat steeds hoogstens
één groep open. De groep van de huidige pagina opent automatisch, ook bij een directe
link of de terugknop van de browser. Op kleine schermen staan de hoofdkeuzes in twee kolommen.

De startpagina **Dashboard** (`/`) begint met een centrale momentopname van de gekozen
maand en daarnaast compacte geregistreerde rekeningsaldi van vandaag, met de actualiteit
van de bankgegevens erbij. Standaard staan de betaalrekeningen (zichtrekeningen) aan.
Er staat geen introductiekop of rij actieknoppen boven de cijfers. Importeren en
transacties invoeren blijven bereikbaar via de navigatie en de bijbehorende pagina's.
Via **Aanpassen** (Rekeningen kiezen) kunt u ook spaar- en beleggingsrekeningen tonen
of alle rekeningsaldi verbergen. De keuze wordt per browser
bewaard, niet in de financiële database; zonder browseropslag alleen voor de sessie.
Herstel de standaard via **Betaalrekeningen tonen (standaard)**. Een eigenaarfilter
verbergt rekeningen buiten die selectie zonder uw rekeningkeuze te wissen.

De maandcijfers, uitgavengrafiek en budgetbewaking volgen de gekozen maand en eigenaar,
niet de gekozen rekeningsaldi. **Binnenkort** toont bekende betalingen over 7 dagen
en het volgende inkomen voor alle betaalrekeningen binnen het eigenaarfilter, vanaf
vandaag. Onzekere reserves krijgen geen verzonnen betaaldatum en tellen niet mee in
dit 7-dagentotaal. **Recente boekingen** toont de laatste vijf boekingen tot vandaag,
ongeacht de gekozen maand. Signalen verwijzen naar import, categoriseren of vooruitblik.
Eén volgende stap krijgt direct onder de momentopname voorrang: eerst verouderde
bankgegevens, daarna een verwachte cashbufferonderschrijding, nog te categoriseren
boekingen, een negatief maandresultaat of een budgetoverschrijding. Overige punten zijn
uitklapbaar. Zonder aandachtspunten vervalt de actiebalk, behalve bij instelhulp voor
een lege administratie of rekeningselectie. Dit is geen garantie dat de financiën op
orde zijn; de vooruitblik blijft via **Binnenkort** bereikbaar. Het maandresultaat na
sparen is geen rekeningsaldo of vrij besteedbaar bedrag. Uitleg over cijfers en planning
is uitklapbaar.
De vier grootste positieve uitgavencategorieën staan in een uitgavenring en balken;
kleinere categorieën zijn samengevoegd. De ring en balken gebruiken dezelfde bedragen
en kleuren; exacte bedragen en percentages blijven leesbaar naast de ring. Zonder
positieve categoriebedragen verschijnt geen ring. Negatieve categorieën tellen niet
mee in deze verdeling, wel in het maandresultaat. Klik op een categorie bij de balken
of budgetten om de trends te openen.

Het bestaande **Maandoverzicht** blijft ongewijzigd beschikbaar via de navigatie
op `/maand`. Deze pagina begint direct met de maandcijfers, gevolgd door
grafieken, categoriebudgetten, sparen, prognoses en saldi. Langere berekeningen en
categorie-uitleg staan achter **Berekeningsdetails** of **Budgetdetails & categorie-instellingen**.
Kleine waarschuwingen bij de bedragen verwijzen naar de volledige maanduitleg op
**Financiële vooruitblik**. Daar staat ook de bestedingsplanner met aankoopcontrole,
komende betalingen, buffers en dagelijkse saldoverwachtingen.

### Losse Home Assistant-dashboardkaarten (proefversie)

Er zijn acht kaarten die onafhankelijk naast andere Home Assistant-kaarten passen:

| `widget` | Inhoud |
| --- | --- |
| `month-overview` | Maandoverzicht: inkomsten, uitgaven en maandresultaat na sparen |
| `spending` | Waar gaat mijn geld naartoe? Uitgavenring en grootste categorieën |
| `freedom-map` | Compacte financiële vrijheidskaart |
| `budget-watch` | Vier budgetten met de hoogste overschrijding of benutting |
| `upcoming` | Komende betalingen binnen zeven dagen en volgende inkomensdatums |
| `data-status` | Bankactualiteit, ongecategoriseerde boekingen en aandachtspunten |
| `category-trend` | Gekozen categorie over 6, 12 of 24 afgesloten maanden |
| `import-status` | Laatste import per betaalrekening met wekelijkse kleurindicatie |

#### Installeren

1. Gebruik Home Assistant OS met Supervisor en **Home Assistant Core 2026.11 of nieuwer**.
   Deze proefversie gebruikt de `supervisor/ingress/...`-WebSocket-opdrachten.
2. Open Huishoudboek als Home Assistant-beheerder en ga naar **Beheer & import →
   Home Assistant-dashboardkaarten**. Download `huishoudboek-card.js`.
3. Bewaar dit bestand in de Home Assistant-configuratiemap als
   `/config/www/huishoudboek-card.js`. Maak de map `www` zo nodig aan met uw bestaande
   bestandseditor of Samba. Herstart Home Assistant als u `www` voor het eerst aanmaakt.
4. Voeg bij **Instellingen → Dashboards → ⋮ → Bronnen** een bron toe:
   URL `/local/huishoudboek-card.js?v=0.2.1-cards2`, type **JavaScript-module**.
   Schakel zo nodig de geavanceerde modus in uw gebruikersprofiel in om Bronnen te zien.
   Bij YAML-beheer gebruikt u dezelfde URL met `type: module` onder uw Lovelace-bronnen.
5. Zoek de **volledige app-id** in de URL van de Huishoudboek-appbeheerpagina,
   bijvoorbeeld `abcdef12_huishoudboek`. Een lokale installatie gebruikt
   `local_huishoudboek`. Alleen `huishoudboek` is niet voldoende; de repository-id verschilt
   per repository-URL. Gebruik de id van uw eigen installatie, niet die uit dit voorbeeld.
6. Voeg een **Handmatige kaart** toe en plak bijvoorbeeld:

```yaml
type: custom:huishoudboek-card
addon: abcdef12_huishoudboek # vervangen door uw volledige app-id
widget: month-overview
```

Gebruik een van de andere `widget`-waarden uit de tabel voor een andere kaart.
Voor alleen de importcontrole:

```yaml
type: custom:huishoudboek-card
addon: abcdef12_huishoudboek
widget: import-status
```

De categorietrend vereist ook een categorie-id (te vinden bij **Beheer & import →
Home Assistant-dashboardkaarten → Categorie-id's**):

```yaml
type: custom:huishoudboek-card
addon: abcdef12_huishoudboek
widget: category-trend
category: "00000000-0000-0000-0000-000000000000" # vervangen; lege UUID = ongecategoriseerd
history_months: 12
```

Er is in deze proefversie geen visuele kaarteditor of automatische HACS-installatie.
Een app-update vervangt het gekopieerde JavaScript-bestand niet: download het opnieuw
als de kaartcode wijzigt en wijzig de `?v=` in de bron-URL om de browsercache te vernieuwen.
Registreer de bron niet rechtstreeks via een ingress-URL: die heeft zelf eerst een
ingress-sessie nodig. De lokaal geïnstalleerde kaart regelt die sessie vóór het laden.

#### Een aparte pagina Financiën

Dit voorbeeld is een volledige dashboardconfiguratie voor de ruwe configuratie-editor.
Vervang **alle** app-id's en de categorie-id. Op smallere schermen stapelen de secties.

```yaml
views:
  - title: Financiën
    path: financien
    icon: mdi:finance
    type: sections
    max_columns: 3
    sections:
      - type: grid
        cards:
          - type: custom:huishoudboek-card
            addon: abcdef12_huishoudboek
            widget: month-overview
          - type: custom:huishoudboek-card
            addon: abcdef12_huishoudboek
            widget: upcoming
          - type: custom:huishoudboek-card
            addon: abcdef12_huishoudboek
            widget: import-status
      - type: grid
        cards:
          - type: custom:huishoudboek-card
            addon: abcdef12_huishoudboek
            widget: spending
          - type: custom:huishoudboek-card
            addon: abcdef12_huishoudboek
            widget: budget-watch
          - type: custom:huishoudboek-card
            addon: abcdef12_huishoudboek
            widget: data-status
      - type: grid
        cards:
          - type: custom:huishoudboek-card
            addon: abcdef12_huishoudboek
            widget: freedom-map
          - type: custom:huishoudboek-card
            addon: abcdef12_huishoudboek
            widget: category-trend
            category: "00000000-0000-0000-0000-000000000000"
            history_months: 12
```

#### Instellingen per kaart

| Instelling | Waarden | Standaard |
| --- | --- | --- |
| `addon` | Volledige app-id, inclusief repository-id | verplicht |
| `widget` | Een van de acht waarden hierboven | verplicht |
| `period` | `current` of een aangehaalde maand, bijvoorbeeld `"2026-10"` (1900–2200) | `current` |
| `owner` | Een eigenaar-UUID uit **Beheer & import → Eigenaar-id's** | hele huishouden |
| `theme` | `auto`, `light`, `dark` | Home Assistant-thema |
| `language` | `auto`, `nl`, `en` | Home Assistant-taal; niet-Nederlands wordt Engels |
| `refresh_interval` | Geheel aantal seconden, 60–3600 | `300` |
| `category` | UUID van een gewone categorie; lege UUID = ongecategoriseerd | verplicht bij `category-trend` |
| `history_months` | `6`, `12`, `24` (getal) | `12`; alleen categorietrend |

`current` volgt de maand in de ingestelde **app-tijdzone**, niet die van de telefoon.
De vrijheidskaart, komende betalingen en importstatus volgen altijd vandaag; `period`
verandert die niet. Categorietrends eindigen bij `current` of de huidige vaste maand
in de laatste afgesloten maand; een eerdere vaste maand wordt de eindmaand.
Toekomstige trendmaanden en perioden vóór 1900 zijn ongeldig. Onbekende/verwijderde
categorieën geven een foutmelding, geen andere categorie als vervanging.
De gegevensstatus combineert actuele bankgegevens/planning met maandresultaat en
budgetten voor de gekozen maand; ongecategoriseerde boekingen tellen tot vandaag.
Een onbekende/verwijderde eigenaar geeft een foutmelding,
nooit stilzwijgend het hele huishouden. Het filter is geen beveiligingsgrens en
huishoudbudgetten worden niet naar eigenaarsaandeel verdeeld.

De kaarten vernieuwen hun gegevens periodiek en bij terugkeer uit de achtergrond;
de knop **Vernieuwen** haalt direct een nieuwe momentopname op. De huidige maand
verandert uiterlijk bij de volgende verversing. Verborgen pagina's verversen niet.
Ingestelde themawijzigingen volgen direct; de hoogte volgt ook het uitklappen van uitleg.
Links openen de volledige app in een nieuw tabblad. De app heeft daar zijn eigen
maand-/eigenaarfilter; het kaartfilter wordt niet automatisch overgenomen.

#### Importkleuren voor betaalrekeningen

De importkaart toont **per betaalrekening** de laatste niet-ongedaan-gemaakte import,
inclusief datum/tijd, ouderdom in kalenderdagen en afzonderlijk de bankgegevensdatum.
Sparen, beleggen en rekeningen die pas later openen tellen niet mee. De kop volgt
de oudste of onbekende import binnen het eigenaarfilter, zodat één nieuwe import
geen andere verouderde rekening verbergt.

| Dagen sinds import | Kleur | Betekenis |
| --- | --- | --- |
| 0–7 | Groen | Binnen het wekelijkse streefritme |
| 8–14 | Geelgroen | Wekelijkse import gewenst |
| 15–21 | Amber | Achterstallig |
| 22–30 | Roodoranje | Sterk verouderd |
| Meer dan 30 | Donkerrood | Langer dan dertig dagen geleden |
| Ontbrekende/toekomstige datum | Grijs | Onbekend; controleer importgeschiedenis |

Kleuren hebben altijd een tekstlabel. Een recent geïmporteerd oud bestand kan groen
zijn voor **importritme**, maar blijft een waarschuwing voor **oude bankgegevens**
tonen. Geen van beide bevestigt volledige afschriftdekking. Streef naar wekelijks
importeren van alle betaalrekeningen; alleen de kaart vernieuwen importeert niets.

Budgetbewaking behoudt huishoudbudgetten bij eigenaarfilters. Komende betalingen
zijn geen bewijs van achterstand; onzekere reserveringen hebben geen verzonnen
betaaldatum en tellen niet mee in het zevendagentotaal. De trend gebruikt dezelfde
netto-uitgaven en gemiddelden als Categorietrends, met negatieve bedragen onder nul,
gearceerde onbevestigde dekking en een uitklapbare tabel met exacte bedragen.

#### Toegang, actualiteit en beperkingen

- Alleen voor **vertrouwde Home Assistant-beheerders**, overeenkomstig de bestaande
  `panel_admin: true`-instelling. Een dashboard verbergen is geen toegangsbeveiliging.
  Deze kaarten maken de app niet geschikt voor gasten of een onbeveiligde wandtablet.
- De kaart maakt en verlengt een ingress-sessie namens de ingelogde gebruiker.
  Geen aparte app-token, externe poort, MQTT, cloud of Home Assistant-integratie nodig.
  De bestaande desktop-/editor-API blijft uitgeschakeld in Home Assistant-modus.
- Het zijn weergavekaarten, **geen afzonderlijke leesrechten**. Gebruikers die de
  app mogen openen behouden daar hun volledige rechten. Kaartinstellingen en filters
  worden niet in de financiële database opgeslagen.
- Elke verversing levert alleen statische HTML/CSS (inclusief SVG) op: geen scripts of Blazor-circuit per kaart,
  ruwe transactiedatabase of blijvende WebSocket naar de app. De Home Assistant-
  verbinding blijft nodig voor ingress-sessies. Er worden geen sensorentiteiten of
  extra financiële histories in Home Assistant Recorder aangemaakt.
- De kaarten gebruiken dezelfde boekhoudregels als de app: maandresultaat na sparen
  is geen rekeningsaldo of vrij besteedbaar bedrag. De uitgavenring toont positieve
  categoriebedragen; netto retouren kunnen het maandresultaat wel verlagen.
- Laden, een gestopte app, geweigerde toegang en verbindingsproblemen tonen een melding,
  geen nulbedragen. Er blijft bij een mislukte verversing geen oude financiële kaart staan.
  Bij netwerkverlies of ingetrokken rechten kan dit pas worden vastgesteld zodra de
  browser/ingress dat bij een nieuwe controle meldt; eerder getoonde gegevens zijn
  daardoor niet op afstand terug te halen.
- Bankactualiteit wordt apart van de laadtijd getoond, met de oudste bankdatum van
  de geselecteerde rekeningen (onbekend als een rekening geen datum heeft).
  Herhaald verversen haalt geen nieuwe bankgegevens op: daarvoor blijft import nodig.
  De vrijheidskaart behoudt waarschuwingen voor ontbrekende historie en voorlopige bedragen.

#### Controle na installatie

Controleer de acht kaarten op een nieuw geopend dashboard zonder eerst de app te openen,
in licht/donker thema, op telefoonbreedte en na slaapstand. Klap uitleg open en controleer
dat de hoogte meeverandert. Importeer of categoriseer in een tweede tabblad en gebruik
Vernieuwen. Controleer ook een vaste maand, een eigenaarfilter, een ongeldige eigenaar,
onvoldoende inzichtenhistorie, een app-herstart en een niet-beheerdersaccount.

#### Realistische lokale galerij

Start vanuit de repository `./preview.ps1` en open
**http://127.0.0.1:5099/kaarten/preview**, of kies de galerij bij **Beheer & import →
Home Assistant-dashboardkaarten** in de lokale preview. Bij een andere `-Port` gebruikt
u die poort. Er is geen Home Assistant-installatie of aanmelding nodig.

De galerij gebruikt de echte kaartcode voor de kaartweergave, hoogte, thema's, filters
en verversknoppen, met een lokale adapter in plaats van ingress. Alleen de omliggende
Home Assistant-kaartvlakken en dashboardindeling zijn nagebootst. Kies desktop
(maximaal 1280 px), tablet (820 px) of telefoon (390 px), licht/donker, Nederlands/Engels,
een maand, een eigenaar en een trendcategorie met 6/12/24 maanden historie.
De breedte past zich ook aan een smaller browservenster aan.
Instellingen blijven in de galerij-URL staan; de financiële database verandert niet.
Onder de kaarten staan YAML-voorbeelden. Vervang de app-id en gebruik eigenaar-id's
uit uw Home Assistant-administratie, niet zomaar die uit de demodata.

Deze galerij bestaat alleen in de expliciete loopback-preview. Alleen de afzonderlijke
kaartpagina's mogen daar in frames van dezelfde oorsprong worden getoond; de galerij en
overige apppagina's blijven tegen framing beschermd. Peer- en Host-controles blijven
actief. Publiceer of tunnel deze preview niet. De administratie kan naast fictieve
gegevens ook echte financiële gegevens bevatten die u zelf in de preview importeerde.

De galerij test geen echte Home Assistant-authenticatie, ingress-sessieverlenging of
companion-app. Controleer die op Home Assistant vóór gebruik. De volledige technische
handleiding, mogelijke vervolgkaarten en instructies voor een nieuwe werksessie staan in
[`HOME-ASSISTANT-CARDS.md`](../HOME-ASSISTANT-CARDS.md).

Dezelfde losse pagina's zijn direct in de loopback-preview te bekijken:
`/kaarten/<widget>` met elk van de acht widgetnamen, bijvoorbeeld
`/kaarten/import-status?theme=dark&language=en` of
`/kaarten/category-trend?category=00000000-0000-0000-0000-000000000000&history_months=12`.
De kaartpagina's zijn niet ingeschakeld voor
een gewone losstaande website-installatie.

### Inzichten: financiële vrijheid en geldvingerafdruk

Open **Inzichten** (`/inzichten`) via de navigatie of de link onderaan het dashboard.
De financiële vrijheidskaart staat alleen op Inzichten, niet op het dashboard.
De analyse gebruikt de saldi van vandaag en maximaal 12 afgesloten
maanden, onafhankelijk van de maandkeuze in de werkbalk. Het eigenaarfilter geldt
wel; gedeelde rekeningen tellen eenmaal en worden niet naar eigenaarsaandeel verdeeld.

1. Kies bij **Budgetten & categorieën → Categorie instellen** voor **Essentiële
   uitgaven** bij noodzakelijke kosten. Kies **Uitzonderlijk** voor uitgaven die geen
   normaal leefpatroon zijn, bijvoorbeeld een woningaankoop. Beide tegelijk kan niet.
   Deze instellingen zijn alleen voor gewone categorieën, niet sparen of eigen
   rekeningbewegingen. Er zijn geen transactieoverrides: gebruik aparte categorieën
   voor bijvoorbeeld een woningaankoop en gewone hypotheeklasten.
2. Uitzonderlijke uitgaven blijven werkelijke uitgaven in saldi, budgetten en
   maand-/jaarcijfers. Ze verhogen geen dagelijkse uitgavenschatting en komen niet
   in de gewone kostenbasis of gedragspatronen. Bekende toekomstige uitzonderlijke
   betalingen blijven wel cash vergen. Wijzigen herberekent ook eerdere analyses;
   oorspronkelijke bankgegevens veranderen nooit.
3. Kies onder **Rekeningen en gereserveerd geld** welke betaal- en spaarrekeningen
   vrij toegankelijk zijn. Beleggingen en woningwaarde zijn nooit liquide reserve.
   **Extra gereserveerd geld** wordt afgetrokken van het bereik én beschermd in de
   bestedingsprognose. Geef geen bedrag op dat al als toekomstige uitzonderlijke
   betaling geregistreerd is: die betalingen over 12 maanden worden apart gereserveerd.
   Een spaardoel reserveert niet automatisch geld. De minimale cashbuffer telt wel
   als noodreserve, maar niet als extra bestedingsruimte.
4. Het midden toont **bruikbare reserves / essentiële maandlasten**. De ringen
   tonen bescherming tegen het instelbare bufferdoel (standaard 6 maanden),
   bestedingsruimte, vastgelegd inkomen en het bereik van uw volledige gewone
   leefstijl. Elke ring heeft een eigen betekenis, geen gedeelde euroverdeling of
   totaalscore. Exacte waarden blijven zichtbaar, ook boven een volle ring.

Alleen maanden met expliciet bevestigde volledige bankdekking tellen mee voor
gemiddelden; minimaal 3 afgesloten maanden zijn nodig. Een rekeningopening midden
in de maand telt niet als volledige maand. Lege bevestigde maanden zijn echte
waarnemingen; ontbrekende imports zijn geen nul. Actieve maand-, kwartaal- en
jaarposten worden omgerekend naar maandlasten en vervangen hun expliciet gekoppelde
historische boekingen. Gesplitste vaste posten behouden hun waargenomen
categorieverhouding bij het omrekenen naar maandbedragen.
Koppel ontbrekende herkenningen bij Vaste posten om overlap met ongekoppelde
uitgaven te voorkomen. Minder dan 12 maanden kan seizoenen en jaarlasten missen.

**Ziet u ‘Nog niet berekend’ of ‘Afschriftdekking niet bevestigd’?** Inzichten toont
onder **Bekijk ontbrekende rekeningen en perioden** per maand welke rekening en
welke datums nog niet bevestigd zijn. Alle toen bestaande betaal- én spaarrekeningen
binnen het eigenaarfilter moeten dezelfde volledige maanden dekken. Een bevestiging
voor uw betaalrekening bevestigt niet automatisch uw spaarrekening.

Open **Rekeningen → Bankperioden bevestigen · ook zonder import → Periode bevestigen**
bij de betreffende rekening. Kies de volledige periode uit uw bankomgeving of
afschrift, controleer dat alle rente, opnames en overboekingen zijn vastgelegd en
vink dit expliciet aan. Ook een spaarrekening zonder activiteit kunt u zo bevestigen.
Eén periode mag meerdere maanden of jaren omvatten: per maand bevestigen is niet nodig.
Er wordt geen import gemaakt en geen boeking of saldo gewijzigd. Bevestigde imports
en rekeningperioden tellen samen; gaten ertussen blijven onbevestigd. Inzichten
wordt na opslaan herberekend. Zijn minstens drie volledige maanden bevestigd, maar
ontbreekt het bufferbereik nog? Markeer noodzakelijke uitgavencategorieën als
**Essentiële uitgaven (nodig zonder inkomen)**.

Rekeningperioden kunnen worden gewijzigd of verwijderd. Wijzigt u bankgegevens
binnen de periode of de openingsgegevens van de rekening, dan verschijnt
**Bankgegevens gewijzigd · opnieuw bevestigen**. Alleen categoriseren, een eigen
omschrijving of vaste-postkoppeling verandert deze bevestiging niet. Een periode
bevestigen is geen saldocontrole; die blijft afzonderlijk onder **Rekeningen** beschikbaar.

De **geldvingerafdruk** toont uitgavenregelmaat, uitgavenpieken, spaarritme, vaste
verplichtingen, uitgavenconcentratie en financiële ruimte. Kies een band voor de
definitie en exacte waarde. Een grotere waarde is niet automatisch beter; intenties
zoals impulsiviteit worden niet geraden. Ontdekkingen vereisen 6 opeenvolgende
bevestigde maanden en minstens 20 verschillende aankopen. Een vergelijking van de
laatste vijf dagen gebruikt daggemiddelden. Onderliggende boekingen en maandbedragen
zijn uitklapbaar; bij splitsingen toont bewijs alleen het geanalyseerde uitgavendeel.

Reservegroei sinds vorige maandultimo gebruikt dezelfde huidige essentiële
kostenbasis. Dit is geen opgeslagen historische score. Onbevestigde actuele saldi
verbergen de vergelijking. Onbekende essentiële kosten of inkomen leveren een
uitleg op in plaats van een berekend bereik. Financieel bereik zonder inkomen is geen
financiële onafhankelijkheid; duurzaam beleggingsinkomen wordt niet verondersteld.

Alle berekeningen en SVG-visualisaties werken lokaal, zonder externe diensten.
Instellingen en rekeningperiodebevestigingen blijven behouden in databaseback-ups. Databaseschema 11 maakt vóór
migratie een veiligheidskopie; bestaande categorieën worden niet automatisch
essentieel of uitzonderlijk gemarkeerd.

### Historische uitgaven per categorie

Open **Categorietrends** (`/categorietrends`) vanuit het dashboard of de navigatie.
Klikken op een categorienaam in de maand-/jaartabel opent direct die categorie.
Standaard ziet u de grootste uitgavencategorie over de recente historie en 24
afgesloten maanden. Kies 6, 12 of 24 maanden of een eigen begin- en eindmaand
(maximaal 120 maanden). Deze periode staat los van de maand/het jaar in de werkbalk;
het eigenaarfilter blijft gelden en gedeelde rekeningen tellen eenmaal.

- De bedragstaven tonen netto uitgaven; de lijn toont het gemiddelde van de huidige
  en twee voorafgaande maanden binnen de gekozen periode. Daarom begint de lijn
  pas bij de derde afgesloten maand. Negatieve bedragen blijven onder de nullijn.
- De vergelijking legt de laatste 12 afgesloten maanden over de vorige 12.
  Bij minder dan 24 afgesloten maanden wordt geen vergelijking getoond. Bij een
  nul- of negatieve uitgangswaarde staat een bedragverschil in plaats van een percentage.
- De kalenderheatmap toont uitgaven in groen en netto retouren in paars; gestreepte
  vakjes hebben geen uitgavenboekingen. Beweeg, tik of gebruik het toetsenbord voor
  details. Exacte bedragen staan ook in de uitklapbare tabellen.
- **Huidige maand toevoegen** voegt de lopende maand toe aan de gekozen afgesloten
  maanden. Die bevat alleen boekingen tot vandaag en telt niet mee voor gemiddelden,
  de hoogste maand of de jaarvergelijking. Een stippelrand maakt dit zichtbaar.
- Splitsingen, retouren en ontvangen vergoedingen volgen de bestaande boekhoudregels.
  Een terugbetaling verlaagt de uitgaven op de ontvangstdatum. Gewone inkomsten,
  sparen en eigen rekeningbewegingen tellen niet mee. Categorieën wijzigen verandert
  ook deze historie, nooit de bankbedragen of saldi.
- Afschriftdekking is alleen bevestigd als voor alle in-scope rekeningen expliciet
  volledige afschriften beschikbaar zijn, rekening houdend met de openingsdatum.
  Gaten en ongedaan gemaakte imports blijven onbevestigd. Ontbrekende gegevens kunnen
  lijken op een daling; een maand zonder boekingen is geen bewijs dat er niets is uitgegeven.

Alle grafieken werken lokaal, passen zich aan de schermbreedte aan en respecteren
de browservoorkeur voor minder animatie.

**Instellen** staat rechtstreeks onder **Beheer → Instellen** en blijft ook bereikbaar
via **Beheer & import → Instellen & navigatie**. **Wekelijkse controle** staat onder
**Transacties**. Kies op die pagina of onder
**Beheer & import → Instellen & navigatie** voor **Wekelijkse controle verbergen in de
navigatie** om de menulink te verbergen; vink dit daar weer uit om hem te herstellen.
De voorkeur wordt alleen in deze browser voor deze app bewaard, niet in de financiële
database of back-ups. Bij geblokkeerde browseropslag geldt hij alleen voor deze sessie.
De pagina blijft via Beheer & import bereikbaar en financiële controles veranderen niet.

## Nieuw beginnen: uw eerste week

Een bestaande database is niet nodig. Kies op het lege dashboard **Aan de slag**,
of open **Beheer & import → Instellen**, en volg **Uw eerste week**:

1. Maak bij **Rekeningen** eerst een eigenaar aan en daarna uw rekeningen met IBAN.
2. Kies een openingsdatum en het saldo **vóór** de eerste boeking op die datum.
   Importeer geen oudere boekingen zonder eerst de openingsdatum en het saldo aan te passen.
   Vergelijk met uw bank en kies **Beginsaldo bevestigen**; ook € 0 kan correct zijn.
3. Importeer afschriften per rekening en controleer het voorbeeld vóór opslaan.
4. Open **Rekeningen → Beginsaldi en saldocontrole**. Voer de controledatum en het
   banksaldo aan het einde van die dag zelf in. Alleen gelijke saldi worden bevestigd;
   een verschil moet worden onderzocht, niet met een automatische correctie weggeboekt.
5. Categoriseer boekingen en splitsingen; sla herkenningsregels op voor volgende imports.
6. Stel minstens één budget en een vaste post in (bijvoorbeeld salaris of huur).

De checklist geldt voor het hele huishouden, ook als een eigenaarfilter actief is.
Hij blijft uitklapbaar op de pagina Instellen. Afgeronde stappen worden berekend uit de
gegevens; alle rekeningen moeten een import en saldocontrole hebben voor die stappen
klaar zijn. Een administratie die alleen handmatige boekingen gebruikt kan de importstap
open laten. Beginsaldobevestigingen en saldocontroles blijven in volledige back-ups bewaard.
Een gewijzigd beginsaldo heropent de bevestiging. Cashwijzigingen en nieuwe werkelijke
boekingen na de controledatum heropenen de saldocontrole; alleen categorisatie doet dat niet.

### Licht dagelijks gebruik en wekelijkse controle

Bij **Transacties** bieden **Uitgave toevoegen** en **Inkomst toevoegen** een kort formulier.
Voer een positief bedrag in; de knop bepaalt het teken. **Uitgebreide boeking** blijft
beschikbaar voor transfers, hypotheekbetalingen en vaste-postkoppelingen. De editor
verbergt tegenrekening/aflossing wanneer die niet van toepassing zijn. Importeert u later
dezelfde handmatige boeking, dan wordt deze opnieuw toegevoegd tenzij u de regel
handmatig uitvinkt in het importvoorbeeld.

Op schermen tot 700 px vervangt een compacte lijst de transactietabel. Tik op een boeking
voor de oorspronkelijke banktekst, eventuele hypotheek-/transfer-/vaste-postgegevens,
banksaldo en acties. Filters, categorieën, paginering en financiële verwerking blijven gelijk.

Open wekelijks de pagina **Wekelijkse controle**: importeer, bekijk categorieën,
vaste-postvoorstellen en saldi, en controleer betalingen van vandaag tot over 7 dagen.
De controle volgt het eigenaarfilter maar niet de gekozen maand. Recente bankboekingen
of expliciet bevestigde volledige afschriftperioden tellen voor de importactualiteit;
een oude export opnieuw uploaden maakt een rekening dus niet actueel. Ook rustige
rekeningen kunnen via een volledig afschrift hun actualiteit bevestigen.
De lijst met rekeningen omvat alleen ongekoppelde vaste uitgaven;
zonder ingestelde planning is dit geen bevestiging dat er niets te betalen is.

## Uw bestaande database importeren

1. Gebruik bij voorkeur **Back-up maken** in de bestaande Windows-app/website.
   U kunt ook uw bestaande `%LOCALAPPDATA%\Huishoudboek\Data\huishoudboek.db`
   gebruiken, maar sluit dan **zowel de desktopapp als de financiële Windows-service**
   voordat u kopieert. Kopieer nooit alleen een actieve `.db`: recente wijzigingen
   kunnen nog in SQLite-WAL-bestanden staan. De app kan ontbrekende WAL-gegevens
   niet achteraf herkennen of terughalen.
2. Open in de Home Assistant-app **Instellen → Bestaande database importeren**,
   of **Beheer & import → Bestaande database importeren / back-up herstellen**.
3. Selecteer uw `.db`-bestand (maximaal **256 MB**) en kies
   **Bestaande database importeren…**. Selecteren alleen slaat niets op.
4. Controleer de waarschuwing en bevestig expliciet met **Database importeren**.
   Annuleren wijzigt niets. Een import vervangt de volledige administratie;
   het is geen samenvoeging van twee huishoudboeken.
5. De app valideert applicatiemarker, schema (versies 1–11), SQLite-integriteit en
   financiële verwijzingen. Vervolgens bewaart hij de huidige administratie als
   veiligheidskopie en slaat hij de geïmporteerde administratie atomair op.
   Een onbekende versie of ongeldig bestand wordt geweigerd, niet gereset.
6. Controleer na afloop rekeningen, transacties en bedragen. Uw bronbestand op
   Windows blijft ongewijzigd. Bestaande versies 1–10 worden bij import ingelezen
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

- ING (NL CSV), Rabobank (NL CSV) en KBC/CBC (BE CSV) zijn beschikbaar als kant-en-klare
  profielen en worden bij bekende kolomkoppen automatisch herkend. Argenta en het
  bestaande banktemplate blijven ondersteund. Profielen zijn voor exports **met kolomkoppen**;
  exportvarianten kunnen verschillen. Controleer mappings en betaalrichting in het voorbeeld.
  ING gebruikt `yyyyMMdd` en Af/Bij; Rabobank en KBC gebruiken een bedrag met teken.
  Alleen EUR wordt ondersteund. Bij een rekeningkolom moet het IBAN met de gekozen rekening overeenkomen.
- Een bankimport boekt uitsluitend op de gekozen **Importrekening**. Bedragen
  blijven positief of negatief zoals in het bestand; er wordt geen boeking op een
  andere rekening aangemaakt en geen tegenrekening automatisch gekoppeld.
- Importeer voor zicht-/betaal-, spaar- en beleggingsrekeningen ieder het eigen
  bestand, of voer de eigen transacties handmatig in. Ook beide zijden van een
  betaling tussen eigen rekeningen worden zo afzonderlijk geregistreerd.
- De categorie bepaalt de verwerking: gewone inkomsten/uitgaven, sparen/beleggen
  of tussen eigen rekeningen. Categoriseren verandert alleen rapporten, nooit saldi.
  Gebruik dezelfde verwerking voor beide zijden als u beide rekeningen importeert.
  Twee bankboekingen koppelen is niet meer nodig. Ongecategoriseerde boekingen blijven
  gewone inkomsten/uitgaven totdat u ze categoriseert.
- Alle geldige regels binnen de gekozen periode zijn standaard geselecteerd, ook
  identieke betalingen en regels die overeenkomen met bestaande boekingen. Er is
  geen automatische dubbelcontrole. Hetzelfde afschrift opnieuw importeren voegt
  alle geselecteerde boekingen opnieuw toe. Handmatig uitvinken blijft mogelijk;
  ongeldige regels kunnen niet worden opgeslagen.
- Bestaande opgeslagen transfers worden niet herschreven. Het oude handmatige type
  kan nog beide saldi beïnvloeden; importeer diezelfde beweging niet nogmaals zonder
  eerst op dubbelen te controleren. De financiële velden zijn in de editor beschermd.
- Bevestig optioneel in het voorbeeld dat het afschrift **alle bankboekingen** voor
  de gekozen rekening en periode bevat. Dit kan alleen met alle regels geselecteerd
  en geldig. Dekking loopt nooit voorbij vandaag; een leeg afschrift kan een periode
  zonder activiteit bevestigen. Een gefilterde of gedeeltelijke export bevestigt u niet.
- Bij oudere imports is de volledige periode onbekend. Controleer uw bankafschrift
  en gebruik **Beheer & import → Importgeschiedenis → Afschriftperiode bevestigen**.
  Importeer geen bestand opnieuw alleen om de periode vast te leggen: dat maakt dubbelen.
  Zonder import kunt u dezelfde volledigheid per rekening bevestigen onder
  **Rekeningen → Bankperioden bevestigen · ook zonder import**.
  Aaneengesloten bevestigde perioden tellen samen; gaten en teruggedraaide imports niet.
  Wijzigen of verwijderen van bankgegevens trekt de periodebevestiging van die import in;
  categoriseren en vaste-postkoppelingen veranderen de bevestiging niet.

## Sparen en verplaatsen tussen eigen rekeningen

1. Open **Budgetten & categorieën → Categorie instellen → Telt als**. Kies:
   - **Gewone inkomsten & uitgaven:** salaris, rente, boodschappen, enzovoort.
   - **Sparen & beleggen:** bijdragen apart opzijgezet, niet als uitgaven.
   - **Tussen eigen rekeningen:** geen inkomsten, uitgaven of spaarbijdrage.
2. Categoriseer bijvoorbeeld een afboeking van € 500 op uw betaalrekening als sparen.
   De maand toont € 500 opzijgezet; u hoeft de spaarrekening niet te importeren of
   een tegenboeking te zoeken. Het saldo van die andere rekening wordt niet verzonnen:
   importeer het eigen afschrift als u ook dat saldo wilt bijhouden.
3. Als u beide zijden importeert, geef beide dezelfde verwerking. De ontvangst op
   sparen/beleggen telt niet nogmaals. Een terugontvangst van € 100 op de betaalrekening
   verlaagt netto opzijgezet naar € 400; dit is geen inkomen. Echte rente of kosten op
   een spaarrekening tellen wel als gewone inkomsten/uitgaven met een gewone categorie.
4. Verplaatsingen tussen betaalrekeningen of tussen spaar-/beleggingsrekeningen krijgen
   **Tussen eigen rekeningen**. Regels kunnen deze categorieën net als andere categorieën
   toewijzen. Er wordt niet geraden op basis van alleen gelijke bedragen of nabije datums.
5. Nieuwe handmatige boekingen werken uitsluitend de gekozen rekening bij. Voer bij
   handmatig bijhouden van twee rekeningen beide bankbewegingen apart in.

Netto opzijgezet wordt gemeten op de eigen bankdatum van de **betaalrekening**, ook
over maand- of jaargrenzen. Het eigenaarfilter volgt die rekening; een gedeelde
rekening telt eenmaal. Sparen is een bijdrage, geen vermogensgroei of rendement.
Geld dat u op een betaalrekening laat staan is maandoverschot, niet automatisch opzijgezet.

Het maandoverzicht toont inkomsten, uitgaven, inkomsten min uitgaven, apart opzijgezet
en het maandresultaat na sparen. Bij € 3.000 inkomsten, € 2.600 uitgaven en € 600 sparen
staat er € 400 overschot vóór sparen en −€ 200 na sparen. Dat is geen € 200 extra uitgave.
Uitgaven boven inkomen, overschreden categoriebudgetten en sparen boven het overschot
worden afzonderlijk uitgelegd op **Financiële vooruitblik**, met compacte verwijzingen
bij de maandcijfers. De cashbuffercontrole blijft altijd per betaalrekening.

Spaarcategorieën hebben optionele maanddoelen in plaats van uitgavenlimieten. Boven
het spaardoel is **Doel bereikt**, geen rode overschrijding. Vaste spaarbijdragen en
toekomstige bankboekingen tellen in de prognose als sparen; ze reserveren wel cash en
tellen niet in het gemiddelde voor dagelijkse uitgaven of als volgende inkomensdag.
Ook splitsingen gebruiken de verwerking van iedere deelcategorie.

Wijzigen van **Telt als** of verwijderen van een categorie verandert ook historische
rapporten, niet bankbedragen of saldi. Namen zoals ‘Sparen’ hebben geen verborgen werking.
Bestaande categorienamen worden bij de update daarom niet automatisch anders verwerkt.
Categorieën met hypotheekbetalingen of retour-/vergoedingskoppelingen kunnen niet zomaar
naar sparen/eigen rekeningbeweging worden omgezet; die horen bij gewone uitgaven.

### Bestaande transferhistorie

De oude koppelwerkruimte, voorstellen, afwijzingen en verplichte controles na import
zijn uit het dagelijkse gebruik verwijderd. De oude URL toont alleen uitleg en
bevestigde historie; bereikbaar via de categoriepagina als er oude transfers zijn.
De update bewaart oude handmatige transfers en transferbeslissingen ongewijzigd om
saldi en historische rapporten te beschermen. Hun categorielabel verandert de oude
verwerking niet. De oorspronkelijke datum- en eigenaarregels blijven gelden: de
spaarbijdrage telt volledig wanneer minstens één betrokken rekening in het filter valt.

Bankbedrag, datum, rekeningen en koppelingen van deze oude transfers zijn in de editor
vergrendeld. Omschrijvingen en labels blijven bewerkbaar. Verwijderen of import-undo
kan nog wel de oude saldo-/rapportagewerking verwijderen; de bevestiging waarschuwt
daarvoor. Bij een gekoppeld paar blijft de andere bankboeking bestaan: controleer en
categoriseer die overgebleven zijde. Maak altijd eerst een volledige back-up.

## Acties bij transacties en transactieherkenning

Open de **⋯**-knop rechts op een rij voor de beschikbare acties, zoals **Bewerken**,
**Herkennen als…** of **Verwijderen**. Bij mobiele transacties klapt u eerst de boeking open.
Het menu sluit na een keuze, bij buiten het menu klikken of met Escape; verwijderen
vraagt nog steeds om bevestiging. Gebruik het toetsenbord met Enter of spatie om te
openen, de pijltjestoetsen om een actie te kiezen en Enter om deze uit te voeren.

## Transactieherkenning: één instelling, optioneel met planning

Open **Transacties → Herkennen als…** of **Transactieherkenning** in de navigatie.
De oude automatische categorisatie en vaste-postherkenning staan voortaan in één
beheerlijst en gebruiken dezelfde instelling; u hoeft de banktekst niet opnieuw in te voeren.

1. Vul bij **Omschrijving bevat** een vast, onderscheidend stuk van de banktekst in.
   Bijvoorbeeld `01051832244` voor een polis, niet de volledige bankomschrijving met
   iedere maand een andere datum. De oorspronkelijke banktekst staat bij het formulier;
   bij een nieuwe herkenning vanuit een boeking kiest u dit tekstfragment expliciet.
2. Kies optioneel **Tonen als**, bijvoorbeeld ‘AXA – autoverzekering’, en een categorie.
   Alleen een naam of alleen een categorie mag ook. Een lege categorie betekent geen
   automatische categorietoewijzing, niet dat bestaande categorieën worden gewist.
   Beperk desgewenst op rekening en betaalrichting. Er is geen AI, fuzzy matching of
   automatische afleiding van veranderende nummers.
3. Kies optioneel **Vaste-postplanning toevoegen**. Naam, categorie en herkenning
   worden gedeeld met die planning. Bestaande vaste posten hebben al een eigen gedeelde
   instelling: kies deze bij **Nieuwe of bestaande herkenning** om haar opnieuw te gebruiken.
   Wisselen vervangt de niet-opgeslagen formulierinvoer; het voegt geen bestaande regels samen.
4. Kies **Voorbeeld bekijken**. Controleer de passende bankteksten, overlappende namen,
   voorrang van eerdere categorieregels en voorgestelde historische wijzigingen.
   **Alleen instellingen opslaan** laat de bankhistorie ongewijzigd. Selecteer expliciet
   welke oude boekingen u wel wilt bijwerken; bij openen vanuit een transactie is alleen
   die bronboeking voorgeselecteerd als zij een geldige wijziging heeft.

Nieuwe Excel/CSV-imports tonen direct de herkende naam. De originele bankomschrijving
blijft beschikbaar onder **Oorspronkelijke omschrijving**; een eigen omschrijving heeft
altijd voorrang. Zoeken ondersteunt zowel banktekst als herkende/eigen namen. CSV-export
bewaart banktekst en herkende naam in afzonderlijke kolommen; een databaseback-up bewaart
ook de volledige instellingen en herkenningsverwijzingen.

Herkennen wat een betaling is, staat los van vaststellen welke geplande betaling zij
vervult. Een afwijkend bedrag of een late bankdatum blokkeert de naam en categorie niet.
Alleen een eenduidige match binnen de ingestelde planningsmarges wordt automatisch
gekoppeld. De maandstatus **Gekoppeld** betekent dat een echte bankboeking de schatting
vervangt. Niet gekoppeld betekent niet dat de identiteit onbekend is of dat de betaling
niet is gedaan. Importkoppelingen worden bij opslaan opnieuw gecontroleerd op basis van
de geselecteerde boekingen en categorieën.

Bestaande categorieregels behouden hun volgorde: de eerste passende regel met een
categorie wint. Verschillende passende namen worden niet willekeurig gekozen. Een
bevestigde planningskoppeling blijft leidend voor de identiteit. Regels en planningen
worden niet automatisch samengevoegd. Verwijderen van een herkenningsregel behoudt
bestaande herkende namen en categorieën; verwijderen van een planning behoudt de
tekstherkenning wanneer deze een herkenningstekst heeft.

Via **Historische herkenning controleren** kunt u ook later oude boekingen selecteren.
Bestaande categorieën, eigen omschrijvingen, splitsingen, vergoedingen en bevestigde
koppelingen worden niet overschreven. Herkende namen kunnen na selectie worden vernieuwd;
de banktekst, bankdatum, bedragen en saldi veranderen nooit door herkenning.

## Bestedingsruimte en vaste inkomsten/lasten

Op **Financiële vooruitblik → Hoeveel kan ik verantwoord uitgeven?** staat een schatting
vanaf vandaag, onafhankelijk van het geselecteerde maandfilter. De berekening
blijft per betaalrekening: gedeelde rekeningen tellen eenmaal, maar geld op een
andere rekening of spaarrekening dekt een rekeninggebonden betaling niet automatisch.

1. Voeg via **Vaste posten → + Vaste post** bekende inkomsten en lasten toe.
   Positief is inkomen, negatief is een uitgave. Kies maandelijks, per kwartaal,
   jaarlijks of eenmalig, een bedrag per betaling en de juiste rekening.
   Kwartaal/jaar volgen de startmaand en ingestelde dag; betalingen vóór de startdatum
   worden overgeslagen. Dag 29–31 wordt in korte maanden ingekort. Eenmalig gebruikt
   de startdatum als betaaldatum. Een planning maakt geen echte bankboeking aan.
2. Gebruik vaste leverancier-/werkgevertekst voor herkenning; verwijder veranderende
   datums en referenties uit de herkenningstekst. Verschillen in spaties, hoofdletters
   en leestekens worden genegeerd. Exacte omschrijvingen van eerder gekoppelde voorbeelden
   worden ook herkend; veranderende cijfers worden nooit stilzwijgend weggefilterd.
   Kies **Vast bedrag** met een passende bedragmarge, of **Wisselend bedrag** voor
   bijvoorbeeld energierekeningen of variabel salaris. Het bedrag blijft de schatting
   voor de volgende betaling. Herkenning vereist nog steeds dezelfde rekening,
   betaalrichting, passende verwerking en een eenduidige geplande betaling.
3. Naam en categorie worden onafhankelijk van de planning herkend. Eenduidige matches
   worden bij import automatisch gekoppeld. Afwijkende vaste bedragen
   en nabije bankdatums buiten de automatische marge blijven voorstellen. Bij opslaan
   van een vaste post verschijnt een lijst historische matches met datum, bedrag en
   selectievakjes. Selecteer de juiste boekingen en bevestig samen, of sla zonder koppelen op.
   Onder **Bankboekingen koppelen aan een geplande betaling** kunt u eenduidige matches
   selecteren of zelf voorstellen kiezen en de selectie samen bevestigen. Conflicterende
   voorstellen kunnen niet tegelijk worden geselecteerd. Bestaande koppelingen,
   splitsingen en vergoedingsverwerking worden niet automatisch overschreven.
4. Kies bij een transactie **Aan vaste post koppelen** om een bestaande vaste post
   en geplande betaaldatum te kiezen, of **Vaste post maken van deze boeking** voor
   een vooraf ingevuld formulier. Pas de begindatum aan als u oudere historie wilt
   meenemen. **Handmatig koppelen** op Vaste posten blijft ook beschikbaar.
   De geplande datum mag in een andere maand vallen dan de bankdatum. Eén bankboeking
   vervangt één schatting, met het echte bedrag en de echte datum. Bankbedragen,
   omschrijvingen en saldi blijven intact; volgende schattingen veranderen niet.
   Extra of deelbetalingen blijven ongekoppeld. Koppelingen zijn te verwijderen.
5. Stel via **Buffer instellen** of **Rekeningen → Bewerken** een minimale
   cashbuffer per rekening in. De bestedingsruimte is het laagste verwachte saldo
   tot en met de volgende inkomensdag minus deze buffer, minimaal 0.
6. Kies de rekening van uw aankoop en voer bijvoorbeeld `150,00` in. De controle
   kijkt naar alle geprojecteerde dagen: minstens 30, of tot het volgende inkomen
   als dat later valt (zoekhorizon maximaal 366 dagen). De dagelijkse saldotabel
   toont ook het laagste saldo en zijn datum. Kosten gaan voorzichtig vóór ontvangsten
   op dezelfde dag, zodat een positief maandultimo een eerder tekort niet verbergt.

De komende betalingen over 7/30 dagen omvatten vaste-postschattingen en geregistreerde
toekomstige afboekingen, inclusief expliciete overboekingen. Extra variabele uitgaven
worden apart geschat met maximaal 90 dagen historie tot de laatste geïmporteerde
bankdatum (zonder import: vandaag). Gekoppelde vaste lasten, hypotheekbetalingen
en sparen/eigen rekeningbewegingen (ook historische transfers) vallen buiten dat gemiddelde. Geplande variabele boekingen
vullen het gemiddelde per dag in, zonder dubbele laag. Het eindsaldo van de
betaalrekeningen gebruikt deze dagelijkse schatting; het maandresultaat behoudt zijn
maandgebonden daggemiddelde en is daarom niet dezelfde prognose.

De maandlijst op **Vaste posten** onderscheidt **Verwacht**, **Wacht op bankgegevens**,
**Gekoppeld**, **Te controleren** en **Niet gevonden · controleer betaling**. Geen koppeling
betekent niet onbetaald: pas na de volledige herkenningsperiode én volledige bevestigde
bankdekking wordt een betaling zonder match een controlepunt. Een bestaand voorstel
kan wel direct worden gecontroleerd. Zonder volledige dekking krijgt u één neutrale
importsamenvatting per rekening, geen achterstandsmelding per betaling.

De laatste onbevestigde uitgave per vaste post blijft als aparte **onzekere reserve**
in de cashprognose staan. Die telt voorzichtig aan het begin van de prognose mee,
maar wordt niet als een bekende betaling voor morgen gepresenteerd of in gedateerde
7/30-dagentotalen opgenomen. Oudere ontbrekende betalingen worden niet opgeteld.
Koppel echte betalingen om dubbeltelling te voorkomen; werkelijke bedragen en saldi
bevatten nooit deze reserves. Onbevestigd eerder inkomen is geen beschikbare ontvangst.
Zonder volgende inkomensdatum wordt voor 30 dagen gereserveerd met een waarschuwing.
Oude databases behouden hun maandgebonden koppelingen.

Bankgegevens zijn na 7 dagen verouderd. Ook een recente upload met alleen oude bankdatums
geeft geen zekerheid over het huidige saldo. Bevestigde volledige afschriftperioden
tellen ook op dagen zonder activiteit. Importactualiteit, beperkte historie, een buffer
van 0 en echte controlepunten staan gegroepeerd en uitklapbaar op Financiële vooruitblik.
Een aankoop die rekenkundig past krijgt bij onzekerheid geen stellige bevestiging. Onverwachte kosten,
gewijzigde facturen en niet-geregistreerde overboekingen ontbreken. Dit blijft een
schatting, geen bankverbinding of garantie. Maak regelmatig een volledige back-up.

## Splitsingen, retouren en vergoedingen

Open **Transacties → Splitsen / retour / vergoeding** bij een gewone bankboeking.
De bankdatum, oorspronkelijke omschrijving, bedrag, import en saldo blijven intact.
Activeer de verdeling om categoriebedragen toe te voegen: uitgaven negatief,
ontvangsten positief, maximaal twee decimalen. Het nog te verdelen bedrag moet exact
€ 0 zijn. Handmatige/gekoppelde transfers en hypotheekbetalingen zijn niet splitsbaar.

- **Supermarkt:** verdeel bijvoorbeeld een bankbetaling van € 120 in € 70 voeding,
  € 30 kleding en € 20 huishouden. Het banksaldo verandert slechts eenmaal met € 120.
- **Retour:** selecteer bij de bijschrijving **Retour** en de oorspronkelijke aankoop
  of splitsing. De ontvangst verlaagt die categorie in de ontvangstmaand, niet inkomsten
  of het historische aankoopbedrag. Zonder aankoopkoppeling kunt u zelf een categorie kiezen.
- **Werkgever of gedeelde aankoop:** registreer bij de uitgave wie terugbetaalt, welk
  bedrag en eventueel wanneer. De volledige aankoop blijft een uitgave totdat betaling
  is ontvangen. Registreer de bankontvangst als **Vergoeding**, gekoppeld aan die
  verwachting. Deelbetalingen verlagen het openstaande bedrag. Eén ontvangst kan
  meerdere verwachtingen aflossen, of gewone inkomsten plus vergoeding bevatten.
- **Openstaand:** de tabel onder Transacties toont verwachtingen voor alle datums
  binnen het eigenaarfilter, onafhankelijk van het maandfilter. Verwachtingen verhogen
  nooit saldi, vermogen of bestedingsruimte en worden niet als toekomstige bankontvangst
  aangenomen. Een geplande bankontvangst telt pas op zijn bankdatum als werkelijk.
- **Niet terugbetaald:** sluit de verwachting als geen verdere betaling wordt verwacht.
  Ontvangen vergoedingen blijven behouden; er ontstaat geen extra afschrijving, omdat
  het onbetaalde deel al als uitgave is geboekt.
- **Ongedaan maken:** kies bij een ontvangst ‘Alleen categorie’ om alleen de koppeling
  te verwijderen en de uitgavenvermindering te behouden; kies ‘Inkomst’ of schakel de
  verdeling uit om de classificatie ongedaan te maken. Verwijder afhankelijke koppelingen
  eerst voordat u een aankoop/splitsing of verwachting verwijdert. Ook import-undo weigert
  een aankoop te verwijderen zolang buiten die import nog gekoppelde ontvangsten bestaan.

Gekoppelde retouren en vergoedingen mogen samen de oorspronkelijke uitgave niet
overschrijden. Verwachtingen moeten passen binnen de aankoop na retouren; pas ze zo
nodig aan of sluit ze eerst. Een gesplitste aankoop vereist een specifieke splitsing
als bron. Categorisatieregels en de gewone categorie-editor wijzigen geen splitsingen.
Categorieën hernoemen werkt ook door naar gekoppelde ontvangsten; verwijderen maakt
de betreffende bedragen ongecategoriseerd zonder bankgegevens te verwijderen.

Maand-/jaarbudgetten tonen netto uitgaven, inclusief negatieve categoriebedragen in
maanden met meer retouren/vergoedingen dan aankopen. De cirkelgrafiek toont alleen
positieve netto categorieën; de toelichting en tabellen tonen het verschil. De dagelijkse
cashprognose gebruikt het volledige bankbedrag en bruto uitgavenhistorie, zodat
een incidentele terugbetaling de noodzakelijke dagelijkse cashreserve niet verkleint.
Retouren/vergoedingen zijn geen volgende reguliere inkomensdatum.

CSV-export bevat één rij per bankboeking. De JSON-kolommen **Splitsingen (JSON)** en
**Vergoedingsverwachtingen (JSON)** bewaren bedragen, verwerking, toelichtingen en
koppelingen zonder het bankbedrag te herhalen. **Categorie telt als** en
**Categorie-instellingen (JSON)** bewaren ook de verwerking van de hoofd- en deelcategorieën.
Bankimport herstelt deze metagegevens
niet automatisch: gebruik een volledige databaseback-up voor compleet herstel.

## Aankopen, bonregels en garanties

Open **Transacties → Aankopen & garanties** (`/aankopen`), of kies **Bon toevoegen**
bij een gewone afschrijving. De pagina gebruikt alle aankoopdatums, los van de maandkeuze.
Het eigenaarfilter volgt de gekozen rekening; bonnen zonder rekening staan alleen bij
het hele huishouden. Dit zijn weergavefilters, geen toegangsbeperking voor documenten.

1. Voeg een JPEG/PNG-foto of PDF toe, of begin met handmatige invoer. U kunt ook tekst
   uit een digitale factuur plakken. Bestanden zijn maximaal **8 MB**; afbeeldingen
   maximaal 30 megapixels/20.000 pixels per zijde en PDF's maximaal vijf pagina's.
2. De app leest lokaal tekst en stelt productregels, aantallen/gewichten, eenheidsprijzen,
   regelbedragen, kortingen en statiegeld voor. Controleer de winkel en aankoopdatum ook.
   Slechte foto's, afwijkende winkelindelingen en acties vragen vaak om correcties.
3. Corrigeer, voeg toe, verwijder of combineer regels. Onbekende bedragen blijven leeg.
   De regels en correcties moeten exact aansluiten op het bontotaal voordat u de bon
   als gecontroleerd kunt markeren. Een verschil wordt nooit automatisch weggeboekt;
   opslaan als concept blijft mogelijk.
4. Kies rechts optioneel een **Standaardcategorie voor deze bon**, bijvoorbeeld voeding.
   Alle regels volgen deze categorie, tenzij u op een regel een andere categorie kiest,
   bijvoorbeeld schoonmaak of cadeaus. Een gewijzigde standaard behoudt handmatige
   keuzes; kies op een regel **Standaard** om weer de boncategorie te volgen. Een korting
   kan aan een product worden gekoppeld en volgt dan diens categorie; een algemene korting
   volgt de boncategorie tenzij u zelf een andere kiest. Btw die al in de prijzen zit,
   is geen extra productregel.
5. Vul bij betaling met maaltijdcheques het positieve bedrag in bij **Betaald met
   maaltijdcheques (EUR)**, niet als negatieve productregel. Bij € 40 aan producten en
   € 30 maaltijdcheques blijven het bontotaal en de regels € 40; de resterende betaling is
   € 10. Duidelijk benoemde maaltijdchequebetalingen worden uit de bontekst voorgesteld;
   controleer het bedrag en vul het zo nodig zelf in. Het bedrag mag niet hoger zijn dan
   het bontotaal. Een volledig met maaltijdcheques betaalde bon kan zonder bankkoppeling
   als gecontroleerd worden opgeslagen.
6. Koppel optioneel een bankboeking. Voorstellen sluiten aan op de **resterende betaling
   na maaltijdcheques** en hebben een datum binnen zeven dagen; u bevestigt ze zelf.
    Andere gemengde betalingen kunnen nog een zichtbaar verschil geven. Alleen de
    betaalgegevens invullen wijzigt geen cijfers: neem de gecontroleerde bon expliciet
    op zoals hieronder beschreven. Oorspronkelijke bankbedragen en banksaldi blijven intact.

### Het bonbestand wel of niet bewaren

**Bonbestand bewaren** staat bij een nieuwe bon standaard **uit**. Zonder vinkje bewaart
de app alleen aankoopgegevens en productregels, inclusief de oorspronkelijk herkende
producttekst. De volledige OCR-tekst, afbeelding en PDF worden niet in de database
opgeslagen. Herkenning gebruikt geheugen en procespijpen, geen tijdelijke bonbestanden
op schijf. Uploads blijven maximaal 15 minuten beschikbaar voor beoordeling, en worden
bij opslaan, annuleren of verlaten van de pagina vrijgegeven. Een verlopen upload kunt
u opnieuw toevoegen; gegevens zonder document blijven ook dan als concept opslaanbaar.
Het oorspronkelijke bestand op uw telefoon of computer wordt uiteraard niet verwijderd.

Zet het vinkje **aan** voor een garantiebewijs, elektronica of een andere aankoop waarvan
u het origineel wilt behouden. Het bestand staat dan als bijlage in dezelfde private
SQLite-database en gaat mee in volledige `.db`-back-ups en herstel. Gewone pagina's laden
alleen metadata en productregels, niet alle afbeeldingen. Downloads vereisen dezelfde
aanmelding als de app. Opnieuw uitvinken verwijdert de bijlage bij opslaan uit de actieve
administratie; eerdere back-ups en reeds gedownloade kopieën veranderen niet. De app
versleutelt de database of bijlagen niet. CSV-export bevat geen bonregels of documenten;
gebruik daarvoor altijd een volledige databaseback-up. Database-import ondersteunt
bestanden tot 256 MB; bewaarde foto's vergroten de back-up.

### De volledige bon opnemen in de cijfers

Vink na controle **Deze bon opnemen in de cijfers (categorieën en maaltijdchequebetaling)**
aan en sla op. Bestaande bonnen worden bij een update **niet automatisch toegepast**.
Alle regels moeten aansluiten op een positief bontotaal; na kortingen mag geen categorie
een negatief totaal hebben. Voor het niet met cheques betaalde restant is één exacte
bankkoppeling nodig. Bij volledige betaling met maaltijdcheques is geen bankkoppeling nodig.
Andere betaalwijzen zoals een contant restant worden nog niet verwerkt.

- De boncategorieën worden opgeteld, inclusief productgebonden en algemene kortingen.
- Het bankdeel wordt als echte transactiesplitsingen over de categorieën verdeeld;
  het cheque-deel vult iedere categorie aan tot het volledige bonbedrag. Zo telt een
  bon van € 90 voeding en € 10 huishouden **€ 100 kosten**, ook bij € 60 maaltijdcheques
  en slechts € 40 bankbetaling. De bankbetaling wordt niet nogmaals opgeteld.
- Betalingen worden eerst evenredig verdeeld, met exact aansluitende centen. Deze
  verdeling zegt niets over welke producten wettelijk met maaltijdcheques betaald mogen
  worden. Via de gewone splitsingeneditor kunt u het bankdeel anders verdelen, zolang
  iedere categorie binnen haar bontotaal blijft. Het chequedeel volgt als restant.
  Opnieuw opslaan van een toegepaste bon berekent de evenredige splitsingen opnieuw.
- De volledige aankoop volgt in rapporten de gekoppelde bankdatum; zonder bankkoppeling
  volgt deze de aankoopdatum. Het maaltijdchequesaldo wordt op de aankoopdatum verlaagd.
- Maand-/jaaroverzichten, dashboard, budgetten, categorietrends, inzichten en gekoppelde
  Home Assistant-overzichtskaarten gebruiken dezelfde bedragen. Cheque-inkomsten en het
  met cheques betaalde deel zijn gezamenlijk en staan alleen bij **Alle eigenaren**.
  Een eigenaarfilter bevat alleen het bankbetaalde deel van de geselecteerde rekeningen.
- Een bankboeking kan maar via één toegepaste bon meetellen. Bestaande handmatige
  splitsingen of retour-/vergoedingskoppelingen worden niet stilzwijgend overschreven.
- Uitvinken of de bon verwijderen verwijdert de bonsplitsingen en chequebesteding;
  de oorspronkelijke bankcategorie geldt weer. Afhankelijke retouren of verwachtingen
  moeten eerst worden ontkoppeld. Een bankboeking verwijderen of een import terugdraaien
  ontkoppelt de bon en stopt de toepassing; aankoopgegevens en bijlage blijven behouden.

### Eén gezamenlijk maaltijdchequetegoed

Open **Transacties → Maaltijdcheques** (`/maaltijdcheques`). Er is één gedeeld saldo;
u hoeft niet te weten van wie de gebruikte cheques waren. De pagina toont alle datums,
los van de maandkeuze en het eigenaarfilter.

1. Leg eventueel één **Beginsaldo maaltijdcheques** vast voor reeds aanwezige cheques.
   Vul zonder bekend aantal bijvoorbeeld aantal 1 en het volledige saldo als waarde in.
   Kies een datum op of vóór de eerste geregistreerde betaling. Dit is **geen inkomen**.
2. Gebruik **Ontvangen maaltijdcheques vastleggen** met datum, aantal en waarde per cheque.
   Verschillende waarden voert u apart in, bijvoorbeeld 20 × € 8 en 18 × € 10. De volledige
   ontvangen waarde telt eenmaal als inkomen op de ontvangstdatum, niet nogmaals bij uitgeven.
3. Vul op de bon het betaalde chequebedrag in en neem de gecontroleerde bon op in de cijfers.
   De betaling gebruikt automatisch dit gezamenlijke tegoed, zonder keuze van persoon of batch.
4. Het saldo is ontvangen waarde plus beginsaldo min toegepaste bonbetalingen tot de datum.
   Toekomstige ontvangsten en bonbetalingen tellen nog niet mee in het saldo van vandaag.
   Een negatief saldo waarschuwt voor ontbrekende ontvangsten, een beginsaldo of onjuiste
   betalingen; de app verzint geen inkomen en verwijdert geen echte aankoop om dit op te lossen.

**Nettoloon en werknemersbijdrage:** als de eigen bijdrage al op het geïmporteerde nettoloon
is ingehouden, combineert u dat lagere nettoloon met de volledige chequeontvangst. Boek de
inhouding dan niet nogmaals als uitgave, en de chequeontvangst niet ook als bankinkomen.
Een afzonderlijk via de bank betaalde eigen bijdrage vraagt een consistente aparte verwerking;
de cheque-invoer herkent of corrigeert zulke bankboekingen niet automatisch.

Ongebruikte maaltijdcheques zijn **beperkt bruikbaar betaaltegoed**, geen vrij bankgeld,
spaargeld of noodbuffer. Ze verhogen geen banksaldi, vrijheidsreserves of geschatte
cashbestedingsruimte. Een maandresultaat kan wel nog ongebruikt chequetegoed bevatten;
het is dus niet hetzelfde als vrij besteedbaar bankgeld. De pagina toont dit expliciet.

CSV-export bevat ook niet-contante cheque-inkomsten en toegepaste chequebetalingen, herkenbaar
als `MealVoucherIncome` en `MealVoucherPayment`, met categorieverdeling, bonverwijzing en
ontvangstgegevens. Het beginsaldo, niet-toegepaste bonnen en bonbestanden zijn geen gewone
exportboekingen. Gebruik een volledige databaseback-up voor de volledige administratie.

### Retour en garantie

Per product kunt u merk/model, serienummer, locatie, status, retourdatum en garantiedatum
vastleggen. Datums worden niet afgeleid uit algemene wettelijke aannames: controleer de
voorwaarden van de aankoop zelf. De pagina toont komende termijnen binnen 30 dagen,
maar verstuurt nog geen Home Assistant-pushmeldingen. Een bestaande vergoedingsverwachting
van de gekoppelde aankoop is selecteerbaar; een productstatus boekt geen terugbetaling.

Een productstatus boekt zelf geen terugbetaling. De bestaande retour-/vergoedingsfunctie
werkt op de werkelijk bankbetaalde splitsingen; een terugbetaling van chequetegoed wordt
niet automatisch afgeleid uit een productstatus of bankontvangst. Persoonsverrekening
is niet onderdeel van deze functie.

### Lokale herkenning en computerpreview

De Home Assistant-container bevat **Tesseract** met Nederlandse/Engelse taalbestanden
en **Poppler** (`pdfinfo`, `pdftotext`, `pdftoppm`). Een digitale PDF gebruikt bestaande
tekst waar mogelijk; foto's en gescande pagina's gebruiken OCR. Er is geen cloud-API,
API-sleutel of groot taalmodel nodig. Er draait maximaal één herkenning tegelijk;
verwerking heeft invoer-/uitvoerlimieten en een tijdslimiet. Internet is alleen nodig
bij het bouwen/installeren van de containerpakketten, niet bij het lezen van bonnen.

Voor de lokale Windows-/computerpreview zijn deze programma's niet automatisch aanwezig.
Installeer Tesseract met `nld` en `eng` en de genoemde Poppler-programma's zelf, en zorg
dat ze op `PATH` staan wanneer de preview start. Anders meldt de pagina dat herkenning
niet beschikbaar is; tekst plakken, regels bewerken en documenten bewaren werken wel.
Controleer de herkenningskwaliteit met uw eigen winkelbonnen voordat u daarop vertrouwt.

## Opslag, back-ups en updates

- Actieve administratie: `/data/administration/huishoudboek.db`, met bijbehorende
  SQLite-bestanden, lease en service-marker. Configuratie: `/data/home-assistant.json`.
  `/data` is de private, permanente app-opslag van de Supervisor, geen Windows-pad.
- Normale herstarts en updates behouden `/data`. Een bestaande ontbrekende of
  beschadigde database wordt nooit automatisch vervangen door een lege database.
- Bij een Home Assistant-update worden ondersteunde schema's 1–15 na validatie
  onder de exclusieve schrijfvergrendeling automatisch bijgewerkt naar schema 16. Vooraf wordt
  een SQLite-bewuste `.voor-versie-16-….db`-veiligheidskopie naast de database gemaakt.
  De schemawijziging gebeurt in één transactie; bij een fout worden die wijzigingen teruggedraaid.
  Bestaande categorisatieregels behouden hun ID, tekst, categorie en volgorde. Voor vaste
  posten wordt een gedeelde herkenningsinstelling toegevoegd achter de bestaande regels,
  zonder samenvoegen, herimporteren, hercategoriseren of opnieuw koppelen van bankhistorie.
  Onbekende schema's worden geweigerd. Teruggaan naar een oude appversie vereist
  herstel van een passende oude back-up; schema-16-back-ups zijn niet leesbaar in
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
- Geldberekeningen blijven decimal, schema is versie 11. Saldi zijn berekend uit
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
- Grote database: de huidige uploadgrens is 256 MB. Niet verkleinen door transacties
  uit uw echte administratie te verwijderen; deze versie ondersteunt grotere imports niet.
