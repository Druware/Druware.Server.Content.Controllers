# Change Log

## 2026-10-15 - reviews

* Requires Druware.Server.Content 1.4.1 or later, which adds `Review`, the `ReviewTag` rows behind `Review.Tags`, the `Reviews` DbSet and the static helpers used here. The Content reference moves from `1.3.*` to `1.4.*` and the version to 1.4.0, published as 1.4.1, a minor move from the 1.3 line.
* Added `ReviewController` at `api/review` and `review`, following `SiteSettingController`: anonymous reads behind `LogRequest`, mutations behind `UpdateUserAccess`, the entity bound directly with no DTO. Reviews use the News roles and add none: add and update take `NewsSecurityRole` author or editor, delete takes the editor role, and a `SystemAdministrator` may do all three.
* The list is `Review.List`, paged with `page` and `count`, newest `posted` first and then by descending id, with tags. Only a caller in the `NewsSecurityRole` author or editor role, or a `SystemAdministrator`, gets drafts; everyone else, signed in or not, gets only published reviews, and the total counts only those. The roles are read from the signed-in principal with `User.IsInRole`, one role at a time, because the Identity cookie carries them as claims, which is what `[Authorize(Roles = ...)]` checks too. A single review is addressed by id or permalink through `Review.ByPermalinkOrId`; an unknown one, or a draft asked for by anyone else, answers `Not Found` with a 400, as News does.
* `Add` and `Update` answer HTTP 400 with a plain sentence for every validation failure, never `Ok(Result.Error(...))`: the `Review.Validate` rules, a photo asset id that does not exist, and a permalink another review already uses. Both run `Review.Normalize` and then `Review.EnsurePermalink` first, so takes are trimmed, a blank take or product URL is stored as null, and a missing or blank permalink is built from the title with the News rule; the result is validated and checked for uniqueness, leaving out the review's own id on `Update`, so regenerating its current permalink is not a duplicate. `Add` sets `posted` to now when the body has none, and sets `modified` to now. Both return the saved review, read back so `tags` reflects the stored rows.
* `Update` replaces every editable field with the body's value, so an omitted or null optional field clears it and an omitted or null `tags` removes the tags. The exceptions are the permalink, which is built from the title again when the body's is missing or blank, so clearing it never takes a review's page down; `posted`, which keeps the stored value when the body has none; and `modified`, which is always set to now. Tags are trimmed, blanks are skipped and duplicates collapse to one row; missing tags are created in the shared pool through `Tag.ByNameOrId`, as `NewsController` does.

## 2026-10-15 - article takes and meta

* Requires Druware.Server.Content 1.3.2, which adds `Article.EddieTake`, `Article.DruTake`, the `ArticleMeta` rows behind `Article.Meta`, and the static helpers used here. The `1.3.*` reference and the csproj version are unchanged; CI publishes this as the next patch.
* `NewsController` carries `eddieTake`, `druTake` and `meta` on the list, on a single article and in POST and PUT bodies. The list now includes `ArticleMeta`, so `meta` is populated there too (the site's prerender daemon hashes the list records to spot edits); a single article gets it through `Article.ByPermalinkOrId`.
* `Add` and `Update` check the takes and the meta with `Article.ValidateTakesAndMeta` and answer HTTP 400 with a plain message when they fail, so a client that only reads the status sees it. The existing validation paths (model state, duplicate permalink) still answer `Ok(Result.Error(...))` and are unchanged.
* Takes are stored as `Article.NormalizeTake` leaves them: trimmed, with a blank take stored as null. `Update` copies both alongside the other fields, so a PUT that omits a take clears it.
* Meta goes through `Article.ApplyMeta`. On `Add`, an object creates the rows and an omitted or null `meta` creates none. On `Update`, an omitted or null `meta` leaves the stored meta untouched, and an object replaces the whole set: missing keys are deleted and `{}` clears them all. Keys are trimmed, a null value is stored as an empty string, and keys starting with `$` (such as the `$id` that `ReferenceHandler.Preserve` writes into the dictionary) are dropped rather than refused.
* `ProductController.Update` no longer deletes a product's meta when a PUT omits it. It read the body's meta through `Product.Meta`, which reads as an empty set rather than null on a body without `meta`, so every such PUT removed all of the product's meta rows. It now reads `Product.SubmittedMeta` (Druware.Server.Content 1.3.2): an omitted or null `meta` leaves the stored meta untouched, and an object still replaces the whole set, with `{}` clearing it. `Add` reads `SubmittedMeta` too, with no change in behaviour.
* `NewsController.Update` now copies `IsFeatured`, so a PUT can feature or unfeature an article; before, it kept whatever was stored.

## 2026-10-15 - site setting controller

* Requires Druware.Server.Content 1.3.1, which adds `SiteSetting`, the
  `SiteContentSecurityRole` pair and the matching `DbSet`s. The Content
  reference moves from `1.2.*` to `1.3.*` and the version to 1.3.0,
  published as 1.3.1, a minor move from the 1.2 line.
* Added `SiteSettingController` at `api/sitesetting` and `sitesetting`,
  following `CollectionController` - anonymous reads behind `LogRequest`,
  mutations behind `UpdateUserAccess`, the entity bound directly with no
  DTO and no mapper. Add and update take the `SiteContentSecurityRole`
  author or editor role, delete takes the editor role, and a
  `SystemAdministrator` may do all three. The list is ordered by key and
  paged with `page` and `count`; a single setting is addressed by id or
  key through `SiteSetting.ByKeyOrId`, and an unknown one answers
  `Not Found` with a 400.
* Validation failures answer HTTP 400 with a plain message rather than
  `Ok(Result.Error(...))`, so a client that only reads the status sees
  the failure. `Add` runs `SiteSetting.Validate` (key and description
  rules) and refuses a key that already exists rather than leaving it to
  the unique index. The id is always assigned by the database; any
  `settingId` in the body is discarded.
* `Update` copies only `Value` and `Description`. The key never changes,
  so a different or even invalid key in the body is ignored rather than
  validated, and only the description length is checked. `Updated` is
  set in code on every add and update. The response is the entity that
  was saved, not a fresh lookup.
* From this release the package is published to the Satori feed only,
  `https://nuget.satori-assoc.com/v3/index.json`, and no longer to
  nuget.org; the publish workflow's nuget.org push step is removed.
  Consumers need that feed as a package source to restore 1.3.1 and
  later. The repo now carries a `nuget.config` at its root that adds the
  feed alongside nuget.org, so local and CI restores can find
  Druware.Server.Content 1.3.x.

## 2026-08-31 - collection controller

* `Druware.Server.Content` 1.2.1 adds a Collections layer - `Collection`, the
  `CollectionProduct` join, a `CollectionSecurityRole` pair, and the two
  matching `DbSet`s on `ContentContext`. `Collection` mirrors `Product`'s
  idiom: a `[NotMapped] string[] Products` projection over the join, built
  from each product's `Short`, readable and writable so membership can ride
  along on the entity itself.
* Added `CollectionController`, following `ProductController` throughout -
  anonymous reads behind `LogRequest`, mutations behind `UpdateUserAccess`
  and the `Collection` author/editor roles, the entity bound directly with
  no DTO and no mapper.
* `Get` cannot lean on `Collection.ByNameOrId` alone. That lookup only
  eager loads `CollectionProducts.Product` on the id branch; matched by
  name it returns the bare row, and the `Products` projection - which reads
  the join, not the database - comes back empty. The join and the products
  behind it are loaded explicitly before the entity is returned, the same
  way `ProductController.Get` loads `ProductMeta`.
* `Add` and `Update` resolve each submitted short through
  `Product.ByShortOrId` and refuse the whole request with
  `Unknown product: x` when one does not resolve. Unlike `Tag.ByNameOrId`,
  which creates on miss, there is no sensible product to invent here, and
  silently dropping the entry would report a success the caller did not
  get. Repeats within the submitted array are dropped rather than left to
  violate the unique index on `(collection_id, product_id)`.
* `Update` copies only `Name` and `Description`. `CollectionId` is the key,
  and `Created` and `Updated` are `DatabaseGenerated(Computed)`, so none of
  the three may be assigned. Membership is replace on update - the existing
  join is loaded, cleared, and rebuilt - and a null `Products` leaves it
  alone entirely. The response is the entity that was saved rather than a
  fresh lookup on the route value: addressed by name, a rename leaves that
  value pointing at a name that no longer exists, and the endpoint would
  answer a successful save with a null body.
* `Remove` loads `CollectionProducts` before removing the collection. The
  join's FK to `Collection` is `ClientCascade`, so EF deletes only the
  dependents it is actually tracking; with a name lookup that set is empty
  and the delete comes back as a foreign key violation. The products
  themselves are never touched - the join is the dependent on that side
  too.
* Added `{value}/products` to page the membership, and
  `{value}/products/{productValue}` as POST and DELETE to add and remove a
  single product. The add reports `Already a member` on a repeat rather
  than letting the unique index throw.

## 2026-08-13 - product update now persists app store and direct links

* `Druware.Server.Content` 1.1.17 adds seven new nullable string columns to
  `Product`: `AppStoreApple`, `AppStoreMs`, `AppStoreGoogle`, and
  `AppStoreAmazon` for store listing links, and `DirectOsx`, `DirectWinArm`,
  and `DirectWinX64` for direct download links per platform. The csproj
  already floats `Druware.Server.Content` at `1.1.*`, so no reference change
  was required.
* `ProductController.Add` posts the model wholesale, so it already persisted
  the new fields with no change.
* `ProductController.Update` copies scalars onto the loaded entity
  field-by-field instead - the only place in the repo that enumerates
  `Product` scalars this way - so the seven new fields were unsettable
  through the update endpoint. `obj` is the tracked entity returned by
  `Product.ByShortOrId`, loaded from the database; any property never
  assigned simply keeps its stored value, so a client posting these fields
  had the values silently discarded. Existing stored values survived an
  update untouched, but new ones could never land - only `Add` could set
  them, at creation time. Added the matching `obj.X = model.X;`
  assignments for all seven, after `IconUrl` and before `Updated`.
* `Get` and `GetList` serialize the entity directly, so the new fields
  surface with no change.

## 2026-07-28 - product update no longer collides with itself on short

* `ProductController.Update` checked the short for duplicates with
  `Product.IsShortAvailable`, which has no notion of the record being
  edited - it simply asks whether *any* product owns that short, and the
  product being updated always does. The only thing keeping it from
  rejecting every save was the `obj.Short != model.Short` guard in front of
  it, an ordinal string comparison. Anything that makes the submitted short
  differ from the stored one without actually changing which product owns
  it - a case or whitespace difference, a client that normalises the value,
  a `Short` that fails to bind and arrives null - takes the guard's true
  branch, the lookup then finds the product itself, and the update is
  rejected as a duplicate of itself. A case- or trailing-space-insensitive
  collation (the SQL Server default) does the same on a rename that differs
  only in case.
* Replaced both with a single self-excluding query - match on the short,
  exclude the current `ProductId` - the same shape `NewsController` and
  `DocumentController` get from `IsPermalinkValid(context, permalink, id)`.
  A short held by a *different* product is still rejected.

## 2026-07-28 - product meta tag support

* `Druware.Server.Content` 1.1.16 adds `ProductMeta`, a `(property, value)`
  child table on `Product`, surfaced as a `[NotMapped]
  Dictionary<string, string> Meta` projection - the same idiom as the
  existing `Tags`/`ProductTags` pair. `ProductController` wires it up inline
  in `Add` and `Update`, mirroring how tags are already handled; there is
  deliberately no separate `/meta` route family.
* `Add` translates the posted `Meta` dictionary into `ProductMeta` rows,
  skipping null or whitespace properties.
* `Update` reconciles instead of clear-and-rebuild: `content.product_meta`
  carries a unique index on `(product_id, property)`, so severing the whole
  collection and re-adding risks violating it if EF batches the inserts
  ahead of the deletes. Existing properties are updated in place, new ones
  are added, and absent ones removed. A null `Meta` leaves existing meta
  untouched; an empty `Meta` clears it - callers need to send the full set
  they want kept, not just the properties that changed.
* `Get` loads the `ProductMeta` collection explicitly, since
  `Product.ByShortOrId` lives in the library and does not include it. This
  also makes it correct where lazy-loading proxies are not enabled.
* `GetList` now includes `ProductMeta` so `Meta` is populated in list
  responses.
* Also reorders product news by `Posted` instead of `Modified`, and product
  history by `Posted` descending.

## 2026-07-24 - build fix and dependency advisories

* Floated `Microsoft.AspNetCore.Identity` (was pinned at 2.3.1) and
  `Microsoft.AspNetCore.Mvc.Core` (was 2.2.*) to 2.3.*. Druware.Server 1.1.16
  requires 2.3.11 of both, and the lower pins made that a package downgrade,
  which fails the build outright rather than warning.
* **Breaking:** `DocumentController` and `ProductController` no longer take an
  `IMapper` constructor parameter. It was injected but never used - neither
  controller ever called it - so this removes dead wiring rather than
  behaviour. Applications resolving them through DI need no change.
* Dropped the `AutoMapper` reference along with it, clearing CVE-2026-32933.
* Removed the unused `Microsoft.AspNetCore.Server.Kestrel` reference, source of
  the Critical `Kestrel.Core` advisory. A web host supplies its own server.
* Bumped `MailKit` 3.4.1 to 4.17.x, clearing the MailKit, MimeKit and
  `System.Security.Cryptography.Pkcs` advisories.
* Floated `Druware.Server.Content` from the exact 1.1.13 pin to 1.1.*, matching
  how the other Druware references in this project are declared.

`dotnet list package --vulnerable --include-transitive` now reports nothing.

`Microsoft.AspNetCore.Server.IISIntegration` is deliberately left in place: no
advisory, and not in the Kestrel dependency chain.

