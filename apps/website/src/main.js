const tabs = [...document.querySelectorAll('[role="tab"]')];
const panel = document.querySelector("#demo-panel");
const heading = document.querySelector("#demo-heading");
const description = document.querySelector("#demo-description");
const next = document.querySelector("#demo-next");
const states = [
  {
    title: "A goal is all you need to start.",
    text: "Describe the task in your own words. Gemini chooses a starting site and opens a new browser window, or asks for clarification.",
  },
  {
    title: "Follow the visual guide.",
    text: "The floating companion explains the step and draws a target. Try Search trains in this illustration. In the app, you make the click with your own mouse.",
  },
  {
    title: "Check the result. Then continue.",
    text: "Check screen asks for a fresh image to verify what changed. The live app waits through loading screens before choosing the next step. This example is only an illustration.",
  },
];
let step = 0;
let demoActionDone = false;
let targetRetryCount = 0;
const targetArea = document.querySelector("#demo-target-area");
const targetStatus = document.querySelector("#demo-target-status");
const findTarget = document.querySelector("#demo-find");
function syncDemoControls() {
  document.querySelector("#demo-prompt").hidden = step !== 0;
  document.querySelector("#demo-browser").hidden = step === 0;
  targetArea.hidden = step !== 1 || demoActionDone;
  document.querySelector("#demo-result").hidden =
    step === 0 || (step === 1 && !demoActionDone);
  document.querySelector("#demo-browser-status").textContent =
    step === 2
      ? "Illustration · Visible result checked"
      : demoActionDone
        ? "Your action is ready to check"
        : "Visual guide · You make the click";
  findTarget.hidden = step !== 1 || demoActionDone;
  next.disabled = step === 1 && !demoActionDone;
  next.replaceChildren(
    document.createTextNode(
      step === 2
        ? "Try again "
        : step === 1
          ? "Check screen "
          : "Try the flow ",
    ),
  );
  const arrow = document.createElement("span");
  arrow.setAttribute("aria-hidden", "true");
  arrow.textContent = step === 2 ? "↺" : "→";
  next.append(arrow);
}
function showStep(index) {
  step = index;
  demoActionDone = step === 2;
  targetRetryCount = 0;
  targetStatus.textContent = "";
  targetArea.classList.remove("target-relocated");
  tabs.forEach((tab, i) => {
    tab.setAttribute("aria-selected", String(i === step));
    tab.tabIndex = i === step ? 0 : -1;
  });
  panel.setAttribute("aria-labelledby", tabs[step].id);
  panel.dataset.step = String(step);
  heading.textContent = states[step].title;
  description.textContent = states[step].text;
  syncDemoControls();
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
document.querySelector("#demo-launch").addEventListener("click", () => {
  showStep(1);
  document.querySelector("#demo-search").focus({ preventScroll: true });
});
document.querySelector("#demo-search").addEventListener("click", () => {
  demoActionDone = true;
  heading.textContent = "You made the move.";
  description.textContent =
    "The example result is now visible. Choose Check screen to see the next part of the flow. In the app, this checks a fresh screenshot of your task window.";
  targetStatus.textContent = "Illustrative result ready to check.";
  syncDemoControls();
  next.focus();
});
findTarget.addEventListener("click", () => {
  targetRetryCount++;
  targetArea.classList.add("target-relocated");
  targetStatus.textContent = `Target highlighted again (${targetRetryCount}). The instruction has not advanced.`;
});
syncDemoControls();

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
