# Changelog

## 0.1.15

- Nieuwe pagina Inzichten met een financiële vrijheidskaart: maanden essentiële
  uitgaven zonder inkomen, bufferdoel, bestedingsruimte, vaste verplichtingen en
  leefstijlbereik. Compacte kaart op het dashboard, exacte bedragen en uitleg per ring.
- Categorieën als essentieel of uitzonderlijk markeren. Uitzonderlijke kosten blijven
  in werkelijke saldi, budgetten en maand-/jaaroverzichten, maar verstoren geen gewone
  uitgavenschattingen of gedragspatronen. Toekomstige betalingen blijven gereserveerd.
- Geldvingerafdruk met zes verklaarbare dimensies, daggemiddelde-vergelijkingen en
  bronboekingen. Alleen voldoende bevestigde bankhistorie ondersteunt ontdekkingen;
  ontbrekende gegevens worden nooit als nul of een perfecte score gepresenteerd.
- Instelbare toegankelijke reserverekeningen, extra gereserveerd geld en een bufferdoel.
  Beleggingen tellen niet mee als liquide noodreserve; bereik is geen financiële
  onafhankelijkheid. Nederlands/Engels, responsive SVG's zonder externe diensten.
- Databaseschema 10 met veiligheidskopie vóór migratie van schema's 1–9 en veilige
  standaardinstellingen. Nieuwe regressietests voor berekeningen, migratie en weergave.

## 0.1.14

- Nieuwe pagina Categorietrends: historische maanduitgaven per categorie met
  kleurrijke bedragstaven, een voortschrijdend gemiddelde over drie maanden,
  een vergelijking van twee jaren en een interactieve kalenderheatmap.
- Kies 6, 12 of 24 afgesloten maanden of een eigen periode tot tien jaar.
  De huidige maand is optioneel en telt niet mee voor gemiddelden en jaarvergelijkingen.
  Eigenaarfilters, splitsingen, retouren en vergoedingen volgen de bestaande boekhoudregels.
- Exacte bedragen blijven uitklapbaar beschikbaar. Ontbrekende afschriftbevestigingen
  worden zichtbaar gemeld; geen boekingen betekent niet automatisch geen uitgaven.
- Rechtstreeks bereikbaar vanaf het dashboard, de navigatie en categorietotalen in
  maand-/jaaroverzichten. Beide talen, mobiele weergave en verminderde animatie ondersteund.

## 0.1.13

- Nieuw dashboard als startpagina met individuele rekeningsaldi van vandaag,
  compacte maandcijfers, uitgavencategorieën en budgetbewaking, komende betalingen,
  volgende inkomensdatums, recente boekingen en gerichte aandachtspunten.
- Rekeningkaarten kiezen per browser; standaard de betaalrekeningen/zichtrekeningen.
  Spaar- en beleggingsrekeningen zijn optioneel, met cash en effecten apart vermeld.
  De kaartkeuze verandert geen rapporten; eigenaarfilters blijven overal gelden.
- Het bestaande maandoverzicht blijft behouden als aparte pagina op `/maand`.
  Dashboard en maandoverzicht hebben elk een eigen navigatielink.
- Nederlandse/Engelse teksten, documentatie en regressietests voor de nieuwe startpagina,
  lokale voorkeuren, bankactualiteit, eigenaarfilters en onzekere betalingen.

## 0.1.12

- Rustiger maandoverzicht: maandcijfers direct bovenaan, zonder eerste-weekchecklist,
  wekelijkse controle, uitgebreide maanduitleg of bestedingsplanner erboven.
  Berekeningsdetails zijn uitklapbaar; compacte waarschuwingen verwijzen naar de uitleg.
- Aparte pagina's voor Financiële vooruitblik, Instellen en Wekelijkse controle.
  Instellen staat onder Beheer & import. De wekelijkse menulink is per browser te
  verbergen en daar te herstellen, zonder financiële gegevens of controles te wijzigen.
- Nederlandse/Engelse teksten, documentatie en regressietests bijgewerkt voor de
  nieuwe routes, Home Assistant-ingress en blijvende navigatievoorkeuren.

- Vaste posten met een maandlijst: verwacht, wacht op bankgegevens, herkend, te
  controleren of niet gevonden. Ontbrekende bankimports leveren geen waarschuwing
  per betaling meer op; de financiële vooruitblik groepeert onzekerheid per rekening.
- Expliciet volledige afschriftperioden bevestigen bij import, ook zonder activiteit.
  Oudere imports zijn onder Beheer & import te bevestigen zonder opnieuw te importeren.
  Gaten, rekeninggrenzen en import-undo blijven gerespecteerd; gewijzigde/verwijderde
  bankgegevens laten de bijbehorende bevestiging vervallen.
- Herkenning van vaste of wisselende bedragen, onschuldige tekstformatering en exact
  eerder gekoppelde omschrijvingen. Afwijkende vaste bedragen en nabije afwijkende
  bankdatums blijven controlevoorstellen, nooit automatische koppelingen.
- Vanuit een transactie een vaste post koppelen of maken; historische matches met
  datums, bedragen en selectievakjes bekijken en samen bevestigen. Dubbelzinnige
  boekingen, rekeninggrenzen en expliciete verwerking blijven beschermd.
- Onbevestigde uitgaven blijven een aparte, onzekere planningsreserve, geen als
  ‘morgen te betalen’ gepresenteerde rekening. Ze staan niet in gedateerde 7/30-dagentotalen.
  Werkelijke bankboekingen, bedragen, datums en saldi worden niet aangepast.
- Databaseschema 9 met veiligheidskopie vóór migratie van schema's 1–8. Bestaande
  vaste posten blijven standaard op vaste bedragen staan; oude importdekking wordt
  niet geraden. Nieuwe instellingen en afschriftperioden blijven in volledige back-ups bewaard.

## 0.1.11

- Eenvoudiger sparen zonder transfers koppelen: categorieën tellen als gewone
  inkomsten/uitgaven, sparen/beleggen of bewegingen tussen eigen rekeningen.
  Spaarbijdragen tellen alleen op de betaalrekening, zonder verplichte tweede import.
  Terugontvangsten verlagen opzijgezet, niet inkomsten; de spaarzijde telt niet dubbel.
- Maandoverzicht met overschot vóór sparen, apart opzijgezet en maandresultaat na sparen.
  Uitgaven boven inkomen, categoriebudgetoverschrijdingen en sparen boven het overschot
  krijgen verschillende verklaringen. Cashbuffer- en rekeningcontroles blijven actief.
- Budgetpagina splitst uitgavenbudgetten, spaardoelen en eigen rekeningbewegingen.
  Een gehaald spaardoel is geen rode budgetoverschrijding. Splitsingen, vaste posten,
  maand-/jaaroverzichten, CSV en prognoses gebruiken dezelfde categorie-instellingen.
- Transfermatching en het nieuwe handmatige transfertype uit dagelijks gebruik verwijderd.
  Oude transfers blijven ongewijzigd als historie, met beschermde financiële velden.
  Nieuwe bankboekingen wijzigen alleen hun eigen rekening; categoriseren nooit saldi.
- Databaseschema 8 met veiligheidskopie vóór migratie van schema's 1–7. Bestaande
  bankboekingen, transferbeslissingen en categorienamen worden niet herschreven.
  Beide talen en tests uitgebreid voor sparen, opnames, splitsingen, prognoses,
  eigenaarfilters, jaargrenzen en compatibel back-up/herstel.

## 0.1.10

- Automatische koppeling van vaste posten bij bankimport respecteert nu de ingestelde
  bedrag- en datummarges, zonder verborgen limiet van 1 cent of 5 dagen. De geselecteerde
  regels worden samen beoordeeld: meerdere mogelijke boekingen of geplande betalingen
  blijven ter controle. Werkelijke bankbedragen en volgende schattingen blijven intact.
- Automatische dubbelcontrole bij bankimports verwijderd: alle geldige regels binnen
  de gekozen periode zijn standaard geselecteerd, ook identieke en eerder geïmporteerde
  boekingen. Handmatig uitvinken en invoervalidatie blijven behouden. Een afschrift
  opnieuw importeren voegt de geselecteerde boekingen opnieuw toe.

## 0.1.9

- Eerste-weekchecklist: eigenaren en rekeningen → beginsaldi → afschriften importeren
  → banksaldi controleren → categoriseren → budgetten en vaste posten. Nieuwe gebruikers
  starten zonder database-import; voortgang volgt echte gegevens en expliciete saldocontroles.
- Snelle uitgave-/inkomsteninvoer met een positief bedrag en alleen de noodzakelijke velden.
  De uitgebreide editor toont tegenrekening en aflossing alleen bij het bijbehorende type.
- Telefoonvriendelijke transactielijst met datum, rekening, categorie en bedrag;
  banktekst, koppelingen en bewerken/verwerken/verwijderen zijn uitklapbaar. Desktop behoudt de tabel.
- Kant-en-klare CSV-profielen en automatische herkenning voor ING (NL), Rabobank en
  KBC/CBC, naast Argenta en het bestaande banktemplate. Profielen blijven aanpasbaar;
  importvoorbeeld, EUR- en rekeningcontrole blijven verplicht.
- Compacte wekelijkse controle voor recente imports, categorisatie-/koppelvoorstellen,
  ongecontroleerde saldi en ongekoppelde rekeningen over 7 dagen, ook over maandgrenzen.
- Expliciete beginsaldo- en banksaldocontroles worden in back-ups bewaard. Wijzigingen
  aan cashgegevens of nieuwe werkelijke boekingen heropenen de saldocontrole; geen automatische correctieboekingen.
- Bij toevoegen of opslaan van een vaste post verschijnt het aantal eenduidige
  historische matches binnen de ingestelde marges. Eén bevestiging koppelt ze
  allemaal; opslaan zonder koppelen blijft mogelijk. Dubbelzinnige matches blijven
  ter controle; bankgegevens, saldi, categorieën, bestaande koppelingen en expliciete
  transactieverwerking blijven intact. De planning en batch worden samen opgeslagen.
- Compacte taalkeuze `NL - EN` onder Beheer & import in de zijbalk, zonder keuzelijst
  of aparte bevestigingsknop. De zijbalk blijft staan tijdens het scrollen van de pagina.
- Pagina's, formulieren, bevestigingen, grafieken en bestedingsruimte gebruiken de
  gekozen interfacetaal. Bedraginvoer volgt het decimaalteken van de taal; eigen namen
  en bankteksten blijven ongewijzigd. Tests controleren taalwisselingen in beide
  richtingen, ook achter het Home Assistant-ingresspad.
- Bankboekingen verdelen over meerdere categorieën met bedragen die exact aansluiten
  op het oorspronkelijke bankbedrag. De banktekst, datum, importgegevens en saldi blijven intact.
- Expliciete retouren en vergoedingen verlagen uitgaven in de ontvangstmaand, nooit
  inkomsten. Koppel de oorspronkelijke aankoop of splitsing, of kies alleen een categorie.
- Verwachte vergoedingen en gedeelde aankopen bijhouden per betaler, met optionele
  betaaldatum, deelbetalingen en sluiten van een verwachting. Voorschotten blijven
  uitgaven totdat terugbetaling is ontvangen; verwachtingen zijn geen beschikbaar geld.
- Eén ontvangst kan meerdere aankopen vergoeden of salaris en vergoeding combineren.
  Openstaande bedragen en ontvangen betalingen zijn zichtbaar bij Transacties.
- Budgetten, maand-/jaaroverzichten en prognoses gebruiken dezelfde verwerking.
  Negatieve uitgavencategorieën blijven zichtbaar; de cirkelgrafiek licht het verschil toe.
- Afhankelijke koppelingen beschermen tegen onbedoeld wijzigen/verwijderen en import-undo.
  Categorisatieregels en transferkoppelingen overschrijven geen expliciete splitsingen.
- Databaseschema 7, met veilige migratie van schema's 1–6 en een veiligheidskopie vooraf.
  Volledige back-ups bewaren alle gegevens; CSV blijft één rij per bankboeking en
  bevat splitsingen en vergoedingsverwachtingen als JSON-kolommen.
- Tests voor splitsingen, retouren, deelbetalingen, maandgrenzen, cashveiligheid,
  eigenaarfilters, koppelingen, migratie, back-up/herstel, export en beide interfacetalen.

## 0.1.8

- Taalkeuze in de werkbalk: Nederlands (standaard) en Engels, per browser onthouden.
  Wijzigen van de taal herlaadt de pagina; sla open formulieren eerst op.
- Vertaalbasis met standaard .NET-`.resx`-bestanden, benoemde sleutels en
  `IStringLocalizer`. Navigatie, werkbalk, gedeelde knoppen, paginering en
  verbindingsmeldingen zijn vertaald. Overige paginateksten zijn nog Nederlandstalig;
  de Engelse vertaling is dus nog niet volledig.
- Maandnamen en getoonde bedragen volgen de gekozen taal; de valuta blijft EUR.
  Rekeningnamen, categorieën en bankomschrijvingen blijven ongewijzigd.
- Lokalisatietests voor beide resourcebestanden, standaardtaal, ondersteunde
  browservoorkeuren, veilige terugkeeradressen, EUR-notatie en Razor-weergave.
- Bestedingsruimte vanaf vandaag per betaalrekening, tot het volgende inkomen,
  na bekende betalingen, dagelijkse uitgavenschatting en een instelbare cashbuffer.
- Aankoopcontrole (bijvoorbeeld € 150), betalingen over 7/30 dagen, dagelijkse
  saldotabel en het laagste verwachte saldo met datum. Geen automatische dekking
  door geld op een andere rekening; kosten gaan voorzichtig vóór ontvangsten op dezelfde dag.
- Waarschuwingen bij oude/ontbrekende imports, beperkte historie, onbekende betaaldag
  en vervallen, niet-gekoppelde posten. Achterstallig inkomen wordt niet aangenomen.
- Vaste posten uitgebreid met kwartaal-, jaar- en eenmalige betalingen. Bankboekingen
  zijn aan een specifieke geplande betaling te koppelen, ook over maandgrenzen en
  met een afwijkend bedrag. Volgende schattingen en oorspronkelijke boekingen blijven intact.
- Controlevoorstellen met instelbare bedrag-/datummarges, handmatig koppelen en
  ontkoppelen. Automatische herkenning blijft strikt (± 1 cent, maximaal 5 dagen).
- Databaseschema 5 bewaart buffers, frequenties en geplande betaaldatums, met veilige
  migratie van schema's 1–4 en een veiligheidskopie vooraf. CSV-export bevat vaste-postkoppelingen.
- Tests voor betaaldagtekorten, rekeninggrenzen, dagelijkse uitgaven, importactualiteit,
  frequenties, koppelingen, maandgrenzen, migratie/back-ups en gerenderde schermen.

## 0.1.7

- Nieuwe werkruimte **Transfers koppelen**: mogelijke paren van bestaande af- en
  bijboekingen bekijken, expliciet bevestigen, afwijzen of handmatig koppelen.
- Transferkoppelingen wijzigen alleen rapportage: beide bankboekingen, bedragen,
  datums, omschrijvingen en rekeningsaldi blijven intact. Geen automatische
  tegenboekingen of koppelingen tijdens import.
- Bevestigde paren uitgesloten van inkomsten, uitgaven, categoriebestedingen en
  variabele uitgavenprognose. Netto spaar-/beleggingsbijdragen tellen één keer op
  de bankdatum van de betaalrekening, ook bij verschillende maanden.
- Koppelingen en afwijzingen zijn omkeerbaar. Financiële wijzigingen, verwijderen
  van een boeking en terugdraaien van een import verwijderen betrokken beslissingen.
- Databaseschema 4 bewaart transferbeslissingen in volledige back-ups. Home
  Assistant werkt ondersteunde eerdere schema's bij onder de schrijfvergrendeling,
  na validatie en een SQLite-bewuste veiligheidskopie. Onbekende versies worden geweigerd.
- CSV-export aangevuld met transactie-ID's en bevestigde transferkoppelingen.
- Tests voor saldobehoud, rapportage, prognoses, maandgrenzen, eigenaarfilters,
  conflicterende voorstellen, wijzigingen, import-undo en migratie/back-up/herstel.

## 0.1.6

- Excel/CSV-imports boeken uitsluitend op de gekozen rekening en behouden het
  oorspronkelijke bedrag, ook bij betalingen tussen eigen rekeningen.
- Automatische tegenrekeningkoppeling en de tegenrekeningkeuze in het importvoorbeeld
  verwijderd. Elke rekening gebruikt het eigen importbestand; boekingen op andere
  rekeningen worden niet als dubbelen gebruikt.
- Bestaande opgeslagen overboekingen blijven ongewijzigd; controleer en corrigeer
  eerder gekoppelde imports zo nodig handmatig. Importprofielen blijven compatibel.
- Regressietests toegevoegd voor rekeningonafhankelijke imports, bedragen,
  dubbelen, saldocontrole en het weigeren van tegenrekeningkoppelingen via de API.

## 0.1.5

- Blazor-JavaScript-koppeling voor maand- en jaargrafieken hersteld: geen dubbele
  objectreferenties meer, zodat grafiekupdates de verbinding niet verbreken.
- Productiepublicatie gecontroleerd met gesimuleerde Home Assistant-ingress,
  inclusief laden van scripts, navigatie en wijzigen van het jaarfilter.

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
