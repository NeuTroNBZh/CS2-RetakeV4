# HudProbe — résultats du prototype HUD (phase 0)

Build testé : commit `<hash>` — Serveur : `<Dathost / local>` — CSSharp : `<version>` — Date : `<date>`

## Protocole
1. `dotnet build spikes/HudProbe/HudProbe.csproj -c Release`
2. Copier `spikes/HudProbe/bin/Release/net10.0/HudProbe.dll`, `HudProbe.deps.json`, `HudProbe.runtimeconfig.json` et `RetakeV4.Domain.dll` dans `addons/counterstrikesharp/plugins/HudProbe/`.
3. `css_plugins load HudProbe` (ou redémarrer la map).
4. En jeu, pendant le freeze time puis en round live : `css_hudprobe` (ouvre/ferme).
   - `css_hudprobe_orient 0|1|2` : formule d'orientation du texte (s'applique en direct).
   - `css_hudprobe_dist <unités>` : distance du menu (défaut 60).
   - `css_hudprobe_parent` : bascule le mode « parenté au pawn » (s'applique à la prochaine ouverture).

## Questions (répondre OUI/NON + remarques)
| # | Question | Réponse |
|---|---|---|
| 1 | Quelle formule d'orientation (`css_hudprobe_orient 0/1/2`) rend le texte lisible face au joueur ? | |
| 2 | Le menu ancré à l'ouverture suit-il la position du joueur sans tremblement gênant en marchant ? | |
| 3 | Précision viseur : sur 20 essais par ligne, combien de mauvaises sélections ? Distance confortable (`css_hudprobe_dist`) ? | |
| 4 | Le clic gauche sur une ligne visée ne tire PAS (freeze time) ? | |
| 5 | Le clic gauche sur une ligne visée ne tire PAS (round live, arme en main) ? | |
| 6 | Les touches 1-9 sélectionnent sans changer d'arme ? | |
| 7 | W/S déplacent le curseur et E valide pendant le freeze time ? | |
| 8 | Un second joueur ne voit PAS le menu (CheckTransmit) ? | |
| 9 | Le changement de couleur de la ligne visée est visible immédiatement ? | |
| 10 | `css_hudprobe_parent` puis ouverture : le menu suit-il le pawn de façon plus fluide que le repositionnement par tick ? | |
| 11 | Fin de round / restart avec menu ouvert : aucune erreur console, aucun crash ? | |
| 12 | Impact perf perçu (fps serveur/client) avec 2+ menus ouverts ? | |

## Décisions
- Sélection au viseur : **GO / NO-GO**
- Formule d'orientation retenue :
- Mode de positionnement retenu (tick / parent pawn) :
- Layout retenu (distance, hauteur de ligne, police, WorldUnitsPerPx) :
- Écarts d'API constatés :
