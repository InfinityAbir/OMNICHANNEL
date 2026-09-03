/* Omnichannel AI Inbox - self-hosted website chat widget (PRD 64).
 * Served by the product API at /widget/embed.js. A site embeds it with:
 *   <script src="https://YOUR-API/widget/embed.js" data-slug="YOUR-BUSINESS-SLUG" defer></script>
 *
 * The widget self-locates the API base URL from its own <script> src, so no per-site config is
 * needed beyond the slug. All message content is rendered as text (HTML-escaped) because message
 * text is untrusted user/agent content. Origin validation happens server-side: the /session call
 * is only accepted when the embedding site's Origin is in the tenant's widget allowlist.
 */
(function () {
  "use strict";

  var script = document.getElementById("omnichannel-widget-embed") || (function () {
    var s = document.querySelectorAll('script[data-slug]');
    for (var i = 0; i < s.length; i++) { if (/embed\.js/i.test(s[i].getAttribute("src") || "")) return s[i]; }
    return null;
  })();
  if (!script || !script.getAttribute("data-slug")) return;

  var FILE = /\/[^\/]*\.js(\?.*)?$/i;
  var apiBase = (script.getAttribute("src") || "").replace(FILE, "");
  var slug = script.getAttribute("data-slug");
  var storageKey = "omnichannel_widget_" + slug + "_visitor";

  // ---- tiny helper functions ---------------------------------------------------------------
  function escapeHtml(value) {
    return String(value == null ? "" : value)
      .replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;")
      .replace(/"/g, "&quot;").replace(/'/g, "&#39;");
  }
  function fmtTime(iso) {
    try { return new Date(iso).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" }); }
    catch (e) { return ""; }
  }
  function getVisitorKey() {
    try {
      var k = localStorage.getItem(storageKey);
      if (!k) { k = "v_" + Date.now().toString(36) + "_" + Math.random().toString(36).slice(2, 10); localStorage.setItem(storageKey, k); }
      return k;
    } catch (e) { return "v_" + Date.now().toString(36); }
  }
  function loadScript(src, integrityCb) {
    return new Promise(function (resolve, reject) {
      var el = document.createElement("script");
      el.src = src;
      el.async = true;
      el.onload = function () { if (integrityCb) integrityCb(); resolve(); };
      el.onerror = function () { reject(new Error("Failed to load " + src)); };
      document.head.appendChild(el);
    });
  }
  function ensureCss() {
    if (document.getElementById("omnichannel-widget-css")) return;
    var link = document.createElement("link");
    link.id = "omnichannel-widget-css";
    link.rel = "stylesheet";
    link.href = apiBase + "/widget/widget.css";
    document.head.appendChild(link);
  }

  // ---- state -------------------------------------------------------------------------------
  var session = null; // { sessionToken, conversationId, connectionUrl }
  var connection = null;
  var active = false;
  var rootEl = null;

  function vm() { return window.omnichannelSignalR; }

  function connect() {
    if (connection) return;
    if (!vm() || !vm().HubConnectionBuilder) return;
    connection = new vm().HubConnectionBuilder()
      .withUrl(session.connectionUrl + "?access_token=" + encodeURIComponent(session.sessionToken))
      .withAutomaticReconnect()
      .build();
    connection.on("newMessage", function (msg) {
      if (!msg || !msg.messageId) return;
      appendMessage({ id: msg.messageId, direction: msg.direction, text: msg.text, createdAt: msg.createdAt });
    });
    connection.start().catch(function () { /* retry via automatic reconnect */ });
  }

  function appendMessage(msg) {
    var list = rootEl.querySelector(".ocw__messages");
    var incoming = String(msg.direction || "").toLowerCase() === "inbound";
    var el = document.createElement("div");
    el.className = "ocw__msg " + (incoming ? "ocw__msg--in" : "ocw__msg--out");
    el.innerHTML =
      '<div class="ocw__bubble">' + escapeHtml(msg.text || "") +
      '<span class="ocw__time">' + escapeHtml(fmtTime(msg.createdAt)) + "</span></div>";
    list.appendChild(el);
    list.scrollTop = list.scrollHeight;
  }

  function openSession() {
    return fetch(apiBase + "/widget/" + encodeURIComponent(slug) + "/session", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ visitorKey: getVisitorKey(), visitorName: null })
    }).then(function (r) {
      if (!r.ok) throw new Error("session-failed-" + r.status);
      return r.json();
    }).then(function (data) {
      session = data;
      connection = null;
      connect();
      loadThread();
    });
  }

  function loadThread() {
    return fetch(apiBase + "/widget/conversations/" + session.conversationId + "/messages", {
      headers: { "Authorization": "Bearer " + session.sessionToken }
    }).then(function (r) { return r.ok ? r.json() : { messages: [] }; })
      .then(function (data) {
        (data.messages || []).forEach(function (m) {
          appendMessage({ id: m.messageId, direction: m.direction, text: m.text, createdAt: m.createdAt });
        });
      }).catch(function () { /* ignore */ });
  }

  function sendMessage(text) {
    if (!session || !text.trim()) return;
    appendMessage({ id: null, direction: "inbound", text: text.trim(), createdAt: new Date().toISOString() });
    return fetch(apiBase + "/widget/conversations/" + session.conversationId + "/messages", {
      method: "POST",
      headers: { "Content-Type": "application/json", "Authorization": "Bearer " + session.sessionToken },
      body: JSON.stringify({ conversationId: session.conversationId, text: text.trim() })
    }).catch(function () { /* the optimistic message stays; reload thread will reconcile */ });
  }

  // ---- UI ----------------------------------------------------------------------------------
  function build() {
    rootEl = document.createElement("div");
    rootEl.id = "omnichannel-widget-root";
    rootEl.innerHTML =
      '<button class="ocw__launcher" type="button" aria-label="Open chat" aria-expanded="false">' +
      '<svg width="24" height="24" viewBox="0 0 24 24" fill="none" aria-hidden="true">' +
      '<path d="M12 3C7 3 3 6.6 3 11c0 4.4 4 8 9 8 .6 0 1.1 0 1.6-.1L18 21v-3.4c2-1.5 3-3.6 3-6.6 0-4.4-4-8-9-8z" fill="currentColor"/></svg>' +
      "</button>" +
      '<div class="ocw__panel" role="dialog" aria-label="Chat with us" hidden>' +
      '<header class="ocw__header"><span class="ocw__title">Chat with us</span>' +
      '<button class="ocw__close" type="button" aria-label="Close chat">&times;</button></header>' +
      '<div class="ocw__messages" aria-live="polite"></div>' +
      '<form class="ocw__form">' +
      '<textarea class="ocw__input" rows="1" placeholder="Type a message…" aria-label="Message"></textarea>' +
      '<button class="ocw__send" type="submit" aria-label="Send message">Send</button>' +
      "</form></div>";

    document.body.appendChild(rootEl);
    var launcher = rootEl.querySelector(".ocw__launcher");
    var panel = rootEl.querySelector(".ocw__panel");
    var form = rootEl.querySelector(".ocw__form");
    var input = rootEl.querySelector(".ocw__input");
    var close = rootEl.querySelector(".ocw__close");

    function toggle(open) {
      active = open;
      panel.hidden = !open;
      launcher.setAttribute("aria-expanded", String(open));
      launcher.hidden = open;
      if (open) {
        if (!session) openSession().catch(function () { panel.querySelector(".ocw__messages").innerHTML = '<div class="ocw__error">Unable to start chat.</div>'; });
        setTimeout(function () { input.focus(); }, 50);
      }
    }

    launcher.addEventListener("click", function () { toggle(true); });
    close.addEventListener("click", function () { toggle(false); });
    form.addEventListener("submit", function (e) {
      e.preventDefault();
      var text = input.value;
      input.value = "";
      sendMessage(text).then(function () { input.focus(); });
    });
    input.addEventListener("keydown", function (e) {
      if (e.key === "Enter" && !e.shiftKey) { e.preventDefault(); form.requestSubmit(); }
    });
  }

  // ---- init --------------------------------------------------------------------------------
  ensureCss();
  loadScript(apiBase + "/widget/signalr.min.js").then(build).catch(function () {
    // SignalR unavailable: still provide a usable (non-realtime) widget.
    build();
  });
})();
