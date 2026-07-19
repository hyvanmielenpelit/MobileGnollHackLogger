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

    // RPG Table Layout Toggle Logic
    var layoutKey = 'rpgTableLayout';
    var savedLayout = localStorage.getItem(layoutKey) || 'auto';
    var $tables = $('#topScoreTable, #questLogsTable, #topScoresTable, .js-table');

    function applyLayout(layout) {
        $tables.removeClass('force-table-view force-card-view');
        
        if (layout === 'table') {
            $tables.addClass('force-table-view');
        } else if (layout === 'card') {
            $tables.addClass('force-card-view');
        }

        // Update button UI
        $('.layout-toggle-btn').removeClass('btn-primary active').addClass('btn-outline-secondary').css('border-color', 'rgba(255,255,255,0.2)');
        var $activeBtn = $('.layout-toggle-btn[data-layout="' + layout + '"]');
        $activeBtn.removeClass('btn-outline-secondary').addClass('btn-primary active').css('border-color', '');
    }

    // Apply only if there's a toggle button on the page
    if ($('.layout-toggle-btn').length > 0) {
        applyLayout(savedLayout);

        $('.layout-toggle-btn').on('click', function () {
            var layout = $(this).data('layout');
            localStorage.setItem(layoutKey, layout);
            applyLayout(layout);
        });
    } else if ($tables.length > 0 && savedLayout !== 'auto') {
        // If there's no toggle but there is a table, still apply the saved layout
        applyLayout(savedLayout);
    }
});
