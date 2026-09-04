// The "updated" notice.
//
// Whether a phone has already seen it is a per-device thing — one parent reading it should not
// make it disappear for the other — so it lives in that browser's own storage rather than in
// the database or on the sign-in cookie. It is also plain JavaScript rather than anything on
// the circuit, so the notice still behaves on a page that has lost the server.
//
// The banner is rendered hidden and shown from here, so a phone that has already read it never
// sees the thing flash up on every load.

(function () {
    'use strict';

    const SeenKey = 'babytracker.seenVersion';

    const banner = document.getElementById('whats-new');
    if (!banner) {
        return;
    }

    const version = banner.dataset.version;

    function markSeen() {
        try {
            localStorage.setItem(SeenKey, version);
        } catch {
            // Private browsing, or storage turned off. The notice will come back; no harm done.
        }
    }

    let seen = null;
    try {
        seen = localStorage.getItem(SeenKey);
    } catch {
        seen = null;
    }

    if (seen === version) {
        return;
    }

    // Nothing stored means a phone that has not seen a notice before, which includes every phone
    // the first time this ships. Showing it then is the point: the notice announcing itself is
    // what release 1.6 is.
    banner.hidden = false;

    banner.querySelector('[data-dismiss]')?.addEventListener('click', () => {
        markSeen();
        banner.hidden = true;
    });

    // Going to read it counts as having seen it.
    banner.querySelector('a')?.addEventListener('click', markSeen);
})();
