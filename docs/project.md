# Inkhound — Brief projet

> Pipeline self-hosted de gestion de bibliothèque digitale pour BD, Comics et Manga.  
> De la découverte d'un titre jusqu'à une arborescence propre et lisible par [Kavita](https://www.kavitareader.com/).

![Logo Inkhound](favicon-inkhound.png)

---

## Sommaire

1. [Contexte & vision](#1-contexte--vision)
   - [Principe fondamental](#principe-fondamental)
2. [Identité du projet](#2-identité-du-projet)
3. [Stack technique](#3-stack-technique)
4. [Entités](#4-entités)
   - [4.1 L'entité Issue](#41-lentité-issue-le-numéro)
   - [4.2 L'entité Volume](#42-lentité-volume)
   - [4.3 Tableau Comparatif](#43-tableau-comparatif)
5. [Flux global de l'application](#5-flux-global-de-lapplication)
6. [Fonctionnalités prévues](#6-fonctionnalités-prévues)
   - [6.1 Catalogue & Recherche](#61-catalogue--recherche-détaillé-section-7)
   - [6.2 Acquisition & Download Manager](#62-acquisition--download-manager-détaillé-section-8)
   - [6.3 Traitement](#63-traitement--normalisation-conversion-et-enrichissement-détaillé-section-9)
   - [6.4 Export & Sync vers Kavita](#64-export--sync-vers-kavita-détaillé-section-9)
7. [Fonctionnalité — Catalogue & Recherche](#7-fonctionnalité--catalogue--recherche-section-61)
   - [7.1 Vue d'ensemble](#71-vue-densemble)
   - [7.2 Modèle de données](#72-modèle-de-données)
   - [7.3 Flux d'ajout d'un Volume via ComicVine](#73-flux-dajout-dun-volume-via-comicvine)
8. [Fonctionnalité — Acquisition & Download Manager](#8-fonctionnalité--acquisition--download-manager-section-62)
   - [8.1 Vue d'ensemble](#81-vue-densemble)
   - [8.2 Chemin A — Recherche automatique via indexeur](#82-chemin-a--recherche-automatique-via-indexeur)
   - [8.3 Chemin B — Import manuel](#83-chemin-b--import-manuel)
9. [Fonctionnalité — Traitement](#9-fonctionnalité--traitement-sections-63-et-64)
   - [9.1 Dossiers](#91-dossiers)
   - [9.2 Détails du traitement](#92-détails-du-traitement)
   - [9.3 Nommage et arborescence](#93-nommage-et-arborescence)
   - [9.4 Structure d'un CBZ final](#94-structure-dun-cbz-final)
   - [9.5 Génération du ComicInfo.xml](#95-génération-du-comicinfoxxml)
   - [9.6 Export & Sync vers Kavita](#96-export--sync-vers-kavita)
10. [Services tiers utilisés](#10-services-tiers-utilisés)

---

## 1. Contexte & vision

**Inkhound** est une application web self-hosted qui automatise l'ensemble du cycle de vie d'une collection de BD, Comics et Manga numériques :

```
Titre souhaité  →  Recherche source externe  →  Acquisition  →  Formatage  →  Kavita
```

L'utilisateur exprime ce qu'il veut lire. Inkhound s'occupe du reste.

### Principe fondamental

L'idée centrale est de **déclarer des intentions** plutôt que de gérer des fichiers. L'utilisateur indique les séries qu'il souhaite avoir dans sa bibliothèque — en s'appuyant sur une source externe de référence comme **ComicVine** — et Inkhound prend en charge :

1. La **recherche automatique** des fichiers via des indexeurs torrent
2. Le **téléchargement** des issues manquantes (ou l'**import manuel** de fichiers existants)
3. La **normalisation** des fichiers (format, nommage)
4. L'**enrichissement** des métadonnées (génération du `ComicInfo.xml`)
5. La **construction de l'arborescence** compatible Kavita

Inkhound ne remplace pas Kavita — il l'alimente. Le lecteur reste Kavita ; Inkhound est le pipeline en amont.

---

## 2. Identité du projet

| | |
|---|---|
| **Nom** | Inkhound |
| **Logo** | Chien courant avec une plume de stylo — vitesse d'acquisition + écriture de métadonnées |
| **Inspiration** | Sonarr / Radarr — mais dédié BD, Comics, Manga |
| **Type** | Application web self-hosted, containerisée (Docker) |

---

## 3. Stack technique

Elle est basé sur le fichier :

[architecture](architecture.md)

Le codage de la partie métier (dédié au fonctionnement interne de InkHound) côté backend se fera dans un projet library dotnet séparé du projet Backend. Il respectera toutes les conventions de codage définit sur le projet Backend.

---

## 4. Entités

### 4.1 L'entité Issue (Le Numéro)
L'Issue représente l'unité de base, l'objet physique ou numérique que vous lisez. C’est un numéro unique au sein d'une série.

- Contenu : Elle contient les détails spécifiques à cette parution précise (le titre du chapitre, le résumé de l'histoire, la date de publication, le prix d'origine).
- Crédits : C'est ici que l'on trouve les contributeurs exacts du numéro (scénariste, dessinateur, encreur, coloriste).
- Apparitions : Elle répertorie les personnages, les équipes et les lieux qui apparaissent réellement dans ces pages.
- Numérotation : La numérotation est un point crucial, car l'identifiant de Volume + la numérotation doivent être unique. Exemple : "The Amazing Spider-Man 121" ou "Lucky Lucke T.03"

### 4.2 L'entité Volume
C'est le cadre qui regroupe les issues.

- Identité : Il définit le titre global de la série, l'éditeur (Marvel, DC, etc.) et l'année de lancement.
- Structure : Un volume sert de "dossier" parent. Il indique combien de numéros sont prévus ou ont été publiés dans cette itération spécifique.
- Distinction cruciale : Un volume s'étale sur une période du premier numéro d'Issue au dernier. On utilisera surtout la date de début et une date de fin si la série est terminée.

### 4.3 Tableau Comparatif



| Caractéristique | Volume | Issue (Numéro) |
|---|---|---|
| `Rôle` | Le contenant / La collection | L'unité / L'exemplaire |
| `Temporalité` | Définit une période (ex: 2011-2016) | Définit un mois précis (ex: Mai 2012) correspondant à la date de publication |
| `Granularité` | Informations générales sur la série | Crédits détaillés et résumé de l'intrigue |
| `Lien` | Possède plusieurs Issues | Appartient à un seul Volume |

## 5. Flux global de l'application

```
┌─────────────────────────────────────────────────────────────┐
│                        INKHOUND                             │
│                                                             │
│  1. Catalogue    →   L'utilisateur déclare les séries       │
│     & Recherche      souhaitées via ComicVine               │
│                                                             │
│  2. Acquisition  →   Recherche via indexeurs torrent        │
│                      OU import manuel de fichiers           │
│                                                             │
│  3. Traitement   →   Normalisation CBZ, renommage,          │
│                      génération ComicInfo.xml               │
│                                                             │
│  4. Export       →   Dépôt dans l'arborescence Kavita,      │
│                      déclenchement du scan                  │
└─────────────────────────────────────────────────────────────┘
                              ↓
                    ┌─────────────────┐
                    │     KAVITA      │
                    │  (lecture seule)│
                    └─────────────────┘
```

---

## 6. Fonctionnalités prévues

### 6.1 Catalogue & Recherche *(détaillé section 7)*
Gestion de la liste de séries souhaitées, adossée à ComicVine comme source de référence.

### 6.2 Acquisition & Download Manager *(détaillé section 8)*
Recherche automatique des issues manquantes via des indexeurs torrent (Prowlarr). Suivi de l'état des téléchargements. Import manuel de fichiers en alternative.

### 6.3 Traitement — Normalisation, Conversion et Enrichissement *(détaillé section 9)*
Conversion de tout fichier entrant (CBR, ZIP, dossier d'images) en CBZ. Renommage selon les conventions Kavita.
Génération et injection du `ComicInfo.xml` dans chaque CBZ à partir des données ComicVine. Régénération à la demande.

### 6.4 Export & Sync vers Kavita *(détaillé section 9)*
Dépôt des fichiers dans l'arborescence de la librairie. Notification à Kavita via son API REST pour déclencher le scan.


---

## 7. Fonctionnalité — Catalogue & Recherche *(section 6.1)*

### 7.1 Vue d'ensemble

Le catalogue est le point d'entrée de l'application. L'utilisateur y déclare les séries qu'il souhaite avoir dans sa bibliothèque. Chaque série ajoutée est immédiatement mise en surveillance : Inkhound va chercher les issues manquantes et les télécharger.

L'ajout d'une série se fait via l'API **ComicVine**, ce qui garantit des métadonnées riches et des identifiants stables qui serviront tout au long du pipeline.

---

### 7.2 Modèle de données

#### Entité `LIBRARY`

Représente un dossier racine Kavita. Chaque librairie correspond à un répertoire physique sur le serveur, configuré comme source dans Kavita.

| Champ | Type | Description |
|---|---|---|
| `id` | UUID PK | Identifiant interne |
| `name` | string | Nom affiché (ex. "Comics DC") |
| `root_path` | string | Chemin absolu sur le serveur |
| `kavita_folder` | string | Chemin du dossier surveillé par Kavita |
| `created_at` | timestamp | Date de création |

---

#### Entité `VOLUME`

Représente un Volume. Toujours associé à une `LIBRARY`.

| Champ | Type | Description |
|---|---|---|
| `id` | UUID PK | Identifiant interne |
| `comicvine_id` | string | ID ComicVine du Volume |
| `library_id` | UUID FK | Librairie parente |
| `title` | string | Titre de la série |
| `year` | int | Année de début |
| `description` | string | Synopsis |
| `image_url` | string | URL de la couverture |
| `publisher` | string | Éditeur |
| `authors` | JSON `[{name, role}]` | Auteurs avec rôles (Writer, Penciller…) |
| `genres` | string[] | Genres |
| `status` | enum | Voir statuts ci-dessous |
| `created_at` | timestamp | — |
| `updated_at` | timestamp | — |

**Statuts `VOLUME`**

| Valeur | Comportement |
|---|---|
| `MONITORED` | Inkhound recherche activement les Issues manquantes |
| `COMPLETED` | Toutes les Issues sont présentes — aucune recherche déclenchée |
| `FREEZE` | Suspendu manuellement — aucune recherche déclenchée |

> **Note** : Le champ `authors` est stocké en JSON pour conserver les rôles ComicVine (`writer`, `penciller`, `inker`, `colorist`, `cover`), indispensables au mapping vers les balises `ComicInfo.xml`.

---

#### Entité `ISSUE`

Représente un numéro individuel. Toujours associé à un `VOLUME`.

| Champ | Type | Description |
|---|---|---|
| `id` | UUID PK | Identifiant interne |
| `comicvine_id` | string | ID ComicVine de l'Issue |
| `volume_id` | UUID FK | Volume parent |
| `issue_number` | int | Numéro dans la série |
| `title` | string | Titre de l'issue |
| `year` | int | Année de parution |
| `description` | string | Synopsis de l'issue |
| `image_url` | string | URL de la couverture |
| `file_path` | string | Chemin absolu du fichier CBZ local |
| `cbz_filename` | string | Nom normalisé Kavita (ex. `Batman (2016) - 001.cbz`) |
| `published_at` | timestamp | Date de publication |
| `status` | enum | Voir statuts ci-dessous |

**Statuts `ISSUE`**

| Valeur | Description |
|---|---|
| `SEEKING` | Issue connue via ComicVine, introuvable localement — en attente d'acquisition |
| `DOWNLOADING` | Acquisition en cours (torrent ou import) |
| `DOWNLOADED` | Fichier traité et présent dans la librairie Kavita |

---

### 7.3 Flux d'ajout d'un Volume via ComicVine

Le flux se déroule en 5 étapes, entièrement guidées par l'interface :

#### Étape 1 — Recherche

L'utilisateur saisit un texte (titre, personnage, auteur) et applique des filtres optionnels.

```
GET https://comicvine.gamespot.com/api/volumes/
  ?filter=name:{query}
  &format=json
  &api_key={key}
```

Filtres disponibles : `name`, `start_year`, `publisher`, `count_of_issues`.

#### Étape 2 — Sélection du résultat

Les résultats sont affichés avec : titre, éditeur, année de début, nombre d'issues, `comicvine_id`. L'utilisateur sélectionne le volume correspondant.

#### Étape 3 — Prévisualisation des Issues

Second appel ComicVine pour récupérer toutes les issues du volume :

```
GET https://comicvine.gamespot.com/api/issues/
  ?filter=volume:{comicvine_id}
  &format=json
  &api_key={key}
```

Les issues sont affichées en prévisualisation. Aucune écriture en base à cette étape.

#### Étape 4 — Choix de la librairie

L'utilisateur sélectionne la `LIBRARY` de destination. Le chemin du dossier est généré automatiquement :

```
{library.root_path}/{volume.title} ({volume.year})/
```

Exemple : `/data/library/comics-dc/Batman (2016)/`

#### Étape 5 — Confirmation et écriture

1. Création de l'entrée `VOLUME` en base avec `status = MONITORED`
2. Création de toutes les `ISSUE` en base avec `status = SEEKING`
3. Création du dossier physique dans la librairie
4. Déclenchement immédiat du premier cycle de recherche

---

## 8. Fonctionnalité — Acquisition & Download Manager *(section 6.2)*

### 8.1 Vue d'ensemble

Une fois un `VOLUME` en `MONITORED`, Inkhound parcourt ses Issues au statut `SEEKING` selon deux chemins :

### 8.2 Chemin A — Recherche automatique via indexeur

Inkhound interroge un indexeur torrent (via **Prowlarr**) avec le titre et le numéro de l'Issue. Si un résultat est trouvé :

1. L'Issue passe en `DOWNLOADING`
2. Le torrent est envoyé au client de téléchargement (qBittorrent)
3. À la fin du téléchargement, le fichier entre dans le pipeline de traitement

### 8.3 Chemin B — Import manuel

L'utilisateur dépose un ou plusieurs fichiers depuis l'interface. Inkhound tente de les associer aux Issues existantes en base (par nom de fichier ou numéro détecté). Les fichiers non reconnus peuvent être associés manuellement.

---

## 9. Fonctionnalité — Traitement *(sections 6.3 et 6.4)*

### 9.1 Dossiers
```
/
├── download/  # Contient les fichiers téléchargé par le client torrent. 
├── import/    # Contient les demandes d'import du FrontEnd
├── process/   # Dossier de travail temporaire
├── libraryA   # root path de l'entité Library
├── libraryB   # root path de l'entité Library
├── libraryXX

```

L'application doit être en surveillance du dossier "download" pour déclencher le traitement.

### 9.2 Détails du traitement

Tout fichier acquis (par téléchargement ou import) passe par le même pipeline :

```
Fichier brut reçu (CBR / CBZ / ZIP / dossier d'images)
        ↓
Normalisation → conversion en CBZ
        ↓
Renommage selon convention Kavita
        ↓
Génération du ComicInfo.xml (depuis les données ISSUE + VOLUME)
        ↓
Injection du ComicInfo.xml dans le CBZ (à la racine de l'archive)
        ↓
Déplacement vers le dossier de la librairie
        ↓
Mise à jour du statut ISSUE → DOWNLOADED
        ↓
Appel POST /api/libraries/scan sur l'API Kavita
```

### 9.3 Nommage et arborescence

```
[Volume Title] [Volume Parution date]
  ├── [Volume Title] [Volume Parution date] - [Issue Number (format XXX)] - [Issue Title] [Issue publication date ].cbz
```
Traitement sur les données :
- `[Volume Title]` et `[Issue Title]`, sont normalisé pour supprimer les accents et les caractéres spéciaux.
- `[Issue Number]` : est mit au format XXX soit 12 -> 012
- `[Issue publication date]` et `[Volume Parution date]`, si la date n'est pas renseigné on ne met rien. Sinon on ne fait apppaitre que l'année de publication entre parenthèse. ex : `12/01/1993 -> (1993)`

Exemples de rendu final: 
```
Lucky Luke (1988)
  ├── Lucky Luke (1988) - 001 - La mine d or de Dick Dinger (1967).cbz
  ├── Lucky Luke (1988)- 002 - Rodeo.cbz
```


### 9.4 Structure d'un CBZ final

```
Batman (2016) - 001 - L ombre des tenebres (2018).cbz
  ├── ComicInfo.xml       ← généré et injecté par Inkhound
  ├── 001.jpg
  ├── 002.jpg
  └── ...
```

---

### 9.5 Génération du ComicInfo.xml

#### Mapping ISSUE → ComicInfo.xml

| Champ base (ISSUE / VOLUME) | Balise ComicInfo | Note |
|---|---|---|
| `volume.title` | `<Series>` | Titre de la série parente |
| `issue.issue_number` | `<Number>` | Numéro de l'Issue |
| `issue.title` | `<Title>` | Titre de l'Issue |
| `issue.published_at` (year) | `<Year>` | — |
| `issue.published_at` (month) | `<Month>` | — |
| `volume.publisher` | `<Publisher>` | — |
| `volume.authors` (role=writer) | `<Writer>` | Filtré par rôle |
| `volume.authors` (role=penciller) | `<Penciller>` | Filtré par rôle |
| `volume.genres[]` | `<Genre>` | Jointure par virgule |
| `issue.description` | `<Summary>` | — |
| `count(issues where volume_id = X)` | `<Count>` | Calculé dynamiquement |
| `issue.comicvine_id` | `<Web>` | URL ComicVine de l'Issue |

#### Exemple de ComicInfo.xml généré

```xml
<?xml version="1.0"?>
<ComicInfo>
  <Series>Batman</Series>
  <Number>1</Number>
  <Title>I Am Gotham (Part 1)</Title>
  <Year>2016</Year>
  <Month>7</Month>
  <Publisher>DC Comics</Publisher>
  <Writer>Tom King</Writer>
  <Penciller>David Finch</Penciller>
  <Genre>Super-héros, Crime</Genre>
  <Summary>After a disaster strikes Gotham City…</Summary>
  <Count>152</Count>
  <Web>https://comicvine.gamespot.com/batman-1/4000-528073/</Web>
</ComicInfo>
```

#### Régénération à la demande

```
POST /api/issues/{id}/regenerate-comicinfo
```

Utile en cas de mise à jour des métadonnées ComicVine ou de correction manuelle.

---

### 9.6 Export & Sync vers Kavita

Déplacement dans le dossier Library correspondant et envoi à Kavita d'une demande de Scan sur la Library.

---

## 10. Services tiers utilisés

| Service | Usage | Accès |
|---|---|---|
| [ComicVine](https://comicvine.gamespot.com/api/) | Recherche et métadonnées volumes & issues | API key gratuite |
| [Prowlarr](https://github.com/Prowlarr/Prowlarr) | Agrégateur d'indexeurs torrent | Instance locale |
| qBittorrent | Client de téléchargement | API locale |
| [Kavita REST API](https://www.kavitareader.com/) | Déclenchement scan de librairie | API key Kavita |


L'ensemble de ces services devront être configurable sur le FrontEnd avec des check de conection.
---