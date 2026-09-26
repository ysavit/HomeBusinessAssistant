# Sample Business Agent

This deliberately harmless agent demonstrates the Home Business Assistant SDK and generic configuration UI. `run` inspects only top-level files in a configured relative folder beneath its Runner-assigned data directory, emits lifecycle/progress/metric events, and writes one JSON report beneath its assigned artifact directory. `diagnose` validates configuration without doing business work. `protocol-demo` exercises the SDK directly.

It is disabled when first discovered and is excluded from production publishing unless `scripts/publish.ps1 -IncludeSampleAgent` is used. It never sends notifications; `notificationSecretReference` exists only to demonstrate the secret-reference convention and separate protected-secret UI.
