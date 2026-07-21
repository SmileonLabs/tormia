# Tormia

Tormia is a Unity 6 prototype that connects a fact-based ontology simulation to character customization, animation selection, quests, and world interaction.

## Requirements

- Unity `6000.4.7f1`
- Git LFS
- Windows, macOS, or Linux with the matching Unity editor modules

## Getting started

1. Clone the repository and open the project root in Unity Hub.
2. Wait for package resolution and the first asset import to finish.
3. Open `Assets/Scenes/TormiaMain.unity`.
4. Enter Play Mode.

If the main scene has not been generated yet, run **Tools > Tormia > Project > Ensure Main Scene**. The command creates the project-owned scene from the third-party character demonstration scene without modifying the source asset.

## Architecture

For the product-level architecture, ontology data policy, authority boundaries,
and established design decisions that should guide future work, read
[`docs/PROJECT_CONTEXT.md`](docs/PROJECT_CONTEXT.md) and its Korean companion
[`docs/PROJECT_CONTEXT.ko.md`](docs/PROJECT_CONTEXT.ko.md) first.

## Development harness

The repository includes a lightweight harness for repeatable human/AI-assisted
development. Read [`AGENTS.md`](AGENTS.md), then run:

```powershell
.\scripts\verify-development.ps1
# When the local Docker stack is expected to be running:
.\scripts\verify-development.ps1 -RequireServices
# Unity regression suites (EditMode by default):
.\scripts\run-unity-harness-tests.ps1
```

Core regression scenarios are listed in
[`tests/harness/core-regression-scenarios.json`](tests/harness/core-regression-scenarios.json).
Use the bilingual templates in [`docs/changes`](docs/changes) for system-level
decisions and implementation records.

- `Assets/Scripts/Ontology/Core`: engine-independent facts, rules, actions, quests, simulation, and save models.
- `Assets/Scripts/Ontology/Unity`: scene adapters, world bootstrap, ticking, input, animation, character parts, and persistence.
- `Assets/Scripts/Ontology/UI`: runtime diagnostics and character customization UI.
- `Assets/Scripts/Ontology/Editor`: authoring, validation, scene setup, and test utilities.
- `Assets/Data/Ontology`: runtime ScriptableObject databases.

Runtime flow:

```text
Scene OntologyObjects -> World facts -> Available actions -> Rule simulation
  -> Quest state -> Animation/character adapters -> Runtime UI
```

## Tests

From Unity, open **Window > General > Test Runner** and run EditMode and PlayMode tests.

Command-line EditMode example:

```powershell
& "C:\Program Files\Unity\Hub\Editor\6000.4.7f1\Editor\Unity.exe" `
  -batchmode -nographics -projectPath . -runTests -testPlatform EditMode `
  -testResults Logs/editmode-results.xml -logFile Logs/editmode-tests.log
```

GitHub Actions runs EditMode and PlayMode tests with GameCI. Configure the repository secrets `UNITY_LICENSE`, `UNITY_EMAIL`, and `UNITY_PASSWORD` before enabling CI for a Unity Personal license.

## Local platform services

The production-oriented local PostgreSQL and Redis environment is in
[`infrastructure`](infrastructure/README.md). It stores only durable world data;
the Unity authority server will keep observations and inferred facts in memory.

## Content authoring

- Actor animation setup: **Tools > Ontology > Actor Animation Setup Wizard**
- Ontology maps and terrain: tools under **Tools > Ontology**
- Runtime databases: `Assets/Data/Ontology`

Do not edit third-party demonstration assets for project-specific changes. Make changes in `TormiaMain.unity` or project-owned prefabs and data assets.

## Large assets

The repository uses Git LFS for assets large enough to materially affect clone size. Run `git lfs install` before committing or checking out large asset changes. When adding a new file larger than 10 MB, review whether it should be added to `.gitattributes` instead of storing the full binary in regular Git history.
