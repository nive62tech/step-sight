const tabs = [...document.querySelectorAll('[role="tab"]')];
const panel = document.querySelector("#demo-panel");
const heading = document.querySelector("#demo-heading");
const description = document.querySelector("#demo-description");
const next = document.querySelector("#demo-next");
const states = [
  {
    title: "You choose where we begin.",
    text: "Pick the window you want help with. In the app, you preview the source and grant observation before guidance begins.",
  },
  {
    title: "Save your first note.",
    text: "Open File, then choose Save as. You make the click; I’ll help with what comes next. Try the highlighted item in this demo.",
  },
  {
    title: "A little more confident.",
    text: "You did the step. In a live lesson, Desktop Guide checks the visible result before moving on. Here, we’re simply showing how the flow feels.",
  },
];
let step = 0;
function showStep(index) {
  step = index;
  tabs.forEach((tab, i) => {
    tab.setAttribute("aria-selected", String(i === step));
    tab.tabIndex = i === step ? 0 : -1;
  });
  panel.setAttribute("aria-labelledby", tabs[step].id);
  panel.dataset.step = String(step);
  heading.textContent = states[step].title;
  description.textContent = states[step].text;
  document.querySelector("#demo-file-menu").hidden = step !== 1;
  document.querySelector("#source-outline").hidden = step !== 0;
  document.querySelector("#saved-note").hidden = step !== 2;
  document.querySelector("#demo-filename").textContent =
    step === 2 ? "My first note.txt — Notepad" : "Untitled — Notepad";
  next.replaceChildren(
    document.createTextNode(step === 2 ? "Try again " : "Next step "),
  );
  const arrow = document.createElement("span");
  arrow.setAttribute("aria-hidden", "true");
  arrow.textContent = step === 2 ? "↺" : "→";
  next.append(arrow);
}
tabs.forEach((tab, i) => {
  tab.addEventListener("click", () => showStep(i));
  tab.addEventListener("keydown", (event) => {
    let index;
    if (event.key === "ArrowRight") index = (i + 1) % tabs.length;
    if (event.key === "ArrowLeft") index = (i + tabs.length - 1) % tabs.length;
    if (event.key === "Home") index = 0;
    if (event.key === "End") index = tabs.length - 1;
    if (index !== undefined) {
      event.preventDefault();
      showStep(index);
      tabs[index].focus();
    }
  });
});
next.addEventListener("click", () => showStep((step + 1) % states.length));
document
  .querySelector("#demo-restart")
  .addEventListener("click", () => showStep(0));
document.querySelector("#demo-save").addEventListener("click", () => {
  showStep(2);
  next.focus();
});

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
    `${count} available release${count === 1 ? "" : "s"}`;
});

const motionQuery = window.matchMedia("(prefers-reduced-motion: reduce)");
const motionToggle = document.querySelector("#motion-toggle");
let motionPaused = motionQuery.matches;
function syncMotion() {
  document.body.classList.toggle("motion-paused", motionPaused);
  motionToggle.setAttribute("aria-pressed", String(motionPaused));
  motionToggle.textContent = motionQuery.matches
    ? "Reduced motion enabled"
    : motionPaused
      ? "Resume motion ▷"
      : "Pause motion Ⅱ";
  motionToggle.disabled = motionQuery.matches;
}
motionToggle.addEventListener("click", () => {
  motionPaused = !motionPaused;
  syncMotion();
});
motionQuery.addEventListener("change", () => {
  motionPaused = motionQuery.matches;
  syncMotion();
});
syncMotion();
const stage = document.querySelector(".hero-stage");
const scene = document.querySelector("#scene");
stage.addEventListener("pointermove", (event) => {
  if (motionPaused || event.pointerType !== "mouse") return;
  const bounds = stage.getBoundingClientRect();
  scene.style.setProperty(
    "--tilt-x",
    `${(0.5 - (event.clientY - bounds.top) / bounds.height) * 5}deg`,
  );
  scene.style.setProperty(
    "--tilt-y",
    `${((event.clientX - bounds.left) / bounds.width - 0.5) * 7}deg`,
  );
});
stage.addEventListener("pointerleave", () => {
  scene.style.setProperty("--tilt-x", "0deg");
  scene.style.setProperty("--tilt-y", "0deg");
});
// Only the visible hero animates; no recurring JavaScript render loop is needed.
new IntersectionObserver(
  ([entry]) => {
    stage.style.setProperty(
      "visibility",
      entry.isIntersecting ? "visible" : "hidden",
    );
  },
  { rootMargin: "80px" },
).observe(stage);
