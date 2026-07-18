# osu! online APIs, mirrors & the preview CDN — Fallcall map-browser audit

> **RES2 deliverable.** What Fallcall can pull from osu!'s servers, mirrors and CDN for the
> **map browser (U6)**, and what is structurally impossible. Every claim below was **verified
> empirically against live endpoints or read out of `ppy/osu-web` / `ppy/osu` source on
> 2026-07-15** — not from forum posts or docs prose (the published docs are wrong or vague on
> several of these).
>
> **Bottom line:** the browser needs **no credentials at all**. `.osz` downloads *must* go
> through mirrors (hard scope wall, no workaround). Everything else comes unauthenticated from
> ppy's own CDN.

---

## 0. Summary table

| Need | Source | Auth | Bytes |
| --- | --- | --- | --- |
| Search + metadata | mirror (osu.direct; see §7 — mirrors must do TLS 1.2) | none | ~KB (JSON) |
| Search + metadata (optional upgrade) | official API v2 `beatmapsets/search` | client_credentials | ~KB (JSON) |
| Preview audio | `b.ppy.sh/preview/{setId}.mp3` | **none** | ~100 KB |
| Cover art | `assets.ppy.sh/beatmaps/{setId}/covers/{variant}.jpg` | **none** | ~KB |
| `.osu` (single difficulty) | `osu.ppy.sh/osu/{beatmapId}` | **none** | ~90 KB |
| `.osz` (full set) | mirrors **only** | none | ~5–15 MB |

**"Credential-free" ≠ "mirror-only."** Preview, covers and `.osu` come straight from ppy and
are built for that traffic. Only the multi-MB `.osz` touches volunteer mirror bandwidth.

---

## 1. The preview clip — exactly characterised

`https://b.ppy.sh/preview/{beatmapSetId}.mp3` — no auth. Confirmed as the URL osu!lazer itself
uses (`osu.Game/Audio/PreviewTrackManager.cs`, `TrackManagerPreviewTrack.GetTrack()`).

Three findings, each verified on live files:

1. **It is Ogg/Vorbis, not MP3 — the extension lies.** `Content-Type: audio/ogg`; `ffprobe`
   reports `codec_name=vorbis`. True on every set sampled (2354986, 39804, 1).
2. **Always 10.100 s**, ~100 KB, ~81 kbps, 44.1 kHz stereo.
3. **The clip is the full audio windowed to `[PreviewTime − 100 ms, PreviewTime + 10000 ms]`.**
   Established by FFT cross-correlation of the preview against the real `audio.mp3` extracted
   from the `.osz`, on two independent sets:

   | Set | `PreviewTime` | Correlated start | Delta |
   | --- | --- | --- | --- |
   | 2354986 (Camellia — Tojita Sekai) | 71225 ms | 71.125 s | **−100.0 ms** |
   | 39804 (xi — FREEDOM DiVE) | 164126 ms | 164.026 s | **−100.0 ms** |

   The −100 ms lead-in is also why the duration is 10.1 s and not 10.0 s. There is a ~100 ms
   fade-in and a ~500 ms fade-out (measured on the RMS envelope).

**Therefore the sync relation is exact:**

```
songTimeMs = clipPlaybackMs + PreviewTime - 100
```

### ⚠ Live bug this uncovered

`SongSelectUI.cs:999` requests the stream as `AudioType.MPEG`:

```csharp
using var req = UnityWebRequestMultimedia.GetAudioClip($"https://b.ppy.sh/preview/{id}.mp3", AudioType.MPEG);
```

The stream is Vorbis. Failure path (`:1003`, `:1007`) is a silent `yield break`, so online
previews almost certainly do nothing today. Fix: **`AudioType.OGGVORBIS`**. Not yet verified
in-editor (no headless path).

---

## 2. `.osu` files are public — this is what makes autoplay-preview cheap

`https://osu.ppy.sh/osu/{beatmapId}` returns the raw `.osu` as plain text, **unauthenticated**,
~90 KB. It contains `PreviewTime` plus every hit object.

So an **autoplay preview panel needs ~200 KB per map** (preview ogg + `.osu`) and **never
touches the 13 MB `.osz`**. Feed the fetched text to the existing `BeatmapParser`
(`Beatmap.General.PreviewTime` is already parsed — `Beatmap.cs:11`) and route through
`Playfield.ToWorld` like any other drawable.

**Gameplay density in the 10 s window is never a problem** — preview points sit on the
kiai/chorus by construction. Measured object counts inside `[PreviewTime, PreviewTime+10000]`:

| Map | Objects in window |
| --- | --- |
| Tojita Sekai [Insane] | 33 |
| Tojita Sekai [Breakthrough] | 49 |
| FREEDOM DiVE [Another] | 82 |
| FREEDOM DiVE [FOUR DIMENSIONS] | 118 |

---

## 3. There is no "default difficulty"

osu-web's `findDefault()` (`resources/js/utils/beatmap-helper.ts`) picks the difficulty whose
`difficulty_rating` is closest to the viewer's **`userRecommendedDifficulty`** (a pp-derived,
per-user value). With no user it degrades to first-visible = lowest-star non-convert.

**No server-side default flag exists.** Fallcall must pick its own rule — make it an
`Osu3DSettings` tunable. Suggested default: **highest-star osu!std difficulty** (best showcase
for a preview).

---

## 4. Official API v2 — what login actually buys (less than expected)

### Search needs only the `public` scope, and guest tokens have it

`BeatmapsetsController.php:38`:

```php
$this->middleware('require-scopes:public', ['only' => ['lookup', 'search', 'show']]);
```

Advanced filters are gated on `checkBeatmapsetAdvancedSearch` — `app/Singletons/OsuAuthorize.php:508`:

```php
if (oauth_token() === null && !$GLOBALS['cfg']['osu']['beatmapset']['guest_advanced_search']) {
    $this->ensureLoggedIn($user);
}
return 'ok';
```

`ensureLoggedIn` fires **only when there is no OAuth token** (i.e. cookie-session website
guests). **Any** token short-circuits it. So a **client_credentials** token gets the *full*
filter set — query, status (`s`), genre (`g`), language (`l`), mode (`m`), extras (`e`),
converts/featured/spotlights (`c`), nsfw, sort. No user, no supporter.

Without a token the else-branch (`BeatmapsetSearchRequestParams.php`) sets `$sort = null` and
skips `parseQuery()` entirely — guests get a bare default listing.

### What a real user login adds

Only the **user-scoped** filters: `s=favourites`, `s=mine`, `played`/`unplayed`, `r=` (rank
achieved), `c=follows`, `c=recommended`. Nice-to-have, not required. **Login does not unlock
downloads** (see §5).

### Login does not remove the shipped secret

`OAuth/ClientsController.php:57` issues `'secret' => str_random(40)` for **every** app — there
is no public-client / PKCE-only option exposed. Authorization Code still needs that secret at
token exchange, so any shipped Fallcall build contains an extractable secret either way. This
is the normal position for third-party osu! clients; it is **not** an argument for login.

---

## 5. `.osz` download is impossible without mirrors — a scope wall

The download route sits in a group with a bare `require-scopes` (no argument). `RequireScopes.php:50`:

```php
if (!$token->can('*')) { throw new MissingScopeException(); }
```

So **download requires the `*` scope**. The complete list of requestable scopes —
`app/Providers/AuthServiceProvider.php:68`, `Passport::tokensCan([...])`:

```
delegate, forum.write, forum.write_manage, chat.read, chat.write,
chat.write_manage, friends.read, group_permissions, identify,
multiplayer.write_manage, public
```

**`*` is not on the list.** Passport rejects unregistered scopes outright, so no app created via
the public form — client_credentials *or* full user login — can ever hold it.

**How the real client does it:** lazer/stable are *first-party* clients whose OAuth client has
**no owner user**. `OAuth/Token.php:222`:

```php
} elseif ($client->user === null) {
    // Only "*" scope is allowed for clients with no user
    throw new InvalidScopeException('client_missing_owner');
}
```

Owner-less clients are *required* to use `*`, and are the only ones that get it. They're
inserted directly into ppy's database, not created through `/home/account/edit#oauth`. Your app
is owned by you, so it can never be one. lazer then calls
`beatmapsets/{id}/download?noVideo=1` and eats a per-user quota of **10 downloads/hour (20 for
supporters)** — `config/osu.php:71-72`, `429` past that.

`checkBeatmapsetDownload` itself only calls `ensureLoggedIn`, so a *user* token clears that
check — but the `*` wall upstream still blocks it. **Mirrors are structural, not a preference.**

---

## 6. Rate limits are per-user, not app-wide

### Official API: keyed per **token**, not per client_id

`app/Http/Middleware/ThrottleRequests.php:52`:

```php
protected function resolveRequestSignature($request)
{
    $token = oauth_token();
    if ($token !== null) {
        return sha1($token->getKey());
    }
    return parent::resolveRequestSignature($request);
}
```

Every player's install fetches its own client_credentials token → its own bucket. One shared
`client_id` does **not** mean one shared quota.

The enforced ceiling is `'global' => '1200,1,api'` (`config/osu.php:34`) = **1200 req/min per
token**. The published policy is **60 req/min**. Treat 60 as the rule to honour and 1200 as the
wall you'd actually hit.

### Mirrors: per-IP

Downloads go **direct from each player's machine to the mirror** — no shared credential, so no
shared bucket. Both mirrors we use expose the budget, and **they differ by 10×**:

```
osu.direct (the one we actually use — §7):
Ratelimit-Limit: 120
Ratelimit-Remaining: 119
Ratelimit-Reset: 60                          → 120/min per IP

catboy (TLS-unreachable from Unity 2022, §7):
X-Ratelimit-Limit: 1200
X-Ratelimit-Remaining: 1199 → 1198 → 1197    (decrements per-IP only)
X-Ratelimit-Reset: <~60 s window>            → 1200/min per IP
```

**Budget in force today: 120/min per IP** — catboy's roomier 1200 is not ours to spend while Unity
can't reach it. Two requests a second, shared between search and `.osz` downloads, so the search
debounce (`MapBrowserSearch.debounce`, 0.45 s) is doing real work rather than just smoothing the
UI: it is what keeps a fast typist under the ceiling. Cover/preview art does **not** count against
this — it comes from ppy's CDN (§1), not the mirror. Nerinyan is behind Cloudflare and exposes no
limit headers.

### Where app-wide risk *does* live

Neither has an app-wide *throttle*. Both have app-wide *enforcement*:

- **Official API** — abuse gets the `client_id` revoked, killing every install at once.
- **Mirrors** — they can't rate-limit us as an app, but they *can* ban by **User-Agent**.
  Volunteer-run, donation-funded, ~13 MB per `.osz`.

> **Do not proxy mirror traffic through a Fallcall-hosted service.** It would collapse per-user
> buckets into one shared bucket on our IP, manufacturing the exact app-wide bottleneck that
> does not currently exist. Peer-to-mirror is the correct topology.

**Good-citizen rules for U6:** send an honest identifying User-Agent (so mirror ops can *contact*
us instead of just banning), cache `.osu` + previews on disk, never re-download an owned set,
honour `429`/`Retry-After`, keep the osu.direct → catboy download fallback.

---

## 7. Mirror APIs

Both are unauthenticated and return **osu!-API-v2-shaped** beatmapset JSON (same field names),
which is why a mirror → official swap would be near drop-in. Already wired in
`BeatmapDownloader.cs`.

| Mirror | TLS 1.2 | Search | Download |
| --- | --- | --- | --- |
| **osu.direct** | ✅ | `osu.direct/api/v2/search?query=&mode=&limit=` | `osu.direct/api/d/{setId}` |
| catboy | ❌ **1.3-only** | `catboy.best/api/v2/search?query=&mode=&limit=` | `catboy.best/d/{setId}` (`{setId}n` = no video) |
| nerinyan | ✅ | ~~`api.nerinyan.moe/search?q=&m=&ps=&p=`~~ dead | ~~`api.nerinyan.moe/d/{setId}`~~ 404s |

`.osz` verified: osu.direct served a 10.8 MB set over TLS 1.2; catboy served 13.4 MB in 1.3 s
(from curl — **not** reachable from Unity, see below).

**Parameter names are per-mirror and a wrong one is silently ignored** — the mirror answers `200`
with its default listing instead of an error, which reads as "search works, returns junk".
osu.direct and catboy take `query/mode/limit`; nerinyan takes `q/m/ps`.

### ⚠ Unity 2022 caps at TLS 1.2 — this decides which mirrors exist

UnityWebRequest's TLS stack on Unity 2022 negotiates **TLS 1.2 at best**. A TLS-1.3-only host is
therefore **permanently unreachable from the game**, however healthy it looks from curl or a
browser. The failure is fast and generic, so it mimics a transient network blip:

```
× search  0  0 B  127 ms  https://catboy.best/api/v2/search?query=&mode=0&limit=50
      ConnectionError: Unable to complete SSL connection
```

**catboy.best is TLS-1.3-only** — it answers a TLS 1.2 ClientHello with alert 70
(`protocol_version`). It is the best mirror by API behaviour and the one we cannot use. It stays
last in both lists purely so the code is already correct if the ceiling lifts (Unity 6 / newer
`unitytls`).

**Vet every candidate mirror before adding it.** Must reach `Verify return code: 0`:

```
openssl s_client -connect HOST:443 -servername HOST -tls1_2
```

Probed 2026-07-16: `osu.direct` ✅, `beatconnect.io` ✅, `mino.pw` ✅, `catboy.best` ❌ (1.3-only),
`api.chimu.moe` ❌ (dead), `api.osu.direct` ❌ (dead — the API lives on the apex, not this subdomain).

### nerinyan search is dead too (2026-07-16)

`api.nerinyan.moe/search` ignores the query and returns **the same static listing for every term**
(`?q=Ado`, `?q=Camellia` and `?q=` are byte-identical; `q`/`query`/`+p`/`+option` all behave the
same, so it is not a spelling problem). Its `/d/{setId}` 404s for every id tried, while catboy
serves the same ids fine — the mirror looks dead, not misconfigured.

Consequence for `BeatmapDownloader.Search`: it accepts **the first mirror whose body parses**, and
nerinyan's junk parses perfectly, so the next mirror was never reached and every search returned
the same 50 sets. nerinyan is therefore **removed from `SearchUrls` entirely** rather than demoted
— at any position it can only turn an honest failure into silently wrong results. Re-add only once
`?q=` demonstrably filters again. It is out of `MirrorUrls` too: 404-for-every-id buys nothing but
a wasted round-trip now that osu.direct leads.

Between the two, the browser was broken end-to-end and not just in search: the only mirror Unity
could *reach* (nerinyan) was serving junk and 404s, and the only mirror *behaving* (catboy) was
TLS-unreachable. Downloads had no working path either until osu.direct was added.

Note this is the second time this exact shape bit us: catboy previously ignored a nerinyan-spelled
`q` and answered with its default listing (`docs/progress-u6.md`). A `200` from a mirror is not
evidence the query was honoured.

### Mirror filter/sort support — resolved (2026-07-16, osu.direct)

The §7 open question is **answered: osu.direct honours status + sort server-side**, so the
category and ordering rows need no credentials. Probed against live `?…` params:

- **`status=<int>`** filters by beatmap state — confirmed distinct result sets: `-2`=graveyard,
  `-1`=wip, `0`=pending, `1`=ranked, `2`=approved, `3`=qualified, `4`=loved. Must be a number
  (`status=any` → `403`); **omit it for the "Has Leaderboard" default listing**.
- **`sort=<attr>:asc|desc`** — a wrong attr `500`s with the whole allowlist:
  `artist, beatmaps.bpm, beatmaps.difficulty_rating, beatmaps.hit_length, beatmaps.passcount,
  beatmaps.playcount, beatmaps.total_length, bpm, favourite_count, id, last_updated, play_count,
  ranked_date, submitted_date, title`. The listing's Title/Artist/Difficulty/Ranked/Plays/
  Favourites map to `title / artist / beatmaps.difficulty_rating / ranked_date / play_count /
  favourite_count`. **osu!'s "Rating" has no backing attr — dropped from the row.**
- **`e=` / `g=` / `l=` (extra / genre / language) are ignored** — the mirror answers its default
  listing (the §7 silent-`200` trap). But every result carries `video`, `storyboard`, `genre_id`,
  `language_id`, `rating`, `play_count`, `favourite_count`, `status` as fields, so **"Has Video" /
  "Has Storyboard" are filtered client-side** on the returned page instead.
- **Favourites / My Maps stay impossible credential-free** (login-scoped, §4) — omitted from the UI.

Wired in `BeatmapDownloader.SearchUrls` (status + sort params) and the `MapBrowser` filter rows
(`BrowseQuery` maps the words → params; `BrowseFilters` does the client-side Extra gate).

---

## 8. Verdict for U6

1. **Ship credential-free.** Mirror search + ppy CDN covers the whole browser.
2. **Mirrors for `.osz` forever** — no amount of auth changes this.
3. **Autoplay preview is viable and cheap** (~200 KB/map, ppy CDN only, exact sync formula in §1).
4. **Add client_credentials later, if ever** — only if mirror filters prove weak (§7). It's an
   isolated change and costs nothing app-wide (§6).
5. **Never proxy.** (§6)
