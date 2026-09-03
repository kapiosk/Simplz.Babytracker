// Baby Tracker circuit watchdog.
//
// This is a Blazor *Server* app: every tap travels to the container over a SignalR circuit
// and the server sends the new HTML back. When that circuit dies — the phone slept, the
// wifi dropped, the container restarted — the page keeps looking perfectly fine while
// nothing it does reaches the server. That is the "unresponsive" state: buttons that do
// nothing, and no error to explain it.
//
// Blazor does notice on its own, but only once its 30 second timeout expires, and a phone
// waking from sleep throttles the very timers that would fire it. So the server says so
// instead: CircuitHeartbeat calls in every few seconds, and this watches for the beats
// stopping.
//
//   beats arriving  -> nothing is wrong, carry on
//   beats stopped   -> rejoin the circuit, and reload the page if that fails
//
// It has to go that way round. This file used to ask the question itself, calling into .NET
// to see whether anything answered, and that was what broke the app: interop from the browser
// while the connection is between states throws inside Blazor's own send, Blazor treats a
// failed interop call as an unrecoverable circuit error, and up comes the error bar. The
// check was causing the crash it was looking for. Nothing here calls into .NET any more.
//
// Plus the Reload buttons, wired up here rather than as Blazor click handlers — the circuit
// is exactly what is broken when they matter.

(function () {
    'use strict';

    const BeatEveryMs = 5000;     // must match CircuitHeartbeat.razor
    const SilentForMs = 17000;   // three beats missed before anything is assumed
    const CheckEveryMs = 3000;
    const SettleMs = 1500;       // give Blazor's own reconnect a moment to get there first

    // While Blazor knows it is disconnected it shows its own dialog and runs its own retry
    // loop; two of us calling reconnect() at once helps nobody. We only step in for the case
    // Blazor cannot see, which is the circuit that is dead without having said so.
    let blazorSaysDown = false;
    let recovering = false;

    // Not every page has a circuit to lose: the sign-in page, the error page and the not-found
    // page are rendered once on the server and never become interactive, so no beat will ever
    // arrive there and none is missing. A page has to have had a circuit before we act on
    // having lost one.
    let hadCircuit = false;
    let lastBeat = 0;

    const loadedAt = Date.now();
    let hiddenAt = 0;
    let awayMs = 0;      // how long the page was off screen before it last came back
    const MaxReports = 5;

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

    // The server says so, every few seconds, through CircuitHeartbeat. The page deliberately
    // does not ask: calling into .NET from here while the connection is between states throws
    // inside Blazor's own send, and Blazor treats a failed interop call as an unrecoverable
    // circuit error and shows the error bar. Asking the question was breaking the page.
    window.babytrackerBeat = function () {
        lastBeat = Date.now();
        hadCircuit = true;
        if (!recovering) {
            setBanner(null);
        }
    };

    // Read-only, for looking at what the watchdog thinks from a browser console. Blazor caches
    // the JS function it resolves for a beat, so wrapping babytrackerBeat to count them does not
    // work — ask here instead.
    window.babytrackerStatus = () => ({
        hadCircuit,
        msSinceLastBeat: lastBeat ? Date.now() - lastBeat : null,
        blazorSaysDown,
        recovering,
        deadAfterMs: SilentForMs
    });

    // Survives the reload, which is the point: without it a fault that reappears on the fresh
    // page would send it round again immediately.
    const RecoveredKey = 'babytracker.recoveredAt';
    const RecoveredWithinMs = 30000;

    function recentlyRecovered() {
        try {
            return Date.now() - Number(sessionStorage.getItem(RecoveredKey) || 0) < RecoveredWithinMs;
        } catch {
            return false;   // private browsing, storage disabled: treat it as the first time
        }
    }

    function markRecovered() {
        try {
            sessionStorage.setItem(RecoveredKey, String(Date.now()));
        } catch {
            // Nothing to do; the worst case is one extra reload.
        }
    }

    // Separate from the heartbeat's recovery, and deliberately blunter. That one rejoins first
    // because the page state is still good and worth keeping. This one runs after Blazor has
    // declared an unhandled error, and at that point its own idea of the circuit is not worth
    // trusting — so the page goes and comes back, which is what the bar asks for anyway.
    function recoverFromErrorBar() {
        if (recovering || recentlyRecovered()) {
            return;
        }

        recovering = true;
        setBanner('Something went wrong — reloading…');
        markRecovered();
        location.reload();
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
                // Blazor's own, and made for exactly this: safe to call when it is down.
                rejoined = await Blazor.reconnect();
            } catch {
                rejoined = false; // server unreachable
            }

            if (rejoined) {
                // Give the beats a chance to start again before believing it.
                lastBeat = Date.now();
                setBanner(null);
                return;
            }

            // The circuit is gone for good, or the server was restarted and no longer has it.
            // Reloading gets a working page back; nothing is lost, because a running feed
            // lives in the database rather than in the page.
            markRecovered();
            location.reload();
        } finally {
            recovering = false;
        }
    }

    async function check() {
        if (document.hidden || blazorSaysDown || recovering || !hadCircuit) {
            return;
        }

        if (Date.now() - lastBeat > SilentForMs) {
            await recover();
        }
    }

    // Never left as a bare promise: an unhandled rejection here is itself something the page
    // would have to report, and this is the code that does the reporting.
    const run = () => { check().catch(() => { }); };
    const checkSoon = () => setTimeout(run, SettleMs);

    // ---------- when to ask ----------

    document.addEventListener('visibilitychange', () => {
        if (document.visibilityState !== 'visible') {
            hiddenAt = Date.now();
            return;
        }

        awayMs = hiddenAt ? Date.now() - hiddenAt : 0;
        hiddenAt = 0;

        // Away long enough for the beats to have stopped: rejoin now rather than after a
        // settling period. Blazor refreshes its root components the moment the page comes
        // back, and if the connection died while it was hidden that send throws and puts the
        // error bar up — so the less time it spends holding a dead connection, the better.
        if (awayMs > BeatEveryMs * 2) {
            run();
        } else {
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
    setInterval(run, CheckEveryMs);

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

    // ---------- telling the server when the page breaks ----------

    // The error bar this reports on is raised by Blazor for an unhandled error, and when that
    // error is on the server it is already in the log. When it is not — something failing in
    // the browser — nothing reaches the log at all and the fault is invisible from the Pi. So
    // the page says so itself, along with how long it had been away, because the reports are
    // that this happens on coming back to it.

    let reported = 0;

    function report(kind, detail) {
        if (reported >= MaxReports) {
            return;
        }
        reported++;

        try {
            fetch('/clientlog', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    kind,
                    detail: String(detail ?? '').slice(0, 1500),
                    url: location.pathname,
                    awayMs,
                    ageMs: Date.now() - loadedAt,
                    hadCircuit,
                    blazorSaysDown
                }),
                keepalive: true
            }).catch(() => { /* the page is already having a bad time; do not pile on */ });
        } catch {
            // ignore
        }
    }

    window.addEventListener('error', event => {
        report('window.onerror', (event.message || '') + ' @ ' + (event.filename || '') + ':' + (event.lineno || 0));
    });

    window.addEventListener('unhandledrejection', event => {
        const reason = event.reason;
        report('unhandledrejection', reason && reason.stack ? reason.stack : reason);
    });

    // Blazor shows this bar for an unhandled error, whichever side it came from, so it is the
    // one signal that always coincides with what gets reported as "the app crashed".
    const errorUi = document.getElementById('blazor-error-ui');
    if (errorUi) {
        new MutationObserver(() => {
            if (getComputedStyle(errorUi).display === 'none') {
                return;
            }

            report('blazor-error-ui shown', errorUi.textContent.trim().slice(0, 200));

            // The bar means Blazor has given up on the circuit, and the entry in the issue
            // tracker for that is "you need to refresh the page". So refresh it. Once only:
            // an error that comes back on the reloaded page is a real fault and reloading at
            // it forever would be worse than leaving the bar up with its own Reload link.
            recoverFromErrorBar();
        }).observe(errorUi, { attributes: true, attributeFilter: ['style', 'class'] });
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
