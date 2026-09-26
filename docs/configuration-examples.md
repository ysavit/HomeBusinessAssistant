# Configuration and Manifest Examples

## Bootstrap appsettings.json

Only bootstrap values belong here.

```json
{
  "Host": {
    "Url": "http://127.0.0.1:5180",
    "DatabaseFileName": "assistant.db",
    "ScheduleIntervalSeconds": 30,
    "WakeIntervalSeconds": 60,
    "RecoveryIntervalSeconds": 120,
    "NotificationIntervalSeconds": 15
  }
}
```

The packaged Host defaults its data directory to `%LOCALAPPDATA%/HomeBusinessAssistant/data`, its agent root to `%LOCALAPPDATA%/HomeBusinessAssistant/app/agents`, and Runner/manifests to the validated application root. Development builds discover the repository's built Release/Debug Runner and checked-in manifests. Override bootstrap values with `HBA_Host__DataDirectory`, `HBA_Host__AgentDirectory`, `HBA_Host__ManifestDirectory`, `HBA_Host__RunnerExecutablePath`, `HBA_Host__RunnerWorkingDirectory`, or equivalent `--Host:<Name>` command-line keys. `--urls` overrides the URL. Paths are normalized and Runner paths must remain inside `Host:ApplicationRoot`; the URL must be exact HTTP loopback on `127.0.0.1`.

Host loop intervals are bounded from one second through one hour. Rolling Host logs are retained as 30 daily files under `<data>/logs`; this is a fixed bootstrap safety policy in Stage 07, not mutable agent configuration.

Mutable agent configuration is stored as immutable canonical revisions in SQLite. Known secret-bearing properties must contain a validated `secret://namespace/name` reference; the referenced value is stored separately under the data root's DPAPI-protected `secrets` directory.

## Versioned schedule definitions

The initial Windows schedule time zone is `Central Standard Time`. Each persisted schedule stores its command, arguments, policies, and one versioned definition whose lower-case `type` must match the schedule kind. Founder Scout discovery uses completion-based cooldown:

```json
{
  "version": "1.0",
  "type": "fixed-delay",
  "delay": "00:10:00",
  "initialDueAtUtc": null,
  "startImmediately": true
}
```

Wake & Remote weekdays use local wall-clock time in the schedule's configured time zone:

```json
{
  "version": "1.0",
  "type": "selected-weekdays",
  "days": ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday"],
  "localTime": "07:55:00"
}
```

Retry policy is separately versioned; retries remain child occurrences and never advance fixed-delay cadence:

```json
{
  "version": "1.0",
  "maximumRetries": 2,
  "initialDelay": "00:01:00",
  "maximumDelay": "01:00:00"
}
```

## Founder Scout manifest

```json
{
  "manifestVersion": "1.0",
  "id": "founder-scout",
  "displayName": "Founder Scout",
  "description": "Captures, evaluates, and drafts introductions for startup founder profiles without sending invitations.",
  "version": "1.5.0",
  "executable": "FounderScout.exe",
  "supportedCommands": [
    "run",
    "discover",
    "analyze",
    "authenticate",
    "import",
    "report",
    "diagnose",
    "record-fixture"
  ],
  "capabilities": [
    "fixture-import",
    "profile-capture",
    "playwright-browser-discovery",
    "persistent-browser-profiles",
    "manual-browser-authentication",
    "browser-diagnostics",
    "durable-discovery-checkpoints",
    "candidate-persistence",
    "normalized-profile-processing",
    "protected-attribute-redaction",
    "deterministic-fast-screening",
    "candidate-deduplication",
    "durable-processing-leases",
    "structured-ai-evaluation",
    "deterministic-candidate-scoring",
    "evidence-grounding-validation",
    "evaluation-input-caching",
    "human-review-invitation-drafts",
    "immutable-draft-revisions",
    "manual-invitation-queue",
    "manual-outcome-tracking",
    "candidate-results-ui",
    "safe-multiformat-reporting",
    "raw-artifact-retention",
    "domain-diagnostics"
  ],
  "defaultTimeoutSeconds": 3600,
  "defaultConcurrencyPolicy": "Forbid",
  "supportsScheduling": true,
  "supportsManualRun": true,
  "requiresInteractiveUserSession": true,
  "configurationSchemaVersion": "1.0"
}
```

## Wake & Remote manifest

```json
{
  "manifestVersion": "1.0",
  "id": "wake-remote",
  "displayName": "Wake & Remote",
  "description": "Maintains a bounded local wake and remote-availability window without changing permanent power policy.",
  "version": "1.1.0",
  "executable": "WakeRemote.exe",
  "supportedCommands": [
    "run",
    "diagnose",
    "check-remote",
    "wake-test"
  ],
  "capabilities": [
    "keep-awake",
    "availability-window",
    "network-readiness",
    "remote-provider-readiness",
    "read-only-diagnostics"
  ],
  "defaultTimeoutSeconds": 14400,
  "defaultConcurrencyPolicy": "Forbid",
  "supportsScheduling": true,
  "supportsManualRun": true,
  "requiresInteractiveUserSession": true,
  "configurationSchemaVersion": "1.0"
}
```

## Founder Scout typed configuration

Secrets are represented by references. Browser authentication lives in protected browser-profile directories, not in this JSON.

```json
{
  "schemaVersion": "1.0",
  "dataDirectory": "%LOCALAPPDATA%/HomeBusinessAssistant/data/founder-scout",
  "discovery": {
    "enabled": true,
    "browserConcurrency": 1,
    "maxNewProfilesPerRun": 20,
    "maxViewedProfilesPerRun": 40,
    "maxNewProfilesPerDay": 60,
    "maxRuntimeSeconds": 600,
    "cooldownAfterCompletionSeconds": 600,
    "stopAfterConsecutiveKnownProfiles": 8,
    "stopOnAuthenticationFailure": true,
    "stopOnAccessDenied": true,
    "stopOnThrottle": true,
    "stopOnChallenge": true,
    "automaticFailoverAfterEnforcementSignal": false
  },
  "analysis": {
    "enabled": true,
    "batchSize": 20,
    "maximumConcurrency": 2,
    "fastScreenEnabled": true,
    "deepAnalysisThreshold": 65,
    "skipUnchangedProfiles": true,
    "maximumRetries": 3
  },
  "processing": {
    "parserVersion": "founder-profile-parser-1.0",
    "redactorVersion": "founder-profile-redactor-1.0",
    "rulesetVersion": "founder-screen-rules-1.0",
    "minimumParserCompleteness": 0.35,
    "maximumConsecutiveParserFailures": 3,
    "processingLeaseSeconds": 300,
    "deepAnalysisThreshold": 65,
    "monitorThreshold": 45,
    "preferNonTechnical": true,
    "requireLocationCompatibility": false,
    "hardFilterUnpaidImplementationLabor": true,
    "filterIdenticalTechnicalPreference": true
  },
  "ranking": {
    "strongConnectThreshold": 82,
    "exploratoryThreshold": 72,
    "monitorThreshold": 62,
    "minimumConfidence": 0.60,
    "topCandidateCount": 30,
    "manualInvitationQueueSize": 15,
    "reserveQueueSize": 15
  },
  "priority": {
    "ourFitWeight": 0.55,
    "founderQualityWeight": 0.25,
    "confidenceWeight": 0.15,
    "activityWeight": 0.05,
    "maximumRiskPenalty": 100
  },
  "activity": {
    "recentDays": 7,
    "recentScore": 100,
    "activeDays": 30,
    "activeScore": 80,
    "staleDays": 90,
    "staleScore": 55,
    "olderScore": 20,
    "unknownScore": 25
  },
  "invitation": {
    "generateShortVersion": true,
    "generateDetailedVersion": true,
    "maximumCharacters": 1000,
    "requireCandidateSpecificFact": true,
    "requireComplementarityStatement": true,
    "requireConversationTopic": true,
    "similarityThreshold": 0.85
  },
  "ai": {
    "provider": "AzureOpenAI",
    "endpoint": "",
    "deployment": "founder-evaluator",
    "apiKeySecretReference": "secret://founder-scout/azure-openai-key",
    "requestTimeoutSeconds": 120,
    "apiVersion": null,
    "maxOutputTokens": 6000,
    "temperature": null,
    "reasoningEffort": null,
    "providerPolicyVersion": "responses-provider-1.0"
  },
  "retention": {
    "rawProfileDays": 30,
    "errorArtifactDays": 14,
    "reportDays": 90
  },
  "persona": {
    "schemaVersion": "1.0",
    "reference": "founder-persona/default",
    "displayName": "Local founder",
    "targetRole": "Technical Co-Founder / CTO",
    "strengths": [
      "Software architecture",
      "Product engineering",
      "Responsible AI workflows"
    ],
    "seeking": [
      "Complementary business leadership",
      "Customer access or distribution",
      "Founder-level commitment"
    ],
    "messageTone": "Direct, thoughtful, founder-to-founder",
    "avoidClaims": [
      "Do not imply a commitment to join.",
      "Do not promise investment or delivery.",
      "Do not mention automated scoring."
    ]
  },
  "startupSchool": {
    "adapterVersion": "startup-school-1.0",
    "entryUrl": "https://www.startupschool.org/cofounder-matching",
    "allowedHosts": ["www.startupschool.org", "startupschool.org"],
    "authenticatedLocators": [
      { "kind": "css", "value": "[data-testid='cofounder-matching']" },
      { "kind": "role", "value": "heading", "name": "Co-Founder Matching" }
    ],
    "loginLocators": [
      { "kind": "css", "value": "form[action*='login']" },
      { "kind": "text", "value": "Log in", "exact": true }
    ],
    "profileLinkLocators": [
      { "kind": "css", "value": "a[data-profile-id]" },
      { "kind": "css", "value": "[data-testid='profile-card'] a[href]" }
    ],
    "profileRootLocators": [
      { "kind": "css", "value": "main[data-profile-id]" },
      { "kind": "css", "value": "[data-testid='founder-profile']" }
    ],
    "displayNameLocators": [
      { "kind": "css", "value": "[data-testid='profile-name']" },
      { "kind": "role", "value": "heading" }
    ],
    "nextPageLocators": [{ "kind": "role", "value": "link", "name": "Next" }],
    "loadMoreLocators": [{ "kind": "role", "value": "button", "name": "Load more" }],
    "challengeLocators": [{ "kind": "css", "value": "[data-testid='challenge']" }],
    "throttleLocators": [{ "kind": "css", "value": "[data-testid='throttled']" }],
    "accessDeniedLocators": [{ "kind": "css", "value": "[data-testid='access-denied']" }],
    "discoveryMode": "pagination",
    "navigationTimeoutSeconds": 30,
    "authenticationTimeoutSeconds": 900,
    "settleDelayMilliseconds": 500,
    "minimumRequestSpacingMilliseconds": 1000,
    "maximumTransientNavigationRetries": 1,
    "browserChannel": null,
    "headlessDiscovery": true,
    "storeRawHtml": false
  }
}
```

`provider` is `OpenAI` or `AzureOpenAI` in production. `DiagnosticFake` with the exact `diagnostic://fake` endpoint is reserved for synthetic tests and local smoke validation. The OpenAI endpoint may be empty to select `https://api.openai.com/v1/`; Azure requires an HTTPS resource endpoint and is normalized to `/openai/v1/`. That Azure v1 route uses implicit versioning, so the current adapter requires `apiVersion: null`. The configured model or Azure deployment is sent as the Responses `model` value. Runner resolves `apiKeySecretReference` through the protected secret store and passes the value only in its short-lived private execution-input file.

Deep analysis can be run independently or after deterministic screening:

```powershell
FounderScout.exe analyze --phase deep --max 20 <standard Agent SDK options>
FounderScout.exe analyze --phase all --max 20 <standard Agent SDK options>
```

Prompt `founder-evaluation-prompt-1.0`, scorecard `yc-scorecard-v1`, and response schema `founder-evaluation-response-1.0` are versioned inputs to the evaluation cache. Changing the normalized input, persona, prompt, scorecard, provider/model policy, ranking/draft policy, or activity bucket invalidates reuse. Invitations are generated only as human-review drafts; no command sends them.

Locator arrays are ordered fallbacks. Only semantic roles/text/labels and stable attributes are accepted; XPath and positional `nth-child` selectors are rejected. Source URLs must remain on the explicit host allow-list. `browserChannel` is either null for the Playwright-managed Chromium revision or one allow-listed installed Chrome/Edge channel. `storeRawHtml` is false by default; when explicitly enabled, the raw content-addressed capture envelope includes a bounded DOM-sanitized HTML copy with forms, scripts, images, and session-like values removed. It is not copied into normalized snapshot JSON.

## Browser account record

Browser accounts are mutable domain records, not appsettings entries.

```json
{
  "id": "account-01",
  "displayName": "Primary founder-search account",
  "browserProfileRelativePath": "browser/account-01",
  "enabled": true,
  "sessionStatus": "Unknown",
  "assignedSegmentIds": ["us-nontechnical-fulltime"],
  "lastAuthenticatedAtUtc": null,
  "lastSuccessfulRunAtUtc": null
}
```

## Founder Scout fixture capture envelope

Stage 09 accepts a single object, an array of up to 100 objects, JSON Lines, or bounded plain text. JSON and JSONL entries use schema 1.0; the file must be absolute and beneath the Runner-assigned Founder Scout `imports` directory.

```json
{
  "captureSchemaVersion": "1.0",
  "source": "fixture",
  "sourceAccountId": "fixture-account",
  "sourceSegmentId": "fixture-segment",
  "sourceProfileKey": "candidate-001",
  "profileUrl": "https://example.invalid/profile/candidate-001",
  "capturedAtUtc": "2026-08-30T20:00:00Z",
  "displayName": "Synthetic Candidate One",
  "rawText": "Synthetic local test fixture.",
  "structuredFields": {
    "skills": ["operations"]
  },
  "sourceAdapterVersion": "fixture-1.0"
}
```

The reader rejects traversal, reparse-point escapes, oversized inputs, unsupported fields, browser/session secrets, and protected demographic/photo fields. The raw envelope is stored as a content-addressed artifact before the snapshot transaction queues later work. Repeating the same strong identity and content hash is idempotent.

## Founder persona

```json
{
  "schemaVersion": "1.0",
  "reference": "founder-persona/default",
  "displayName": "Local founder",
  "targetRole": "Technical Co-Founder / CTO",
  "strengths": [
    "Software architecture",
    "Product engineering",
    "Responsible AI workflows"
  ],
  "seeking": [
    "Complementary business leadership",
    "Customer access or distribution",
    "Founder-level commitment"
  ],
  "messageTone": "Direct, thoughtful, founder-to-founder",
  "avoidClaims": [
    "Do not imply a commitment to join.",
    "Do not promise investment or delivery.",
    "Do not mention automated scoring."
  ]
}
```

## Scorecard

```json
{
  "version": "yc-scorecard-v1",
  "categories": [
    { "key": "founderExecution", "displayName": "Founder execution quality", "maximum": 20 },
    { "key": "commitmentAndPosture", "displayName": "Commitment and co-founder posture", "maximum": 15 },
    { "key": "tractionAndValidation", "displayName": "Traction and validation", "maximum": 15 },
    { "key": "marketPotential", "displayName": "Market potential", "maximum": 15 },
    { "key": "gtmAndDomainAdvantage", "displayName": "GTM and domain advantage", "maximum": 10 },
    { "key": "ctoFit", "displayName": "CTO fit", "maximum": 10 },
    { "key": "ideaClarity", "displayName": "Idea and problem clarity", "maximum": 5 },
    { "key": "moatPotential", "displayName": "Moat potential", "maximum": 5 },
    { "key": "technicalFeasibility", "displayName": "Technical feasibility", "maximum": 5 }
  ],
  "riskPenalties": {
    "unpaidDeveloperRisk": 20,
    "founderReservesMostEquity": 15,
    "overscopedTechnicalBuild": 10,
    "noCustomerAccessPath": 10,
    "ctoExpectedToOwnEverything": 10,
    "indefinitePartTimeCommitment": 10,
    "unavailableExternalDependency": 10
  }
}
```

## Wake & Remote configuration

```json
{
  "schemaVersion": "1.0",
  "timeZoneId": "Central Standard Time",
  "networkReadyTimeoutSeconds": 120,
  "networkProbeIntervalSeconds": 5,
  "windowHeartbeatIntervalSeconds": 60,
  "maximumWakeStalenessSeconds": 900,
  "keepDisplayOn": false,
  "releaseToNormalPowerPolicyAfterWindow": true,
  "forceSleepAfterWindow": false,
  "remoteProvider": {
    "kind": "ChromeRemoteDesktop",
    "serviceNames": ["chromoting"],
    "processNames": ["remoting_host"],
    "checkLocalListener": false,
    "listenerHost": "127.0.0.1",
    "listenerPort": 3389,
    "diagnosticReady": false
  },
  "dnsProbe": {
    "enabled": false,
    "hostName": null
  },
  "tcpProbe": {
    "enabled": false,
    "host": null,
    "port": 0
  }
}
```

Availability windows live in central schedules, not in this configuration revision. The schedule stores a duration/required-provider template; each planned occurrence captures a stable `windowInstanceId`, scheduled UTC wake, immutable UTC deadline, provider-required flag, and explicit immediate-misfire allowance. Normal window schedules use `WakePolicy.Required` and a timeout equal to the window duration plus bounded startup grace.

`DiagnosticFake` is an explicit development/test-only provider kind. Any metric, artifact, or summary produced with it is labeled `DiagnosticFake`; it is not evidence that Chrome Remote Desktop or RDP is available. Windows RDP checks are read-only and depend on a Windows edition/policy capable of hosting RDP plus independently configured firewall/network access. Wake Remote never enables RDP or changes the firewall.

## Agent JSONL events

```json
{"protocolVersion":"1.0","type":"started","runId":"...","sequence":1,"timestampUtc":"2026-08-29T18:00:00Z","payload":{"command":"analyze"}}
{"protocolVersion":"1.0","type":"progress","runId":"...","sequence":2,"timestampUtc":"2026-08-29T18:00:03Z","payload":{"current":5,"total":20,"percentage":25,"phase":"profile-processing","message":"Parsing and screening one claimed profile."}}
{"protocolVersion":"1.0","type":"metric","runId":"...","sequence":3,"timestampUtc":"2026-08-29T18:00:04Z","payload":{"name":"processing.profiles.completed","numericValue":20,"unit":"profiles"}}
{"protocolVersion":"1.0","type":"checkpoint","runId":"...","sequence":4,"timestampUtc":"2026-08-29T18:00:07Z","payload":{"key":"founder-scout-screening","state":{"phase":"screen","claimed":20,"completed":20,"changed":20,"unchanged":0,"reasonCodeDistribution":{"screen.outcome.deepAnalyze":8}}}}
{"protocolVersion":"1.0","type":"summary","runId":"...","sequence":5,"timestampUtc":"2026-08-29T18:00:08Z","payload":{"status":"Completed","text":"Founder Scout processed 20 of 20 claimed profiles without AI calls.","data":{"result":"Completed","phase":"screen","claimed":20,"completed":20,"noAiCalls":true}}}
{"protocolVersion":"1.0","type":"completed","runId":"...","sequence":6,"timestampUtc":"2026-08-29T18:00:08Z","payload":{"exitCode":0}}
```
