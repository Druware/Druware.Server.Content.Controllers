# Change Log

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

