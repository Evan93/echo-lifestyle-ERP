// Product image gallery - primary selection and client-side downscaling.
//
// The resizing here is a convenience, not a control. The server independently
// caps the size, verifies the bytes really are the image type they claim to be,
// and decides the stored file name. Everything below can be bypassed by anyone
// who wants to; nothing depends on it.

(function () {
    "use strict";

    var MAX_EDGE = 1600;
    var JPEG_QUALITY = 0.85;

    // -----------------------------------------------------------------------
    // "Main image" radios drive a hidden boolean per row.
    //
    // A radio group cannot post a boolean per item, and one primary per gallery
    // is exactly what a radio group is for - so the radio owns the interaction
    // and the hidden field owns the value.
    // -----------------------------------------------------------------------
    function initPrimarySelection() {
        var radios = document.querySelectorAll(".js-primary-image");

        if (radios.length === 0) {
            return;
        }

        function sync() {
            radios.forEach(function (radio) {
                var target = document.getElementById(radio.getAttribute("data-target"));

                if (target) {
                    target.value = radio.checked ? "true" : "false";
                }
            });
        }

        radios.forEach(function (radio) {
            radio.addEventListener("change", sync);
        });

        sync();
    }

    // -----------------------------------------------------------------------
    // Downscaling
    // -----------------------------------------------------------------------
    function loadImage(file) {
        return new Promise(function (resolve, reject) {
            var url = URL.createObjectURL(file);
            var image = new Image();

            image.onload = function () {
                URL.revokeObjectURL(url);
                resolve(image);
            };

            image.onerror = function () {
                URL.revokeObjectURL(url);
                reject(new Error("not an image this browser can read"));
            };

            image.src = url;
        });
    }

    function resize(file) {
        // Types the server accepts. Anything else is passed through untouched
        // and refused server-side with a message naming the file.
        if (["image/jpeg", "image/png", "image/webp"].indexOf(file.type) === -1) {
            return Promise.resolve(file);
        }

        return loadImage(file).then(function (image) {
            var longest = Math.max(image.width, image.height);

            if (longest <= MAX_EDGE) {
                return file;
            }

            var scale = MAX_EDGE / longest;
            var canvas = document.createElement("canvas");
            canvas.width = Math.round(image.width * scale);
            canvas.height = Math.round(image.height * scale);

            var context = canvas.getContext("2d");
            context.drawImage(image, 0, 0, canvas.width, canvas.height);

            return new Promise(function (resolve) {
                canvas.toBlob(
                    function (blob) {
                        if (!blob || blob.size >= file.size) {
                            // Re-encoding made it bigger, which happens with
                            // flat graphics. Keep the original.
                            resolve(file);
                            return;
                        }

                        // The type is preserved rather than forced to JPEG:
                        // converting a PNG would flatten its transparency, and
                        // packaging shots are often cut out on a clear
                        // background.
                        resolve(new File([blob], file.name, {
                            type: file.type,
                            lastModified: Date.now()
                        }));
                    },
                    file.type,
                    file.type === "image/png" ? undefined : JPEG_QUALITY);
            });
        }).catch(function () {
            // Unreadable here does not mean unreadable everywhere - HEIC, for
            // one. Send it as-is and let the server give the verdict.
            return file;
        });
    }

    function initUpload() {
        var form = document.getElementById("imageUploadForm");
        var input = document.getElementById("imageFiles");
        var hint = document.getElementById("uploadHint");

        if (!form || !input || typeof DataTransfer === "undefined") {
            return;
        }

        form.addEventListener("submit", function (event) {
            if (form.dataset.resized === "true" || input.files.length === 0) {
                return;
            }

            event.preventDefault();

            if (hint) {
                hint.textContent = "Preparing images...";
            }

            var work = Array.prototype.map.call(input.files, resize);

            Promise.all(work).then(function (files) {
                var transfer = new DataTransfer();
                files.forEach(function (file) {
                    transfer.items.add(file);
                });

                input.files = transfer.files;
                form.dataset.resized = "true";

                if (hint) {
                    hint.textContent = "Uploading...";
                }

                // Programmatic submit does not re-raise the event, so this
                // cannot loop.
                form.submit();
            }).catch(function () {
                form.dataset.resized = "true";
                form.submit();
            });
        });
    }

    document.addEventListener("DOMContentLoaded", function () {
        initPrimarySelection();
        initUpload();
    });
})();
