// All scenarios are local illustrations. No app connections, capture or outbound requests.
const card = (content, kind = "") =>
  '<div class="event-card ' + kind + '">' + content + "</div>";
const line = (label, value) =>
  '<div class="detail-line"><span>' +
  label +
  "</span><strong>" +
  value +
  "</strong></div>";
const handoff = (text) =>
  '<span class="manual-handoff">Your turn · ' + text + "</span>";
const route =
  '<div class="route-preview"><div><small>From</small><strong>Kochi</strong></div><span aria-hidden="true">→</span><div><small>To</small><strong>Bengaluru</strong></div></div>';
const scenarios = {
  meeting: {
    prompt:
      "Create a meeting and help me share the joining link with Amal on WhatsApp.",
    boundary:
      "You handle sign-in, copy the link, switch apps, and press Send. App switching and clipboard transfer currently need your help.",
    steps: [
      {
        app: "Meeting",
        symbol: "▱",
        title: "Create a place to meet.",
        description:
          "Start in your meeting app. Desktop Guide can guide the next click or handle ordinary steps with your permission.",
        screenTitle: "A link to bring<br />everyone together.",
        screenDescription: "Start with a place to meet.",
        content: card(
          '<strong>Catch up with Amal</strong><p>Create a joining link, then share it with your friend.</p><span class="mock-primary">Create a meeting link ↗</span>',
        ),
      },
      {
        app: "Meeting",
        symbol: "↗",
        title: "Keep the joining link handy.",
        description:
          "Once the link is visible, the guide helps you find it. Copy it yourself—the pilot does not transfer clipboard contents between apps.",
        screenTitle: "One link.<br />Ready to share.",
        screenDescription: "An example of what the next screen could show.",
        content: card(
          '<strong>Your meeting link</strong><span class="link-preview">[Example joining link]</span><p>Illustration only · This is not a real meeting URL.</p>' +
            handoff("Copy the link yourself"),
        ),
      },
      {
        app: "WhatsApp",
        symbol: "↗",
        title: "Bring the guide to WhatsApp.",
        description:
          "Open WhatsApp, find Amal’s chat, then summon Desktop Guide in that window. You switch the app and paste the link; the companion can help with the next visible step.",
        screenTitle: "Same goal.<br />A new task window.",
        screenDescription:
          "Select the right chat before preparing the message.",
        content: card(
          '<strong>Amal</strong><p>Open the conversation and check the recipient.</p><span class="link-preview">Ctrl + Alt + Space</span>' +
            handoff("Switch apps and paste"),
        ),
      },
      {
        app: "WhatsApp",
        symbol: "✓",
        title: "A personal message. Your call.",
        description:
          "Review Amal’s name, the joining link and your message. When everything is right, you press Send in WhatsApp. The final message stays under your control.",
        screenTitle: "Ready for your<br />final look.",
        screenDescription: "Example draft · Nothing has been sent.",
        content: card(
          '<strong>To Amal</strong><p>Hey Amal! Here’s the joining link for our catch-up.</p><span class="link-preview">[Example joining link]</span>' +
            handoff("Review, then send yourself"),
          "chat-card",
        ),
      },
    ],
  },
  tatkal: {
    prompt:
      "Help me look for a Tatkal train ticket from Kochi to Bengaluru for tomorrow.",
    boundary:
      "You handle login, CAPTCHA, payment and booking confirmation. No live train availability is shown; the pilot cannot guarantee a ticket or booking success.",
    steps: [
      {
        app: "Train search",
        symbol: "↗",
        title: "Start with the journey.",
        description:
          "Describe your route and date. Desktop Guide can open the booking site and help you locate the journey fields. Confirm the exact stations and travel date yourself.",
        screenTitle: "Your journey.<br />A clearer first step.",
        screenDescription: "Example route · Confirm your exact stations.",
        content: card(
          route +
            line("Travel date", "Tomorrow · example") +
            '<span class="mock-primary">Find the journey fields ↗</span>',
        ),
      },
      {
        app: "Train search",
        symbol: "▦",
        title: "Find the right options.",
        description:
          "The companion can point out the quota and travel-class controls on the current screen. Choose Tatkal and your preferred class, then review the search details.",
        screenTitle: "Know what<br />you’re searching for.",
        screenDescription: "Your preferences guide the search.",
        content: card(
          line("Route", "Kochi → Bengaluru") +
            line("Quota", "Tatkal") +
            line("Class", "Your preferred class") +
            '<span class="mock-primary">Review search details</span>',
        ),
      },
      {
        app: "Train results",
        symbol: "⌖",
        title: "Make sense of the options.",
        description:
          "Use the guide to find the displayed departure time, class and availability. It checks what is visible on the page; you decide which option suits your trip.",
        screenTitle: "Less searching<br />for what matters.",
        screenDescription: "Illustration only · No live train results.",
        content: card(
          line("Departure", "Check on the booking site") +
            line("Availability", "Check live status yourself") +
            line("Fare", "Review before you continue"),
        ),
      },
      {
        app: "Booking review",
        symbol: "◎",
        title: "You make the booking decision.",
        description:
          "Check the passenger details, route, date and fare. Login, CAPTCHA, payment and the final booking confirmation are yours to complete. The guide pauses for sensitive steps.",
        screenTitle: "A helping hand.<br />Your final decision.",
        screenDescription: "No ticket has been booked in this walkthrough.",
        content: card(
          "<strong>Before you confirm</strong>" +
            line("Journey & passengers", "Review carefully") +
            line("Payment & confirmation", "You complete these") +
            handoff("Finish on the booking site"),
        ),
      },
    ],
  },
  schedule: {
    prompt:
      "Help me schedule a 30-minute catch-up with Amal on Thursday at 3 PM and add a reminder.",
    boundary:
      "Check the exact date, time zone, invitees and reminders before saving. This illustrates creating a calendar event; the pilot does not run unattended scheduled tasks.",
    steps: [
      {
        app: "Calendar",
        symbol: "▦",
        title: "Turn a plan into a next step.",
        description:
          "Open your calendar and describe the event. Desktop Guide can help you locate the new-event control and work through the visible fields.",
        screenTitle: "Make room for<br />what matters.",
        screenDescription: "A simple plan, in your own words.",
        content: card(
          "<strong>Catch up with Amal</strong>" +
            line("Day", "Thursday · example") +
            line("Time", "3:00 – 3:30 PM") +
            '<span class="mock-primary">Start a new event ↗</span>',
        ),
      },
      {
        app: "Calendar",
        symbol: "⌖",
        title: "Get the details in place.",
        description:
          "Follow the visual guide, or allow ordinary typing and clicks in the selected calendar window. Review the title, exact date and time zone before continuing.",
        screenTitle: "The right details.<br />In the right place.",
        screenDescription: "Always check the event’s time zone.",
        content: card(
          line("Title", "Catch up with Amal") +
            line("Date", "Your chosen Thursday") +
            line("Duration", "30 minutes") +
            line("Time zone", "Confirm in your calendar"),
        ),
      },
      {
        app: "Calendar",
        symbol: "◎",
        title: "Add a useful nudge.",
        description:
          "Ask the guide to help find reminder settings and choose the reminder you want. If you need an online meeting, it can also help locate the meeting-link option.",
        screenTitle: "A reminder.<br />One less thing to remember.",
        screenDescription: "Example preferences for your event.",
        content: card(
          line("Reminder", "10 minutes before · example") +
            line("Meeting link", "Add if needed") +
            line("Invitees", "Review before saving"),
        ),
      },
      {
        app: "Calendar review",
        symbol: "✓",
        title: "Review it. Then make it real.",
        description:
          "Check the date, time, reminder and any attendees. You confirm the final save or invitation. Your calendar handles the reminder after you create the event.",
        screenTitle: "Your plan,<br />ready to review.",
        screenDescription: "Example event · Nothing has been scheduled.",
        content: card(
          "<strong>Catch up with Amal</strong>" +
            line("When", "Thursday · 3:00 – 3:30 PM") +
            line("Reminder", "10 minutes before") +
            handoff("Review and save yourself"),
        ),
      },
    ],
  },
};

const tabs = [...document.querySelectorAll("[data-scenario]")];
const panel = document.querySelector("#scenario-panel");
const next = document.querySelector("#demo-next");
const restart = document.querySelector("#demo-restart");
let selectedScenario = "meeting";
let step = 0;

function renderScenario() {
  const scenario = scenarios[selectedScenario];
  const current = scenario.steps[step];
  for (const tab of tabs) {
    const active = tab.dataset.scenario === selectedScenario;
    tab.setAttribute("aria-selected", String(active));
    tab.tabIndex = active ? 0 : -1;
  }
  panel.setAttribute("aria-labelledby", "tab-" + selectedScenario);
  document.querySelector("#scenario-prompt").textContent =
    "“" + scenario.prompt + "”";
  document.querySelector("#scenario-boundary").textContent = scenario.boundary;
  document.querySelector("#scenario-app").textContent =
    "Example workflow · " + current.app;
  document.querySelector("#step-counter").textContent =
    String(step + 1).padStart(2, "0") + " / 04";
  document.querySelector("#step-heading").textContent = current.title;
  document.querySelector("#step-description").textContent = current.description;
  // Only authored constants above are rendered as markup; no external/user input.
  document.querySelector("#demo-screen").innerHTML =
    '<div class="app-topline"><span>' +
    current.app +
    "</span><span>Illustrative app</span></div>" +
    '<div class="app-sheet"><span class="app-symbol" aria-hidden="true">' +
    current.symbol +
    "</span><h3>" +
    current.screenTitle +
    "</h3><p>" +
    current.screenDescription +
    "</p>" +
    current.content +
    "</div>";
  document.querySelectorAll("#step-track span").forEach((marker, index) => {
    marker.classList.toggle("active", index === step);
    marker.classList.toggle("past", index < step);
  });
  next.innerHTML =
    step === scenario.steps.length - 1
      ? 'Explore again <span aria-hidden="true">↺</span>'
      : 'Next step <span aria-hidden="true">→</span>';
}
function chooseScenario(id) {
  selectedScenario = id;
  step = 0;
  renderScenario();
}
tabs.forEach((tab, index) => {
  tab.addEventListener("click", () => chooseScenario(tab.dataset.scenario));
  tab.addEventListener("keydown", (event) => {
    let target;
    if (event.key === "ArrowRight") target = (index + 1) % tabs.length;
    if (event.key === "ArrowLeft")
      target = (index + tabs.length - 1) % tabs.length;
    if (event.key === "Home") target = 0;
    if (event.key === "End") target = tabs.length - 1;
    if (target === undefined) return;
    event.preventDefault();
    event.stopPropagation();
    chooseScenario(tabs[target].dataset.scenario);
    tabs[target].focus();
  });
});
next.addEventListener("click", () => {
  step = (step + 1) % scenarios[selectedScenario].steps.length;
  renderScenario();
});
restart.addEventListener("click", () => {
  step = 0;
  renderScenario();
});
next.hidden = restart.hidden = false;
renderScenario();

// Keep the generated archive usable even when JavaScript is disabled.
const filter = document.querySelector("#release-filter");
const entries = [...document.querySelectorAll(".release-entry")];
filter.addEventListener("change", () => {
  let count = 0;
  for (const entry of entries) {
    entry.hidden =
      filter.value !== "all" && entry.dataset.channel !== filter.value;
    if (!entry.hidden) count++;
  }
  document.querySelector(".release-table").hidden = count === 0;
  document.querySelector("#release-empty").hidden = count !== 0;
  document.querySelector("#release-count").textContent =
    count + " available release" + (count === 1 ? "" : "s");
});

// Presentation is a local view of the same content, with no fullscreen permission.
const chapters = [...document.querySelectorAll("[data-chapter]")];
const presentToggle = document.querySelector("#present-toggle");
const controls = document.querySelector(".presentation-controls");
const previousChapter = document.querySelector("#present-prev");
const nextChapter = document.querySelector("#present-next");
let presenting = false;
let chapterIndex = 0;

function setChapter(index, updateHash = true, moveFocus = true) {
  chapterIndex = Math.max(0, Math.min(chapters.length - 1, index));
  chapters.forEach((chapter, i) =>
    chapter.classList.toggle("present-active", i === chapterIndex),
  );
  const chapter = chapters[chapterIndex];
  previousChapter.disabled = chapterIndex === 0;
  nextChapter.disabled = chapterIndex === chapters.length - 1;
  document.querySelector("#present-status").textContent =
    chapterIndex +
    1 +
    " / " +
    chapters.length +
    " · " +
    chapter.dataset.chapter;
  if (updateHash) history.replaceState(null, "", "#" + chapter.id);
  window.scrollTo({ top: 0, behavior: "instant" });
  if (moveFocus) {
    const heading = chapter.querySelector("h1, h2");
    heading.tabIndex = -1;
    heading.focus({ preventScroll: true });
  }
}
function setPresentation(enabled) {
  presenting = enabled;
  document.body.classList.toggle("presenting", enabled);
  presentToggle.setAttribute("aria-pressed", String(enabled));
  presentToggle.textContent = enabled ? "Exit" : "Present";
  presentToggle.setAttribute(
    "aria-label",
    enabled ? "Exit presentation" : "Start presentation",
  );
  controls.hidden = !enabled;
  if (enabled) {
    setChapter(0);
  } else {
    const chapter = chapters[chapterIndex];
    presentToggle.focus({ preventScroll: true });
    chapter.scrollIntoView({ behavior: "instant", block: "start" });
  }
}
presentToggle.addEventListener("click", () => setPresentation(!presenting));
document
  .querySelector("#present-exit")
  .addEventListener("click", () => setPresentation(false));
previousChapter.addEventListener("click", () => setChapter(chapterIndex - 1));
nextChapter.addEventListener("click", () => setChapter(chapterIndex + 1));
presentToggle.hidden = false;

document.addEventListener("keydown", (event) => {
  if (!presenting || event.altKey || event.ctrlKey || event.metaKey) return;
  if (event.key === "Escape") {
    event.preventDefault();
    setPresentation(false);
    return;
  }
  if (
    event.target.closest(
      "button, a, input, textarea, select, summary, [role=tablist], [contenteditable=true]",
    )
  )
    return;
  if (event.key === "ArrowRight" || event.key === "PageDown") {
    event.preventDefault();
    setChapter(chapterIndex + 1);
  } else if (event.key === "ArrowLeft" || event.key === "PageUp") {
    event.preventDefault();
    setChapter(chapterIndex - 1);
  } else if (event.key === "Home") {
    event.preventDefault();
    setChapter(0);
  } else if (event.key === "End") {
    event.preventDefault();
    setChapter(chapters.length - 1);
  }
});
function revealHash() {
  let id;
  try {
    id = decodeURIComponent(location.hash.slice(1));
  } catch {
    return;
  }
  const target = document.getElementById(id);
  if (!target) return;
  if (id === "releases") target.open = true;
  if (presenting) {
    const chapter = target.closest("[data-chapter]");
    if (chapter) setChapter(chapters.indexOf(chapter), false);
  }
}
window.addEventListener("hashchange", revealHash);
document.addEventListener("click", (event) => {
  const anchor = event.target.closest('a[href^="#"]');
  if (!anchor) return;
  const target = document.getElementById(anchor.getAttribute("href").slice(1));
  if (!target) return;
  if (target.id === "releases") target.open = true;
  if (presenting) {
    const chapter = target.closest("[data-chapter]");
    if (chapter) {
      event.preventDefault();
      setChapter(chapters.indexOf(chapter));
      if (target !== chapter) target.scrollIntoView({ behavior: "instant" });
    }
  }
});
revealHash();
