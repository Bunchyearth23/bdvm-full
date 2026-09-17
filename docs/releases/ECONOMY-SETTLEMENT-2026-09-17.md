# Corrections économie et contrats — 17 septembre 2026

## Symptôme observé

Player.log indique le contrat `stock-transport:remote-industry-stock-start:d5ccb6d1-74e6-4c9a-9a26-b70d5702e88c`,
chargement confirmé puis livraison et reprise de production confirmées. Entre les
deux dernières notifications, `wallet-sync` réimporte 2000 depuis le jeu
(`source=before-economic-clock`, lignes 2928–2946 du journal observé).
L'ancien chemin créditait le compte BDVM, puis écrasait son solde avec celui du
jeu sans y exporter la recette. Le salaire natif du job SelfShunt reste nul pour
éviter un second versement : le montant affiché est celui du contrat BDVM.

## Correctif du règlement

Le portefeuille hôte utilise désormais le miroir bidirectionnel existant :
base commune persistée, import des changements externes, export des changements
internes, refus des changements divergents simultanés. Le cycle SelfShunt
synchronise avant mutation, invalide la projection, applique l'observation,
stage le checkpoint et synchronise après. Les bénéficiaires distants utilisent
le miroir Multiplayer existant ; les revenus de compagnie restent en compagnie.
Une reprise après crédit natif observe les soldes égaux et ne crédite pas de nouveau.
Le staging reste distinct d'une sauvegarde disque réussie.

La récupération au chargement est conservatrice : une suite de crédits
IndustrialRevenue suivie, sans autre opération du même compte, d'un débit
ExternalWalletSync de même montant et provenant de before-economic-clock ou
periodic-vanilla-observation est remboursée. Le reçu comptable persistant empêche
un second remboursement. L'historique ambigu n'est pas reconstitué arbitrairement.
Cette réparation ne revalorise pas les anciens contrats au nouveau tarif.

## Nouveau barème

Les nouveaux contrats manuels utilisent une politique versionnée v2, sans toucher
les devis des contrats existants. Base par chargement complet : 65 % de la formule
moyenne SelfShunt Direct Haul (500 de composante moyenne + distance et propriétés
du cargo), plancher de service 1000, multiplicateur économique du jeu appliqué,
arrondi supérieur à 50. Aucun tirage aléatoire. La modulation industrielle de
stock existante reste appliquée : le plancher est une base, pas un minimum net
universel. Les devis sont proportionnels à la quantité réelle, sans dilution
par la capacité de l'entrepôt et sans prime fixe exploitable par découpage.
Ce réglage est un premier équilibrage, pas une mesure de rentabilité en jeu.

Catalogue : DE2 40000, DM3 65000, DH4 90000, DE6 180000, S060 55000,
S282 120000, BE2 50000, microshunter 20000. Wagons selon famille : base 12000,
boxcar 15000, hopper 18000, tank 20000, refrigerated 22000, passenger 25000,
caboose 8000. Locomotive personnalisée non reconnue : repli explicite 100000.
Les modèles personnalisés nécessitent encore un calibrage propre si ce repli
ne convient pas. Variation des nouvelles bases limitée à 0.9–1.1.
Migration des entrées correspondant exactement aux anciens paramètres par défaut ;
les valeurs personnalisées différentes restent conservées. Une personnalisation
identique aux anciens défauts ne peut pas être distinguée faute de provenance
historique. Seules les offres de matériel neuf encore disponibles sont réévaluées ;
les offres réservées, paiements en cours et achats conclus restent figés.

## Interface

Livret : titre limité à 16 caractères route + suffixe ; ID complet conservé pour
corrélation et sauvegarde. Contracts Web : sélection ouverte, recherche nom/ID/voie,
wagons non sélectionnables visibles avec motif. Catalogue sans dépôt ni texte
répétitif Added to your fleet. Quantités entières et transferts Fleet conservés.

## Validation

Tests ciblés : 150 tests domaine et 63 contrôles industriels ; tests Web compris
dans la préparation complète. Les cas ajoutés couvrent crédit effacé, reçu après
reload, nouvel essai après crédit natif, conflit de soldes, historique ambigu,
prix déterministes, migration des offres disponibles et protection des réservées,
petit chargement et devis stable au rejeu. Build Full Release sans erreur.
La validation automatisée ne prouve pas le règlement réel de la mission sur la
sauvegarde du joueur ni la rentabilité, les frames Unity ou le host/client réel.

## Déploiement

Candidat `unity-candidate-20260917-economy-settlement-r4` préparé et installé
avec autorisation explicite, jeu fermé. 25 groupes de préparation validés,
96 fichiers préparés ; 95 fichiers sélectionnés hors configuration candidate,
95 SAME après activation. Réglages installés conservés.
Sauvegarde : `artifacts/unity-candidates/unity-candidate-20260917-economy-settlement-r4/backups/activation-20260917-194734`.
Preuves : `artifacts/economy-settlement-r4-build.log`,
`artifacts/r4-industrial-tests.log`, `artifacts/economy-settlement-r4-installed.csv`.
Les domaines sont liés dans BDVM.Full.dll ; les deux binaires modifiés à installer
sont BDVM.Full.dll et BDVM.Management.dll. Jeu non lancé par l'agent.
