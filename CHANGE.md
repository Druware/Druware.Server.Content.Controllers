# Change Log

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

