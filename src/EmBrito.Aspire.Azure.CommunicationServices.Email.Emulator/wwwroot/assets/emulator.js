(() => {
  const workspace = document.querySelector("[data-mail-workspace]");
  const inboxResults = document.querySelector("[data-inbox-results]");
  const messageDetail = document.querySelector("[data-message-detail]");
  const searchForm = document.querySelector("[data-search-form]");
  const searchInput = searchForm?.querySelector("input[name='q']");
  const emptyTemplate = document.querySelector("#empty-message-template");
  const liveStatus = document.querySelector("[data-live-status]");

  if (!workspace || !inboxResults || !messageDetail || !searchForm || !searchInput) {
    return;
  }

  let selectedId = workspace.dataset.selectedId || "";
  let refreshSequence = 0;
  let scheduledRefresh = 0;

  const currentQuery = () => searchInput.value.trim();

  const updateLocation = (mode = "replace") => {
    const url = new URL(window.location.href);
    const query = currentQuery();

    if (selectedId) {
      url.searchParams.set("message", selectedId);
    } else {
      url.searchParams.delete("message");
    }

    if (query) {
      url.searchParams.set("q", query);
    } else {
      url.searchParams.delete("q");
    }

    const state = { message: selectedId, q: query };
    if (mode === "push") {
      window.history.pushState(state, "", url);
    } else {
      window.history.replaceState(state, "", url);
    }
  };

  const setSelectedRow = () => {
    inboxResults.querySelectorAll("[data-message-link]").forEach(row => {
      const selected = row.dataset.messageId === selectedId;
      row.classList.toggle("selected", selected);
      if (selected) {
        row.setAttribute("aria-current", "true");
      } else {
        row.removeAttribute("aria-current");
      }
    });
  };

  const clearDetail = () => {
    selectedId = "";
    workspace.dataset.selectedId = "";
    messageDetail.innerHTML = emptyTemplate?.innerHTML || "";
    setSelectedRow();
  };

  const selectMessage = async (operationId, historyMode = "push") => {
    if (!operationId) {
      clearDetail();
      updateLocation(historyMode);
      return;
    }

    messageDetail.setAttribute("aria-busy", "true");
    try {
      const response = await fetch(
        `/_emulator/ui/messages/${encodeURIComponent(operationId)}`,
        { headers: { "X-Requested-With": "fetch" } });

      if (!response.ok) {
        clearDetail();
        updateLocation("replace");
        await refreshInbox(true);
        return;
      }

      messageDetail.innerHTML = await response.text();
      selectedId = operationId;
      workspace.dataset.selectedId = operationId;
      setSelectedRow();
      updateLocation(historyMode);
    } finally {
      messageDetail.removeAttribute("aria-busy");
    }
  };

  const refreshInbox = async (selectFirstWhenEmpty = false) => {
    const sequence = ++refreshSequence;
    const url = new URL("/_emulator/ui/inbox", window.location.origin);
    const query = currentQuery();
    if (query) {
      url.searchParams.set("q", query);
    }
    if (selectedId) {
      url.searchParams.set("selected", selectedId);
    }

    const response = await fetch(url, { headers: { "X-Requested-With": "fetch" } });
    if (!response.ok || sequence !== refreshSequence) {
      return;
    }

    inboxResults.innerHTML = await response.text();
    setSelectedRow();

    if (selectFirstWhenEmpty && !selectedId) {
      const first = inboxResults.querySelector("[data-message-link]");
      if (first) {
        await selectMessage(first.dataset.messageId, "replace");
      }
    }
  };

  const scheduleInboxRefresh = (selectFirstWhenEmpty = false) => {
    window.clearTimeout(scheduledRefresh);
    scheduledRefresh = window.setTimeout(
      () => refreshInbox(selectFirstWhenEmpty),
      80);
  };

  document.addEventListener("click", event => {
    const link = event.target.closest("[data-message-link]");
    if (!link) {
      return;
    }

    event.preventDefault();
    selectMessage(link.dataset.messageId);
  });

  document.addEventListener("submit", async event => {
    const form = event.target;

    if (form.matches("[data-search-form]")) {
      event.preventDefault();
      updateLocation("push");
      await refreshInbox(!selectedId);
      return;
    }

    if (form.matches("[data-delete-message]")) {
      event.preventDefault();
      if (!window.confirm("Delete this captured message?")) {
        return;
      }

      const id = selectedId;
      const response = await fetch(
        `/_emulator/api/messages/${encodeURIComponent(id)}`,
        { method: "DELETE" });
      if (response.ok) {
        clearDetail();
        updateLocation("replace");
        await refreshInbox(true);
      }
      return;
    }

    if (form.matches("[data-delete-all]")) {
      event.preventDefault();
      if (!window.confirm("Delete all captured messages?")) {
        return;
      }

      const response = await fetch("/_emulator/api/messages", { method: "DELETE" });
      if (response.ok) {
        clearDetail();
        updateLocation("replace");
        await refreshInbox();
      }
    }
  });

  window.addEventListener("popstate", async () => {
    const url = new URL(window.location.href);
    searchInput.value = url.searchParams.get("q") || "";
    const operationId = url.searchParams.get("message") || "";

    selectedId = operationId;
    workspace.dataset.selectedId = operationId;
    if (operationId) {
      await refreshInbox();
      await selectMessage(operationId, "replace");
    } else {
      clearDetail();
      await refreshInbox(true);
    }
  });

  const events = new EventSource("/_emulator/events");
  events.addEventListener("open", () => {
    liveStatus.dataset.state = "connected";
    liveStatus.querySelector("strong").textContent = "Live updates";
  });
  events.addEventListener("inbox", event => {
    let notification = {};
    try {
      notification = JSON.parse(event.data);
    } catch {
      // A malformed notification should not stop future inbox refreshes.
    }

    const selectedWasDeleted =
      notification.kind === "all-messages-deleted" ||
      (notification.kind === "message-deleted" &&
        notification.operationId === selectedId);

    if (selectedWasDeleted) {
      clearDetail();
      updateLocation("replace");
    }

    scheduleInboxRefresh(selectedWasDeleted || !selectedId);
  });
  events.addEventListener("error", () => {
    liveStatus.dataset.state = "connecting";
    liveStatus.querySelector("strong").textContent = "Reconnecting";
  });
})();
