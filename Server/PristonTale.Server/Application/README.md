# Application services

Transport-free use cases and the shared authoritative world live in
[Shared/PristonTale.Application](../../../Shared/PristonTale.Application), used by the game server and documented in
[docs/application.md](../../../docs/application.md).

Keep this server-local directory for orchestration that is specific to server hosting. Do not duplicate auth,
character/session services or world rules here. The TCP boundary in [Net/](../Net) maps requests to application inputs
and outcomes; see [docs/server.md](../../../docs/server.md).

See [AGENTS.md](../../../AGENTS.md) for repository guidance.
