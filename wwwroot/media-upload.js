// Sending a photo or a clip to /media.
//
// Blazor could carry the file itself, but it would go through the SignalR circuit in small
// pieces — slow for a photo, hopeless for a two minute video, and over the one connection in
// this app that is least reliable. A plain multipart post is what browsers are good at, and it
// leaves the circuit free to do nothing but say when the upload has finished.

window.babytrackerUpload = async function (inputId, eventId) {
    const input = document.getElementById(inputId);

    // <AntiforgeryToken /> renders no id of its own, and any token on the page is good for
    // the session, so the first one will do.
    const token = document.querySelector('input[name="__RequestVerificationToken"]');

    if (!input || !input.files || input.files.length === 0) {
        return { added: 0, refused: 0, error: null };
    }

    const form = new FormData();
    form.append('eventId', String(eventId));

    if (token) {
        form.append('__RequestVerificationToken', token.value);
    }

    for (const file of input.files) {
        form.append('files', file, file.name);
    }

    try {
        const response = await fetch('/media', { method: 'POST', body: form });

        if (!response.ok) {
            return { added: 0, refused: 0, error: 'The server refused it (' + response.status + ').' };
        }

        const result = await response.json();
        return { added: result.added, refused: result.refused, error: null };
    } catch (err) {
        return { added: 0, refused: 0, error: 'The upload did not reach the server.' };
    } finally {
        // Whatever happened, the picker should not still be holding the last selection.
        input.value = '';
    }
};
