# ChatGPT integration branch notes

Branch: `chatgpt/ux-integration-fixes`
Base: `master` @ `7f49ed6`

## Implemented on this branch

- Fixtures and Channels GUI selection mirrors into the existing CommandComposer.
- Fixtures View now enters the same EditorContext path as Channels/Command Surface.
- Group Apply participates in the shared selection cycle and mirrors its resolved fixture selection into the Command Surface.
- Fixture is now the default command object: `1 THRU 5` means Fixtures 1 thru 5.
- Selection-only commands accumulate across Enter without needing `+`.
- `AT` closes the current selection cycle but leaves the used fixtures visibly selected.
- The next new Fixture/Group selection after execution starts fresh automatically.
- Single empty-line `CLEAR` restores the selection state before the last recorded selection gesture.
- `CLEAR CLEAR` clears the entire selection through the normal undoable command path.
- Group/range gestures are recorded as one selection step, so single CLEAR can remove the whole gesture rather than only one member fixture.

## Still intentionally deferred

- Contextual recall (`Fixture .`, `Group .`, `AT .` / LAST).
- Parameter-level selection-cycle tracking beyond current Fixture/Group target selection.
- Full Groups pool/tile redesign and overwrite/update interaction.
- Local browser verification on `/workspace-preview`.
- Full `dotnet test` pass: GitHub Actions is not configured and the current ChatGPT container cannot resolve github.com, so this branch remains Draft until tested on the user's machine/Claude environment.
