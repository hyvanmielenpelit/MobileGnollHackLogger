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

    // Cookie Consent: Google Tag Manager loads only after an explicit Accept
    const $consentDiv = $("#consentDiv");
    if ($consentDiv.length > 0) {
        const consentStorageKey = "cookieConsentChoice";
        const gtmId = $consentDiv.attr("data-gtm-id");
        let gtmLoaded = false;
        let settingsReopened = false;

        const readConsentChoice = function () {
            try {
                return localStorage.getItem(consentStorageKey);
            } catch (e) {
                return null;
            }
        };

        const saveConsentChoice = function (choice) {
            try {
                localStorage.setItem(consentStorageKey, choice);
            } catch (e) {
                // Without storage the choice applies to the current page only
            }
        };

        // True when the page's query, URL-decoded, carries an account ID, a code, an email address or a token
        const urlCarriesIdentifier = function () {
            let query = window.location.search;
            try {
                query = decodeURIComponent(query);
            } catch (e) {
                // A malformed escape is tested undecoded
            }
            return /[?&](userId|code|email|token)=/i.test(query);
        };

        const loadTagManager = function () {
            if (gtmLoaded || urlCarriesIdentifier()) {
                return;
            }
            gtmLoaded = true;
            window.dataLayer = window.dataLayer || [];
            window.dataLayer.push({ "gtm.start": new Date().getTime(), event: "gtm.js" });
            const script = document.createElement("script");
            script.async = true;
            script.src = "https://www.googletagmanager.com/gtm.js?id=" + encodeURIComponent(gtmId);
            document.head.appendChild(script);
        };

        // Removes first-party Google Analytics and Google Ads cookies on this host and its parent domains
        const clearTrackingCookies = function () {
            const hostParts = window.location.hostname.split(".");
            const domainAttributes = [""];
            for (let i = 0; i < hostParts.length - 1; i++) {
                domainAttributes.push("; domain=." + hostParts.slice(i).join("."));
            }
            document.cookie.split(";").forEach(cookie => {
                const name = cookie.split("=")[0].trim();
                if (/^_(ga|gid|gat|gcl|gac)(_|$)/.test(name)) {
                    domainAttributes.forEach(domainAttribute => {
                        document.cookie = name + "=; Max-Age=0; path=/" + domainAttribute;
                    });
                }
            });
        };

        const storedChoice = readConsentChoice();
        if (storedChoice === "accepted") {
            loadTagManager();
        } else {
            clearTrackingCookies();
            if (storedChoice !== "declined") {
                $consentDiv.show();
            }
        }

        $consentDiv.on("click", "button[data-consent-choice]", function () {
            const choice = $(this).attr("data-consent-choice");
            saveConsentChoice(choice);
            $consentDiv.hide();
            if (choice === "accepted") {
                loadTagManager();
            } else {
                clearTrackingCookies();
                // Tags already running on this page stop only when it is reloaded
                if (gtmLoaded) {
                    window.location.reload();
                    return;
                }
            }
            if (settingsReopened) {
                settingsReopened = false;
                $("#cookieSettingsButton").trigger("focus");
            }
        });

        $("#cookieSettingsButton").on("click", function () {
            settingsReopened = true;
            $consentDiv.show();
            $("#consentAcceptButton").trigger("focus");
        });
    }

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
