# Interface Web testable — 17 septembre 2026

## Installation du candidat r3

Le 17 septembre, `unity-candidate-20260917-web-fleet-catalog-r3` remplace le r2
pour tous les changements locaux, y compris la refonte Web, quantités entières,
propriétaires et transferts Fleet, catalogue sans dépôt ni texte répétitif.
New/Test ont réussi : 25 groupes, 96 fichiers préparés. Activation demandée et
réalisée jeu fermé ; comparaison officielle après installation : 95 SAME,
aucun écart sur les fichiers installés. Configuration candidate non appliquée,
réglages existants conservés. Manifest de déploiement `state: complete`.

Sauvegarde : `artifacts/unity-candidates/unity-candidate-20260917-web-fleet-catalog-r3/backups/activation-20260917-185223`.
Preuves : `artifacts/web-fleet-catalog-r3-build.log`,
`artifacts/web-fleet-catalog-r3-installed.csv` et manifeste du candidat.
Les mentions ci-dessous du r2 non installé sont historiques : r3 inclut les
ajustements et est installé. Aucun lancement ni essai de gameplay effectué ici.

## Ajustement après préparation du candidat

Le catalogue utilise des fiches par offre avec modèle, prix autoritaire et
achat sur la fiche (paiement personnel ou compagnie selon le descripteur hôte).
Recherche par modèle, tri nominal ou prix croissant/décroissant,
compteur et remise à zéro facilitent la comparaison. Le listingId exact relie
chaque achat ; sans action correspondante, la fiche reste non achetable. Les
identités techniques sont repliées, le paramétrage avancé reste en dessous.
Le dépôt est retiré : le matériel acheté rejoint la flotte pour être déployé.
Aperçu enrichi à cinq offres ; recherche/tri et largeur 390 px inspectés au navigateur.
28 tests frontend Management passent, build Management sans avertissement/erreur.
Ce changement est postérieur au candidat r2 : reconstruction avant installation.


Fleet expose désormais Owner Type (Player/Company), Owner et une action de transfert
sur la ligne concernée. Les actions autorisées viennent du host : personnel vers
sa compagnie, compagnie vers soi pour le responsable ou un délégué ManageFleet,
uniquement pour Available/Stored et hors liquidation. Le handler utilise
exclusivement l'identité authentifiée et sa compagnie comme destinataires ; le
moteur existant revalide permissions, état et versions. L'aperçu contient des
véhicules personnels et de compagnie avec transferts simulés. Vérifications :
27 tests frontend Management, 28 contrôles de présentation, 148 tests domaine,
build Full sans erreur/avertissement. Colonnes et boutons inspectés au navigateur ;
transferts réels Unity encore à qualifier. Ce changement est aussi postérieur au
candidat r2 et exige sa reconstruction avant installation.

Le champ **Planned cargo quantity** accepte désormais uniquement des entiers
strictement positifs (minimum 1, pas 1, contrôle avant envoi). Le bouton reprenant
la quantité embarquée refuse une valeur fractionnaire sans arrondir le cargo réel.
26 tests Management passent ; build Management sans erreur ni avertissement.
L'aperçu utilise déjà ce changement. Le candidat r2 ci-dessous est antérieur
à cet ajustement : le reconstruire avant de livrer cette correction dans le jeu.

## Périmètre livré

Le shell anthracite/ambre conserve Dispatch et Management comme destinations
distinctes. L'accueil présente leurs accès ; les sept rubriques actives restent
Companies, Wallets, Fleet, Catalog, Contracts, Industry et Diagnostics. Aucun
chantier voyageurs, dédié, location, financement ou triage n'est réactivé.

- Navigation de module et retour navigateur raccordés ; onglet Management
  conservé dans l'URL, bouton actif visible, menu mobile avec fermeture extérieure.
- Erreur de connexion visible, reconnexion explicite, attente bornée. Le shell
  ne partage plus son conteneur DOM avec les modules et ne démarre pas de vue
  métier avant réception de l'identité du shell.
- Statut Management visible sur desktop et mobile ; recherche nommée, états
  vides explicites, formulaires/champs/filtres conservés par rubrique et refresh.
- Captures concurrentes : seule la dernière réponse publie ; une vue quittée
  ne publie plus de statut. Les notifications ne déclenchent pas de snapshot.
- Une commande en vol ne peut pas être doublée par un nouveau clic. Une réponse
  inconnue garde son enveloppe pour Retry / reconcile. L'enveloppe en attente
  est conservée dans sessionStorage par origine et identité, même après reload ;
  seul un résultat terminal confirmé la retire. Le host garde toute autorité.
- Export diagnostics : les échecs sont affichés.

## Accès de développement

Depuis `src/BDVM.Web`, `bun run dev` écoute uniquement sur `127.0.0.1:5173`.
Le relais cible `http://localhost:18080` par défaut ; `BDVM_GAME_ORIGIN` permet
une autre cible. Les assets Web et Management proviennent des sources locales.
Les routes Dispatch, y compris trainset, junctionState et WebSocket, rejoignent
le host. L'origine entrante est contrôlée avant sa traduction vers celle du
host ; les requêtes cross-site sont refusées, l'authentification est conservée.
Les variables serveur `BDVM_GAME_USER` / `BDVM_GAME_PASSWORD` peuvent fournir
l'authentification existante sans être intégrées dans les assets navigateur.

- Interface réelle : <http://127.0.0.1:5173/>
- Aperçu explicite : <http://127.0.0.1:5173/management?preview=1>
- Site embarqué après installation et chargement du monde :
  <http://localhost:18080/management> et <http://localhost:18080/dispatch>.

L'aperçu est **un jeu de données simulées**, importé uniquement par l'entrée
de développement. Ses sept écrans ne représentent pas une sauvegarde réelle.
Le sélecteur permet Populated, Empty, Offline, Command refused, Version conflict
et Lost command response. Les créations de compagnie et renommages modifient
les fixtures en mémoire ; les autres réponses servent à tester la présentation.
Les données simulées sont réinitialisées au rechargement. Dispatch indique
explicitement qu'une carte et des commandes physiques exigent le host réel.
Le build embarqué n'inclut ni fixtures, ni barre d'aperçu, ni entrée dev.

## Vérifications effectuées

- `bun run check` : zéro erreur / zéro avertissement.
- Build des assets embarqués réussi.
- Tests Web : 8/8, dont relais local, routes Dispatch et refus d'origine étrangère.
- Tests Management : 25/25, dont ordre des réponses, abandon de vue, double clic,
  reprise de commande et restauration d'une enveloppe en attente.
- Navigateur réel : les sept rubriques remplies et vides ; création simulée de
  compagnie ; réponse perdue puis retry avec le même identifiant, une seule ligne ;
  refus de permission et conflit visibles ; aller Dispatch puis retour vers le
  bon onglet ; état hors ligne visible sans faux indicateur Live.
- Inspection visuelle desktop et largeur 390 px : rubriques et industrie lisibles,
  menu mobile ouvrable/fermable et statut visible.

## Campagne réelle à exécuter

Candidat final : `artifacts/unity-candidates/unity-candidate-20260917-web-testable-r2/`.
Préparation complète et `Test-UnityCandidate` réussis : **25 groupes, 96 fichiers**.
La comparaison officielle indique **17 DLL différentes et 78 fichiers runtime
identiques**, hors configuration alternative. Elle inclut les dépendances du lot
industriel précédent. Le candidat r1 de cette session est remplacé par r2.
**Candidat non installé ; jeu non lancé.** Les réglages installés sont conservés.
Journaux : `artifacts/web-testable-candidate-r2.log`,
`artifacts/web-testable-compare-r2.txt` et `evidence/logs/` dans le candidat.
Graphe agrégé reconstruit après les changements.

1. Installer le candidat final jeu fermé, sans appliquer sa configuration alternative.
2. Charger une carrière de test ; ouvrir le site réel, authentifier le compte et
   vérifier les deux destinations, le retour navigateur et les sept rubriques.
3. Compagnies : création/adhésion/permissions ; Wallets : transfert personnel et
   compagnie ; Fleet : renommage/transfert/vente ; Catalog : achat et radio de livraison.
4. Contracts : reprendre les wagons chargés de SM-B40, créer plusieurs dossiers,
   charger/décharger partiellement puis contrôler le paiement unique.
5. Industry : lecture des stocks ; Diagnostics : export ; Dispatch : tags,
   localisation, itinéraire et aiguillages avec les permissions appropriées.
6. Reconnexion, deuxième joueur, autosave/reload et mesure des temps de frame.

Ces étapes exigent Unity. Les fixtures, builds et tests automatisés ne prouvent
ni la physique, ni les paiements réels, ni le réseau à deux joueurs, ni la fluidité.
