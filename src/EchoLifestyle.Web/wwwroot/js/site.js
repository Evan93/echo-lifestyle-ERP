// Echo Lifestyle ERP - shared client behaviour.
// Feature-specific scripts live in wwwroot/js/<feature>/, never inline in a view.

(function () {
    "use strict";

    var STORAGE_KEY = "echo.sidebar.collapsed";

    // -----------------------------------------------------------------------
    // Anti-forgery for AJAX.
    //
    // Every state-changing request is validated server-side
    // (AutoValidateAntiforgeryToken), so jQuery posts must carry the token too.
    // -----------------------------------------------------------------------
    function antiForgeryToken() {
        var input = document.querySelector('input[name="__RequestVerificationToken"]');
        return input ? input.value : null;
    }

    if (window.jQuery) {
        jQuery.ajaxSetup({
            beforeSend: function (xhr, settings) {
                var method = (settings.type || "GET").toUpperCase();
                if (method === "GET" || method === "HEAD" || method === "OPTIONS" || settings.crossDomain) {
                    return;
                }

                var token = antiForgeryToken();
                if (token) {
                    xhr.setRequestHeader("RequestVerificationToken", token);
                }

                xhr.setRequestHeader("X-Requested-With", "XMLHttpRequest");
            }
        });

        // Unhandled AJAX failures surface as a toast with the trace id, so a
        // silent failure never looks like "nothing happened".
        jQuery(document).ajaxError(function (event, jqxhr) {
            var traceId = "";
            try {
                traceId = (jqxhr.responseJSON && jqxhr.responseJSON.traceId) || "";
            } catch (e) {
                traceId = "";
            }

            if (jqxhr.status === 0) {
                return; // navigated away or request aborted
            }

            window.echo.toast(
                "That request could not be completed." + (traceId ? " Reference: " + traceId : ""),
                "danger");
        });
    }

    // -----------------------------------------------------------------------
    // Sidebar: collapse on desktop, slide-over on mobile. The desktop choice
    // is remembered per browser - a convenience, nothing depends on it.
    // -----------------------------------------------------------------------
    function initSidebar() {
        var shell = document.getElementById("echoShell");
        var toggle = document.getElementById("echoSidebarToggle");
        var backdrop = document.getElementById("echoBackdrop");

        if (!shell || !toggle) {
            return;
        }

        function isDesktop() {
            return window.matchMedia("(min-width: 992px)").matches;
        }

        try {
            if (window.localStorage.getItem(STORAGE_KEY) === "true" && isDesktop()) {
                shell.classList.add("is-collapsed");
            }
        } catch (e) {
            // Private browsing or blocked storage - the sidebar simply starts expanded.
        }

        toggle.addEventListener("click", function () {
            if (isDesktop()) {
                var collapsed = shell.classList.toggle("is-collapsed");
                try {
                    window.localStorage.setItem(STORAGE_KEY, collapsed ? "true" : "false");
                } catch (e) {
                    // ignore
                }
            } else {
                shell.classList.toggle("is-open");
            }
        });

        if (backdrop) {
            backdrop.addEventListener("click", function () {
                shell.classList.remove("is-open");
            });
        }

        document.addEventListener("keydown", function (event) {
            if (event.key === "Escape") {
                shell.classList.remove("is-open");
            }
        });
    }

    // -----------------------------------------------------------------------
    // Toasts
    // -----------------------------------------------------------------------
    function initToasts() {
        var container = document.getElementById("echoToasts");
        if (!container || !window.bootstrap) {
            return;
        }

        container.querySelectorAll(".toast").forEach(function (element) {
            new window.bootstrap.Toast(element, { delay: 5000 }).show();
        });
    }

    window.echo = window.echo || {};

    window.echo.toast = function (message, type) {
        var container = document.getElementById("echoToasts");
        if (!container || !window.bootstrap) {
            return;
        }

        var element = document.createElement("div");
        element.className = "toast align-items-center text-bg-" + (type || "success") + " border-0";
        element.setAttribute("role", "alert");
        element.setAttribute("aria-live", "assertive");
        element.setAttribute("aria-atomic", "true");

        var body = document.createElement("div");
        body.className = "toast-body";
        body.textContent = message;          // textContent, not innerHTML - no injection path

        var close = document.createElement("button");
        close.type = "button";
        close.className = "btn-close btn-close-white me-2 m-auto";
        close.setAttribute("data-bs-dismiss", "toast");
        close.setAttribute("aria-label", "Close");

        var row = document.createElement("div");
        row.className = "d-flex";
        row.appendChild(body);
        row.appendChild(close);
        element.appendChild(row);

        container.appendChild(element);
        new window.bootstrap.Toast(element, { delay: 6000 }).show();

        element.addEventListener("hidden.bs.toast", function () {
            element.remove();
        });
    };

    // -----------------------------------------------------------------------
    // Duplicate submit protection.
    //
    // A double-clicked Save must not create two documents. The server still
    // owns correctness; this removes the easy way to trigger it.
    // -----------------------------------------------------------------------
    function initSubmitOnce() {
        document.addEventListener("submit", function (event) {
            var form = event.target;
            if (!(form instanceof HTMLFormElement) || form.dataset.submitting === "true") {
                return;
            }

            var button = form.querySelector("[data-submit-once]");
            if (!button) {
                return;
            }

            if (form.checkValidity && !form.checkValidity()) {
                return;
            }

            form.dataset.submitting = "true";
            button.disabled = true;
            button.innerHTML =
                '<span class="spinner-border spinner-border-sm me-2" role="status" aria-hidden="true"></span>Working...';
        }, true);
    }

    document.addEventListener("DOMContentLoaded", function () {
        initSidebar();
        initToasts();
        initSubmitOnce();
    });
})();
