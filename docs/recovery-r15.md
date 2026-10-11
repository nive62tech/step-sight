# Banner recovery, resume, and voice companion

The reported IRCTC banner pause exposed several generic recovery problems:

- A failed coordinate lookup removed the click proposal, and the desktop immediately paused. It now asks for at most two fresh screenshot decisions for an ordinary unavailable target. The model receives the previous uncertainty and can choose a visible dismissal control or a bounded scroll inside the visible panel.
- The model instructions did not distinguish an obstructing notice from a loading page or an unknowable state. They now describe those distinctions, panel scrolling, and fresh replanning on Resume. Sensitive actions and genuinely unknown dispatch outcomes still require the user.
- Local risk matching prohibited even a wheel scroll when the instruction mentioned login/payment. Scrolling a grounded reading region is permitted under the existing low-risk grant; the same sensitive click remains prohibited.
- Resume could skip the observation because an earlier turn still held the local gate, or collide with a server inference that survived HTTP cancellation. Resume now drains local work, invalidates the old server version, waits for its inference lock, and uses the returned authoritative pending step. Stop and epoch changes still win over late results.
- The UI's general busy guard could silently drop Resume. It now waits for the old operation and displays that wait, rejecting a queued resume if Stop changes the epoch.

The companion work already in progress is included: a locally rendered animated 3D blob, click-to-record voice drafts with a 30-second limit, typing, a Guided/Autonomous toggle, and matching green/red panels, cursor, outline and tray. Stop or mode changes discard late transcripts; an edited draft clears autonomous consent. Microphone callbacks belong to individual recording sessions so an old stop cannot cancel a newer recording.

Focused verification on 11 October 2026: 7 Core/Platform cases for banner scrolling, bounded recovery, pause/resume races and Stop; 7 backend cases for pending-turn recovery and verification; 3 voice-draft/cancellation cases. All 17 passed. The changed backend files passed Ruff. The Windows app compiled and published. Two headless companion renders inspected Guided and Autonomous listening states with disarmed native adapters.

These are deterministic regression checks and UI renders, not a completed live IRCTC workflow. No real cursor/keyboard actions, microphone recording, broad test suite, or paid model probe was run for r15. The new model instructions' real-site accuracy remains to be confirmed during normal use. Existing screenshot freshness, focus, scope and input limits remain enforced.
