# Attic — scripts caducs

Scripts rendus caducs par `apply_genre_corrections.py` (dans `data/scripts/`), qui a acté le choix d'un
encodage des genres largement manuel plutôt qu'une suggestion automatique. Conservés ici (plutôt que
supprimés) parce que les dictionnaires de mots-clés par bucket qu'ils contiennent documentent des dizaines
de cas réels en commentaire — coûteux à reconstituer, mais plus maintenus. Voir `docs/refactor-decisions.md`
section 6.

- `audit_genres.py` — suggestion de tags "genre large" à partir des genres Spotify par artiste (ambigu en cas d'homonymie).
- `audit_genres_deezer.py` — même idée via l'ISRC/Deezer, plus fiable par morceau ; importe `audit_genres.py` et dépend de son cache — les deux doivent rester ensemble ici.
- `audit_genres_musicbrainz.py` — troisième tentative via MusicBrainz/ISRC, explicitement marqué "PROTOTYPE" dans sa propre docstring, jamais stabilisé.
- `build_tag_editor.py` + `tag_editor_template.html` — éditeur de tags visuel autonome (HTML généré, `localStorage`), autre piste abandonnée au profit de la relecture manuelle en texte brut.

**Ces scripts ne sont plus exécutables tels quels depuis ce dossier** : `audit_genres_deezer.py` et
`audit_genres_musicbrainz.py` importent `fetch_spotify_playlist` (resté dans `data/scripts/`, toujours
actif) — les relancer demanderait d'ajouter `data/scripts/` au `PYTHONPATH` ou de les exécuter depuis là.
