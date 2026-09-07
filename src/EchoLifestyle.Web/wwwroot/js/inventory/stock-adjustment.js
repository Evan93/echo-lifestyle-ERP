// Stock adjustment - line entry.
//
// Close to Quick Purchase, with one difference that matters: a line names a
// batch as well as a product, because cost lives on the batch. Picking a product
// loads its batches and the line is not complete until one is chosen.
//
// The reason supplies the direction. Nobody types a minus sign for "damaged" -
// that is the one mistake that would double a write-off - so the quantity box
// takes a plain positive number and the server applies the sign. Correction is
// the exception, and says so.

(function () {
    "use strict";

    var lineIndex = 0;

    function initStockAdjustment(options) {
        var $search = jQuery("#lineSearch");
        var $results = jQuery("#lineSearchResults");
        var $lines = jQuery("#lineRows");
        var $empty = jQuery("#noLines");
        var $reason = jQuery("#Reason");

        // -------------------------------------------------------------------
        // The reason decides what the quantity box means
        // -------------------------------------------------------------------
        function rules() {
            return options.reasonRules[String($reason.val())] || {};
        }

        function isSigned() {
            var current = rules();
            return !current.outboundOnly && !current.inboundOnly;
        }

        function syncReason() {
            var current = rules();

            var hint = current.outboundOnly
                ? "Takes stock out. Enter how many, not a negative number."
                : current.inboundOnly
                    ? "Brings stock in. Enter how many were found."
                    : "Either direction. A negative quantity takes stock out.";

            jQuery("#reasonHint").text(hint);

            // Re-point the quantity inputs at the new direction. An existing
            // negative under an outbound reason would otherwise be sent as a
            // positive and refused by the server for no visible cause.
            $lines.find(".js-quantity").each(function () {
                var $input = jQuery(this);

                $input.attr("min", isSigned() ? null : "0.01");

                if (!isSigned()) {
                    $input.val(Math.abs(Number($input.val() || 0)) || 1);
                }
            });

            $lines.find(".js-direction").text(
                current.outboundOnly ? "out" : current.inboundOnly ? "in" : "");
        }

        $reason.on("change", syncReason);

        // -------------------------------------------------------------------
        // Searching for a product
        // -------------------------------------------------------------------
        var searchTimer = null;

        function closeResults() {
            $results.empty().hide();
        }

        function renderResults(matches) {
            $results.empty();

            if (matches.length === 0) {
                $results.append(
                    jQuery("<div>").addClass("list-group-item text-muted small")
                        .text("Nothing matches that."));
                $results.show();
                return;
            }

            matches.forEach(function (match, position) {
                var $item = jQuery("<button>")
                    .attr("type", "button")
                    .addClass("list-group-item list-group-item-action py-2")
                    .toggleClass("active", position === 0)
                    .data("match", match);

                $item.append(jQuery("<div>").addClass("fw-semibold").text(match.display));
                $item.append(jQuery("<div>").addClass("small text-muted")
                    .text(match.sku + " · " + match.brandName));

                $results.append($item);
            });

            $results.show();
        }

        $search.on("input", function () {
            var term = jQuery(this).val();

            window.clearTimeout(searchTimer);

            if (!term || term.length < 2) {
                closeResults();
                return;
            }

            searchTimer = window.setTimeout(function () {
                jQuery.getJSON(options.lookupUrl, { term: term })
                    .done(renderResults)
                    .fail(closeResults);
            }, 200);
        });

        $search.on("keydown", function (event) {
            var $items = $results.find(".list-group-item-action");

            if ($items.length === 0) {
                return;
            }

            var current = $items.index($items.filter(".active"));

            if (event.key === "ArrowDown" || event.key === "ArrowUp") {
                event.preventDefault();

                var next = event.key === "ArrowDown" ? current + 1 : current - 1;
                next = Math.max(0, Math.min($items.length - 1, next));

                $items.removeClass("active").eq(next).addClass("active");
            } else if (event.key === "Enter") {
                event.preventDefault();
                addLine($items.filter(".active").data("match"));
            } else if (event.key === "Escape") {
                closeResults();
            }
        });

        $results.on("click", ".list-group-item-action", function () {
            addLine(jQuery(this).data("match"));
        });

        jQuery(document).on("click", function (event) {
            if (!jQuery(event.target).closest("#lineSearchWrapper").length) {
                closeResults();
            }
        });

        // -------------------------------------------------------------------
        // Lines
        // -------------------------------------------------------------------
        function addLine(match) {
            if (!match) {
                return;
            }

            var i = lineIndex++;
            var name = function (field) { return "Lines[" + i + "]." + field; };

            var $row = jQuery("<tr>")
                .attr("data-line", i)
                .attr("data-variant", match.id);

            var hidden = function (field, value) {
                return jQuery("<input>")
                    .attr({ type: "hidden", name: name(field) })
                    .addClass("js-" + field.toLowerCase())
                    .val(value);
            };

            $row.append(jQuery("<td>")
                .append(jQuery("<div>").addClass("fw-semibold").text(match.display))
                .append(jQuery("<div>").addClass("small text-muted").text(match.sku))
                .append(hidden("ProductVariantId", match.id))
                .append(hidden("Sku", match.sku))
                .append(hidden("Description", match.display)));

            var $batchSelect = jQuery("<select>")
                .attr("name", name("StockBatchId"))
                .addClass("form-select form-select-sm js-batch")
                .append(jQuery("<option>").val("").text("Loading batches..."));

            $row.append(jQuery("<td>")
                .append($batchSelect)
                .append(jQuery("<input>").attr({ type: "hidden", name: name("BatchLabel") })
                    .addClass("js-batchlabel")));

            var $quantity = jQuery("<input>")
                .attr({ type: "number", name: name("Quantity"), step: "0.01" })
                .addClass("form-control form-control-sm text-end js-quantity")
                .val(1);

            if (!isSigned()) {
                $quantity.attr("min", "0.01");
            }

            $row.append(jQuery("<td>").addClass("text-end")
                .append($quantity)
                .append(jQuery("<div>").addClass("small text-muted js-direction")));

            $row.append(jQuery("<td>").append(
                jQuery("<input>").attr({
                    type: "text", name: name("Notes"), maxlength: 500, placeholder: "Optional"
                }).addClass("form-control form-control-sm")));

            $row.append(jQuery("<td>").addClass("text-end").append(
                jQuery("<button>").attr({ type: "button", title: "Remove" })
                    .addClass("btn btn-sm btn-outline-danger js-remove").text("×")));

            $lines.append($row);
            $empty.hide();

            loadBatches($row, match.id);

            $search.val("").focus();
            closeResults();
            syncReason();
            recount();
        }

        function loadBatches($row, variantId) {
            var $select = $row.find(".js-batch");

            jQuery.getJSON(options.batchesUrl, {
                productVariantId: variantId,
                warehouseId: jQuery("#WarehouseId").val()
            }).done(function (batches) {
                $select.empty();

                if (!batches || batches.length === 0) {
                    // No batch means the product has never been received. There
                    // is nothing to adjust and nothing to cost it at, and
                    // saying so here is kinder than a server error later.
                    $select.append(jQuery("<option>").val("")
                        .text("Never received - receive it first"));
                    $select.addClass("is-invalid");
                    return;
                }

                batches.forEach(function (batch) {
                    $select.append(jQuery("<option>")
                        .val(batch.stockBatchId)
                        .text(batch.display)
                        .attr("data-on-hand", batch.quantityOnHand));
                });

                $row.find(".js-batchlabel").val($select.find("option:selected").text());
            }).fail(function () {
                $select.empty().append(
                    jQuery("<option>").val("").text("Could not load batches"));
            });
        }

        $lines.on("change", ".js-batch", function () {
            var $select = jQuery(this);
            $select.closest("tr").find(".js-batchlabel").val($select.find("option:selected").text());
        });

        // Reloading batches when the warehouse changes: a batch's quantity is
        // per warehouse, so the old list is about the wrong place.
        jQuery("#WarehouseId").on("change", function () {
            $lines.find("tr[data-line]").each(function () {
                var $row = jQuery(this);
                loadBatches($row, $row.attr("data-variant"));
            });
        });

        $lines.on("click", ".js-remove", function () {
            jQuery(this).closest("tr").remove();

            if ($lines.find("tr[data-line]").length === 0) {
                $empty.show();
            }

            recount();
        });

        function recount() {
            jQuery("#lineCount").text($lines.find("tr[data-line]").length);
        }

        syncReason();
        recount();
        $search.focus();
    }

    window.echo = window.echo || {};
    window.echo.stockAdjustment = initStockAdjustment;
})();
