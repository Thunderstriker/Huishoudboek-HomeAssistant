# Huishoudboek voor Home Assistant

Een volledig lokaal, Nederlandstalig huishoudboek in de Home Assistant-zijbalk.
Inclusief maand-/jaaroverzicht, transacties, categorisatie, rekeningen/eigenaren,
budgetten, vaste posten, hypotheek, Excel/CSV-import, regels, export en back-up/herstel.
EUR, geen MQTT, cloud, telemetry of automatische bankverbinding.

Importeer uw **bestaande Huishoudboek-database (.db)**. Daarna draait de administratie
volledig in de opslag van deze app, zonder Windows-backend of gedeelde netwerkmap.

Deze eerste versie is voor **Home Assistant OS op amd64/x64**, bijvoorbeeld een VM
op een Windows-pc. De host en VM moeten blijven draaien. De Windows-financeapp niet.
De installatie in een echte Supervisor moet nog in uw doelomgeving worden gecontroleerd.
Zie **Documentatie** voor installatie, database-import en veiligheidsbeperkingen.

Publiceren vanuit de bron: `Publish-HuishoudboekHomeAssistant.ps1`.
Installeer de complete gegenereerde map, niet alleen deze metadata.

Liever via uw eigen GitHub-repository installeren? Genereer met
`Publish-HuishoudboekHomeAssistantRepository.ps1` een accountonafhankelijk
broncoderepositorypakket. Upload de inhoud naar uw eigen publieke repository
en voeg die URL toe aan de Home Assistant-appwinkel. Geen GitHub-token of
vooraf gepubliceerde container-image nodig; Home Assistant bouwt de broncode.
