# 🎯 Aimmy++ — Architecture Dual-PC & Injection Matérielle KMBox Net

> **Aimmy++ est un système d'assistance de visée par vision IA 100% externe (Dual-PC), basé sur capture vidéo matérielle, inférence ONNX DirectML, simulation biomécanique humaine et injection HID indétectable via KMBox Net.**

---

## 📑 Sommaire

1. [Origine & Ce qui a été forké (Quoi et d'où)](#1-origine--ce-qui-a-été-forké-quoi-et-doù)
2. [Historique des étapes & Liste des implémentations](#2-historique-des-étapes--liste-des-implémentations)
3. [Architecture Hardware & Composants requis (Dernière version)](#3-architecture-hardware--composants-requis-dernière-version)
4. [Liens et Références du Matériel](#4-liens-et-références-du-matériel)
5. [Comment fonctionne l'implémentation (Pipeline de bout en bout)](#5-comment-fonctionne-limplémentation-pipeline-de-bout-en-bout)
6. [Mitigations Comportementales V2 (Bypass Anti-Cheat)](#6-mitigations-comportementales-v2-bypass-anti-cheat)
7. [Calibration & Performances recommandées (Ryzen AI 9 365 / Radeon 880M)](#7-calibration--performances-recommandées-ryzen-ai-9-365--radeon-880m)
8. [Guide d'installation, Compilation & Démarrage](#8-guide-dinstallation-compilation--démarrage)
9. [Instructions de déploiement GitHub](#9-instructions-de-déploiement-github)

---

## 1. Origine & Ce qui a été forké (Quoi et d'où)

### A. Dépôt source principal
* **Projet d'origine** : [Babyhamsta/Aimmy](https://github.com/Babyhamsta/Aimmy) (Branche `Aimmy-V2`).
* **Nature du projet d'origine** : Application Windows C# WPF (.NET 8) mono-PC utilisant `DirectML`, `ONNX Runtime` et des modèles YOLOv8 pour la détection visuelle d'adversaires à l'écran, avec injection de mouvements souris via l'API Win32 (`mouse_event` / `SendInput`).

### B. Recherches comportementales & Références
* **Modélisation de trajectoire humaine** : [sarperavci/human_mouse](https://github.com/sarperavci/human_mouse).
* **Apports & Analyse** : Étude des interpolations B-splines et mise en évidence des failles des générateurs pseudo-aléatoires classiques (PRNG) face aux analyseurs statistiques des anti-cheats modernes (EAC, Vanguard, BattlEye).

### C. Ce qui a été conservé de la base
* Le moteur d'inférence ONNX Runtime avec accélération DirectML (`Aimmy2/AILogic/AIManager.cs`).
* L'interface graphique WPF de base (menus de configuration, styles, thèmes, gestionnaire de profils).
* La persistance des paramètres dans le dictionnaire applicatif (`Aimmy2/Class/Dictionary.cs`).
* Les filtres de prédiction mathématique (Filtre de Kalman, algorithmes WiseTheFox et Shalloe).

### D. Ce qui a été éliminé ou remplacé
* ❌ **Éliminé : Capture d'écran locale (DirectX ScreenGrab / Desktop Duplication)** : Cette méthode tournait sur le même PC que le jeu et laissait des traces mémoire / contextes DirectX repérables. Remplacée par une capture vidéo 100% externe via carte d'acquisition USB.
* ❌ **Éliminé : Injection souris logicielle Windows (`mouse_event`)** : Les flags synthétiques `LLMHF_INJECTED` générés par Windows sont immédiatement détectés par les anti-cheats au niveau noyau (Ring 0).
* ❌ **Éliminé (Legacy) : Arduino Leonardo USB Serial** : Utilisé dans les premières phases de développement, l'Arduino souffre de limitations critiques (descripteurs USB génériques identifiables par l'anti-cheat, latence et saturation du port série COM virtuel).
* ✅ **Ajouté : Boîtier matériel dédié KMBox Net** piloté en réseau local TCP/IP avec clonage complet des descripteurs USB de la souris physique du joueur.
* ✅ **Ajouté : Pipeline de calibration multi-résolution 4 étapes** (`ScalingCalculator.cs`).
* ✅ **Ajouté : Suite complète de mitigations biomécaniques V2** (bruit physique Pixart 1/f, loi de Fitts, cycle overshoot/pause/correction, déformation log-normale des heatmaps).

---

## 2. Historique des étapes & Liste des implémentations

Le développement du projet a progressé à travers plusieurs phases majeures :

```
[Phase 1 : Arduino Dual-PC] ──► [Phase 2 : Capture Matérielle] ──► [Phase 3 : Triple-Résolution]
           │
           ▼
[Phase 4 : Anti-Cheat Analysis] ──► [Phase 5 : Behavioral V2] ──► [Phase 6 : KMBox Net Final]
```

### Étape 1 : Architecture Dual-PC initiale avec Arduino Leonardo (Legacy)
* Déportation de la logique de calcul sur un deuxième ordinateur (**PC B - Inférence**).
* Hook souris global basse latence (`Aimmy2/External/MouseHooker.cs` via `WH_MOUSE_LL`).
* Communication série USB à 500 000 bauds (`Aimmy2/External/ArduinoSerial.cs`).
* Firmware Arduino custom (`Arduino/AimAssist_Leonardo_DualStream.ino`) recevant les mouvements utilisateurs (`U,X,Y`) et les corrections IA (`A,X,Y`) sur microcontrôleur ATmega32u4.

### Étape 2 : Capture vidéo DirectShow et OpenCV
* Remplacement de la capture logicielle d'écran par un flux vidéo externe.
* Création de `Aimmy2/External/CaptureDeviceEnumerator.cs` : énumération native DirectShow (pure Windows API COM, 0 dépendance externe) capable d'isoler les cartes d'acquisition USB des webcams intégrées et d'extraire leur VID/PID.
* Création de `Aimmy2/External/VideoCaptureManager.cs` : moteur de capture OpenCV multithreadé à 60 FPS avec modèle Producteur-Consommateur (file FIFO non bloquante limitée à 3 frames avec politique de drop automatique des frames périmées pour maintenir une latence minimale).

### Étape 3 : Système de calibration multi-résolution 4 étapes
* Création de `Aimmy2/External/ScalingCalculator.cs` pour résoudre le problème fondamental du Dual-PC où 3 résolutions coexistent :
  1. **Résolution de capture** (ex. 1280×720 à 60 FPS).
  2. **Résolution en jeu sur PC A** (ex. 1920×1080, 2560×1440 ou format 4:3 étiré).
  3. **Résolution native de l'écran PC A** (ex. 1080p, 1440p, 4K).
* Implémentation de `Aimmy2/VideoPreviewWindow.xaml` : fenêtre de prévisualisation vidéo en temps réel avec calque de rendu pour le cercle FOV et les boîtes de détection ESP.
* Ajout des sélecteurs de résolutions et champs de saisie manuelle dans `SettingsMenuControl.xaml`.

### Étape 4 : Analyse anti-détection (EAC / Vanguard) & Abandon d'Arduino
* **Le problème Arduino** :
  * Descripteurs USB génériques : VID `0x2341` / PID `0x8036` de l'Arduino Leonardo facilement blacklistés.
  * Impossibilité de cloner le firmware d'une souris de jeu (firmware signé et chiffré par Logitech/Razer).
  * Windows Device Manager liste un port série virtuel suspect.
* **La solution KMBox Net** :
  * Matériel dédié ARM/FPGA conçu spécifiquement pour le passthrough et le spoofing HID.
  * Port USB-A pour brancher la vraie souris de jeu (Logitech / Razer).
  * Port USB-B vers PC A émulant une souris matérielle 1:1 via clonage des descripteurs USB (VID, PID, chaînes fabricant, HID Report Descriptor hex).
  * Port Ethernet RJ45 recevant les commandes IA du PC B sans passer par un port série USB.

### Étape 5 : Suite des Mitigations Comportementales V2
Mise en place de 4 modules biomécaniques dans `Aimmy2/AILogic/Behavioral/` pour contrer l'analyse statistique côté serveur :
1. **`SensorNoiseInjector.cs`** : Génération d'un bruit corrélé temporellement (dérive *random walk* simulant les micro-imperfections du tapis et du capteur Pixart), tremblement physiologique involontaire à 8-12 Hz, couplage diagonal X/Y et force de rappel élastique.
2. **`HumanReactionSimulator.cs`** : Modélisation du temps de réaction biologique via la **loi de Fitts** ($MT = a + b \cdot \log_2(2D/W)$) combinée à une distribution gaussienne (plage réaliste 100-400 ms avec présence d'outliers naturels).
3. **`HumanAimingModel.cs`** : Modèle de visée en 5 phases réalistes (`Approach` ➔ `Overshoot` naturel proportionnel à la distance ➔ `Pause` de réévaluation cérébrale de 50-100 ms ➔ `Correct` contre-réaction ➔ `Track`).
4. **`AimBiasProfile.cs`** : Simulation de l'asymétrie individuelle du joueur par distribution log-normale (évite la dispersion gaussienne trop parfaite propre aux bots).
5. Conservation de **`MovementMerger.cs`** (variance 60/30/10) et **`PredictionManager.cs`** (filtre de Kalman avec jitter temporel).

### Étape 6 : Protocole KMNet, Client Réseau & Interface
* Création de `Aimmy2/External/KMNetProtocol.cs` : définition des opcodes (`MouseMove`, `MouseClick`, `KeepAlive`) et structure du paquet binaire réseau de 7 octets.
* Création de `Aimmy2/External/KMNetClient.cs` : client TCP/IP asynchrone non-bloquant avec reconnexion automatique.
* Adaptation de `Aimmy2/InputLogic/MouseManagerDualPC.cs` : transmission directe des corrections IA via KMNet. La souris utilisateur est gérée directement en passthrough transparent par le KMBox Net.
* Intégration dans l'interface `SettingsMenuControl.xaml.cs` (champs IP KMBox, Port, bouton "Connect KMBox") et persistance dans `Dictionary.cs`.

---

## 3. Architecture Hardware & Composants requis (Dernière version)

Le système repose sur une séparation matérielle stricte entre la machine de jeu (**PC A**) et la machine de calcul IA (**PC B**).

```
 ┌─────────────────────────────────────────────────────────────┐
 │                    PC A — JEU (Gaming PC)                   │
 │                     (ex. Apex Legends)                      │
 └──────────────┬──────────────────────────────▲───────────────┘
                │ HDMI (Vidéo du jeu)           │ USB-B (HID Souris clonée)
                ▼                               │
 ┌─────────────────────────────┐                │
 │    Carte d'Acquisition      │                │
 │       USB 3.0 HDMI          │                │
 └──────────────┬──────────────┘                │
                │ Flux vidéo UVC (USB 3.0)      │
                ▼                               │
 ┌────────────────────────────────────────┐     │
 │        PC B — DÉTECTION IA (AI PC)     │     │
 │    (Ryzen AI 9 365 + Radeon 880M)      │     │
 │  • VideoCaptureManager (OpenCV 60 FPS) │     │
 │  • Inférence ONNX DirectML (416×416)   │     │
 │  • Behavioral Mitigations V2           │     │
 │  • KMNetClient (Client TCP/IP)         │     │
 └──────────────────┬─────────────────────┘     │
                    │ Ethernet RJ45 (Commandes) │
                    ▼                           │
       ┌────────────────────────────┐           │
       │         KMBOX NET          ├───────────┘
       │     (Contrôleur HID ARM)   │
       └────────────▲───────────────┘
                    │ USB-A
                    │
       ┌────────────┴───────────────┐
       │   Souris Physique Joueur   │
       │ (Logitech / Razer / etc.)  │
       └────────────────────────────┘
```

### Rôle et Comportement des Équipements

1. **PC A (PC de Jeu)** :
   * Exécute le jeu et le système anti-cheat (Easy Anti-Cheat / Vanguard / BattlEye).
   * Ne contient **aucun exécutable, aucun hook, aucun driver suspect, aucune injection**.
   * Voit **une seule et unique souris branchée en USB** (le KMBox Net ayant copié son VID/PID et ses descripteurs).
2. **PC B (PC Détection IA)** :
   * Reçoit le flux vidéo via la carte d'acquisition USB en arrière-plan.
   * Exécute Aimmy V2 avec DirectML et le modèle YOLOv8.
   * Calcule les corrections de trajectoire et les envoie via Ethernet TCP/IP vers le KMBox Net.
3. **KMBox Net** :
   * Reçoit les mouvements de votre vraie souris sur son port **USB-A**.
   * Reçoit les deltas de correction IA via son port **Ethernet RJ45**.
   * Fusionne matériellement les deux flux et transmet le rapport résultant au **PC A** via son port **USB-B**.

---

## 4. Liens et Références du Matériel

Voici la liste des composants matériels nécessaires pour finaliser l'installation :

| Composant | Spécifications Recommandées | Estimation Prix | Liens / Références |
|---|---|---|---|
| **KMBox Net** | Modèle **KMBox Net** avec écran OLED (processeur ARM, double port USB 2.1, port Ethernet RJ45 100M). Compatible protocole KMNet B+ Pro. | ~50 - 55 $ | • [AliExpress — KMBox Net Officiel](https://www.aliexpress.com/w/wholesale-kmbox-net.html)<br>• Boutiques spécialisées d'accessoires HID DMA |
| **Carte d'acquisition USB HDMI** | Entrée HDMI 1080p60 / 4K30, sortie USB 3.0 UVC non compressée (puce MS2130, Ezcap Cam Link ou équivalent). Prise en charge DirectShow native sans driver. | ~15 - 45 € | • [Amazon — Carte d'acquisition USB 3.0 MS2130](https://www.amazon.fr/s?k=capture+card+usb+3.0+ms2130)<br>• [Elgato Cam Link 4K](https://www.amazon.fr/s?k=elgato+cam+link+4k) |
| **Splitter HDMI Passthrough (Optionnel)** | Nécessaire si le GPU de votre PC A n'a pas de seconde sortie HDMI/DP dupliquée. Support 4K@60Hz ou 1080p@120/144Hz Passthrough avec gestion EDID (ex: Ezcoo). | ~25 - 40 € | • [Amazon — Splitter HDMI 2.0 Ezcoo 144Hz Passthrough](https://www.amazon.fr/s?k=ezcoo+hdmi+splitter) |
| **Câble Réseau Ethernet** | Câble RJ45 Cat 6 / Cat 5e (reliant le KMBox Net à votre routeur, switch ou port Ethernet du PC B). | ~5 € | • Câble Ethernet RJ45 standard |
| **Câbles USB** | 2× Câbles USB 2.0 / USB-C de qualité pour liaison PC A et alimentation KMBox (généralement inclus avec le boîtier). | Inclus | • Inclus avec le KMBox Net |
| **Souris Physique de Jeu** | Souris légitime dont les descripteurs seront clonés (Logitech G Pro X Superlight, Razer DeathAdder, etc.). | Possédée | • Pilotes officiels (Logitech G HUB ou Razer Synapse) installés sur PC A |
| **Outil de dump USB (Gratuit)** | **USBTreeView** pour inspecter et extraire le HID Report Descriptor de votre souris. | Gratuit | • [Télécharger USBTreeView (Uwe Sieber)](https://www.uwe-sieber.de/usbtreeview_e.html) |

---

## 5. Comment fonctionne l'implémentation (Pipeline de bout en bout)

### Chronologie d'une Frame (Budget de latence : ~30-33 ms)

Pour chaque image traitée, le cycle d'exécution respecte le budget suivant :

```
T+00 ms ───► Génération de l'image par le PC A
T+05 ms ───► Capture par la carte d'acquisition USB & Réception dans VideoCaptureManager
T+08 ms ───► Prétraitement CPU (Redimensionnement 416×416 + Normalisation des flottants)
T+28 ms ───► Inférence ONNX DirectML sur le GPU (AMD Radeon 880M : 20 ms)
T+30 ms ───► Post-traitement (Filtrage de confiance, FOV, NMS & Sélection de la cible)
T+31 ms ───► Calcul de mise à l'échelle (ScalingCalculator) & Pipeline biomécanique V2
T+32 ms ───► Envoi du paquet TCP/IP KMNet vers le KMBox Net (< 1 ms)
T+33 ms ───► Injection matérielle USB HID sur le PC A
```

### Le Rôle de chaque Composant Logiciel

```
      VideoCaptureManager (Capture USB 60 FPS)
                 │
                 ▼
      AIManager.AiLoop (ONNX YOLOv8 @ 416×416)
                 │
                 ▼
      ScalingCalculator (Mapping 4 étapes Capture/Jeu/Écran)
                 │
                 ▼
      HumanAimingModel (Phases : Approche ➔ Overshoot ➔ Pause ➔ Correction)
                 │
                 ▼
      AimBiasProfile (Biais individuel log-normal)
                 │
                 ▼
      MovementMerger (Distribution de variance 60/30/10)
                 │
                 ▼
      SensorNoiseInjector (Bruit capteur 1/f Pixart + Tremblement 8-12 Hz)
                 │
                 ▼
      HumanReactionSimulator (Loi de Fitts + Distribution gaussienne)
                 │
                 ▼
      PredictionManager (Filtre de Kalman + Jitter temporel)
                 │
                 ▼
      KMNetClient.SendMovementAsync (Packet 7 octets TCP/IP)
                 │
                 ▼
      KMBox Net (Fusion matérielle ➔ Injection USB-B sur PC A)
```

---

## 6. Mitigations Comportementales V2 (Bypass Anti-Cheat)

Les anti-cheats compétitifs (EAC sur Apex Legends/Rust, Vanguard sur Valorant) analysent la cinématique des inputs souris. Une trajectoire calculée par ordinateur possède une signature mathématique évidente :
* Absence de tremblement musculaire.
* Décélération linéaire ou Bézier trop fluide.
* Absence d'overshoot naturel (dépassement de la cible).
* Entropie artificielle générée par un PRNG (bruit blanc décorrélé).

### Matrice des Risques : Comparatif des Protections

| Vecteur d'Analyse | Solution Logicielle Seule | Avec Arduino | Avec KMBox Net & Behavioral V2 |
|---|:---:|:---:|:---:|
| **Descripteurs USB** | 🔴 Détection immédiate | 🔴 Très élevé (VID/PID générique) | 🟢 **Indétectable (Clonage 1:1)** |
| **Linéarité du mouvement** | 🔴 Élevé | 🟡 Moyen (Variance simple) | 🟢 **Très faible (Courbes biomécaniques)** |
| **Corrélation Cible-Mouvement** | 🔴 Critique (Réaction robot) | 🟡 Moyen (Délai fixe) | 🟢 **Faible (Loi de Fitts + Gaussienne)** |
| **Spectre du bruit capteur** | 🔴 Élevé | 🔴 Élevé (Bruit blanc PRNG) | 🟢 **Faible (Bruit 1/f corrélé Pixart)** |
| **Overshoot & Micro-corrections** | 🔴 Élevé (Convergence parfaite) | 🔴 Élevé | 🟢 **Très faible (Modèle 5 phases)** |
| **Heatmap d'impact** | 🔴 Centre parfait | 🟡 Gaussienne symétrique | 🟢 **Faible (Biais asymétrique log-normal)** |

---

## 7. Calibration & Performances recommandées (Ryzen AI 9 365 / Radeon 880M)

### Benchmarks sur la Machine de Test

Tests réalisés avec le processeur **AMD Ryzen AI 9 365** et le GPU intégré **AMD Radeon 880M** (architecture RDNA 3.5, 32 Go RAM) :

| Résolution Modèle IA | Temps Inférence GPU | Temps Total Cycle | FPS Effectif | Verdict & Recommandation |
|:---:|:---:|:---:|:---:|:---:|
| **640 × 640** | 50 - 55 ms | 64 - 69 ms | ~14 FPS | ❌ **Non recommandé** (trop lent, lag perceptible) |
| **512 × 512** | 32 - 38 ms | 46 - 52 ms | ~20 FPS | 🟡 **Acceptable** (bonne qualité visuelle) |
| **416 × 416** ⭐ | **20 - 22 ms** | **28 - 33 ms** | **~30 FPS** | ✅ **RECOMMANDÉ** (Fluidité optimale, 0€ de coût) |
| **320 × 320** | 13 - 15 ms | 24 - 26 ms | ~38 FPS | ⚡ **Performance Max** (peut être pixelisé de loin) |

> [!TIP]
> **Configuration Recommandée** : Réglez **Image Size** sur **`416`** dans l'onglet Settings. Vous obtiendrez une latence d'inférence de **20 ms**, assurant un aim assist réactif et naturel sans nécessiter l'achat d'un GPU externe dédié.

---

## 8. Guide d'installation, Compilation & Démarrage

### Prérequis sur le PC B (AI PC)
* **Système d'exploitation** : Windows 10 ou 11 (64-bit).
* **.NET 8 SDK** installé ([Télécharger .NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)).
* **Pilotes graphiques AMD / NVIDIA** à jour.
* **Visual Studio 2022** (avec la charge de travail *Développement .NET Desktop*).

### Compilation du Projet
Ouvrez un terminal PowerShell dans ce répertoire et lancez :

```powershell
# Restauration des packages NuGet et compilation
dotnet restore Aimmy2/Aimmy2.csproj
dotnet build Aimmy2/Aimmy2.csproj -c Release
```

Le binaire exécutable sera généré dans `Aimmy2/bin/Release/net8.0-windows/Aimmy2.exe`.

### Configuration Initiale dans l'Interface
1. Lancez **Aimmy2.exe** sur le **PC B**.
2. **Screen Settings** :
   * Sélectionnez la carte d'acquisition vidéo dans la liste déroulante DirectShow.
   * Renseignez l'IP locale du KMBox Net (ex. `192.168.1.100`) et le port (défaut : `6721`).
   * Cliquez sur **Connect KMBox**.
3. **Resolution Calibration** :
   * Indiquez la résolution de la carte de capture (ex. `1280` × `720`).
   * Indiquez la résolution de rendu du jeu sur PC A (ex. `1920` × `1080`).
   * Indiquez la résolution native du moniteur PC A (ex. `1920` × `1080`).
   * Cliquez sur **Apply Calibration**.
4. **Aim Config** :
   * Activez les options comportementales : *Hit Distribution Variance*, *Reaction Delay*, *Lead Time Variance*.
   * Chargez le modèle ONNX optimisé pour votre jeu (ex. Apex Legends).

---

## 9. Instructions de déploiement GitHub

Ce répertoire a été initialisé proprement pour votre compte GitHub **HUGMAHE**.

> [!NOTE]
> GitHub n'autorisant pas les caractères spéciaux `+` dans les noms de dépôts (uniquement lettres, chiffres, tirets et points), le nom de dépôt recommandé sur GitHub est **`AimmyPlusPlus`** (ou `Aimmy-PlusPlus`). Le projet et le dossier local s'appellent quant à eux **`Aimmy++`**.

Pour publier ce projet sur votre compte GitHub :

1. Rendez-vous sur GitHub et créez un nouveau dépôt vide nommé **`AimmyPlusPlus`** (ou `Aimmy-PlusPlus`) sur votre compte **HUGMAHE** (laissez les cases README, .gitignore et licence décochées).
2. Dans PowerShell ou un terminal Git, exécutez simplement :

```bash
cd "C:\Users\sobyd\Documents\DEV\Aimmy++"

# Le remote est déjà configuré, sinon :
# git remote add origin https://github.com/HUGMAHE/AimmyPlusPlus.git

# Pousser le code vers GitHub
git push -u origin main
```

---

## 📄 Licence & Mentions Légales

Ce projet est distribué sous licence Source-Available à des fins de recherche, d'accessibilité et d'étude de la vision par ordinateur. Consultez le fichier [LICENSE](LICENSE) pour plus de détails. Aimmy est une marque et un projet original développé par BabyHamsta.
