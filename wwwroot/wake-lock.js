// Keep the screen on — the button in the header.
//
// For the feed at 3am with the phone propped up: the timer is on the screen, and the screen going
// dark every thirty seconds means a thumb on it every thirty seconds. Uses the Screen Wake Lock
// API, which is the browser holding the screen awake for as long as this page is the one showing.
//
// Plain JavaScript for the same reasons as the theme and the Reload button: it is about this phone,
// not the account, and it has no business needing the server to be reachable. The choice is kept in
// this browser's own storage, so the other phone can want the opposite.
//
// The browser lets go of the lock by itself whenever the page is hidden — the phone locked, another
// app opened, a different tab — and a page cannot keep it across that. So "on" here means wanted:
// the lock is asked for again whenever the page comes back, and, because some browsers only grant
// it in answer to a tap, on the next tap anywhere if asking without one was refused.
//
// The state is said on <html> as data-wake, like the theme, because the button is rendered by
// Blazor and anything put on it directly can be thrown away on the next render:
//   unsupported — no Wake Lock API here; the button is not shown at all
//   off         — not wanted
//   on          — wanted, and held: the screen will not sleep
//   waiting     — wanted, not held right now; the next tap asks again

(function () {
    'use strict';

    const Key = 'babytracker.awake';   // "on", or absent
    const root = document.documentElement;
    const supported = 'wakeLock' in navigator;

    let sentinel = null;
    let asking = false;

    function wanted() {
        try {
            return localStorage.getItem(Key) === 'on';
        } catch {
            return false;   // private browsing, storage off: off is the safe end to land on
        }
    }

    function remember(on) {
        try {
            if (on) {
                localStorage.setItem(Key, 'on');
            } else {
                localStorage.removeItem(Key);
            }
        } catch {
            // Not remembered past this page, which is no worse than not having the button.
        }
    }

    function state() {
        if (!supported) return 'unsupported';
        if (sentinel) return 'on';
        return wanted() ? 'waiting' : 'off';
    }

    const Titles = {
        on: 'Screen stays on — tap to let it sleep',
        waiting: 'Screen will stay on from your next tap',
        off: 'Keep the screen on'
    };

    function paint() {
        const now = state();
        root.dataset.wake = now;

        // Best effort on the buttons themselves, for a screen reader. If Blazor re-renders one
        // and loses these, the look is still right — that comes from <html> — and the next
        // change puts them back.
        for (const button of document.querySelectorAll('[data-wake-toggle]')) {
            button.setAttribute('aria-pressed', now === 'on' ? 'true' : 'false');
            if (Titles[now]) {
                button.title = Titles[now];
            }
        }
    }

    async function acquire() {
        if (!supported || sentinel || asking || document.visibilityState !== 'visible') {
            return;
        }

        asking = true;
        try {
            const lock = await navigator.wakeLock.request('screen');
            sentinel = lock;

            // Fires when the browser takes it back — the page hidden, the battery low — as well
            // as when it is let go here. Either way it is not held any more.
            lock.addEventListener('release', () => {
                if (sentinel === lock) {
                    sentinel = null;
                }
                paint();
            });
        } catch {
            // Refused: no tap behind the request, the page hidden, battery saver. Left wanted,
            // so the next tap or the next return to the page tries again.
            sentinel = null;
        } finally {
            asking = false;
            paint();
        }
    }

    async function release() {
        const lock = sentinel;
        sentinel = null;

        if (lock) {
            try {
                await lock.release();
            } catch {
                // Already gone.
            }
        }

        paint();
    }

    paint();

    if (!supported) {
        return;
    }

    // Delegated, so it works wherever the button is rendered and whatever Blazor does around it.
    document.addEventListener('click', async event => {
        const button = event.target instanceof Element
            ? event.target.closest('[data-wake-toggle]')
            : null;

        if (!button) {
            return;
        }

        event.preventDefault();

        if (sentinel) {
            remember(false);
            await release();
            return;
        }

        // Off, or waiting: either way the tap is asking for it, and the tap is what some
        // browsers need before they will grant it. If even that is refused, say so by going
        // back to off rather than sitting in a waiting state that will never come good.
        remember(true);
        await acquire();

        if (!sentinel) {
            remember(false);
            paint();
        }
    });

    // Any tap at all, while it is wanted and not held, is a chance to have it back — this is what
    // makes "waiting" come good without anybody having to find the button again.
    document.addEventListener('pointerdown', () => {
        if (wanted() && !sentinel) {
            acquire();
        }
    }, { capture: true, passive: true });

    document.addEventListener('visibilitychange', () => {
        if (document.visibilityState === 'visible' && wanted()) {
            acquire();
        } else {
            paint();
        }
    });

    if (wanted()) {
        acquire();
    }
})();
