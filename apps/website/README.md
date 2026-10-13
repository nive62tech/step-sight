# Desktop Guide website

A static, dependency-free product website with a six-chapter presentation view, three illustrative task walkthroughs, keyboard-accessible scenario tabs, reduced-motion support, and a generated release archive. Fonts are served locally under the included SIL Open Font License. The website never captures a screen or connects to the pilot API.

## Presentation website

The current source introduces Desktop Guide, explains the friction it addresses, demonstrates everyday possibilities, explains onboarding and the two modes, and provides the Windows download. Select **Present** to show one chapter at a time. Use the visible Previous/Next controls or Left/Right, Page Up/Page Down, Home/End keys; **Escape** exits. Arrow keys inside the scenario tabs select scenarios instead of changing chapters. This view does not request browser fullscreen access. On smaller screens, each chapter remains scrollable.

The three four-step examples are **Meet & share** (create a meeting, copy its link, open WhatsApp, review a message to Amal), **Tatkal booking** (route, search preferences, availability review, manual confirmation), and **Plan your day** (create an event, details, reminder, final review). They use local authored data and explicitly identify manual handoffs. No real message, booking, meeting, calendar event or clipboard transfer happens. The scheduling example does not imply an unattended scheduler. Signed-in provider workflows remain unqualified.

The generated release archive and manifest validation are unchanged. The source uses the current manifest at build time, including concurrent release updates; it does not pin a release version in marketing copy. Downloads, release notes, checksums, product copy and FAQs remain available without JavaScript.

The sections below include historical deployment and design evidence. The presentation redesign is a local source change until separately approved for deployment.

### Presentation design and verification

Five built-in Image Gen concept briefs established the complete page: (1) a charcoal/citron editorial hero and calendar companion, (2) three open problem/solution rows, (3) a tabbed meeting/Tatkal/calendar walkthrough, (4) numbered onboarding and green/coral mode panels, and (5) the download, release history and FAQs. The implementation uses semantic HTML/CSS rather than rasterized interface controls.

| Visual comparison | Implemented result |
| --- | --- |
| Palette and typography | Kept near-black, warm white, citron and Manrope; increased the hero and interface type after reviewing native-size browser screenshots. |
| Hero composition | Preserved the three-line headline, two actions, calendar and overlapping companion; moved the companion so the meeting target remains readable. |
| Content and section rhythm | Kept the problem rows, scenario workspace, numbered steps, mode panels and restrained download area. |
| Icons and containers | Used consistent outline value icons, thin section rules and framing only where it clarifies the example or download. |
| Presentation and responsive layout | Verified all six chapters, tab keyboard isolation, Escape, mobile stacking and absence of horizontal overflow from 320px to 1505px. Adjusted demo spacing to leave the presentation controls clear. |
| Copy and product truth | Main headline, navigation and CTA copy match the brief. Removed invented dates, usable-looking provider URLs and decorative OS art from the concepts. Added current pilot limits, manual handoffs, phone control and account requirements. |

Local validation: five release tests and the static build passed; the build verified all five current ZIPs against their manifest sizes and hashes. Headless Chromium checked all 12 scenario states, replay/restart, keyboard tabs, six presentation chapters, exit and boundary controls, release filtering, FAQs, all 11 download/checksum links, reduced motion and downloads with JavaScript disabled. No runtime errors or external requests were observed. Viewports were 1505×1045, 1440×1000, 1024×768, 768×1024, 390×844 and 320×760, with section comparisons at 1435×1100. The built-in browser returned “Browser is not available: iab”, so the existing local Playwright Chromium installation was used. The concepts and final captures were inspected with the image viewer.

These are local website and illustrative-flow checks. No real Windows input, live model inference, signed-in provider workflow, cloud deployment or full application test suite was run for this redesign. Other browser engines and a complete assistive-technology audit remain untested.

Public website: **https://d30cb15ggw36n1.cloudfront.net**

Deployed on 11 October 2026 (Asia/Kolkata) in the authorized AWS account using stack `desktop-guide-website`, S3 bucket `desktop-guide-website-sitebucket-7nd5uxh7tgpd` in `ap-south-1`, and CloudFront distribution `E3MZOH3UT1OX2R`. This is a separate stack from the existing application pilot.

## Build and preview

Requires Node.js 22+. From this directory:

```powershell
npm run check
npm run build
npm run preview
```

Open `http://127.0.0.1:4173`. The generated `dist/` directory is ignored by Git. No package installation is needed for the website.

## Publishing a release

`releases.json` is the versioned source of truth, including past releases. Only verified, available packages belong here; there are currently no stable releases. The manifest's `latest` field selects the current download. Historical archives remain available unchanged.

Historical r13 website content followed the then-current user guide and desktop implementation: describe a task and launch a browser, prepare the foreground window with Ctrl+Alt+Space, follow the floating guide, retry a target, and verify an action. That version required internet and an invited account, removed offline walkthroughs, and did not include autonomy. The current presentation content describes both Guided and Autonomous pilot modes with their documented limits.

The r13 ZIP is preserved byte for byte from the existing application package: 120,032,437 bytes, SHA-256 `d5fcace83287d657fade82399192b39df85ae345bf2c49c0768e56173ae11514`. All 318 entries in its embedded checksum manifest were verified before publication. This establishes package integrity, not native workflow qualification.

1. Produce and qualify the desktop package separately using the repository's Windows packaging and release process. Website deployment does not build or sign the application.
2. Give it a new immutable release ID and copy it into `artifacts/website-packages/<id>/<filename>.zip` at the repository root. Alternatively set `WEBSITE_PACKAGE_DIR` to a directory with the same layout.
3. Append a release entry with its date, channel, honest release notes, byte size, lowercase SHA-256, and `/downloads/<id>/<filename>.zip` URL. Set `latest` to its ID. Preserve all existing entries and binaries.
4. Run the checks/build. The build fails on missing packages, incorrect hashes/sizes, duplicate versions, unsafe paths, or unknown manifest versions. It renders every download and its checksum into HTML so downloads work without JavaScript.
5. Run the deployment script within an authorized AWS deployment scope. The publisher refuses to overwrite an existing version with different bytes and never deletes historical downloads.

For a clean checkout, restore published ZIPs to `artifacts/website-packages/` from the deployed site's paths listed in `releases.json`; the build verifies the downloaded bytes before use. Those archives are deliberately not committed.

## Hosting

`infra/aws/website/stack.yaml` owns a dedicated encrypted, private S3 bucket, CloudFront origin access control, cache policy, security headers and HTTPS distribution. It does not alter the API, database, Cognito, or GCP. The public hostname is AWS-generated. Custom domains and DNS are unnecessary.

```powershell
# From the repository root. Default creates a change set without executing it.
./scripts/deploy-website.ps1
# After the deployment scope has been authorized:
./scripts/deploy-website.ps1 -Apply
```

The account is checked before deployment. Use explicit `-Profile`, `-Region`, `-ExpectedAccount`, and `-StackName` only for a separately authorized account. Deployment receipts are stored in ignored `artifacts/website-deployment.json`. S3 storage, requests and CloudFront transfer are metered; there is no website server or custom domain purchase. Do not claim a fixed or zero cost.

## Design system

Near-black `#070a0c`, warm white `#f4f4ec`, citron `#d4f86b`, muted sage `#a4b0b1`, and thin slate rules. Self-hosted Manrope, generous spacing, fluid editorial headings, small consistent interface type. Sections follow hero/3D product illustration → three value statements → interactive workflow → permission-aware features → download and release archive → FAQs. Only the product illustration, walkthrough and download panel use framing. The hero is live HTML/CSS perspective rather than a raster image, which keeps it crisp, responsive and motion-controllable. No external analytics, video, 3D engine, remote font service or runtime dependencies.

The three built-in Image Gen concept prompts used the same charcoal/citron design system: (1) a spacious hero with a floating Notepad and companion guide, (2) a three-step illustrative workflow and four open feature descriptions, and (3) the download panel, release table, FAQ and footer. All displayed product interfaces are labelled illustrative; they are not screenshots of the desktop application.

The initial design comparison below is historical. The r13 update retains the design system and replaces the Notepad scene with an illustrative browser task, prompt/guide/check interaction, six current feature descriptions, and a current-window shortcut explanation.

Initial visual comparison covered the three section concepts and desktop/mobile browser renders:

| Reference point                                     | Implementation and adjustment                                                                                                                                                   |
| --------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Charcoal, warm white, citron and thin section rules | Kept the specified tokens and open section layout.                                                                                                                              |
| Editorial heading and restrained body type          | Replaced a static font response with the actual variable Manrope font; increased body, release and control sizes after comparing renders.                                       |
| Two-line desktop hero and floating product scene    | Corrected heading wrapping; recreated the scene as layered CSS geometry with a perspective floor, target ring and pointer. The guide is simplified to keep its message legible. |
| Three-step workflow with a light Notepad canvas     | Implemented real tab states, highlighted Save as interaction, restart and keyboard navigation. Starts at source selection so the full flow can be explored.                     |
| Download panel and release archive                  | Kept the layout, with real artifact size, checksum and honest pilot release notes in place of concept data. Added product/privacy FAQ answers required to explain the preview.  |
| Mobile continuation                                 | Stacked the scene, demo and download layout; tested down to 320px with no horizontal document overflow.                                                                         |

## Verification scope

Release integrity and generation are covered by Node tests and the build's actual artifact hash check. Browser QA covers desktop/mobile layout, demo steps and reset, keyboard tabs, channel filter and empty state, accordions, reduced motion, downloads with JavaScript disabled, console errors, and accessibility. It does not exercise the Windows input system or establish real application lesson qualification.

Initial deployment checks on 11 October 2026: all five Node release tests passed; the static build passed; AWS template validation and the six-resource create plan passed. The local and deployed websites passed Chromium checks at widths 1440, 768, 390 and 320px, with no horizontal document overflow, runtime console errors or axe WCAG 2 A/AA and 2.1 AA violations. The illustrative flow, keyboard tabs, reset, release filter/empty state, FAQ, reduced-motion preference and JavaScript-disabled download links were exercised. Built-in browser initialization timed out, so local Playwright Chromium was used. Other browser engines and real Windows input were not tested for this website change.

Initial live anonymous verification returned HTTPS 200 for the page and ZIP. A complete 96,363,210-byte public download matched SHA-256 `e73fa1eff9691cad19c9b805c69c83bb09248373d69d9513f1d46533adffd87e`. Missing downloads return 404, direct anonymous S3 access returns 403, and the deployed CSP and `nosniff` headers were verified. CloudFormation reached `CREATE_COMPLETE` and the CloudFront invalidation completed. Receipts for the most recent deployment are in ignored `artifacts/website-deployment.json` and `artifacts/website-live-check.json`.

The r13 website update was deployed on 11 October 2026. All five release tests and the two-archive build passed. The existing AWS stack required no infrastructure changes, and the CloudFront invalidation completed. Local and public Chromium checks passed at 1440, 768, 390 and 320px, including the prompt/action/check flow, retry without advancement, keyboard tabs, restart, two-version archive, empty stable filter, FAQ, reduced motion, and downloads without JavaScript. No horizontal overflow, runtime console errors, or axe WCAG 2 A/AA and 2.1 AA violations were found in the checked states.

The full public r13 download (120,032,437 bytes, fetched in ordered ranges) matched the SHA-256 above. Both published archives and checksum links returned 200 with the expected sizes/checksums; missing downloads returned 404 and anonymous direct S3 access returned 403. HTTPS, CSP and `nosniff` headers were checked. Browser evidence is in ignored `artifacts/website-browser-check.json`. These were website and package checks; native Windows workflows, live model inference and the application test suite were not rerun for this update.
