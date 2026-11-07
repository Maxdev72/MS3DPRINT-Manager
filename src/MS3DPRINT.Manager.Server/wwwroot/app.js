"use strict";
const $ = id => document.getElementById(id);
const state = { view: "overview", filter: "active", page: 1, clients: [], projects: [], project: null, client: null,
  form: null, target: null, task: null, taskDrafts: new Map(), busy: false, pending: null, online: false, csrf: "", resumeForm: false, hadSession: false };
const statusNames = { Quote: "Devis", InProgress: "En cours", Completed: "Terminé" };
const priorityNames = { None: "Normale", Low: "Basse", Medium: "Moyenne", High: "Haute" };
const dateFormat = new Intl.DateTimeFormat("fr-FR", { day: "numeric", month: "short" });
let toastTimer;

function el(tag, className = "", text) {
  const node = document.createElement(tag);
  if (className) node.className = className;
  if (text !== undefined && text !== null) node.textContent = text;
  return node;
}
function icon(name, className = "icon") {
  const node = document.createElementNS("http://www.w3.org/2000/svg", "svg");
  node.setAttribute("class", className); node.setAttribute("aria-hidden", "true");
  const use = document.createElementNS("http://www.w3.org/2000/svg", "use");
  use.setAttribute("href", "#i-" + name); node.append(use); return node;
}
function button(text, className, action, symbol) {
  const node = el("button", "button " + className); node.type = "button";
  if (symbol) node.append(icon(symbol)); node.append(el("span", "", text));
  node.addEventListener("click", action); return node;
}
function actionIcon(label, symbol, action) {
  const node = el("button", "icon-button"); node.type = "button"; node.title = label;
  node.setAttribute("aria-label", label); node.append(icon(symbol)); node.addEventListener("click", action); return node;
}
function message(target, text) { target.textContent = text || ""; target.hidden = !text; }
function toast(text) { message($("toast"), text); clearTimeout(toastTimer); toastTimer = setTimeout(() => { $("toast").hidden = true; }, 5000); }
function today() { const now = new Date(); return [now.getFullYear(), String(now.getMonth() + 1).padStart(2, "0"), String(now.getDate()).padStart(2, "0")].join("-"); }
function dateText(value) { return value ? dateFormat.format(new Date(value + "T12:00:00")) : "Sans échéance"; }
function clientName(client) { return client?.displayName || client?.companyName || [client?.firstName, client?.lastName].filter(Boolean).join(" ") || "Client"; }
function nameFor(id) { return clientName(state.clients.find(client => client.id === id)); }
function clean(value) { return value.trim() || null; }
function setConnection(online) {
  state.online = online;
  $("connection-state").classList.toggle("offline", !online);
  $("connection-state").lastElementChild.textContent = online ? "Connecté au serveur" : "Connexion à vérifier";
  syncButtons();
}
function syncButtons() {
  document.querySelectorAll("[data-mutation], .edit-form button[type=submit]").forEach(node => {
    node.disabled = state.busy || !!state.pending || !state.online || node.dataset.boundary === "true";
  });
  $("pending-operation").hidden = !state.pending;
}
class ApiError extends Error { constructor(status, text) { super(text); this.status = status; } }
async function api(path, options = {}) {
  let response, text;
  try {
    response = await fetch(path, { credentials: "same-origin", cache: "no-store", ...options });
    text = await response.text();
  }
  catch { setConnection(false); throw new ApiError(0, "Le serveur est injoignable. Vérifiez votre connexion, puis actualisez."); }
  let data = null;
  if (text) { try { data = JSON.parse(text); } catch { if (response.ok) throw new ApiError(502, "La réponse du serveur n’a pas pu être lue."); } }
  if (!response.ok) {
    if (response.status === 401 && path !== "/api/v1/auth/login") showLogin(state.hadSession ? "Votre session a expiré. Reconnectez-vous pour retrouver votre brouillon." : "");
    const fallback = response.status === 409 ? "Cet élément a changé depuis son ouverture. Votre brouillon est conservé. Fermez ce formulaire, actualisez, puis réappliquez vos modifications."
      : response.status === 429 ? "Trop de tentatives rapprochées. Attendez une minute avant de réessayer."
      : response.status === 401 ? "Identifiant ou mot de passe incorrect."
      : "L’opération n’a pas abouti. Vérifiez les champs et réessayez.";
    throw new ApiError(response.status, data?.error || data?.detail || data?.title || fallback);
  }
  return data;
}
async function csrf() { state.csrf = (await api("/api/v1/auth/csrf")).requestToken; }
function showLogin(error = "") {
  $("boot-view").hidden = true; $("workspace-view").hidden = true; $("login-view").hidden = false;
  if ($("form-dialog").open) { state.resumeForm = true; $("form-dialog").close(); }
  if ($("confirm-dialog").open) $("confirm-dialog").close();
  message($("login-error"), error); $("login-password").value = "";
}
async function refresh(showToast = false) {
  $("refresh-button").disabled = true;
  try {
    await api("/api/v1/auth/session");
    const [clients, projects] = await Promise.all([api("/api/v1/clients"), api("/api/v1/projects")]);
    state.clients = clients; state.projects = projects;
    state.hadSession = true;
    if (state.project) state.project = await api("/api/v1/projects/" + state.project.id);
    if (state.client) state.client = await api("/api/v1/clients/" + state.client.id);
    $("boot-view").hidden = true; $("login-view").hidden = true; $("workspace-view").hidden = false;
    message($("global-error"), ""); setConnection(true);
    $("updated-label").textContent = "Actualisé à " + new Intl.DateTimeFormat("fr-FR", { hour: "2-digit", minute: "2-digit" }).format(new Date());
    render();
    if (state.resumeForm && state.form) { state.resumeForm = false; $("form-dialog").showModal(); }
    if (showToast) toast("Atelier actualisé.");
  } catch (error) { if (error.status !== 401) { message($("global-error"), error.message); if (!$("workspace-view").hidden) setConnection(false); else showLogin(error.message); } }
  finally { $("refresh-button").disabled = false; }
}
function navigate(view) {
  state.view = view; state.project = null; state.client = null; state.page = 1; $("search-input").value = ""; render();
  if (view === "downloads") loadDownloads();
}
async function loadDownloads() {
  try { state.downloads = await api("/api/v1/downloads"); }
  catch (error) { state.downloads = []; if (error.status !== 401) message($("global-error"), error.message); }
  if (state.view === "downloads") render();
}
function renderDownloads() {
  const section = $("downloads-section"); section.replaceChildren();
  const size = bytes => (bytes / 1048576).toLocaleString("fr-FR", { maximumFractionDigits: 1 }) + " Mo";
  const when = value => new Intl.DateTimeFormat("fr-FR", { dateStyle: "long", timeStyle: "short" }).format(new Date(value));
  for (const [kind, title, help] of [["windows", "Windows", "Lancez « MS3DPRINT Manager.exe ». Aucune installation n’est nécessaire."],
    ["linux", "Linux", "Extrayez l’archive, puis lancez « MS3DPRINT Manager ». Le fichier LISEZ-MOI.md liste les dépendances."]]) {
    const item = (state.downloads || []).find(download => download.kind === kind);
    const card = el("article", "download-card"); card.append(el("h2", "", title));
    if (!item) { card.append(el("p", "muted", "Pas encore publiée. Dans MS3DPRINT Manager : Paramètres › Publier sur ms3dforge.fr.")); section.append(card); continue; }
    card.append(el("p", "muted", item.label), el("p", "", help));
    const link = el("a", "button primary"); link.href = "/api/v1/downloads/" + kind; link.setAttribute("download", item.fileName);
    link.append(icon("download"), el("span", "", "Télécharger · " + size(item.size)));
    card.append(link, el("p", "download-meta", "Publiée le " + when(item.publishedAt)), el("p", "download-hash", "SHA256 " + item.sha256));
    section.append(card);
  }
}
function render() {
  const detail = state.project || state.client;
  document.querySelectorAll("[data-view]").forEach(node => { const active = node.dataset.view === state.view; node.classList.toggle("active", active); if (active) node.setAttribute("aria-current", "page"); else node.removeAttribute("aria-current"); });
  $("project-count").textContent = state.projects.length; $("client-count").textContent = state.clients.length;
  const downloads = state.view === "downloads";
  $("downloads-section").hidden = !downloads;
  if (downloads) {
    $("breadcrumb-current").textContent = "Téléchargements"; $("page-title").textContent = "Vos logiciels.";
    $("page-subtitle").textContent = "La dernière version de MS3DPRINT Manager pour vos ordinateurs.";
    $("list-section").hidden = true; $("detail-section").hidden = true; $("overview-summary").hidden = true;
    document.querySelector(".page-heading").hidden = false; $("primary-action").hidden = true;
    renderDownloads(); syncButtons(); return;
  }
  $("primary-action").hidden = false;
  $("breadcrumb-current").textContent = state.view === "clients" ? "Clients" : state.view === "projects" ? "Projets" : "Vue d’ensemble";
  $("page-title").textContent = state.view === "clients" ? "Vos clients." : state.view === "projects" ? "Vos projets." : "L’atelier, en vue.";
  $("page-subtitle").textContent = state.view === "clients" ? "Les personnes et les entreprises avec qui vous travaillez." : "Vos projets et les prochaines actions à reprendre.";
  $("search-input").placeholder = state.view === "clients" ? "Rechercher un client…" : "Rechercher un projet…";
  $("search-input").setAttribute("aria-label", state.view === "clients" ? "Rechercher un client" : "Rechercher un projet");
  $("list-tabs").hidden = state.view === "clients";
  $("list-section").hidden = !!detail; $("detail-section").hidden = !detail;
  $("overview-summary").hidden = !!detail || state.view !== "overview";
  document.querySelector(".page-heading").hidden = !!detail;
  const createClient = state.view === "clients" || state.clients.length === 0;
  $("primary-action").lastElementChild.textContent = createClient ? "Nouveau client" : "Nouveau projet";
  $("primary-action").setAttribute("aria-label", createClient ? "Nouveau client" : "Nouveau projet");
  $("primary-action").dataset.mutation = "true";
  if (state.project) renderProject(); else if (state.client) renderClient(); else { renderSummary(); renderList(); }
  syncButtons();
}
function renderSummary() {
  const summary = $("overview-summary"); summary.replaceChildren();
  const active = state.projects.filter(project => project.status !== "Completed");
  const due = active.filter(project => project.dueDate && project.dueDate < today());
  const tasks = active.reduce((total, project) => total + (project.tasks || []).filter(task => !task.isCompleted).length, 0);
  for (const [number, label, attention] of [[active.length, "projets à suivre", false], [tasks, "tâches restantes", false], [due.length, "échéances dépassées", due.length > 0]]) {
    const item = el("span", "summary-item" + (attention ? " summary-attention" : "")); item.append(el("span", "summary-number", number), el("span", "", label)); summary.append(item);
  }
}
function emptyState(title, copy, label, action, symbol) {
  const node = el("div", "empty-state"); node.append(icon(symbol), el("h2", "", title), el("p", "", copy));
  if (label) { const control = button(label, "primary", action, "plus"); control.dataset.mutation = "true"; node.append(control); }
  return node;
}
function renderList() {
  const container = $("list-content"); container.replaceChildren(); $("list-footer").replaceChildren();
  const query = $("search-input").value.normalize("NFD").replace(/[\u0300-\u036f]/g, "").toLowerCase();
  const match = text => String(text).normalize("NFD").replace(/[\u0300-\u036f]/g, "").toLowerCase().includes(query);
  const clients = state.view === "clients";
  let items = clients ? state.clients.filter(client => match(clientName(client) + " " + client.clientCode))
    : state.projects.filter(project => (state.filter === "all" || (state.filter === "active" ? project.status !== "Completed" : project.status === state.filter)) && match(project.projectName + " " + project.reference + " " + nameFor(project.clientId) + " " + (project.tasks || []).map(task => task.title).join(" ")));
  items = [...items].sort((a, b) => clients ? clientName(a).localeCompare(clientName(b), "fr") : ((a.dueDate || "9999") < (b.dueDate || "9999") ? -1 : (a.dueDate || "9999") > (b.dueDate || "9999") ? 1 : a.projectName.localeCompare(b.projectName, "fr")));
  if (!items.length) {
    const filtered = query || (!clients && state.projects.length > 0);
    container.append(emptyState(filtered ? "Aucun résultat pour cette vue" : clients ? "Votre premier client, pour commencer." : "Le prochain projet commence ici.",
      filtered ? "Essayez une autre recherche ou affichez tous les projets." : clients ? "Ajoutez une fiche client pour créer vos projets et leurs références." : state.clients.length ? "Créez un projet, puis ajoutez les tâches qui le feront avancer." : "Commencez par créer le client de votre premier projet.",
      filtered ? null : clients || !state.clients.length ? "Créer un client" : "Créer un projet", () => clients || !state.clients.length ? openClientForm() : openProjectForm(), clients ? "clients" : "project"));
    return;
  }
  if (!clients) { const head = el("div", "list-head"); ["Projet / prochaine action", "Client", "État", "Échéance", ""].forEach(text => head.append(el("span", "", text))); container.append(head); }
  const pageCount = Math.ceil(items.length / 30); state.page = Math.min(state.page, pageCount);
  items.slice((state.page - 1) * 30, state.page * 30).forEach(item => container.append(clients ? clientRow(item) : projectRow(item)));
  $("list-footer").append(el("span", "", `${(state.page - 1) * 30 + 1}–${Math.min(state.page * 30, items.length)} sur ${items.length} ${clients ? "clients" : "projets"}`));
  if (pageCount > 1) { const pager = el("div", "pagination"); const previous = button("Précédent", "secondary", () => { state.page--; renderList(); }); const next = button("Suivant", "secondary", () => { state.page++; renderList(); }); previous.disabled = state.page === 1; next.disabled = state.page === pageCount; pager.append(previous, next); $("list-footer").append(pager); }
}
function clickableRow(node, action) { node.tabIndex = 0; node.setAttribute("role", "button"); node.addEventListener("click", action); node.addEventListener("keydown", event => { if (event.key === "Enter" || event.key === " ") { event.preventDefault(); action(); } }); }
function projectRow(project) {
  const row = el("div", "project-row"); row.setAttribute("aria-label", "Ouvrir le projet " + project.projectName);
  const title = el("div"); title.append(el("span", "project-name", project.projectName), el("span", "project-reference", project.reference));
  const next = (project.tasks || []).find(task => !task.isCompleted); if (next) title.append(el("span", "task-preview", next.title));
  const due = el("span", "due-date" + (project.status !== "Completed" && project.dueDate && project.dueDate < today() ? " overdue" : ""), dateText(project.dueDate));
  row.append(title, el("span", "client-name", nameFor(project.clientId)), statusBadge(project.status), due, icon("arrow", "icon row-action"));
  clickableRow(row, () => openProject(project.id)); return row;
}
function clientRow(client) {
  const row = el("div", "client-row"); const name = clientName(client);
  row.append(el("span", "client-monogram", name.split(/\s+/).slice(0, 2).map(part => part[0]).join("").toUpperCase()));
  const text = el("div", "client-row-main"); text.append(el("span", "project-name", name), el("span", "client-row-code", client.clientCode));
  const count = state.projects.filter(project => project.clientId === client.id).length;
  row.append(text, el("span", "client-row-meta", count + (count === 1 ? " projet" : " projets")), icon("arrow", "icon row-action"));
  row.setAttribute("aria-label", "Ouvrir le client " + name); clickableRow(row, () => openClient(client.id)); return row;
}
function statusBadge(status) { return el("span", "status-badge " + (status === "Completed" ? "completed" : status === "Quote" ? "quote" : ""), statusNames[status] || status); }
async function openProject(id) { try { state.project = await api("/api/v1/projects/" + id); state.client = null; state.view = "projects"; render(); $("main-content").focus(); } catch (error) { message($("global-error"), error.message); } }
async function openClient(id) { try { state.client = await api("/api/v1/clients/" + id); state.project = null; state.view = "clients"; render(); $("main-content").focus(); } catch (error) { message($("global-error"), error.message); } }
function detailStart(name, reference, backView) {
  const section = $("detail-section"); section.replaceChildren();
  const back = button(backView === "clients" ? "Tous les clients" : "Tous les projets", "text-button back-button", () => navigate(backView), "arrow"); section.append(back);
  const header = el("div", "detail-header"), heading = el("div"); heading.append(el("span", "detail-reference", reference), el("h2", "", name)); header.append(heading); section.append(header); return { section, header, heading };
}
function renderProject() {
  const project = state.project;
  const { section, header, heading } = detailStart(project.projectName, project.reference, "projects");
  const meta = el("div", "detail-meta"); meta.append(statusBadge(project.status), el("span", "", nameFor(project.clientId))); heading.append(meta);
  const edit = button("Modifier le projet", "secondary", openProjectEdit, "edit"); edit.dataset.mutation = "true"; header.append(edit);
  if (project.description) section.append(el("p", "detail-copy", project.description));
  const body = el("div", "detail-body"), main = el("div"), aside = el("aside", "detail-aside");
  const title = el("div", "section-title"); const tasks = project.tasks || [], completed = tasks.filter(task => task.isCompleted).length;
  title.append(el("h3", "", "Suivi du projet"), el("span", "", `${completed} / ${tasks.length} terminées`)); main.append(title);
  const list = el("div", "task-list");
  tasks.forEach((task, index) => {
    const row = el("div", "task-row" + (task.isCompleted ? " done" : ""));
    const check = el("input", "task-checkbox"); check.type = "checkbox"; check.checked = task.isCompleted; check.dataset.mutation = "true";
    check.setAttribute("aria-label", (task.isCompleted ? "Rouvrir : " : "Terminer : ") + task.title);
    check.addEventListener("change", () => { const completed = check.checked; check.checked = task.isCompleted; perform(`/api/v1/projects/${project.id}/tasks/${task.id}/complete`, "POST", { expectedUpdatedAt: project.updatedAt, completed }, acceptProject); });
    const content = el("div", "task-content"), label = el("button", "task-title-button", task.title); label.type = "button"; label.addEventListener("click", () => openTaskForm(task)); content.append(label); if (task.notes) content.append(el("span", "task-notes", task.notes));
    const actions = el("div", "task-actions");
    for (const [direction, symbol, text, disabled] of [[-1, "up", "Monter la tâche", index === 0], [1, "down", "Descendre la tâche", index === tasks.length - 1]]) {
      const control = actionIcon(text, symbol, () => perform(`/api/v1/projects/${project.id}/tasks/${task.id}/move`, "POST", { expectedUpdatedAt: project.updatedAt, direction }, acceptProject));
      control.dataset.mutation = "true"; control.dataset.boundary = String(disabled); control.disabled = disabled; actions.append(control);
    }
    const remove = actionIcon("Retirer : " + task.title, "trash", () => confirmRemove(task)); remove.dataset.mutation = "true"; actions.append(remove);
    row.append(check, content, actions); list.append(row);
  });
  if (!tasks.length) list.append(el("p", "task-empty", "Aucune tâche pour l’instant. Quelle est la prochaine action ?")); main.append(list);
  const form = el("form", "add-task-form"), input = el("input"); input.placeholder = "Ajouter une tâche…"; input.setAttribute("aria-label", "Nouvelle tâche"); input.required = true; input.maxLength = 500;
  input.value = state.taskDrafts.get(project.id) || "";
  input.addEventListener("input", () => state.taskDrafts.set(project.id, input.value));
  const add = button("Ajouter", "primary", () => form.requestSubmit(), "plus"); add.dataset.mutation = "true"; form.append(input, add);
  form.addEventListener("submit", event => { event.preventDefault(); if (!input.value.trim()) return; perform(`/api/v1/projects/${project.id}/tasks`, "POST", { expectedUpdatedAt: project.updatedAt, title: input.value.trim(), notes: null }, value => { state.taskDrafts.delete(project.id); acceptProject(value); toast("Tâche ajoutée."); }); }); main.append(form);
  if (project.notes) { const notes = el("div", "section-title"); notes.append(el("h3", "", "Notes")); main.append(notes, el("p", "detail-copy", project.notes)); }
  aside.append(el("h3", "", "Repères")); const facts = el("dl");
  for (const [label, value] of [["Échéance", dateText(project.dueDate)], ["Priorité", priorityNames[project.priority] || project.priority], ["Client", nameFor(project.clientId)]]) facts.append(el("dt", "", label), el("dd", "", value));
  aside.append(facts); body.append(main, aside); section.append(body);
}
function renderClient() {
  const client = state.client; const { section, header } = detailStart(clientName(client), client.clientCode, "clients");
  const controls = el("div", "detail-toolbar"), edit = button("Modifier la fiche", "secondary", () => openClientForm(client), "edit"), create = button("Nouveau projet", "primary", () => openProjectForm(client.id), "plus");
  edit.dataset.mutation = create.dataset.mutation = "true"; controls.append(edit, create); header.append(controls);
  const grid = el("div", "client-detail-grid"), contact = client.primaryContact || {};
  const contactBlock = el("div", "client-detail-block"); contactBlock.append(el("h3", "", "Coordonnées"), el("p", "", [[contact.firstName, contact.lastName].filter(Boolean).join(" "), contact.email, contact.phone, client.address].filter(Boolean).join("\n") || "Aucune coordonnée renseignée."));
  const notes = el("div", "client-detail-block"); notes.append(el("h3", "", "Notes"), el("p", "", client.notes || "Aucune note pour ce client.")); grid.append(contactBlock, notes); section.append(grid);
  const title = el("div", "section-title"); title.append(el("h3", "", "Projets du client")); section.append(title);
  const list = el("div", "list-content"), projects = state.projects.filter(project => project.clientId === client.id);
  projects.forEach(project => list.append(projectRow(project))); if (!projects.length) list.append(el("p", "task-empty", "Aucun projet pour ce client.")); section.append(list);
}
function prepareForm(id, title, description, target = null) {
  if (state.busy || state.pending || !state.online) return false;
  state.form = id; state.target = target;
  document.querySelectorAll(".edit-form").forEach(form => { form.hidden = form.id !== id; message(form.querySelector(".form-error"), ""); });
  $(id).reset(); $("form-title").textContent = title; $("form-description").textContent = description;
  return true;
}
function showForm() { $("form-dialog").showModal(); syncButtons(); }
function closeForm() { $("form-dialog").close(); state.form = null; state.target = null; state.resumeForm = false; }
function clientKindChanged() { const individual = $("client-kind").value === "Individual"; $("company-fields").hidden = individual; $("person-fields").hidden = !individual; $("client-company").required = !individual; $("client-last").required = individual; }
function openClientForm(client = null) {
  if (!prepareForm("client-form", client ? "Modifier le client" : "Nouveau client", client ? "Les coordonnées sont partagées avec vos appareils." : "Une fiche pour vos échanges et vos projets.", client)) return;
  $("client-kind").value = client?.kind || "Professional"; $("client-company").value = client?.companyName || ""; $("client-first").value = client?.firstName || ""; $("client-last").value = client?.lastName || "";
  $("client-code").value = client?.clientCode || ""; $("client-code").readOnly = !!client; $("client-code-help").textContent = client ? "Le code et les dossiers restent inchangés." : "Un code court pour reconnaître vos références de projet.";
  const contact = client?.primaryContact || {}; $("contact-first").value = contact.firstName || ""; $("contact-last").value = contact.lastName || ""; $("contact-email").value = contact.email || ""; $("contact-phone").value = contact.phone || "";
  $("client-address").value = client?.address || ""; $("client-notes").value = client?.notes || ""; clientKindChanged(); showForm();
}
function openProjectForm(clientId = null) {
  if (!state.clients.length) { openClientForm(); return; }
  if (!prepareForm("project-form", "Nouveau projet", "Choisissez son client ; l’atelier attribue la référence.")) return;
  $("project-client").replaceChildren(); state.clients.forEach(client => { const option = el("option", "", clientName(client) + " · " + client.clientCode); option.value = client.id; $("project-client").append(option); });
  if (clientId) $("project-client").value = clientId; $("project-year").value = new Date().getFullYear(); showForm();
}
function openProjectEdit() {
  const project = state.project;
  if (!prepareForm("project-edit-form", "Modifier le projet", project.reference + " · " + project.projectName, project)) return;
  $("edit-status").value = project.status; $("edit-due").value = project.dueDate || ""; $("edit-priority").value = project.priority; $("edit-description").value = project.description || ""; $("edit-notes").value = project.notes || ""; showForm();
}
function openTaskForm(task) {
  if (!prepareForm("task-edit-form", "Modifier la tâche", "Le suivi est enregistré sur le projet.", state.project)) return;
  state.task = task; $("task-title").value = task.title; $("task-notes").value = task.notes || ""; showForm();
}
function confirmRemove(task) {
  if (state.busy || state.pending || !state.online) return;
  state.task = task; $("confirm-item").textContent = task.title; $("confirm-dialog").showModal(); $("confirm-cancel").focus();
}
async function acceptClient(value) { state.client = value; state.project = null; state.view = "clients"; closeForm(); await refresh(); toast("Fiche client enregistrée."); }
function acceptProject(value) { state.project = value; state.client = null; state.view = "projects"; if (state.form) closeForm(); const index = state.projects.findIndex(project => project.id === value.id); if (index < 0) state.projects.push(value); else state.projects[index] = value; render(); }
function commandError(error, form) { const area = form ? $(form).querySelector(".form-error") : $("global-error"); message(area, error.message); }
async function perform(path, method, values, apply, form = null) {
  if (state.busy || state.pending || !state.online) return;
  const operation = { path, method, body: { commandId: crypto.randomUUID(), ...values }, apply, form };
  await sendOperation(operation);
}
async function sendOperation(operation) {
  state.busy = true; syncButtons(); if (operation.form) $(operation.form).setAttribute("aria-busy", "true");
  try {
    await csrf();
    const result = await api(operation.path, { method: operation.method, headers: { "Content-Type": "application/json", "X-CSRF-TOKEN": state.csrf }, body: JSON.stringify(operation.body) });
    state.pending = null; $("retry-command").hidden = true; setConnection(true); message($("global-error"), "");
    await operation.apply(result);
  } catch (error) {
    if (error.status === 0 || error.status >= 500) {
      state.pending = operation; $("retry-command").hidden = true;
      $("pending-message").textContent = "La réponse du serveur n’est pas arrivée. Vérifiez le résultat avant un nouvel envoi.";
    }
    commandError(error, operation.form);
  } finally { state.busy = false; if (operation.form) $(operation.form).removeAttribute("aria-busy"); syncButtons(); }
}
async function verifyCommand() {
  const operation = state.pending; if (!operation) return;
  $("verify-command").disabled = true;
  try {
    const receipt = await api("/api/v1/commands/" + operation.body.commandId);
    setConnection(true);
    if (receipt.state === "Completed") {
      state.pending = null; $("retry-command").hidden = true;
      if (receipt.statusCode >= 200 && receipt.statusCode < 300) { await operation.apply(receipt.result); toast("Envoi retrouvé et confirmé."); }
      else commandError(new Error(receipt.result?.error || receipt.result?.detail || receipt.result?.title || "L’envoi a été refusé. Votre saisie est conservée."), operation.form);
    } else {
      $("pending-message").textContent = "Le serveur a commencé cet envoi, mais son résultat doit être contrôlé. Aucun nouvel envoi n’est effectué.";
      $("retry-command").hidden = true;
    }
  } catch (error) {
    if (error.status === 404) { setConnection(true); $("pending-message").textContent = "Aucun reçu n’a été trouvé. Vous pouvez renvoyer exactement cette commande ; son identifiant évitera un doublon."; $("retry-command").hidden = false; }
    else message($("global-error"), error.message);
  } finally { $("verify-command").disabled = false; syncButtons(); }
}

$("login-form").addEventListener("submit", async event => {
  event.preventDefault(); const submit = $("login-form").querySelector("button[type=submit]"); submit.disabled = true;
  try { await csrf(); await api("/api/v1/auth/login", { method: "POST", headers: { "Content-Type": "application/json", "X-CSRF-TOKEN": state.csrf }, body: JSON.stringify({ name: $("login-name").value.trim(), password: $("login-password").value }) }); $("login-password").value = ""; await refresh(); }
  catch (error) { message($("login-error"), error.status === 401 ? "Identifiant ou mot de passe incorrect." : error.message); }
  finally { submit.disabled = false; }
});
$("logout-button").addEventListener("click", async () => {
  try { await csrf(); await api("/api/v1/auth/logout", { method: "POST", headers: { "X-CSRF-TOKEN": state.csrf } }); state.clients = []; state.projects = []; state.client = state.project = null; showLogin(); }
  catch (error) { message($("global-error"), error.message); }
});
document.querySelectorAll("[data-view]").forEach(node => node.addEventListener("click", () => navigate(node.dataset.view)));
document.querySelectorAll("[data-filter]").forEach(node => node.addEventListener("click", () => { state.filter = node.dataset.filter; state.page = 1; document.querySelectorAll("[data-filter]").forEach(tab => { tab.classList.toggle("selected", tab === node); tab.setAttribute("aria-pressed", String(tab === node)); }); renderList(); syncButtons(); }));
$("list-tabs").querySelector("button").textContent = "À suivre";
$("refresh-button").addEventListener("click", () => refresh(true));
$("search-input").addEventListener("input", () => { state.page = 1; renderList(); syncButtons(); });
$("primary-action").addEventListener("click", () => state.view === "clients" || !state.clients.length ? openClientForm() : openProjectForm());
$("client-kind").addEventListener("change", clientKindChanged);
$("close-dialog").addEventListener("click", () => { if (!state.busy) closeForm(); });
document.querySelectorAll(".cancel-dialog").forEach(node => node.addEventListener("click", () => { if (!state.busy) closeForm(); }));
$("form-dialog").addEventListener("cancel", event => { if (state.busy) event.preventDefault(); else { state.form = null; state.resumeForm = false; } });
$("confirm-cancel").addEventListener("click", () => $("confirm-dialog").close());
$("confirm-remove").addEventListener("click", () => { $("confirm-dialog").close(); const project = state.project, task = state.task; perform(`/api/v1/projects/${project.id}/tasks/${task.id}/remove`, "POST", { expectedUpdatedAt: project.updatedAt, confirmed: true }, acceptProject); });
$("verify-command").addEventListener("click", verifyCommand);
$("retry-command").addEventListener("click", () => { if (state.pending && !state.busy) sendOperation(state.pending); });
$("client-form").addEventListener("submit", event => {
  event.preventDefault(); const target = state.target;
  const input = { kind: $("client-kind").value, companyName: clean($("client-company").value), firstName: clean($("client-first").value), lastName: clean($("client-last").value), address: clean($("client-address").value), notes: clean($("client-notes").value), contact: { ...(target?.primaryContact || {}), firstName: clean($("contact-first").value), lastName: clean($("contact-last").value), phone: clean($("contact-phone").value), email: clean($("contact-email").value) } };
  if (target) input.expectedUpdatedAt = target.updatedAt; else input.clientCode = $("client-code").value.trim();
  perform("/api/v1/clients" + (target ? "/" + target.id : ""), target ? "PUT" : "POST", input, acceptClient, "client-form");
});
$("project-form").addEventListener("submit", event => { event.preventDefault(); perform("/api/v1/projects", "POST", { clientId: $("project-client").value, projectName: $("project-name").value.trim(), year: Number($("project-year").value), dueDate: $("project-due").value || null, description: clean($("project-description").value), priority: $("project-priority").value }, value => { acceptProject(value); toast("Projet créé."); }, "project-form"); });
$("project-edit-form").addEventListener("submit", event => { event.preventDefault(); const target = state.target; perform("/api/v1/projects/" + target.id, "PUT", { expectedUpdatedAt: target.updatedAt, status: $("edit-status").value, dueDate: $("edit-due").value || null, description: clean($("edit-description").value), notes: clean($("edit-notes").value), priority: $("edit-priority").value }, value => { acceptProject(value); toast("Projet enregistré."); }, "project-edit-form"); });
$("task-edit-form").addEventListener("submit", event => { event.preventDefault(); const target = state.target, task = state.task; perform(`/api/v1/projects/${target.id}/tasks/${task.id}`, "PUT", { expectedUpdatedAt: target.updatedAt, title: $("task-title").value.trim(), notes: clean($("task-notes").value) }, acceptProject, "task-edit-form"); });
window.addEventListener("offline", () => { setConnection(false); message($("global-error"), "Connexion interrompue. La dernière vue reste consultable ; les modifications sont suspendues."); });
window.addEventListener("online", () => { toast("Connexion réseau retrouvée. Actualisez pour vérifier l’atelier."); });
refresh();
