# Changelog

## 0.1.1

- Add-on niet meer als experimenteel gemarkeerd, zodat installatie geen
  Advanced Mode meer vereist.
- `arch` uitgebreid naar aarch64, amd64, armhf, armv7 en i386.
  Let op: Microsoft publiceert geen .NET 10 SDK/ASP.NET-images voor
  armhf/armv7 (32-bit ARM) of i386, dus de build zal op die
  architecturen mislukken totdat een alternatieve .NET-runtime of
  build-aanpak beschikbaar is.

## 0.1.0

- Eerste lokale amd64 Home Assistant-app met volledige Nederlandstalige Blazor-interface.
- Aanmelding via vertrouwde Supervisor-ingress, zonder externe netwerkpoort.
- Bestaande Huishoudboek-database importeren met validatie en veiligheidskopie.
- Permanente private opslag, instelbare tijdzone en cold backups.
- Accountonafhankelijk GitHub-repositorypakket met on-device broncodebuild,
  zonder GitHub Actions, containerregister of vooraf ingestelde repository-URL.
- Experimenteel: uiteindelijke installatie en ingress in een echte Home Assistant OS-VM
  moeten nog in de doelomgeving worden geverifieerd.
