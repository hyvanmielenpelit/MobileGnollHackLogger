// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Write your JavaScript code.

$(function () {
    $('.disable-button-on-submit').on('submit', function () {
        if ($(this).valid && !$(this).valid()) {
            return false;
        }
        $(this).find('button[type="submit"]').prop('disabled', true);
    });
});
