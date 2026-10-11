# Mobile controller design verification

Reference: [generated mobile concept](mobile-control-concept.png), 852 × 1846 pixels. It defines the header, serif task heading, device row, two modes, composer, task status, three controls and connection footer. Implementation uses Manrope and Georgia, pale green surfaces, forest text and a dark green action color; Autonomous switches the complete palette to red.

Browser-first verification used connected Chrome, including the live AWS page and real Cognito sign-in form. IAB was unavailable. Chrome's raw-CDP capability did not support the isolated sign-in fixture, so only the simulated-device workflow used a separate headless Playwright Chromium browser. It intercepted the token response locally and talked to a loopback API; no production authentication bypass was added. The simulated PC made no model calls or native input.

The mobile screenshot was captured at 426 × 923 CSS pixels and device scale 2, matching the concept's native viewport resolution. Full-page capture includes extra vertical space for browser font metrics and text wrapping. Additional widths 320, 390 and 1280 had no horizontal overflow. Connected Chrome was returned to its normal viewport.

`view_image` inspected both the concept and final mobile implementation, plus the Autonomous state, desktop layout and latest live Chrome screenshot. The implementation faithfully preserves the concept's structure and visual direction with the functional adaptations below; it is not a claim of pixel identity.

| Comparison | Concept and render evidence | Resolution |
|---|---|---|
| Copy and hierarchy | Same product name, heading, subtitle, mode names, composer label and Send task CTA | Preserved; actual connection/task data replaces illustration text |
| Layout | Header → intro → device → mode/composer → current task → footer | Preserved on phones; wide layouts place composer and task side by side |
| Typography | Serif heading/current-task title with sans-serif controls | Georgia and locally bundled Manrope; explicit form-control typography |
| Palette | Pale green background, forest text and green primary action | Shared variables; corrected inherited text color so Autonomous text also turns red |
| Containers and spacing | Thin device/composer borders, open task status, horizontal separators | Preserved; variable-length instructions expand instead of clipping |
| Icons and blob | Organic green mark, laptop, arrow, pause, play and stop | Animated code-native blob supports mode color/reduced motion; consistent SVG controls |
| States and motion | Illustrated connected, idle Guided screen | Real disabled idle controls, acknowledgements/errors/offline states, red Autonomous and reduced motion |

Above-the-fold copy diff: signed-in idle preserves all substantive concept labels. Sign out becomes Sign in when unauthenticated. “Windows PC / Connected” becomes the actual PC name/connectivity. A signed-out account panel is necessary for authentication. A live task changes status/instruction and shows its acknowledgement. No marketing sections, metrics or invented activity were added.

Intentional adaptations: code-native blob, two-column desktop layout, authentication/connection help/disconnect/screen-check controls, disabled unavailable actions, dynamic task copy and natural full-page height. The static concept's enabled Pause/Resume/Stop controls are disabled when there is no task. No unresolved clipping, missing assets or broken mode colors remained in inspected states.

Evidence:

- [Mobile Guided](mobile-control-render.png) — connected idle state with a simulated PC.
- [Mobile Autonomous after Resume](mobile-control-autonomous.png) — explicit synthetic task/acknowledgement.
- [Wide layout](desktop-control-render.png) — simulated stopped task.
- [Live AWS page in connected Chrome](control-live-chrome.png) — genuine signed-out state.

The first fixture run exceeded the API rate limit because the test PC polled four times faster than the shipped PC. The fixture was corrected to the shipped two-second cadence and the complete focused workflow passed. No production rate limit was relaxed.
