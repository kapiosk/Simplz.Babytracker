// Light, dark, or whatever the phone is set to.
//
// Loaded from <head> rather than the end of the body, and not deferred, so the choice is on the
// page before anything is painted. Deferring it means every load starts in the wrong colours and
// corrects itself a moment later, which at 3am in a dark room is worse than either theme.
//
// Kept in the browser's own storage rather than on the sign-in cookie or in the database: it is
// about this screen in this room, not about the account, and the other phone may well want the
// opposite. Plain JavaScript for the same reason as the Reload button — it has no business
// needing the server to be reachable.

(function () {
    'use strict';

    const Key = 'babytracker.theme';   // "light", "dark", or absent, meaning follow the phone

    const StatusBar = { light: '#6b4ee6', dark: '#14121b' };

    function chosen() {
        try {
            const value = localStorage.getItem(Key);
            return value === 'light' || value === 'dark' ? value : null;
        } catch {
            return null;   // private browsing, storage off: the phone's setting it is
        }
    }

    function resolved() {
        return chosen()
            ?? (window.matchMedia?.('(prefers-color-scheme: dark)').matches ? 'dark' : 'light');
    }

    function apply() {
        const choice = chosen();

        if (choice) {
            document.documentElement.setAttribute('data-theme', choice);
        } else {
            document.documentElement.removeAttribute('data-theme');
        }

        // Which of the three was picked, said on <html> rather than as a class on the buttons.
        // The buttons live inside an interactive page, so Blazor re-renders them from its own
        // tree and throws away anything added here; <html> is outside all of that, and the
        // stylesheet does the highlighting from it.
        document.documentElement.dataset.themeChoice = choice ?? 'system';

        paintStatusBar();
    }

    // The colour behind the clock and the battery on a phone. One tag, set from here, because a
    // theme chosen in the app has to beat the media query the tag would otherwise answer to.
    function paintStatusBar() {
        document.querySelector('meta[name="theme-color"]')
            ?.setAttribute('content', StatusBar[resolved()]);
    }

    apply();

    // Delegated, so it works wherever the buttons are rendered and whatever Blazor does around
    // them — and, like the Reload button, without needing the circuit.
    document.addEventListener('click', event => {
        const button = event.target instanceof Element
            ? event.target.closest('[data-theme-set]')
            : null;

        if (!button) {
            return;
        }

        event.preventDefault();

        try {
            if (button.dataset.themeSet === 'system') {
                localStorage.removeItem(Key);
            } else {
                localStorage.setItem(Key, button.dataset.themeSet);
            }
        } catch {
            // Nothing stored means it reverts to the phone's setting on the next load, which is
            // a reasonable place to end up.
        }

        apply();
    });

    // Following the phone means following it when it changes, which it does on a schedule for
    // most people. The CSS handles the colours on its own; only the status bar needs telling.
    window.matchMedia?.('(prefers-color-scheme: dark)')
        .addEventListener('change', () => {
            if (!chosen()) {
                paintStatusBar();
            }
        });
})();
