if ('serviceWorker' in navigator) {
    // Registration is refused outright on a plain-HTTP origin that is not localhost, which is
    // exactly how the app looks when it is reached by IP rather than through the TLS proxy.
    // Nothing is lost by that — it only costs the offline page and the home-screen install —
    // but the rejection has to be caught, or every single page load reports a failure.
    window.addEventListener('load', () =>
        navigator.serviceWorker.register('service-worker.js').catch(() => { }));
}
