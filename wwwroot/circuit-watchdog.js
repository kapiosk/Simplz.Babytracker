// Baby Tracker circuit watchdog.
//
// This is a Blazor *Server* app: every tap travels to the container over a SignalR circuit
// and the server sends the new HTML back. When that circuit dies — the phone slept, the
// wifi dropped, the container restarted — the page keeps looking perfectly fine while
// nothing it does reaches the server. That is the "unresponsive" state: buttons that do
// nothing, and no error to explain it.
//
// Blazor does notice on its own, but only once its 30 second timeout expires, and a phone
// waking from sleep throttles the very timers that would fire it. So instead of waiting,
// this file asks the question directly: a tiny round trip over the circuit (CircuitPing on
// the server) that either comes back or does not.
//
//   it answers      -> nothing is wrong, carry on
//   it does not     -> rejoin the circuit, and reload the page if that fails
//
// Asked whenever the page comes back on screen, when the network returns, and every so
// often while the page is being looked at. Plus the Reload buttons, wired up here rather
// than as Blazor click handlers — the circuit is exactly what is broken when they matter.

(function () {
    'use strict';

    const AssemblyName = 'Simplz.Babytracker';

    const ProbeTimeoutMs = 5000;   // a round trip on the same wifi is milliseconds
    const ProbeEveryMs = 20000;    // ...but only while the page is actually on screen
    const SettleMs = 1200;         // give Blazor's own reconnect a moment to get there first

    // While Blazor knows it is disconnected it shows its own dialog and runs its own retry
    // loop; two of us calling reconnect() at once helps nobody. We only step in for the case
    // Blazor cannot see, which is the circuit that is dead without having said so.
    let blazorSaysDown = false;
    let probing = false;
    let recovering = false;

    // Not every page has a circuit to lose: the sign-in page, the error page and the
    // not-found page are rendered once on the server and never become interactive, and there
    // the probe fails because there is no dispatcher rather than because anything is wrong.
    // So a page has to prove it had a circuit before we will act on having lost one.
    let hadCircuit = false;

    // ---------- the banner ----------

    function setBanner(text) {
        const banner = document.getElementById('circuit-banner');
        if (!banner) {
            return;
        }

        if (!text) {
            banner.hidden = true;
            document.documentElement.style.removeProperty('--circuit-banner-h');
            return;
        }

        banner.querySelector('.msg').textContent = text;
        banner.hidden = false;

        // The banner is fixed to the top of the screen, so tell the page how much room to
        // leave for it — otherwise it sits on top of the header. Measured rather than
        // hard-coded, because the safe-area inset on a notched phone is part of its height.
        document.documentElement.style.setProperty('--circuit-banner-h', banner.offsetHeight + 'px');
    }

    // ---------- is the circuit there? ----------

    function probe() {
        let timer;
        const timedOut = new Promise(resolve => {
            timer = setTimeout(() => resolve(false), ProbeTimeoutMs);
        });

        // A dead circuit either rejects straight away or never answers at all, so race both.
        const answered = DotNet.invokeMethodAsync(AssemblyName, 'Ping').then(() => true, () => false);

        return Promise.race([answered, timedOut]).finally(() => clearTimeout(timer));
    }

    async function recover() {
        if (recovering) {
            return;
        }
        recovering = true;
        setBanner('Connection lost — reconnecting…');

        try {
            let rejoined = false;
            try {
                rejoined = await Blazor.reconnect();
            } catch {
                rejoined = false; // server unreachable
            }

            // reconnect() resolving true only means the socket is back; ask again before
            // trusting it, so we never leave the page looking fine when it still is not.
            if (rejoined && await probe()) {
                setBanner(null);
                return;
            }

            // The circuit is gone for good (or the server was restarted and no longer has
            // it). Reloading gets a working page back; nothing is lost, because a running
            // feed lives in the database rather than in the page.
            location.reload();
        } finally {
            recovering = false;
        }
    }

    async function check() {
        if (document.hidden || blazorSaysDown || probing || recovering || typeof DotNet === 'undefined') {
            return;
        }
        probing = true;
        try {
            if (await probe()) {
                hadCircuit = true;
            } else if (hadCircuit) {
                await recover();
            }
        } finally {
            probing = false;
        }
    }

    const checkSoon = () => setTimeout(check, SettleMs);

    // ---------- when to ask ----------

    document.addEventListener('visibilitychange', () => {
        if (document.visibilityState === 'visible') {
            checkSoon();
        }
    });
    window.addEventListener('focus', checkSoon);
    window.addEventListener('online', checkSoon);
    window.addEventListener('pageshow', event => {
        if (event.persisted) {
            checkSoon();
        }
    });
    setInterval(check, ProbeEveryMs);

    // ---------- Blazor's own view of things ----------

    const dialog = document.getElementById('components-reconnect-modal');
    if (dialog) {
        dialog.addEventListener('components-reconnect-state-changed', event => {
            const state = event.detail && event.detail.state;
            blazorSaysDown = state !== 'hide';
            if (state === 'hide') {
                setBanner(null);
            }
        });
    }

    // ---------- the Reload buttons ----------

    // Delegated, so it keeps working however Blazor re-renders the header around it, and
    // it is plain JavaScript, so it keeps working when the circuit does not.
    document.addEventListener('click', event => {
        const target = event.target instanceof Element ? event.target.closest('[data-reload]') : null;
        if (target) {
            event.preventDefault();
            location.reload();
        }
    });

    // ---------- start Blazor ----------

    // The default is to give up after 30 attempts, having already slowed to one attempt
    // every 30 seconds. A phone on a nightstand should keep trying, and get back quickly
    // when the wifi returns.
    Blazor.start({
        circuit: {
            reconnectionOptions: {
                maxRetries: 1000,
                retryIntervalMilliseconds: attempt => attempt < 6 ? 0 : attempt < 20 ? 2000 : 10000
            }
        }
    });
})();
