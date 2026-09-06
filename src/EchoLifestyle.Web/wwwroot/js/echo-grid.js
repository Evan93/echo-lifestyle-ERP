// Echo Lifestyle ERP - server-side grid helper.
//
// Every list in this system pages, sorts and searches at the database. This
// wraps DataTables with the conventions used across the back office so each
// screen only declares its columns and its endpoint.
//
//   echo.grid('#branches', {
//       url: '/backoffice/branches/data',
//       columns: [ ... ],
//       filters: function () { return { onlyActive: $('#onlyActive').is(':checked') }; }
//   });

(function () {
    "use strict";

    window.echo = window.echo || {};

    var DEFAULTS = {
        pageLength: 25,
        lengthMenu: [10, 25, 50, 100],
        order: [],
        emptyMessage: "Nothing to show yet.",
        searchPlaceholder: "Search..."
    };

    window.echo.grid = function (selector, options) {
        if (!window.jQuery || !jQuery.fn.DataTable) {
            return null;
        }

        var settings = jQuery.extend({}, DEFAULTS, options || {});
        var $table = jQuery(selector);

        if ($table.length === 0) {
            return null;
        }

        var table = $table.DataTable({
            serverSide: true,
            processing: true,
            searching: settings.searching !== false,
            ordering: true,
            deferRender: true,
            pageLength: settings.pageLength,
            lengthMenu: settings.lengthMenu,
            order: settings.order,
            columns: settings.columns,
            stateSave: false,

            ajax: {
                url: settings.url,
                type: "POST",

                // Flatten DataTables' nested request into the few fields the
                // server binds. Extra per-screen filters are merged in here so
                // filtering happens in SQL rather than in the browser.
                data: function (d) {
                    var sortColumn = null;
                    var sortDirection = null;

                    if (d.order && d.order.length > 0) {
                        var column = d.columns[d.order[0].column];
                        sortColumn = column ? (column.name || column.data) : null;
                        sortDirection = d.order[0].dir;
                    }

                    var payload = {
                        draw: d.draw,
                        start: d.start,
                        length: d.length,
                        search: d.search ? d.search.value : null,
                        sortColumn: sortColumn,
                        sortDirection: sortDirection
                    };

                    if (typeof settings.filters === "function") {
                        jQuery.extend(payload, settings.filters());
                    }

                    return payload;
                },

                error: function (jqxhr) {
                    var traceId = "";
                    try {
                        traceId = (jqxhr.responseJSON && jqxhr.responseJSON.traceId) || "";
                    } catch (e) {
                        traceId = "";
                    }

                    if (jqxhr.status === 403) {
                        window.echo.toast("You do not have permission to view this list.", "warning");
                        return;
                    }

                    window.echo.toast(
                        "The list could not be loaded." + (traceId ? " Reference: " + traceId : ""),
                        "danger");
                }
            },

            language: {
                emptyTable: settings.emptyMessage,
                zeroRecords: "Nothing matches that search.",
                search: "",
                searchPlaceholder: settings.searchPlaceholder,
                lengthMenu: "_MENU_ per page",
                info: "_START_ to _END_ of _TOTAL_",
                infoEmpty: "0 of 0",
                infoFiltered: "(filtered from _MAX_)",
                processing: '<span class="spinner-border spinner-border-sm"></span>'
            },

            layout: settings.layout || {
                topStart: "pageLength",
                topEnd: "search",
                bottomStart: "info",
                bottomEnd: "paging"
            }
        });

        return table;
    };

    // Re-draws a grid when a filter control changes. Kept here so screens do
    // not each reinvent a debounce.
    window.echo.bindFilters = function (table, selector, delay) {
        if (!table) {
            return;
        }

        var timer = null;

        jQuery(document).on("change keyup", selector, function (event) {
            var wait = event.type === "keyup" ? (delay || 350) : 0;

            window.clearTimeout(timer);
            timer = window.setTimeout(function () {
                table.ajax.reload(null, true);
            }, wait);
        });
    };

    // Escapes text before it goes into a cell rendered as HTML. DataTables
    // renders strings as HTML by default, so anything a user typed - a product
    // name, a customer note - must pass through here.
    window.echo.escape = function (value) {
        if (value === null || value === undefined) {
            return "";
        }

        return String(value)
            .replace(/&/g, "&amp;")
            .replace(/</g, "&lt;")
            .replace(/>/g, "&gt;")
            .replace(/"/g, "&quot;")
            .replace(/'/g, "&#39;");
    };

    // Common cell renderers.
    window.echo.render = {
        badge: function (text, variant) {
            return '<span class="badge text-bg-' + (variant || "secondary") + '">' +
                window.echo.escape(text) + "</span>";
        },

        yesNo: function (value, trueVariant, falseVariant) {
            return value
                ? window.echo.render.badge("Yes", trueVariant || "success")
                : window.echo.render.badge("No", falseVariant || "light border");
        },

        code: function (value) {
            return "<code>" + window.echo.escape(value) + "</code>";
        },

        actions: function (buttons) {
            return '<div class="btn-group btn-group-sm">' + buttons.join("") + "</div>";
        },

        editLink: function (url) {
            return '<a class="btn btn-sm btn-outline-secondary" href="' +
                window.echo.escape(url) + '">Edit</a>';
        }
    };
})();
