"use strict";
let saved = {name: "Untitled workspace", theme: "default"};
const byId = id => document.getElementById(id);
document.querySelectorAll("[data-tab]").forEach(button => button.addEventListener("click", () => {
  document.querySelectorAll(".page").forEach(page => {page.hidden = page.id !== button.dataset.tab;});
  document.querySelectorAll("[data-tab]").forEach(tab => tab.classList.toggle("active", tab === button));
}));
byId("settings-form").addEventListener("submit", async event => {
  event.preventDefault();
  const name = byId("name").value.trim();
  if (!name) {byId("status").textContent = "Enter a workspace name before saving."; return;}
  byId("save").disabled = true; byId("status").textContent = "Saving workspace…";
  await new Promise(resolve => setTimeout(resolve, 1500));
  if (byId("failure").checked) {byId("status").textContent = "Save failed. Clear the practice failure option and try again.";}
  else {
    saved = {name, theme: byId("theme").value};
    byId("current-name").textContent = saved.name;
    byId("current-theme").textContent = `Theme: ${saved.theme}`;
    byId("status").textContent = `Workspace saved: ${saved.name}. Theme: ${saved.theme}.`;
  }
  byId("save").disabled = false;
});
byId("reset").addEventListener("click", () => {byId("settings-form").reset(); byId("status").textContent = "Form reset. Saved workspace is unchanged.";});
byId("advanced").addEventListener("click", () => byId("dialog").showModal());
byId("close-dialog").addEventListener("click", () => byId("dialog").close());
byId("unrelated").addEventListener("click", () => {byId("activity-result").textContent = "Success: the activity notice was displayed. This does not save workspace settings.";});
byId("help").addEventListener("click", () => {byId("toast").hidden = false; byId("toast").textContent = "Help opened successfully. To finish the lesson, save your workspace in Settings.";});
