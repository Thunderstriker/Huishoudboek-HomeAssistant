# Changelog

## 0.1.4

- Maandoverzicht opnieuw ingericht met een compacte uitgavendoughnut en een
  vergelijking van werkelijke bedragen met de maandprognose.
- Jaaroverzicht vernieuwd met een inkomsten-en-lastentrend, een uitgavendoughnut
  en netto spaarbijdragen per maand en opgeteld sinds januari.
- Grootste vijf uitgavencategorieën zichtbaar; kleinere categorieën samengevoegd.
- Jaartabellen met exacte bedragen en budgetten uitklapbaar gemaakt.
- Chart.js 4.5.1 lokaal meegeleverd onder de MIT-licentie, zonder CDN-afhankelijkheid.
- Responsieve grafieken zonder horizontaal scrollen, met afhandeling van lege
  gegevens en veilige opruiming bij navigatie en filterwijzigingen.

## 0.1.3

- Maandoverzicht uitgebreid met uitgavengrafieken per categorie, budgetmarkeringen
  en zichtbare budgetoverschrijdingen.
- Werkelijke inkomsten, lasten en het resterende bedrag visueel vergelijken met
  de prognose voor het einde van de maand.
- Jaargrafiek toont inkomsten, lasten, netto opzijgezet en het resterende bedrag
  met negatieve bedragen onder de nullijn; toekomstige maanden worden niet getekend.
- Jaaruitgaven per categorie vergelijken met het volledige jaarbudget.
- Grafieken met lege toestanden, toegankelijke labels en horizontaal scrollen
  op kleine schermen; exacte bedragen blijven beschikbaar.
- README-bestanden vernieuwd met installatie, gebruik, grafiekuitleg en beperkingen.

## 0.1.2

- Automatische herkenning van het Argenta-exportformaat (Boekingsdatum,
  Rekening tegenpartij, Munt, Mededeling) bij importeren.

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
