# Huishoudboek voor Home Assistant

Volledig lokaal huishoudboek in de Home Assistant-zijbalk: transacties,
categorisatie, rekeningen/eigenaren, budgetten, prognoses, vaste posten,
hypotheek, Excel/CSV-import, export en databaseback-ups. Nederlands, EUR.

**Dit is een app-/add-onrepository, niet HACS.** De app draait in Home Assistant OS
op amd64/x64, bijvoorbeeld een VM op een Windows-pc. De host en VM moeten aan
blijven; een Windows-financebackend is niet nodig.

## Zelf op GitHub publiceren

1. Maak een **publieke GitHub-repository** in uw eigen account. De naam is vrij:
   bijvoorbeeld `huishoudboek-home-assistant`. Er is geen vaste gebruikersnaam,
   repository-URL of containerregister ingesteld.
2. Pak `Huishoudboek-HomeAssistant-Repository.zip` uit.
3. Upload de **inhoud** naar de hoofdmap van uw repository en commit. De structuur
   moet direct zo zijn, zonder extra omvattende map:

   ```text
   repository.yaml
   README.md
   .gitignore
   huishoudboek/
     config.yaml
     Dockerfile
     .dockerignore
     DOCS.md
     source/
       Directory.Build.props
       src/
         Huishoudboek.Core/
         Huishoudboek.Infrastructure/
         Huishoudboek.Web/
         Huishoudboek.Service/
   ```

4. Publiceer **geen database, back-up, bankexport of lokale serviceconfiguratie**.
   Het gegenereerde pakket bevat alleen geselecteerde applicatiebroncode en
   documentatie. Uw financiële gegevens worden pas na installatie in de app
   geïmporteerd en komen niet op GitHub.

Geen GitHub Actions, GitHub-token, GHCR-account of vooraf gepubliceerde
container-images nodig. De Home Assistant-VM bouwt tijdens installatie zelf
vanuit de meegeleverde broncode. Daarom heeft de appconfiguratie bewust geen
`image`-verwijzing. De eerste installatie/update kan een paar minuten duren en
vraagt extra VM-geheugen, opslag en internet voor de officiële .NET SDK/runtime
en NuGet-pakketten. De SDK blijft buiten het uiteindelijke runtime-image.

Voor brede distributie zijn vooraf gebouwde images sneller; die zijn in deze
eenvoudige, accountonafhankelijke repository niet ingesteld. De broncode alleen
publiek uploaden kiest niet automatisch een opensourcelicentie. Bepaal zelf de
licentie/rechten voordat u het gebruik door anderen toestaat.

## In Home Assistant installeren

1. Open **Instellingen → Apps/Add-ons → Appwinkel → ⋮ → Repositories**.
2. Voeg de URL van **uw repository** toe, bijvoorbeeld:
   `https://github.com/UW-ACCOUNT/UW-REPOSITORY`.
   Gebruik de repository-URL, niet de ZIP-download, een Release of `/tree/...`.
3. Zoek **Huishoudboek**, kies **Installeren**, daarna **Starten** en desgewenst
   **Tonen in zijbalk**. Controleer de tijdzone (standaard `Europe/Amsterdam`).
4. Open de webinterface. Alleen uw **Home Assistant-aanmelding** is nodig;
   geen apart appwachtwoord, certificaat of API-token instellen.
5. Kies **Bestaande database importeren** en importeer uw bestaande Huishoudboek
   `.db` of databaseback-up. Daarna staat de actieve database volledig in de
   permanente Home Assistant-appopslag. Uw Windows-bronbestand blijft ongewijzigd.

Gebruik bij voorkeur een back-up uit de Windows-app. Sluit anders zowel de app
als de financiële Windows-service voordat u een raw `.db` kopieert; een kopie
van alleen een actieve database kan recente SQLite-WAL-wijzigingen missen.

Meer uitleg: [Documentatie](huishoudboek/DOCS.md).

## Updates en een stabiele repository

- Account en repositorynaam zijn vrij, maar houd **na installatie de repository-URL,
  appmap `huishoudboek` en `slug` stabiel**. Een andere repository kan door Home
  Assistant als een andere app worden behandeld, met andere opslag.
- Publiceer bij een update de bijgewerkte broncode en documentatie. Verhoog
  `version` in `huishoudboek/config.yaml` en houd het versielabel in de Dockerfile
  gelijk. Werk daarna bij via de Home Assistant-appwinkel.
- Verwijder/herinstalleer de app niet om bij te werken. Maak eerst een back-up;
  normaal bijwerken behoudt de permanente opslag.
- Overstappen van een eerdere **lokale** installatie naar deze repository is geen
  automatische datamigratie. Download eerst een databaseback-up, installeer de
  repository-app en importeer de back-up daar. Bewerk niet beide kopieën verder.

## Status

Experimentele eerste versie, alleen amd64. De financiële logica, database-import
en ingress zijn geautomatiseerd getest, inclusief Linux-container en browser.
Een echte installatie vanuit uw GitHub-repository in Home Assistant OS moet nog
in de doelomgeving worden gecontroleerd. De app gebruikt geen MQTT, cloud,
telemetry of automatische bankverbinding. Database/back-ups zijn niet door de
app versleuteld. Alle toegelaten ingress-gebruikers hebben volledige financiële
rechten; de interne ingresscontrole voorkomt directe toegang buiten de HA-gate.
