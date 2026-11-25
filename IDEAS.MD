# Ideeën voor later

Deze uitbreidingen zijn besproken, maar worden nog niet geïmplementeerd.
Abonnementen & contracten is een afzonderlijke opdracht; onderstaande onderdelen
mogen die eerste uitbreiding niet onnodig vergroten.

## 1. Energiecontracten vergelijken — Vlaanderen

- Elektriciteit en optioneel gas, met leverancier, contract/product, peildatum,
  tariefkaart, vaste/variabele/dynamische prijsformule en relevante contractdatums.
- Verbruik vastleggen in kWh voor elektriciteit en kWh of m³ voor gas, altijd met
  begin- en einddatum van de verbruiksperiode en herkomst van de gegevens.
- Vergelijk huidige en alternatieve contracten met hetzelfde verbruiksprofiel.
  Maandelijkse voorschotten zijn betalingen, niet de werkelijke jaarlijkse kosten.
- Houd rekening met locatie, meter-/telsysteem, dag/nacht, zonnepanelen en injectie,
  toepasselijke netkosten, heffingen, btw, vaste kosten en capaciteitstarief.
- Zet gas niet met een universele vaste factor om van m³ naar kWh: gebruik een
  gedocumenteerde conversiefactor uit factuur of gevalideerde gegevensbron.
- Onderzoek een toegestane koppeling met de Vlaamse V-test of andere betrouwbare
  bronnen. Een publiek toegankelijke website is niet automatisch een vrij
  herbruikbare API. Vermeld bron, voorwaarden, peildatum en onvolledige dekking.

### Dynamische elektriciteitscontracten

- Toon gepubliceerde prijzen van vandaag en morgen zodra beschikbaar. Presenteer
  latere dagen zonder gepubliceerde prijzen als onbekend; eventuele voorspellingen
  moeten duidelijk van gepubliceerde tarieven te onderscheiden zijn.
- Ondersteun kwartier- en uurintervallen volgens marktbron en contract. Bewaar
  tijdzonebewuste intervalgrenzen; houd rekening met zomer-/wintertijd.
- Toon naast marktprijzen de contractprijs met de gedocumenteerde leveranciersformule.
  Marktprijzen alleen bevatten niet alle toeslagen, belastingen of netkosten.
- Bewaar beschikbare historische prijzen voor dagelijkse kostenoverzichten.
  Bereken verbruiksafhankelijke kosten door per interval het verbruik met de
  toepasselijke prijs te vermenigvuldigen en de resultaten op te tellen.
- Met alleen dagtotalen is dynamische kostentoerekening een schatting: vermeld de
  aangenomen verdeling over prijsintervallen. Jaarverbruik alleen ondersteunt geen
  exacte vergelijking van dynamische contracten.
- Toon kWh, verbruiksgewogen prijs, berekende energiekosten en gegevensdekking per
  dag. Ontbrekende metingen of tarieven zijn geen nulverbruik of gratis energie.
- Behandel negatieve marktprijzen correct, zonder automatisch te beweren dat de
  volledige contractprijs inclusief toeslagen ook negatief is.
- Registreer netafname en teruglevering afzonderlijk, met hun eigen prijsformules.
- Maak onderscheid tussen berekende energiekosten, geschatte aanvullende kosten
  en werkelijke facturen. Verander of creëer geen bankboekingen op basis van metingen.
- Home Assistant Nord Pool is een mogelijke bron voor gepubliceerde marktprijzen;
  controleer Belgische dekking, historische beschikbaarheid en gebruiksrechten.

## 2. Optionele Home Assistant-verbruiksinvoer

- Begin eventueel met CSV-import voor historie; gebruik hetzelfde gegevensmodel
  voor een latere automatische verbinding.
- Ontwerp het model meteen voor intervalgegevens, ook als eerst alleen dagtotalen
  worden aangeleverd. Bewaar bron, meter-ID, begin/einde, eenheid, hoeveelheid,
  afname/injectie en of het een intervalhoeveelheid of cumulatieve meterstand is.
- Dagelijkse batches met kwartier-/uurgegevens maken nauwkeurigere dynamische
  kostenberekening mogelijk dan één dagtotaal. Onderzoek bestaande P1-/energiemeter-
  gegevens in Home Assistant voordat een nieuwe meetverzameling wordt gebouwd.
- Optioneel een schrijfendpoint waarmee Home Assistant uitsluitend energiegegevens
  kan aanleveren. De huidige add-on heeft geen externe API-poort en blokkeert de
  bestaande API in Home Assistant-modus; dit vraagt een afzonderlijk beveiligingsontwerp.
- Standaard uitgeschakeld, met bewust ingestelde private bereikbaarheid en een
  eigen intrekbare credential zonder lees-/schrijfrechten op financiële gegevens.
  Geen publiek internetendpoint en geen hergebruik van browser-ingresssessies.
- Begrens verzoekgrootte, batchomvang en frequentie. Valideer eenheden, tijdstippen,
  perioden en meettypen voordat een batch atomair wordt opgeslagen.
- Herhaalde batches mogen geen dubbel verbruik opleveren. Definieer expliciete
  correcties, overlapregels, meterresets en verwerking van cumulatieve standen.
- Toon laatste succesvolle invoer, onvolledige perioden en fouten zonder secrets
  of onbewerkte gevoelige meetgegevens te loggen.
- Bewaar instellingen en meetgegevens veilig bij back-up/herstel; houd credentials
  gescheiden. Voorzie grenzen voor opslag en een bewaarbeleid voor intervalhistorie.
- Scope: financiële kosten en contractvergelijking. Geen vervanging voor het Home
  Assistant-energiedashboard en geen automatische aansturing van apparaten.

## 3. Automatische abonnements- en telecomvergelijkingen — bron nog nodig

- Op verzoek uitgesteld terwijl het contractregister wordt geïmplementeerd.
  Automatische prijzen blijven het gewenste einddoel; handmatige offertes zijn
  geen overeengekomen vervanging voor deze functie.
- Vergelijk aanbiedingen in hetzelfde land en toon bron, laatste controle,
  eerstejaarskosten, structurele kosten, eenmalige kosten en actievoorwaarden.
- Streamingdiensten hebben verschillende contentcatalogi; vergelijk relevante
  kenmerken zoals reclame, beeldkwaliteit en gelijktijdige streams zonder te
  beweren dat het aanbod identiek is.
- Gebruik voor telecom snelheid, data/fair use, roaming, netwerkvoorkeur,
  apparaatverplichtingen, bundels en adresbeschikbaarheid als vergelijkingscriteria.
- Eerst een betrouwbare feed met toegestane toegang en hergebruik vaststellen.
  Netflix verbiedt geautomatiseerde scraping in zijn gebruiksvoorwaarden.
  Bestetarief publiceert een website-API, maar externe toegang en hergebruik zijn
  niet vastgesteld; geen sleutel van de website hergebruiken als integratiesleutel.
- BIPT biedt een proces om hergebruik van specifieke overheidsdata aan te vragen.
  Een algemeen open-databeleid is geen bevestiging dat elke tariefdataset vrij
  beschikbaar is. JustWatch heeft een partner-API, maar de dekking van actuele
  abonnementsprijzen per plan is nog niet vastgesteld.
- Geen externe aanvragen, aankopen of accounts namens de gebruiker zonder toestemming.
  Bewaar persoonlijke API-credentials lokaal en buiten financiële exports.
- Bij ontbrekende of verouderde gegevens geen actueel prijs- of besparingsadvies tonen.
  Bankboekingen en contractgegevens blijven onafhankelijk van een marktprijsupdate.

## Referenties

- [V-test — Vlaamse energiecontracten](https://vtest.vreg.be/)
- [Home Assistant Nord Pool](https://www.home-assistant.io/integrations/nordpool/)
- [Netflix-gebruiksvoorwaarden](https://help.netflix.com/nl/legal/termsofuse)
- [BIPT — hergebruik van overheidsdata](https://www.bipt.be/gebruikers/bipt/open-data)
- [JustWatch partner-API](https://www.justwatch.com/us/JustWatch-Streaming-API)

Besproken op 25 november 2026. Bronmogelijkheden en voorwaarden opnieuw controleren
bij uitvoering; bovenstaande tekst is geen toezegging dat een API beschikbaar is.
