# Sample Business Agent

This generated external-process agent demonstrates the Home Business Assistant SDK and generic configuration UI. Rename its local-folder report behavior for the real business use case, while keeping the Runner-owned execution context, protocol, data/artifact boundaries, and cancellation handling intact.

## Generated workflow

1. Review `manifest.json` and keep its identifier equal to the installation directory name.
2. Replace the safe primitive fields in `configuration.schema.json` and the matching typed configuration record.
3. Implement business I/O behind an Infrastructure interface; do not put platform persistence or Windows APIs in Application.
4. Run `dotnet test` from this directory.
5. Publish the Agent executable, then install its output with `manifest.json` and `configuration.schema.json` under `<AgentDirectory>/<agent-id>/`.
6. Run `HomeBusinessAssistant.Runner.exe scan-agents`, review the disabled registration, configure it, and explicitly enable/schedule it.
7. Add an explicit entry to the repository's `packaging/agents.json` only when the agent is ready for supported packaging.

`run` currently inspects only top-level files beneath its Runner-assigned data directory, emits lifecycle/progress/metric events, and writes one JSON report beneath its assigned artifact directory. `diagnose` validates configuration without doing business work. `protocol-demo` exercises the SDK directly. The secret-reference field demonstrates protected secret storage; the agent never echoes or resolves that value.

See the repository's `docs/adding-an-agent.md` for the supported extension and security checklist. Generate the optional Domain project with `--includeDomain true` only when the agent owns meaningful business entities or rules.
