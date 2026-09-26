# Security and privacy

Home Business Assistant V1 is a single-user, local Windows application. Its security boundary is the interactive Windows account that installed and runs it. The UI is a local control plane, not a remote-access surface; remote access must be configured and secured independently.

## Threat model

### Assets

| Asset | Storage/use | Primary protection |
|---|---|---|
| API secrets | DPAPI-protected files addressed by opaque references | Windows `CurrentUser`; never stored in SQLite or mutable JSON |
| Browser profile/session state | Dedicated account directories under the Founder Scout data root | Current-user filesystem boundary; excluded from backups, reports, and diagnostics |
| Founder profiles, evaluations, and drafts | `founders.db` plus bounded raw/report artifacts | Separate database, root confinement, retention, output encoding, no photograph storage |
| Remote availability configuration | Immutable revisions in `assistant.db` | Strict schemas; no passwords, PINs, or firewall/router mutation |
| Audit, logs, summaries, artifacts, backups | Application data root | Redaction, bounded writes, hashes, path validation, retention, explicit export |
| Executables and manifests | Installed `app` directory | Publish manifest SHA-256 verification and pre-launch executable validation |

### Trust boundaries and data flow

```text
current Windows user
  -> owner-authenticated loopback Host/UI
  -> durable assistant.db occurrences
  -> Runner validates executable + resolves short-lived secrets
  -> independently executable agent
       -> dedicated browser profile -> external source (HTTPS)
       -> structured evaluator input -> configured AI provider (HTTPS)
  -> redacted events/artifacts/audit

assistant.db + founders.db
  -> SQLite online backup -> hash/schema-verified backup set
```

- **Current Windows user:** the V1 trust root. Another local Windows account must not control the Host or activation pipe. Code already running as the owner can access owner-readable files and invoke DPAPI; that is a documented residual risk.
- **Host/UI:** Kestrel binds to exact IPv4 loopback. Razor Pages require Windows Negotiate authentication for the Host owner's SID. Health endpoints are deliberately anonymous. Requests must use the configured `Host`; unsafe requests with an `Origin` header must match the configured origin and still pass antiforgery.
- **Runner/agents:** all work flows through durable occurrences. Runner uses argument lists, validates manifests/configuration/hashes and allowed roots, writes a short-lived current-user execution envelope, supervises the process tree, and separately captures protocol stdout and diagnostic stderr.
- **Browser/external source:** persistent profiles contain sensitive session state. Non-loopback navigation must use HTTPS and an allowed host. HTTP is accepted only for loopback test fixtures. Login loss, denial, throttling, challenges, or parser-health failure stop the account/run without automated failover.
- **AI provider:** only redacted evaluator input and the configured request are sent. Responses pass strict schema, evidence, numeric, and draft validation. C# owns arithmetic and final decisions.
- **Filesystem/backups:** database backup uses SQLite online backup and a manifest; promotion and restore verify hashes, schemas, and expected migrations. Secrets, profiles, logs, diagnostics, and raw artifact files are outside ordinary backup scope.

## Enforced controls

- The Host accepts only `http://127.0.0.1:<configured-port>` and rejects alternate host/origin values. Owner-only Windows authentication, authorization, antiforgery, CSP, framing denial, MIME sniffing denial, and Razor encoding protect management pages.
- The single-instance activation pipe uses `PipeOptions.CurrentUserOnly`, a per-user/session mutex, a high-entropy capability suffix, and a 64-byte allow-listed command protocol.
- Reserved `HostAtLogon` and `NextWake` task names are modified or removed only when their semantic ownership markers validate. An unmanaged occupant produces a bounded ownership-conflict error.
- Secrets are addressed by `secret://` references and protected with DPAPI `CurrentUser`. Runner resolves them immediately before launch, bounds the secret map, avoids command-line/environment exposure, clears serialized buffers, and removes stale input files.
- Root-confined file operations reject traversal and relevant reparse-point ambiguity. Artifact copy rechecks file stability and size. Restore stages files, rejects unexpected database shape/hash, and rolls back failed promotion.
- Browser diagnostics omit screenshots and page images, remove active content, metadata, form values, URL-bearing and `data-*` attributes, redact credential-like text, cap HTML at 2 MiB, and preserve the configured deletion deadline through the agent protocol into central artifact retention.
- Founder Scout normalizes and redacts before evaluation. Protected/irrelevant attributes are excluded from DTOs, evaluator inputs, scores, ranking, reports, and drafts. Missing evidence lowers confidence; it is never invented.
- Invitation drafts and queues are decision support only. There is no invitation-send network adapter. A sent/outcome state requires an explicit local user action and audit record.
- Browser automation has bounded sequential navigation and deterministic cooldowns. V1 contains no CAPTCHA solver, stealth plugin, fingerprint spoofing, proxy rotation, traffic laundering, human-like input concealment, or enforcement-triggered account failover.
- CSV exports neutralize formula-leading cells; HTML is encoded; JSON/Markdown/HTML/CSV reports share one bounded raw-free model. Diagnostics ZIP creation does not accept arbitrary archive members, and restore never extracts an untrusted ZIP.
- Publish and install manifests provide content integrity. They do not prove publisher identity; V1 binaries are not Authenticode signed.

## Stage 16 security review

The standard source-backed review found and remediated four reachable issues before the release candidate:

1. **High — local UI lacked a user boundary.** Any other local account able to reach loopback could have changed configuration and triggered work. Razor Pages now require Negotiate authentication and the startup owner's SID; exact Host/Origin checks and regression tests were added.
2. **Medium — activation pipe granted `WorldSid` full control.** The ACL was replaced by the platform's `CurrentUserOnly` pipe boundary; same-user cross-process activation remains tested.
3. **Low — external Founder Scout source URLs allowed cleartext HTTP.** Configuration, discovered links, continuations, and final redirects now require HTTPS, with an explicit loopback-only HTTP fixture exception.
4. **Low — diagnostic HTML could retain credential-bearing attributes and central storage discarded its retention deadline.** Sanitization is structural and defensive, screenshots are omitted, credential carriers are regression-tested, and protocol artifact metadata now carries the UTC deletion deadline.

The review also confirmed and fixed a reliability/integrity discrepancy: Task Scheduler reconcile/uninstall previously used fixed names with forced replacement/deletion. Both tasks now fail closed on an unmanaged occupant.

Dependency inspection on 2026-08-31 reported no known vulnerable package in any project. Production projects reported no deprecated packages. `Microsoft.ApplicationInsights 2.22.0` is a deprecated transitive dependency of the test toolchain only; it is not published with the product. Available major test-tool and transitive updates were recorded but not adopted without a compatibility cycle.

## Data minimization and retention

Ordinary backups contain only `assistant.db`, `founders.db`, and their bounded manifest. They exclude secret files, browser profiles, logs, screenshots, diagnostics, and raw/profile/report files. Founder Scout does not store profile photographs. Raw profiles, browser error artifacts, reports, central artifacts, operational notifications, and summaries have independent retention policies. Active-run data is excluded from destructive retention.

Never place real profiles, session state, cookies, credentials, or diagnostic artifacts in source control. Before sharing a diagnostic export, inspect it and treat it as confidential even though automated redaction has run.

## Residual risks and deployment assumptions

- Malicious code running as the same Windows user can read owner-accessible databases/browser profiles, invoke the local UI, and unprotect DPAPI data. V1 is not a sandbox against its owner account.
- An administrator, backup product, debugger, or kernel-level component can bypass ordinary per-user protections.
- A custom data root with permissive inherited ACLs weakens confidentiality. The supported default is the current user's LocalAppData; operators must secure custom roots.
- Temporary secret-envelope ACL tightening can be unavailable on unsupported filesystems. The default Windows LocalAppData placement is required for the supported security posture.
- TLS protects transport but does not make a user-configured endpoint trustworthy. Only configure AI/source endpoints whose operator and certificate chain you trust.
- A publish SHA-256 manifest detects damage/tampering relative to the manifest but provides no publisher authenticity. Obtain the archive and checksum through a trusted channel.
- Live external markup, model behavior, firmware wake, provider availability, Windows policy, and remote-access security remain outside deterministic release tests.

## Incident response

Pause schedules, allow or cancel active work deliberately, and preserve only the relevant redacted audit/log records and validated database backups. Rotate a suspected credential at its source, then replace its protected secret value. Expire the affected browser session and recreate its dedicated profile when session state may be exposed. Do not upload the data directory or browser profile to an issue tracker.

Uninstall preserves data by default. Explicit data removal deletes databases, DPAPI secret files, browser profiles, logs, artifacts, reports, and backups beneath a validated application data root and is irreversible unless separately backed up.
