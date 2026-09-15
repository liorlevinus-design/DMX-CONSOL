# ChatGPT UX integration branch

This branch is intentionally isolated from `master` while integrating GUI selection with the shared Command Surface.

Current scope:
- Mirror fixture selections made in Channels/Fixtures views into the existing `CommandComposer`.
- Keep Fixtures view on the shared `EditorContextStack` path.
- Preserve existing Application-layer selection commands; no raw DMX or alternate business-logic path.

Still deliberately deferred:
- Full selection-cycle semantics after execution.
- CLEAR / CLEAR CLEAR / contextual LAST recall.
- Group semantic-selection history.
- Broader EditorToolBar object families.

Do not merge until the branch is built/tested and manually checked in `/workspace-preview`.
